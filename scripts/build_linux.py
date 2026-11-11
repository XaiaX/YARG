#!/usr/bin/env python3
# pattern: Imperative Shell
"""Build Linux in the working checkout, then restore the macOS Editor target."""
import argparse
from datetime import datetime
import fcntl
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import uuid

from linux_deploy import locked, package, rotate_share_archives, write_json

NAS_BUILDS = Path('/Volumes/media/scratch/yarg/builds')
MACOS_SHARE_NAME = re.compile(r'macos-\d{8}-\d{6}-[0-9a-f]{6}\.zip\Z')


class BuildCancelled(Exception):
    pass


def cancel(signum, frame):
    raise BuildCancelled("build cancelled; restoring macOS before exiting")


def run_unity(command, console_log, *, cwd=None):
    # A separate process group lets cancellation stop our Unity, never another Editor.
    with console_log.open("w") as log:
        process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=True, cwd=cwd)
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


def require_private_paths_absent(player_output, project):
    roots = {str(project), str(project.resolve())}
    needles = tuple(root.lower().encode(encoding) for root in roots
                    for encoding in ("utf-8", "utf-16le", "utf-16be"))
    for path in player_output.rglob("*"):
        if path.is_symlink() or not path.is_file():
            continue
        if any(part == ".git" or part.endswith(("_DoNotShip", "_ButDontShipItWithYourGame"))
               for part in path.relative_to(player_output).parts):
            continue
        content = path.read_bytes().lower()
        if any(needle in content for needle in needles):
            raise RuntimeError(f"player contains a local checkout path: {path.relative_to(player_output)}")


def normalize_macos_burst_path(app):
    burst = app / "Contents/PlugIns/lib_burst_generated.bundle"
    if burst.is_file():
        subprocess.run(["/usr/bin/install_name_tool", "-id", "@rpath/lib_burst_generated.bundle", str(burst)],
                       check=True)
        sign = ["/usr/bin/codesign", "--force", "--sign", "-", "--timestamp=none",
                "--preserve-metadata=identifier,entitlements,flags,runtime"]
        for path in (burst, app):
            subprocess.run([*sign, str(path)], check=True)
        subprocess.run(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(app)], check=True)


def publish_macos_archive(artifact):
    root = artifact.parent.parent
    with locked(root):
        current = root / "current"
        if current.is_symlink():
            raise RuntimeError(f"share directory cannot be a symlink: {current}")
        current.mkdir(exist_ok=True)
        shared = current / artifact.name
        previous = root / "previous" / artifact.name
        if any(path.exists() or path.is_symlink() for path in (shared, previous)):
            raise FileExistsError(f"share archive already exists: {shared}")
        partial = current / (".package-" + uuid.uuid4().hex)
        try:
            shutil.copyfile(artifact, partial)
            partial.rename(shared)
            active = rotate_share_archives(root, MACOS_SHARE_NAME)
        finally:
            partial.unlink(missing_ok=True)
        return shared if active == shared else previous


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
            raise RuntimeError("another desktop build workflow is already running") from None
        return build_and_restore(project, unity, output, logs_root, args.profile, args.development, args.platform)


