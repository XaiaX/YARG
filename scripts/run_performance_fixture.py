#!/usr/bin/env python3
# pattern: Imperative Shell
"""Run rendered YARG performance fixtures in isolated, repeatable player processes.

Run this script on the target machine (including through SSH into its desktop
session). It inherits that session's display environment and never starts a
headless Unity player or changes existing profiles/settings.
"""
import argparse
import copy
from datetime import datetime, timezone
import json
import math
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
import uuid


class RunCancelled(Exception):
    def __init__(self, signum):
        super().__init__(f"fixture suite cancelled by signal {signum}")
        self.signum = signum


def cancel(signum, frame):
    raise RunCancelled(signum)


def positive_integer(value):
    number = int(value)
    if number <= 0:
        raise argparse.ArgumentTypeError("must be greater than zero")
    return number


def positive_seconds(value):
    number = float(value)
    if not math.isfinite(number) or number <= 0:
        raise argparse.ArgumentTypeError("must be a finite number greater than zero")
    return number


def targeted_sequence(value):
    words = value.lower().split(",")
    if any(word not in ("off", "on") for word in words):
        raise argparse.ArgumentTypeError("use comma-separated off/on values, for example off,on,on,off")
    return [word == "on" for word in words]


def reject_constant(value):
    raise ValueError(f"invalid JSON numeric constant: {value}")


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"), parse_constant=reject_constant)


def save_json(path, value):
    # Replace the summary atomically so remote readers never see a half-written document.
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    temporary.replace(path)


def stop_player(process):
    if process is None or process.poll() is not None:
        return
    # Only this invocation's process/session is targeted, never all YARG processes.
    old_handlers = {s: signal.signal(s, signal.SIG_IGN) for s in (signal.SIGINT, signal.SIGTERM)}
    try:
        try:
            if os.name == "posix":
                os.killpg(process.pid, signal.SIGTERM)
            else:
                process.terminate()
        except ProcessLookupError:
            pass
        try:
            process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            try:
                if os.name == "posix":
                    os.killpg(process.pid, signal.SIGKILL)
                else:
                    process.kill()
            except ProcessLookupError:
                pass
            process.wait(timeout=5)
    finally:
        for signum, handler in old_handlers.items():
            signal.signal(signum, handler)


def run_one(player, fixture, output, timeout):
    report = {"output": str(output), "status": "failed", "process_exit_code": None,
              "error": None, "game_result": None}
    command = [str(player), "-performance-fixture", str(output / "input.json"),
               "-performance-output", str(output), "-offline", "-logFile", str(output / "player.log")]
    report["command"] = command
    started = time.monotonic()
    process = None
    try:
        save_json(output / "input.json", fixture)
        with (output / "console.log").open("w", encoding="utf-8") as log:
            process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT,
                                       start_new_session=(os.name == "posix"))
            try:
                report["process_exit_code"] = process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                raise RuntimeError(f"player timed out after {timeout:g} seconds") from None
        result = read_json(output / "result.json")
        if not isinstance(result, dict):
            raise RuntimeError("result.json must contain an object")
        report["game_result"] = result
        if report["process_exit_code"] != 0:
            raise RuntimeError(f"player exited with code {report['process_exit_code']}")
        expected = "discovered" if fixture["mode"] == "discover" else "passed"
        if result.get("status") != expected:
            raise RuntimeError(f"expected result status {expected!r}, received {result.get('status')!r}: "
                               f"{result.get('error') or 'no error supplied'}")
        report["status"] = "passed"
    except RunCancelled as error:
        report["status"] = "cancelled"
        report["cancel_signal"] = error.signum
        report["error"] = str(error)
    except (OSError, ValueError, RuntimeError) as error:
        report["error"] = str(error)
    finally:
        try:
            stop_player(process)
        except (OSError, subprocess.TimeoutExpired) as error:
            report["status"] = "failed"
            report["error"] = f"{report['error'] or 'player cleanup failed'}; cleanup: {error}"
        if process is not None:
            report["process_exit_code"] = process.poll()
        report["elapsed_seconds"] = round(time.monotonic() - started, 3)
    return report


