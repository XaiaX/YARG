# pattern: Imperative Shell
"""Exercise the build wrapper against a process-boundary Unity stand-in."""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest


WRAPPER = Path(__file__).resolve().parents[1] / "build_linux.py"
FAKE_UNITY = r'''#!/usr/bin/env python3
import json, os, pathlib, signal, sys, time
args = sys.argv[1:]
def arg(name): return args[args.index(name) + 1]
root = pathlib.Path(arg('-projectPath'))
target = arg('-buildTarget')
with (root / 'calls.jsonl').open('a') as f: f.write(json.dumps(args) + '\n')
(root / 'active-target').write_text(target)
if target == 'Linux64':
    if os.environ.get('HANG_BUILD'):
        if os.environ.get('DELAY_TERMINATION'):
            def terminate(signum, frame):
                (root / 'terminating').touch()
                time.sleep(0.8)
                (root / 'child-finished').touch()
                sys.exit(0)
            signal.signal(signal.SIGTERM, terminate)
        (root / 'ready').touch()
        signal.pause()
    if os.environ.get('FAIL_BUILD'): sys.exit(7)
    out = pathlib.Path(arg('-buildOutput'))
    (out / 'YARG.x86_64').write_text('test player')
    if not os.environ.get('OMIT_RECEIPT'):
        pathlib.Path(arg('-buildReceipt')).write_text(json.dumps({'status':'Succeeded','target':'StandaloneLinux64'}))
else:
    if (root / 'terminating').exists() and not (root / 'child-finished').exists(): sys.exit(99)
    if os.environ.get('FAIL_RESTORE'): sys.exit(8)
    pathlib.Path(arg('-restoreReceipt')).write_text(json.dumps({'status':'Succeeded','target':'StandaloneOSX'}))
'''


class LinuxBuildWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="linux workflow ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / "project"
        (self.project / "ProjectSettings").mkdir(parents=True)
        (self.project / "Assets").mkdir()
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.5f2\n")
        self.output = self.root / "NAS builds"
        self.output.mkdir()
        self.unity = self.root / "Unity"
        self.unity.write_text(FAKE_UNITY)
        self.unity.chmod(0o755)
        self.command = [sys.executable, str(WRAPPER), "--project", str(self.project),
                        "--unity", str(self.unity), "--output", str(self.output)]

    def run_build(self, **env):
        return subprocess.run(self.command, env={**os.environ, **env}, capture_output=True,
                              text=True, timeout=15)

    def calls(self):
        return [json.loads(line) for line in (self.project / "calls.jsonl").read_text().splitlines()]

    def test_success_restores_mac_and_preserves_previous_build(self):
        old = self.output / "previous"
        old.mkdir()
        (old / "player").write_text("keep")
        result = self.run_build()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((self.project / "active-target").read_text(), "OSXUniversal")
        self.assertEqual((old / "player").read_text(), "keep")
        self.assertEqual(len(list(self.output.glob("*/YARG.x86_64"))), 1)
        self.assertEqual(len(self.calls()), 2)

    def test_failed_build_still_restores_mac_and_reports_failure(self):
        result = self.run_build(FAIL_BUILD="1")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.project / "active-target").read_text(), "OSXUniversal")
        self.assertIn("build failed", result.stdout + result.stderr)
        self.assertEqual(len(self.calls()), 2)

    def test_zero_exit_without_receipt_is_not_success(self):
        result = self.run_build(OMIT_RECEIPT="1")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(len(self.calls()), 2)

    def test_restore_failure_is_reported_even_after_successful_build(self):
        result = self.run_build(FAIL_RESTORE="1")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("macOS restoration failed", result.stdout + result.stderr)

    def test_missing_destination_does_not_launch_unity(self):
        self.output.rmdir()
        result = self.run_build()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.project / "calls.jsonl").exists())
        self.assertFalse(self.output.exists())

    def test_open_editor_does_not_launch_unity(self):
        (self.project / "Temp").mkdir()
        (self.project / "Temp/UnityLockfile").touch()
        result = self.run_build()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.project / "calls.jsonl").exists())

    def test_profile_option_only_applies_to_build(self):
        self.command.append("--profile")
        result = self.run_build()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("-profileBuild", self.calls()[0])
        self.assertNotIn("-profileBuild", self.calls()[1])

    def test_cancel_waits_for_build_process_then_restores_mac(self):
        process = subprocess.Popen(self.command, env={**os.environ, "HANG_BUILD":"1"},
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            deadline = time.monotonic() + 10
            while not (self.project / "ready").exists() and process.poll() is None and time.monotonic() < deadline:
                time.sleep(0.02)
            self.assertTrue((self.project / "ready").exists(), "Build process did not reach its wait point")
            process.send_signal(signal.SIGTERM)
            out, err = process.communicate(timeout=10)
            self.assertNotEqual(process.returncode, 0, out + err)
            self.assertEqual((self.project / "active-target").read_text(), "OSXUniversal")
            self.assertEqual(len(self.calls()), 2)
        finally:
            if process.poll() is None:
                process.kill()
                process.communicate()

    def test_repeated_cancel_does_not_start_restore_before_build_exits(self):
        process = subprocess.Popen(self.command,
                                   env={**os.environ, "HANG_BUILD":"1", "DELAY_TERMINATION":"1"},
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            for marker in ["ready", "terminating"]:
                deadline = time.monotonic() + 10
                while not (self.project / marker).exists() and process.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.02)
                self.assertTrue((self.project / marker).exists(), marker)
                process.send_signal(signal.SIGTERM)
            out, err = process.communicate(timeout=10)
            self.assertNotEqual(process.returncode, 0)
            self.assertIn("macOS target restored", out, out + err)
        finally:
            if process.poll() is None:
                process.kill()
                process.communicate()


if __name__ == "__main__":
    unittest.main()
