#!/usr/bin/env python3
# pattern: Imperative Shell
"""Build Linux in the working checkout, then restore the macOS Editor target."""
import argparse
from datetime import datetime
import fcntl
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
import uuid


class BuildCancelled(Exception):
    pass


def cancel(signum, frame):
    raise BuildCancelled("build cancelled; restoring macOS before exiting")


def run_unity(command, console_log):
    # A separate process group lets cancellation stop our Unity, never another Editor.
    with console_log.open("w") as log:
        process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait()
        except BaseException:
            for s in (signal.SIGINT, signal.SIGTERM):
                signal.signal(s, signal.SIG_IGN)
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGTERM)
                try:
                    process.wait(timeout=30)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait()
            raise


def require_receipt(path, target):
    receipt = json.loads(path.read_text())
    if receipt.get("status") != "Succeeded" or receipt.get("target") != target:
        raise RuntimeError(f"invalid Unity completion receipt: {path}")


def execute(args):
    project = args.project.resolve()
    version_file = project / "ProjectSettings/ProjectVersion.txt"
    version = next(line.split(":", 1)[1].strip() for line in version_file.read_text().splitlines()
                   if line.startswith("m_EditorVersion:"))
    unity = (args.unity or Path(f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity")).resolve()
    output = args.output.resolve()
    if not unity.is_file() or not os.access(unity, os.X_OK):
        raise RuntimeError(f"Unity executable not found: {unity}")
    if not output.is_dir():
        raise RuntimeError(f"output root must already exist (check the NAS mount): {output}")
    if output.parts[:2] == ("/", "Volumes") and len(output.parts) > 2:
        volume = Path(*output.parts[:3])
        if not os.path.ismount(volume):
            raise RuntimeError(f"NAS volume is not mounted: {volume}")
    if (project / "Temp/UnityLockfile").exists():
        raise RuntimeError("Unity appears open on this project; save and close it first")

    logs_root = project / "Logs/LinuxBuilds"
    logs_root.mkdir(parents=True, exist_ok=True)
    with (logs_root / "workflow.lock").open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise RuntimeError("another Linux build workflow is already running") from None
        return build_and_restore(project, unity, output, logs_root, args.profile)


def build_and_restore(project, unity, output_root, logs_root, profile):
    run_name = "linux-" + datetime.now().strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6]
    logs = logs_root / run_name
    logs.mkdir()
    output = output_root / run_name
    output.mkdir()
    build_receipt = logs / "build-result.json"
    restore_receipt = logs / "restore-result.json"
    common = [str(unity), "-batchmode", "-quit", "-projectPath", str(project),
              "-settingsState", str(logs / "editor-settings.json")]
    # Select the platform before scripts load; switching inside executeMethod is unsupported in batch mode.
    build = common + ["-buildTarget", "Linux64", "-executeMethod", "Editor.AutomatedLinuxBuild.Build",
                      "-buildOutput", str(output), "-buildReceipt", str(build_receipt),
                      "-logFile", str(logs / "linux.log")]
    if profile:
        build.append("-profileBuild")
    restore = common + ["-buildTarget", "OSXUniversal", "-executeMethod", "Editor.AutomatedLinuxBuild.RestoreMac",
                        "-restoreReceipt", str(restore_receipt), "-logFile", str(logs / "restore-mac.log")]
    report = {"project": str(project), "output": str(output), "logs": str(logs), "profile": profile,
              "build_succeeded": False, "mac_restored": False, "build_command": build, "restore_command": restore}
    for label, directory in [("game_commit", project), ("core_commit", project / "YARG.Core")]:
        result = subprocess.run(["git", "-C", str(directory), "rev-parse", "HEAD"], capture_output=True, text=True)
        report[label] = result.stdout.strip() if result.returncode == 0 else None
    started = time.monotonic()
    old_handlers = {s: signal.signal(s, cancel) for s in (signal.SIGINT, signal.SIGTERM)}
    print(f"Building Linux → {output}\nLogs → {logs}\nKeep this project closed until macOS restoration finishes.", flush=True)
    try:
        try:
            code = run_unity(build, logs / "linux-console.log")
            report["build_exit_code"] = code
            if code != 0:
                raise RuntimeError(f"Unity exited with code {code}")
            require_receipt(build_receipt, "StandaloneLinux64")
            if not (output / "YARG.x86_64").is_file():
                raise RuntimeError("Unity reported success but the Linux executable is missing")
            report["build_succeeded"] = True
        except (BuildCancelled, OSError, ValueError, RuntimeError) as error:
            report["build_error"] = str(error)
            print(f"Linux build failed: {error}", file=sys.stderr, flush=True)
        finally:
            # A second Ctrl-C must not interrupt recovery and leave a hidden Unity process running.
            for s in old_handlers:
                signal.signal(s, signal.SIG_IGN)
            print("Restoring macOS target…", flush=True)
            try:
                code = run_unity(restore, logs / "restore-console.log")
                report["restore_exit_code"] = code
                if code != 0:
                    raise RuntimeError(f"Unity exited with code {code}")
                require_receipt(restore_receipt, "StandaloneOSX")
                report["mac_restored"] = True
            except (OSError, ValueError, RuntimeError) as error:
                report["restore_error"] = str(error)
                print(f"macOS restoration failed: {error}. See {logs / 'restore-mac.log'}", file=sys.stderr, flush=True)
    finally:
        for s, handler in old_handlers.items():
            signal.signal(s, handler)
        report["elapsed_seconds"] = round(time.monotonic() - started, 1)
        (logs / "summary.json").write_text(json.dumps(report, indent=2) + "\n")

    if report["build_succeeded"]:
        try:
            (output / "build-info.json").write_text(json.dumps(report, indent=2) + "\n")
        except OSError as error:
            print(f"Could not save build metadata to destination: {error}", file=sys.stderr)
            return 1
        print(f"Linux player: {output / 'YARG.x86_64'}", flush=True)
    if report["mac_restored"]:
        print("macOS target restored; the Editor can be reopened.", flush=True)
    print(f"Summary: {logs / 'summary.json'}", flush=True)
    return 0 if report["build_succeeded"] and report["mac_restored"] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="Existing local/NAS folder; each build gets a new subfolder.")
    parser.add_argument("--profile", action="store_true", help="Development Build + Autoconnect Profiler; no Deep Profiling.")
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--unity", type=Path, help="Override the Unity executable (normally read from ProjectVersion.txt).")
    args = parser.parse_args()
    try:
        return execute(args)
    except (OSError, RuntimeError, StopIteration, ValueError) as error:
        print(f"Cannot start build workflow: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
