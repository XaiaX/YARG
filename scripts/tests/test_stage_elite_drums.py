# pattern: Imperative Shell
from pathlib import Path
import sys
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import elite_release
import stage_elite_drums


class EliteReleaseTests(unittest.TestCase):
    def test_generation_uses_the_single_resolved_drum_reference(self):
        sources = Path('/sources')
        self.assertEqual(stage_elite_drums.generation_reference(sources), sources / 'reference_drum_parts.yargup')

    def test_generator_invocation_passes_only_the_resolved_reference(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            reference = root / 'reference_drum_parts'
            with patch.object(stage_elite_drums.subprocess, 'run', return_value=SimpleNamespace(returncode=0)) as run:
                stage_elite_drums.generate(['yacht'], root / 'generator.py', reference, None, root / 'generated')
            arguments = run.call_args.args[0]
            self.assertEqual(arguments[arguments.index('--sources') + 1], reference)
            self.assertNotIn('--updates', arguments)

    def test_authored_replaces_entire_entry_and_adds_authored_only_songs(self):
        generated = [('foo', '(foo (elite_drums_author "Generated"))'), ('base', '(base (rank (elite_drums 42)))')]
        authored = [('Foo', '(Foo (drums_panning audience))'), ('extra', '(extra (drums_panning drummer))')]
        combined = elite_release.overlay(generated, authored)
        self.assertEqual(combined, [
            ('base', generated[1][1], False),
            ('extra', authored[1][1], True),
            ('Foo', authored[0][1], True),
        ])

    def test_playlist_recomputes_marker_and_deduplicates_title_aliases(self):
        self.assertEqual(elite_release.format_playlist([
            ('Toto - Rosanna', False),
            ('Metallica - Fuel (+)', False),
            ('Metallica - Fuel', True),
            ('A Band - Song (+)', False),
        ]), 'A Band - Song\nMetallica - Fuel (+)\nToto - Rosanna\n')
        self.assertEqual(elite_release.song_label({'songId': 'rosanna'}, {},
            '(rosanna\n\n   ; Toto - Rosanna (+)\n   (rank (elite_drums 353))\n)'), 'Toto - Rosanna (+)')

    def test_known_omissions_do_not_mask_a_shared_input_failure(self):
        report = {'status': 'failed', 'errors': ['bad chart'],
                  'songs': [{'id': 'Broken', 'outcome': 'failed', 'error': 'bad chart'}]}
        self.assertEqual(elite_release.checked_omissions(report, {'broken'}), ['Broken'])
        with self.assertRaises(ValueError):
            elite_release.checked_omissions(dict(report, errors=['bad chart', 'cannot read Updates']), {'broken'})
        with self.assertRaises(ValueError):
            elite_release.checked_omissions(report, set())

    def test_staging_keeps_stable_paths_and_archives_replaced_files(self):
        with tempfile.TemporaryDirectory() as root:
            root = Path(root)
            stage = root / 'release-staging'
            stage.mkdir()
            source = root / 'prepared'
            source.mkdir()
            for name in stage_elite_drums.RELEASE_FILES[:-1]:
                (source / name).write_text(name)
            with self.assertRaises(OSError):
                stage_elite_drums.stage_release(source, stage)
            self.assertEqual(list(stage.iterdir()), [])
            (source / stage_elite_drums.RELEASE_FILES[-1]).write_text('Artist - Title\n')
            build = stage / 'linux-nightly.tar.gz'
            build.write_bytes(b'keep this build')
            result = stage_elite_drums.stage_release(source, stage)
            self.assertEqual(result, stage)
            current_files = {*stage_elite_drums.RELEASE_FILES, build.name}
            self.assertEqual({path.name for path in stage.iterdir()}, current_files)
            name = stage_elite_drums.RELEASE_FILES[0]
            (source / name).write_text('newest')
            stage_elite_drums.stage_release(source, stage)
            self.assertEqual({path.name for path in stage.iterdir()}, current_files)
            self.assertEqual((stage / name).read_text(), 'newest')
            old, = (root / 'release-staging-archive').glob(f'elite-drums-*/{name}')
            self.assertEqual(old.read_text(), name)
            self.assertEqual(build.read_bytes(), b'keep this build')
