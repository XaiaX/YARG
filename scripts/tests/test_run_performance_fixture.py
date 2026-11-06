# pattern: Imperative Shell
"""Exercise fixture orchestration at the real process boundary."""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest


WRAPPER = Path(__file__).resolve().parents[1] / "run_performance_fixture.py"
PLAYER = r'''#!/usr/bin/env python3
import json, os, pathlib, signal, sys, time
args = sys.argv[1:]
def arg(name): return args[args.index(name) + 1]
output = pathlib.Path(arg('-performance-output'))
fixture = json.loads(pathlib.Path(arg('-performance-fixture')).read_text())
(output / 'observed.json').write_text(json.dumps({'args': args, 'fixture': fixture, 'pid': os.getpid()}))
(output / 'ready').touch()
print('stand-in player output', flush=True)
if os.environ.get('HANG_PLAYER'):
    if os.environ.get('IGNORE_TERM'): signal.signal(signal.SIGTERM, signal.SIG_IGN)
    signal.pause()
if os.environ.get('BAD_RESULT'):
    (output / 'result.json').write_text('{broken')
elif not os.environ.get('OMIT_RESULT'):
    result = {'status': 'discovered' if fixture.get('mode') == 'discover' else 'passed'}
    if os.environ.get('FAIL_RESULT'): result = {'status': 'failed', 'error': 'missing requested part'}
    if os.environ.get('WRONG_MODE_RESULT'): result['status'] = 'discovered'
    (output / 'result.json').write_text(json.dumps(result))
sys.exit(int(os.environ.get('PLAYER_EXIT', '0')))
'''


class PerformanceFixtureLauncherTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="fixture workflow ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.output = self.root / "test output"
        self.output.mkdir()
        self.player = self.root / "YARG test player"
        self.player.write_text(PLAYER)
        self.player.chmod(0o755)
        self.fixture = self.root / "source fixture.json"
        self.source = {"version": 1, "mode": "run", "song": {"name": "example"},
                       "settings": {"VSync": False, "TargetedPerformanceChecks": True}}
        self.fixture.write_text(json.dumps(self.source))
        self.command = [sys.executable, str(WRAPPER), "--player", str(self.player),
                        "--fixture", str(self.fixture), "--output", str(self.output)]

    def run_fixture(self, extra=(), **env):
        return subprocess.run(self.command + list(extra), env={**os.environ, **env},
                              capture_output=True, text=True, timeout=15)

    def summary(self):
        files = list(self.output.glob("*/suite-summary.json"))
        self.assertEqual(len(files), 1)
        return json.loads(files[0].read_text()), files[0].parent

    def test_success_runs_rendered_player_and_preserves_source(self):
        result = self.run_fixture()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        summary, suite = self.summary()
        self.assertEqual(summary["status"], "passed")
        self.assertEqual(len(summary["runs"]), 1)
        run = Path(summary["runs"][0]["output"])
        observed = json.loads((run / "observed.json").read_text())
        self.assertNotIn("-batchmode", observed["args"])
        self.assertNotIn("-nographics", observed["args"])
        self.assertIn("-offline", observed["args"])
        self.assertTrue(all(Path(observed["args"][observed["args"].index(key) + 1]).is_absolute()
                            for key in ("-performance-fixture", "-performance-output", "-logFile")))
        self.assertEqual(observed["fixture"]["settings"]["TimingDiagnostics"], True)
        self.assertEqual(observed["fixture"]["settings"]["TargetedPerformanceChecks"], True)
        self.assertEqual(json.loads(self.fixture.read_text()), self.source)
        self.assertIn("stand-in player output", (run / "console.log").read_text())
        self.assertGreaterEqual(summary["runs"][0]["elapsed_seconds"], 0)

    def test_abba_sequence_repeats_without_reusing_output_directories(self):
        result = self.run_fixture(["--targeted", "off,on,on,off", "--repeat", "2"])
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        summary, _ = self.summary()
        self.assertEqual(len(summary["runs"]), 8)
        outputs = [Path(r["output"]) for r in summary["runs"]]
        self.assertEqual(len(set(outputs)), 8)
        values = [json.loads((p / "input.json").read_text())["settings"]["TargetedPerformanceChecks"] for p in outputs]
        self.assertEqual(values, [False, True, True, False] * 2)

    def test_discovery_requires_discovered_receipt(self):
        result = self.run_fixture(["--discover"])
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        summary, _ = self.summary()
        observed = json.loads((Path(summary["runs"][0]["output"]) / "observed.json").read_text())
        self.assertEqual(observed["fixture"]["mode"], "discover")
        self.assertEqual(summary["runs"][0]["game_result"]["status"], "discovered")

    def test_exit_zero_requires_valid_matching_success_receipt(self):
        for env in ({"OMIT_RESULT": "1"}, {"BAD_RESULT": "1"}, {"FAIL_RESULT": "1"},
                    {"WRONG_MODE_RESULT": "1"}, {"PLAYER_EXIT": "7"}):
            with self.subTest(env=env):
                result = self.run_fixture(["--repeat", "3"], **env)
                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                suites = sorted(self.output.glob("*/suite-summary.json"), key=lambda p: p.stat().st_mtime_ns)
                summary = json.loads(suites[-1].read_text())
                self.assertEqual(summary["status"], "failed")
                self.assertEqual(len(summary["runs"]), 1, "Stop the suite on its first failure.")
                self.assertTrue(summary["runs"][0]["error"])

    def test_timeout_kills_only_spawned_process_and_records_failure(self):
        unrelated = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"])
        try:
            result = self.run_fixture(["--timeout", "0.15"], HANG_PLAYER="1", IGNORE_TERM="1")
            self.assertNotEqual(result.returncode, 0)
            summary, _ = self.summary()
            run = summary["runs"][0]
            self.assertIn("timed out", run["error"])
            pid = json.loads((Path(run["output"]) / "observed.json").read_text())["pid"]
            with self.assertRaises(ProcessLookupError): os.kill(pid, 0)
            self.assertIsNone(unrelated.poll())
        finally:
            unrelated.terminate()
            unrelated.wait(timeout=5)

    def test_cancel_finishes_child_and_writes_summary(self):
        process = subprocess.Popen(self.command, env={**os.environ, "HANG_PLAYER": "1"},
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            deadline = time.monotonic() + 5
            ready = []
            while not ready and process.poll() is None and time.monotonic() < deadline:
                ready = list(self.output.glob("*/*/ready"))
                time.sleep(0.02)
            self.assertTrue(ready, "Player did not reach its wait point.")
            process.send_signal(signal.SIGTERM)
            out, err = process.communicate(timeout=10)
            self.assertNotEqual(process.returncode, 0, out + err)
            summary, _ = self.summary()
            self.assertEqual(summary["status"], "cancelled")
            pid = json.loads((ready[0].parent / "observed.json").read_text())["pid"]
            with self.assertRaises(ProcessLookupError): os.kill(pid, 0)
        finally:
            if process.poll() is None:
                process.kill()
            process.communicate()

    def test_separate_invocations_preserve_prior_results(self):
        first = self.run_fixture()
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        before = {p: p.read_bytes() for p in self.output.rglob("*") if p.is_file()}
        second = self.run_fixture()
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
        self.assertEqual(len(list(self.output.glob("*/suite-summary.json"))), 2)
        for path, data in before.items():
            self.assertEqual(path.read_bytes(), data)

    def test_invalid_inputs_do_not_create_a_suite_or_launch_player(self):
        for extra in (["--repeat", "0"], ["--timeout", "nan"], ["--timeout", "-1"],
                      ["--targeted", "maybe"], ["--targeted", "on,,off"]):
            with self.subTest(extra=extra):
                result = self.run_fixture(extra)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(list(self.output.iterdir()), [])
        self.output.rmdir()
        result = self.run_fixture()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.output.exists())

    def test_invalid_fixture_does_not_launch(self):
        for fixture in ([], {"version": True}, {"version": 2}, {"version": 1, "settings": []},
                        {"version": 1, "mode": "unknown"}):
            with self.subTest(fixture=fixture):
                self.fixture.write_text(json.dumps(fixture))
                result = self.run_fixture()
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(list(self.output.iterdir()), [])


if __name__ == "__main__":
    unittest.main()