def build_and_restore(project, unity, output_root, logs_root, profile, development=False, platform="linux"):
    is_mac = platform == "macos"
    platform_label = "macOS" if is_mac else "Linux"
    editor_target = "OSXUniversal" if is_mac else "Linux64"
    receipt_target = "StandaloneOSX" if is_mac else "StandaloneLinux64"
    build_method = "BuildMac" if is_mac else "Build"
    run_name = platform + "-" + datetime.now().strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6]
    logs = logs_root / run_name
    logs.mkdir()
    output = output_root / run_name
    output.mkdir()
    # Keep the app bundle on the Mac filesystem until it is archived with its permissions.
    staging = tempfile.TemporaryDirectory(prefix="player-", dir=logs) if is_mac else None
    player_output = Path(staging.name) if staging else output
    artifact = output / (run_name + ".zip" if is_mac else "YARG.x86_64")
    build_info = output / "build-info.json"
    compiler_response = project / "Assets/csc.rsp"
    compiler_meta = compiler_response.with_suffix(".rsp.meta")
    compiler_response_created = False
    build_receipt = logs / "build-result.json"
    restore_receipt = logs / "restore-result.json"
    common = [str(unity), "-batchmode", "-quit", "-projectPath", str(project),
              "-settingsState", str(logs / "editor-settings.json")]
    # Select the platform before scripts load; switching inside executeMethod is unsupported in batch mode.
    build = common + ["-buildTarget", editor_target, "-executeMethod", "Editor.AutomatedLinuxBuild." + build_method,
                      "-buildOutput", str(player_output), "-buildReceipt", str(build_receipt),
                      "-logFile", str(logs / (platform + ".log"))]
    if profile:
        build.append("-profileBuild")
    elif development:
        build.append("-developmentBuild")
    restore = common + ["-buildTarget", "OSXUniversal", "-executeMethod", "Editor.AutomatedLinuxBuild.RestoreMac",
                        "-restoreReceipt", str(restore_receipt), "-logFile", str(logs / "restore-mac.log")]
    report = {"project": str(project), "output": str(output), "logs": str(logs), "profile": profile,
              "development": profile or development, "platform": platform, "artifact": str(artifact),
              "build_succeeded": False, "mac_restored": False, "build_command": build, "restore_command": restore}
    for label, directory in [("game_commit", project), ("core_commit", project / "YARG.Core")]:
        result = subprocess.run(["git", "-C", str(directory), "rev-parse", "HEAD"], capture_output=True, text=True)
        report[label] = result.stdout.strip() if result.returncode == 0 else None
    started = time.monotonic()
    old_handlers = {s: signal.signal(s, cancel) for s in (signal.SIGINT, signal.SIGTERM)}
    print(f"Building {platform_label} → {output}\nLogs → {logs}\nKeep this project closed until macOS restoration finishes.", flush=True)
    try:
        try:
            if compiler_meta.exists() or compiler_meta.is_symlink():
                raise RuntimeError(f"existing Unity compiler response metadata: {compiler_meta}")
            with compiler_response.open("x", encoding="utf-8") as response:
                compiler_response_created = True
                response.write(f'-pathmap:"{project}"=/_/YARG\n')
            code = run_unity(build, logs / (platform + "-console.log"))
            report["build_exit_code"] = code
            if code != 0:
                raise RuntimeError(f"Unity exited with code {code}")
            require_receipt(build_receipt, receipt_target)
            if is_mac:
                app = player_output / "YARG.app"
                if not (app / "Contents/Info.plist").is_file():
                    raise RuntimeError("Unity reported success but the macOS app bundle is missing")
                normalize_macos_burst_path(app)
                require_private_paths_absent(app, project)
                partial = artifact.with_suffix(".partial")
                code = run_unity(["/usr/bin/zip", "-q", "-r", "-X", "-y", str(partial), app.name,
                                  "-x", "*/.DS_Store", "*/._*", "*/__MACOSX/*"],
                                 logs / "archive.log", cwd=player_output)
                if code != 0 or not partial.is_file() or not partial.stat().st_size:
                    raise RuntimeError(f"macOS archive failed; see {logs / 'archive.log'}")
                partial.replace(artifact)
            elif not artifact.is_file():
                raise RuntimeError("Unity reported success but the Linux executable is missing")
            else:
                require_private_paths_absent(player_output, project)
            report["local_path_check_passed"] = True
            report["build_succeeded"] = True
        except (BuildCancelled, OSError, ValueError, RuntimeError, subprocess.CalledProcessError) as error:
            report["build_error"] = str(error)
            print(f"{platform_label} build failed: {error}", file=sys.stderr, flush=True)
        finally:
            # A second Ctrl-C must not interrupt recovery and leave a hidden Unity process running.
            for s in old_handlers:
                signal.signal(s, signal.SIG_IGN)
            print("Restoring macOS target…", flush=True)
            try:
                if compiler_response_created:
                    compiler_response.unlink(missing_ok=True)
                    compiler_meta.unlink(missing_ok=True)
                code = run_unity(restore, logs / "restore-console.log")
                report["restore_exit_code"] = code
                if code != 0:
                    raise RuntimeError(f"Unity exited with code {code}")
                require_receipt(restore_receipt, "StandaloneOSX")
                report["mac_restored"] = True
            except (OSError, ValueError, RuntimeError) as error:
                report["restore_error"] = str(error)
                print(f"macOS restoration failed: {error}. See {logs / 'restore-mac.log'}", file=sys.stderr, flush=True)
        if report["build_succeeded"] and report["mac_restored"]:
            try:
                if is_mac:
                    shared = publish_macos_archive(artifact)
                else:
                    # The shared packager validates the dated build against this receipt.
                    write_json(build_info, report)
                    shared = package(output)
                report["share_archive"] = str(shared)
                print(f"{platform_label} share archive: {shared}", flush=True)
            except (OSError, RuntimeError) as error:
                report["share_error"] = str(error)
                print(f"Could not publish {platform_label} share archive: {error}", file=sys.stderr, flush=True)
    finally:
        for s, handler in old_handlers.items():
            signal.signal(s, handler)
        report["elapsed_seconds"] = round(time.monotonic() - started, 1)
        (logs / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
        if staging:
            staging.cleanup()

    if report["build_succeeded"]:
        try:
            write_json(build_info, report)
        except OSError as error:
            print(f"Could not save build metadata to destination: {error}", file=sys.stderr)
            return 1
        print(f"{platform_label} player: {artifact}", flush=True)
    if report["mac_restored"]:
        print("macOS target restored; the Editor can be reopened.", flush=True)
    print(f"Summary: {logs / 'summary.json'}", flush=True)
    return 0 if report["build_succeeded"] and report["mac_restored"] and "share_error" not in report else 1


def main(platform="linux"):
    description = "Build and archive a macOS nightly to the NAS." if platform == "macos" else __doc__
    parser = argparse.ArgumentParser(description=description)
    default_output = NAS_BUILDS / ("macOS" if platform == "macos" else "Linux")
    parser.add_argument("--output", type=Path, default=default_output,
                        help=f"Existing output folder (default: {default_output}).")
    parser.set_defaults(platform=platform)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--profile", action="store_true", help="Development Build + Autoconnect Profiler; no Deep Profiling.")
    mode.add_argument("--development", action="store_true", help="Development Build for native recorders; no Profiler autoconnect or Deep Profiling.")
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
