# pattern: Imperative Shell
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'linux_deploy.py'


class DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(SCRIPT.exists(), 'managed deployment implementation is missing')
        spec = importlib.util.spec_from_file_location('linux_deploy', SCRIPT)
        self.m = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.m)
        self.temp = tempfile.TemporaryDirectory(prefix='nightly deploy ')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.nas = self.root / 'NAS builds'; self.nas.mkdir()
        self.deck = self.root / 'YARG Installs'; self.deck.mkdir()

    def build(self, number, root=None, publish=True):
        p = (root or self.nas) / f'linux-202611{number:02d}-120000-abcdef'
        p.mkdir()
        (p / 'YARG.x86_64').write_bytes(b'fake player')
        (p / 'UnityPlayer.so').write_bytes(b'fake library')
        (p / 'YARG_Data').mkdir()
        (p / 'YARG_Data/data file').write_bytes(b'data')
        (p / 'build-info.json').write_text(json.dumps({'build_succeeded': True, 'mac_restored': True, 'profile': False}))
        if publish: self.m.publish(p)
        return p

    def test_publish_rejects_failed_restore(self):
        p = self.build(1, publish=False)
        (p / 'build-info.json').write_text(json.dumps({'build_succeeded': True, 'mac_restored': False}))
        with self.assertRaises(RuntimeError): self.m.publish(p)
        self.assertFalse((p / self.m.MANIFEST).exists())

    def test_install_verifies_then_switches_and_removes_old_managed_build(self):
        old = self.build(1, self.deck)
        (self.deck / 'YOLO-nightly').symlink_to(old.name)
        unrelated = self.deck / 'launcher-uuid'; unrelated.mkdir()
        (unrelated / 'keep').write_text('settings')
        source = self.build(2)
        plan = self.m.install(source, self.deck, apply=False)
        self.assertTrue(old.exists())
        self.assertFalse((self.deck / source.name).exists())
        self.assertEqual(plan['remove'], [old.name])
        self.m.install(source, self.deck, apply=True)
        self.assertEqual((self.deck / 'YOLO-nightly').resolve(), self.deck / source.name)
        self.assertFalse(old.exists())
        self.assertTrue((unrelated / 'keep').exists())
        self.m.verify(self.deck / source.name)
        self.m.install(source, self.deck, apply=True)  # Idempotent retry.

    def test_bad_copy_keeps_previous_and_never_promotes(self):
        old = self.build(1, self.deck)
        (self.deck / 'YOLO-nightly').symlink_to(old.name)
        source = self.build(2)
        def corrupt(src, dst):
            import shutil
            shutil.copytree(src, dst)
            (dst / 'YARG_Data/data file').write_bytes(b'bad')
        with self.assertRaises(RuntimeError): self.m.install(source, self.deck, apply=True, copy=corrupt)
        self.assertEqual((self.deck / 'YOLO-nightly').resolve(), old)
        self.assertFalse((self.deck / source.name).exists())
        self.assertFalse(list(self.deck.glob('.stage-*')))

    def test_tampered_source_is_rejected_before_any_cleanup(self):
        old = self.build(1, self.deck); source = self.build(2)
        (source / 'YARG.x86_64').write_bytes(b'tampered')
        with self.assertRaises(RuntimeError): self.m.install(source, self.deck, apply=True)
        self.assertTrue(old.exists())

    def test_running_player_blocks_install_and_prune(self):
        self.build(1, self.deck); source = self.build(2)
        with self.assertRaises(RuntimeError):
            self.m.install(source, self.deck, apply=True, running=lambda: True)
        self.assertFalse((self.deck / source.name).exists())

    def test_keeps_five_successful_plus_pins_and_ignores_unrelated_incomplete_and_symlinks(self):
        builds = [self.build(n) for n in range(1, 8)]
        (builds[0] / '.keep').write_text('comparison baseline')
        incomplete = self.nas / 'linux-20261001-120000-abcdef'; incomplete.mkdir()
        external = self.root / 'external'; external.mkdir()
        (external / 'important').write_text('keep')
        (self.nas / 'linux-20260901-120000-abcdef').symlink_to(external)
        plan = self.m.prune(self.nas, keep=5, apply=False)
        self.assertEqual(plan['remove'], [builds[1].name])
        self.m.prune(self.nas, keep=5, apply=True)
        self.assertFalse(builds[1].exists())
        self.assertTrue(builds[0].exists())
        self.assertTrue(incomplete.exists())
        self.assertTrue((external / 'important').exists())

    def test_nonlink_latest_is_never_replaced(self):
        (self.deck / 'YOLO-nightly').mkdir()
        with self.assertRaises(RuntimeError): self.m.install(self.build(1), self.deck, apply=True)
        self.assertTrue((self.deck / 'YOLO-nightly').is_dir())

    def test_publish_and_verify_reject_symlink_payload_and_unexpected_files(self):
        p = self.build(1, publish=False)
        (p / 'escape').symlink_to(self.root)
        with self.assertRaises(RuntimeError): self.m.publish(p)
        (p / 'escape').unlink(); self.m.publish(p)
        (p / 'extra').write_text('unexpected')
        with self.assertRaises(RuntimeError): self.m.verify(p)

    def test_matching_name_different_content_does_not_replace_existing(self):
        source = self.build(1); installed = self.build(1, self.deck, publish=False)
        (installed / 'YARG_Data/data file').write_bytes(b'local difference')
        self.m.publish(installed)
        with self.assertRaises(RuntimeError): self.m.install(source, self.deck, apply=True)
        self.assertEqual((installed / 'YARG_Data/data file').read_bytes(), b'local difference')

    def test_player_starting_during_copy_prevents_promotion(self):
        old = self.build(1, self.deck)
        (self.deck / 'YOLO-nightly').symlink_to(old.name)
        source = self.build(2)
        started = [False]
        def copy_then_start(src, dst):
            import shutil
            shutil.copytree(src, dst)
            started[0] = True
        with self.assertRaises(RuntimeError):
            self.m.install(source, self.deck, apply=True, copy=copy_then_start, running=lambda: started[0])
        self.assertEqual((self.deck / 'YOLO-nightly').resolve(), old)
        self.assertFalse((self.deck / source.name).exists())
        self.assertFalse(list(self.deck.glob('.stage-*')))

    def test_keep_marker_and_explicit_deployed_build_survive_nas_retention(self):
        builds = [self.build(n) for n in range(1, 8)]
        self.m.prune(self.nas, keep=5, apply=True, protect={builds[0].name})
        self.assertTrue(builds[0].exists())
        self.assertFalse(builds[1].exists())

    def test_failed_copy_leaves_no_stage_or_current_link(self):
        source = self.build(1)
        def fail_copy(src, dst):
            dst.mkdir(); (dst / 'partial').write_text('partial')
            raise OSError('interrupted')
        with self.assertRaises(OSError): self.m.install(source, self.deck, apply=True, copy=fail_copy)
        self.assertFalse((self.deck / 'YOLO-nightly').exists())
        self.assertFalse(list(self.deck.glob('.stage-*')))

    def test_source_cannot_contain_install_destination(self):
        source = self.build(1)
        nested = source / 'nested'; nested.mkdir()
        with self.assertRaises(RuntimeError): self.m.install(source, nested, apply=True)

    def test_nonlink_created_while_copying_is_preserved(self):
        source = self.build(1)
        def copy_and_create_file(src, dst):
            import shutil
            shutil.copytree(src, dst)
            (self.deck / 'YOLO-nightly').write_text('user file')
        with self.assertRaises(RuntimeError):
            self.m.install(source, self.deck, apply=True, copy=copy_and_create_file)
        self.assertEqual((self.deck / 'YOLO-nightly').read_text(), 'user file')

    def test_linux_process_check_uses_name_when_executable_link_is_restricted(self):
        from unittest.mock import patch
        import inspect
        self.assertIn('proc_root', inspect.signature(self.m.player_running).parameters, 'process checks need a testable proc source')
        proc = self.root / 'proc'; proc.mkdir()
        pid = proc / '123'; pid.mkdir()
        (pid / 'comm').write_text('YARG.x86_64\n')
        (pid / 'cmdline').write_bytes(b'YARG.x86_64\0')
        with patch.object(self.m.sys, 'platform', 'linux'), \
             patch.object(self.m.os, 'readlink', side_effect=PermissionError):
            self.assertTrue(self.m.player_running(proc))
        (pid / 'comm').write_text('pipewire\n')
        (pid / 'cmdline').write_bytes(b'/usr/bin/pipewire\0')
        with patch.object(self.m.sys, 'platform', 'linux'), \
             patch.object(self.m.os, 'readlink', side_effect=PermissionError):
            self.assertFalse(self.m.player_running(proc))

    def test_retention_rejects_zero(self):
        with self.assertRaises(ValueError): self.m.prune(self.nas, keep=0, apply=True)


if __name__ == '__main__': unittest.main()
