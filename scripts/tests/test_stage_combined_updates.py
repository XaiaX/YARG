# pattern: Imperative Shell
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import stage_combined_updates as staging


class CombinedUpdatesTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name).resolve()
        self.sources = root / 'sources'
        self.sources.mkdir()
        self.stage = root / 'staging'
        self.stage.mkdir()
        self.inputs = [self.sources / name for name in staging.SOURCE_FILES]
        for path, content in zip(self.inputs, (b'rb3dx', b'expert'), strict=True):
            path.write_bytes(content)
        self.original_drums = self.sources / 'rock_band_source_exports_drums.yargup'
        self.original_drums.write_bytes(b'rock-band')
        self.reference = self.sources / 'reference_drum_parts.yargup'
        self.output = self.stage / staging.OUTPUT_NAME
        self.record = root / ".staging-state" / staging.RECORD_NAME
        self.calls = []
        self.yacht = patch.object(staging, 'run_yacht', side_effect=self.run_yacht).start()
        patch.object(staging, 'verify_reference_inventory').start()
        self.addCleanup(patch.stopall)

    def run_yacht(self, yacht, *args):
        self.calls.append(args)
        if args[:2] == ('supplement', 'merge'):
            self.assertIn('--replace-files', args)
            base = Path(args[args.index('--base') + 1])
            override = Path(args[args.index('--override') + 1])
            Path(args[args.index('-o') + 1]).write_bytes(base.read_bytes() + b'|' + override.read_bytes())
        elif args[:2] == ('supplement', 'drum-reference'):
            base = Path(args[args.index('--base') + 1])
            updates = Path(args[args.index('--updates') + 1])
            Path(args[args.index('-o') + 1]).write_bytes(base.read_bytes() + b'|drums-from|' + updates.read_bytes())
        elif args[:2] == ('supplement', 'verify'):
            self.assertTrue(Path(args[2]).is_file())
        else:
            self.fail(f'Unexpected command: {args}')

    def run_stage(self):
        return staging.refresh(self.sources, self.stage, ['yacht'])

    def bump_timestamp(self, path):
        state = path.stat()
        os.utime(path, ns=(state.st_atime_ns, state.st_mtime_ns + 2_000_000_000))

    def test_initial_merge_records_only_release_sources_and_retires_old_assets(self):
        old_package = self.stage / 'combined_updates.yargup'
        old_record = self.stage / 'combined_updates_sources.json'
        old_package.write_bytes(b'old release')
        old_record.write_bytes(b'old source record')
        self.assertEqual(self.run_stage(), (True, True))
        self.assertEqual(self.output.name, 'rb3dx_plus_updates.yargup')
        self.assertEqual(self.output.read_bytes(), b'rb3dx|expert')
        self.assertEqual(self.reference.read_bytes(), b'rock-band|drums-from|rb3dx|expert')
        self.assertFalse(old_package.exists())
        self.assertFalse(old_record.exists())
        self.assertEqual((self.stage.with_name('staging-archive') / 'previous' / old_package.name).read_bytes(), b'old release')
        self.assertEqual((self.stage.with_name('staging-archive') / 'previous' / old_record.name).read_bytes(), b'old source record')
        record = json.loads(self.record.read_text())
        for path, source in zip(self.inputs, record['sources'], strict=True):
            self.assertEqual(source['path'], str(path))
            self.assertEqual(source['mtime_ns'], path.stat().st_mtime_ns)
            self.assertEqual(source['sha256'], staging.digest(path))
            self.assertIn('modified_utc', source)

    def test_unchanged_run_reuses_fingerprints_and_package(self):
        self.run_stage()
        self.calls.clear()
        previous = self.record.read_bytes()
        with patch.object(staging, 'digest', side_effect=AssertionError('unexpected hash')):
            self.assertEqual(self.run_stage(), (False, False))
        self.assertEqual(self.calls, [])
        self.assertEqual(previous, self.record.read_bytes())

    def test_touched_input_hash_is_checked_without_rebuilding(self):
        self.run_stage()
        self.calls.clear()
        self.bump_timestamp(self.inputs[0])
        with patch.object(staging, 'digest', wraps=staging.digest) as digest:
            self.assertEqual(self.run_stage(), (False, False))
        digest.assert_called_once_with(self.inputs[0])
        self.assertEqual(self.calls, [])
        self.assertEqual(json.loads(self.record.read_text())['sources'][0]['mtime_ns'], self.inputs[0].stat().st_mtime_ns)

    def test_changed_input_rebuilds_and_failed_build_preserves_staging(self):
        self.run_stage()
        previous = self.record.read_bytes()
        self.inputs[1].write_bytes(b'expert2')
        self.bump_timestamp(self.inputs[1])
        with patch.object(staging, 'run_yacht', side_effect=subprocess.CalledProcessError(4, ['yacht'])):
            with self.assertRaises(subprocess.CalledProcessError):
                self.run_stage()
        self.assertEqual(self.output.read_bytes(), b'rb3dx|expert')
        self.assertEqual(self.record.read_bytes(), previous)
        self.assertEqual(self.run_stage(), (True, True))
        self.assertEqual(self.output.read_bytes(), b'rb3dx|expert2')
        self.assertEqual(self.reference.read_bytes(), b'rock-band|drums-from|rb3dx|expert2')

    def test_missing_output_rebuilds_from_the_current_sources(self):
        self.run_stage()
        self.output.unlink()
        self.assertEqual(self.run_stage(), (True, False))
        self.assertEqual(self.output.read_bytes(), b'rb3dx|expert')

    def test_source_change_during_merge_preserves_the_previous_release(self):
        self.run_stage()
        previous = self.record.read_bytes()
        self.inputs[0].write_bytes(b'rb3dx next')

        def change_input_on_verify(yacht, *args):
            self.run_yacht(yacht, *args)
            if args[:2] == ('supplement', 'verify'):
                self.inputs[1].write_bytes(b'expert next')

        with patch.object(staging, 'run_yacht', side_effect=change_input_on_verify):
            with self.assertRaisesRegex(ValueError, 'expert_plus_updates.yargup changed during the refresh'):
                self.run_stage()
        self.assertEqual(self.output.read_bytes(), b'rb3dx|expert')
        self.assertEqual(self.record.read_bytes(), previous)

    def test_nas_timestamp_adjustment_on_private_candidate_does_not_block_promotion(self):
        file_digest = staging.digest

        def hash_with_nas_timestamp_adjustment(path):
            result = file_digest(path)
            if path.name == staging.OUTPUT_NAME and path.parent.name.startswith('.combined-updates-'):
                self.bump_timestamp(path)
            return result

        with patch.object(staging, 'digest', side_effect=hash_with_nas_timestamp_adjustment):
            self.assertEqual(self.run_stage(), (True, True))
        self.assertEqual(self.output.read_bytes(), b'rb3dx|expert')
        self.assertEqual(self.run_stage(), (False, False))

    def test_changed_original_drums_refresh_only_the_internal_reference(self):
        self.run_stage()
        self.calls.clear()
        record = self.record.read_bytes()
        self.original_drums.write_bytes(b'rock-band-next')
        self.assertEqual(self.run_stage(), (False, True))
        self.assertEqual(self.record.read_bytes(), record)
        self.assertEqual(self.reference.read_bytes(), b'rock-band-next|drums-from|rb3dx|expert')
        self.assertNotIn(('supplement', 'merge'), [args[:2] for args in self.calls])

    def test_outdated_reference_record_rebuilds_only_the_reference(self):
        self.run_stage()
        record = self.sources / staging.REFERENCE_RECORD
        state = json.loads(record.read_text())
        state['version'] = 1
        record.write_text(json.dumps(state))
        self.assertEqual(self.run_stage(), (False, True))
        self.assertEqual(json.loads(record.read_text())['version'], staging.REFERENCE_RECORD_VERSION)


class ReferenceInventoryTests(unittest.TestCase):
    def test_reference_rejects_a_dta_entry_without_its_midi(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'reference.yargup'
            for assets in ([{'Path': 'song/song_update.mid'}], []):
                with ZipFile(path, 'w') as package:
                    package.writestr('manifest.json', json.dumps({
                        'Kind': 'updates', 'Definitions': ['song'], 'Assets': assets,
                    }))
                if assets:
                    staging.verify_reference_inventory(path)
                else:
                    with self.assertRaisesRegex(ValueError, 'exactly one drum MIDI'):
                        staging.verify_reference_inventory(path)