def execute(args):
    player = args.player.resolve()
    output = args.output.resolve()
    fixture_path = args.fixture.resolve()
    if not player.is_file() or not os.access(player, os.X_OK):
        raise RuntimeError(f"player executable not found or not executable: {player}")
    if not output.is_dir():
        raise RuntimeError(f"output root must already exist (check the NAS mount): {output}")
    fixture = read_json(fixture_path)
    if not isinstance(fixture, dict) or type(fixture.get("version")) is not int or fixture["version"] != 1:
        raise ValueError("fixture must be an object with version: 1")
    if not isinstance(fixture.get("settings", {}), dict):
        raise ValueError("fixture settings must be an object")
    if fixture.get("mode", "run") not in ("run", "discover"):
        raise ValueError("fixture mode must be run or discover")
    fixture = copy.deepcopy(fixture)
    fixture["mode"] = "discover" if args.discover else fixture.get("mode", "run")
    fixture.setdefault("settings", {})
    if fixture["mode"] == "run":
        fixture["settings"]["TimingDiagnostics"] = True
    variants = args.targeted if args.targeted is not None else [None]
    name = "performance-" + datetime.now().strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:8]
    suite = output / name
    suite.mkdir()
    report = {"version": 1, "status": "running", "player": str(player), "fixture": str(fixture_path),
              "started_utc": datetime.now(timezone.utc).isoformat(), "runs": []}
    summary = suite / "suite-summary.json"
    started = time.monotonic()
    handlers = {s: signal.signal(s, cancel) for s in (signal.SIGINT, signal.SIGTERM)}
    print(f"Fixture suite: {suite}", flush=True)
    try:
        save_json(summary, report)
        for repetition in range(args.repeat):
            for variant in variants:
                effective = copy.deepcopy(fixture)
                if variant is not None:
                    effective["settings"]["TargetedPerformanceChecks"] = variant
                run_output = suite / f"run-{len(report['runs']) + 1:03d}"
                run_output.mkdir()
                print(f"Starting {run_output.name}; targeted={effective['settings'].get('TargetedPerformanceChecks', False)}", flush=True)
                outcome = run_one(player, effective, run_output, args.timeout)
                outcome["repetition"] = repetition + 1
                report["runs"].append(outcome)
                save_json(summary, report)
                if outcome["status"] != "passed":
                    report["status"] = outcome["status"]
                    print(f"Fixture {outcome['status']}: {outcome['error']}", file=sys.stderr, flush=True)
                    return 128 + outcome["cancel_signal"] if "cancel_signal" in outcome else 1
        report["status"] = "passed"
        return 0
    except RunCancelled as error:
        report["status"] = "cancelled"
        report["error"] = str(error)
        return 128 + error.signum
    except (OSError, ValueError, RuntimeError) as error:
        report["status"] = "failed"
        report["error"] = str(error)
        print(f"Fixture suite failed: {error}", file=sys.stderr, flush=True)
        return 1
    finally:
        # A repeated cancellation must not interrupt the final completion record.
        for signum in handlers:
            signal.signal(signum, signal.SIG_IGN)
        try:
            report["elapsed_seconds"] = round(time.monotonic() - started, 3)
            save_json(summary, report)
            print(f"Summary: {summary}", flush=True)
        finally:
            for signum, handler in handlers.items():
                signal.signal(signum, handler)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, required=True, help="Target-machine YARG executable.")
    parser.add_argument("--fixture", type=Path, required=True, help="Version 1 fixture JSON; never modified.")
    parser.add_argument("--output", type=Path, required=True, help="Existing local/NAS output root.")
    parser.add_argument("--targeted", type=targeted_sequence, help="Override sequence, e.g. off,on,on,off.")
    parser.add_argument("--repeat", type=positive_integer, default=1, help="Repeat the entire variant sequence.")
    parser.add_argument("--timeout", type=positive_seconds, default=1200, help="Hard timeout per player process, seconds.")
    parser.add_argument("--discover", action="store_true", help="Inventory song parts instead of playing.")
    args = parser.parse_args()
    try:
        return execute(args)
    except (OSError, ValueError, RuntimeError) as error:
        print(f"Cannot start fixture suite: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
