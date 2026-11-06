# pattern: Imperative Shell
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))


class NightlyPipelineTests(unittest.TestCase):
    def setUp(self):
        script = SCRIPTS / 'nightly_to_deck.py'
        self.assertTrue(script.exists(), 'nightly pipeline is missing')
        spec = importlib.util.spec_from_file_location('nightly_to_deck', script)
        self.m = importlib.util.module_from_spec(spec); spec.loader.exec_module(self.m)
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.build = self.root / 'linux-20261106-120000-abcdef'; self.build.mkdir()
        self.calls = []
        self.args = self.m.parser().parse_args(['--output', str(self.root), '--existing-build', self.build.name,
            '--host', 'deck@test', '--key', str(self.root/'key')])

    def test_publish_install_then_retention_and_receipt(self):
        with patch.object(self.m.deploy, '_publish_locked', side_effect=lambda p: self.calls.append('publish')), \
             patch.object(self.m, 'remote_install', side_effect=lambda a,b: self.calls.append('install') or {'verified': True}), \
             patch.object(self.m.deploy, '_prune_locked', side_effect=lambda *a,**k: self.calls.append('prune') or {}):
            self.m.execute(self.args)
        self.assertEqual(self.calls, ['publish', 'install', 'prune'])
        self.assertTrue((self.root/'deployment-receipts'/f'{self.build.name}.json').exists())

    def test_failed_deck_copy_never_prunes_nas(self):
        with patch.object(self.m.deploy, '_publish_locked'), \
             patch.object(self.m, 'remote_install', side_effect=RuntimeError('offline')), \
             patch.object(self.m.deploy, '_prune_locked') as prune:
            with self.assertRaises(RuntimeError): self.m.execute(self.args)
            prune.assert_not_called()

    def test_existing_build_cannot_escape_root(self):
        self.args.existing_build = '../elsewhere'
        with self.assertRaises(RuntimeError): self.m.execute(self.args)

    def test_remote_path_spaces_are_quoted_and_source_is_sent_on_stdin(self):
        with patch.object(self.m.subprocess, 'run') as run:
            run.return_value.returncode = 0
            run.return_value.stdout = '{}'
            self.m.remote_install(self.args, self.build)
        command = run.call_args.args[0]
        self.assertIn("'/home/deck/.local/share/YARC/YARG Installs'", command[-1])
        self.assertIn('def install(', run.call_args.kwargs['input'])
        self.assertIn('IdentityAgent=none', command)

    def test_active_release_holds_the_same_lock_as_standalone_pruning(self):
        import subprocess
        def concurrent_prune(args, build):
            child = subprocess.run([sys.executable, str(SCRIPTS/'linux_deploy.py'), 'prune',
                                    '--root', str(self.root), '--apply'], capture_output=True, text=True)
            self.assertNotEqual(child.returncode, 0, child.stdout)
            self.assertIn('another deployment', child.stderr)
            return {}
        with patch.object(self.m.deploy, '_publish_locked'), \
             patch.object(self.m, 'remote_install', side_effect=concurrent_prune), \
             patch.object(self.m.deploy, '_prune_locked', return_value={}):
            self.m.execute(self.args)

    def test_build_command_streams_and_reads_success_receipt(self):
        scripts = self.root/'scripts'; scripts.mkdir()
        summary = self.root/'summary.json'
        summary.write_text(json.dumps({'build_succeeded': True, 'mac_restored': True, 'output': str(self.build)}))
        (scripts/'build_linux.py').write_text('print(' + repr('Summary: ' + str(summary)) + ')\n')
        with patch.object(self.m, 'SCRIPTS', scripts):
            self.assertEqual(self.m.build_new(self.args), self.build)

    def test_zero_exit_without_summary_refuses_deployment(self):
        scripts = self.root/'scripts'; scripts.mkdir()
        (scripts/'build_linux.py').write_text('print("no completion receipt")\n')
        with patch.object(self.m, 'SCRIPTS', scripts):
            with self.assertRaises(RuntimeError): self.m.build_new(self.args)

    def test_build_receipt_with_failed_restore_refuses_deployment(self):
        scripts = self.root/'scripts'; scripts.mkdir()
        summary = self.root/'summary.json'
        summary.write_text(json.dumps({'build_succeeded': True, 'mac_restored': False, 'output': str(self.build)}))
        (scripts/'build_linux.py').write_text('print(' + repr('Summary: ' + str(summary)) + ')\n')
        with patch.object(self.m, 'SCRIPTS', scripts):
            with self.assertRaises(RuntimeError): self.m.build_new(self.args)

    def test_build_failure_prevents_publish(self):
        self.args.existing_build = None
        with patch.object(self.m, 'build_new', side_effect=RuntimeError('build failed')), \
             patch.object(self.m.deploy, '_publish_locked') as publish:
            with self.assertRaises(RuntimeError): self.m.execute(self.args)
            publish.assert_not_called()


if __name__ == '__main__': unittest.main()
