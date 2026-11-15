#!/usr/bin/env python3
# pattern: Imperative Shell
"""Refresh the Elite Drums Upgrades variants in local NAS release staging."""

import argparse
from datetime import datetime, timezone
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import uuid
from zipfile import BadZipFile, ZipFile

from elite_release import checked_omissions, format_playlist, overlay, song_label


REFERENCE_DRUM_PARTS = 'reference_drum_parts.yargup'
PLAYER_UPDATES_NAME = 'rb3dx_plus_updates.yargup'
RELEASE_FILES = ('elite_drums_plus_hand_edited.yargup', 'elite_drums_plus_combined.yargup', 'hand_edited_songs.txt')
GENERATION_OPTIONS = ('--recover-malformed',)
STAGING = Path('/Volumes/media/scratch/yarg/builds/release-staging')
EDIT_ROOT = Path.home() / 'Local/YARG/Edits'
YACHT_ROOT = Path.home() / 'Local/src/YACHT'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def cli_json(yacht, *args):
    return json.loads(subprocess.check_output([*yacht, *args], text=True))


def song_metadata(yacht, path):
    result = cli_json(yacht, 'dta-inspect', '--file', path, '--json')
    if result['schema'] != 'yacht.dta-inspection' or result['version'] != 1:
        raise ValueError(f'Unsupported DTA inspection response for {path}')
    return {song['songId'].casefold(): song for song in result['songs']}


def write_playlist(path, authored, entries, base_metadata, yacht):
    authored_metadata = song_metadata(yacht, authored / 'upgrades.dta')
    charts = []
    for key, entry in entries:
        result = cli_json(yacht, 'elite-drums', 'inspect', authored / f'{key}_plus.mid', '--json')
        if (result['schema'] != 'yacht.elite-drums-inspection' or result['version'] != 1
                or result['elitePresent'] is not True):
            raise ValueError(f'Expected an Elite Drums MIDI for {key}')
        name = song_label(authored_metadata[key.casefold()], base_metadata.get(key.casefold(), {}), entry)
        charts.append((name, any(hit['isDoubleKick'] for hit in result['hits'])))
    path.write_text(format_playlist(charts), encoding='utf-8', newline='\n')


def release_archive(staging):
    return staging.with_name(staging.name + '-archive')


def release_state(staging):
    return staging.with_name('.' + staging.name + '-state')


def stage_release(prepared, staging):
    """Publish verified files at the stable player paths, preserving earlier copies."""
    archive = release_archive(staging)
    state = release_state(staging)
    archive.mkdir(exist_ok=True)
    state.mkdir(exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%SZ')
    previous = archive / f'elite-drums-{stamp}-{uuid.uuid4().hex[:6]}'
    # The combined package is the usual player selection. Publish it last.
    ordered = (RELEASE_FILES[2], RELEASE_FILES[0], RELEASE_FILES[1])
    with (state / '.elite-drums.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        with tempfile.TemporaryDirectory(dir=archive, prefix='promotion-') as temporary:
            incoming = Path(temporary)
            for name in ordered:
                source = prepared / name
                output = incoming / name
                shutil.copyfile(source, output)
                if digest(source) != digest(output):
                    raise OSError(f'Staged copy failed verification: {name}')

            previous_files = [name for name in ordered if (staging / name).exists()]
            if previous_files:
                previous.mkdir()
                for name in previous_files:
                    shutil.copy2(staging / name, previous / name)
                    if digest(staging / name) != digest(previous / name):
                        raise OSError(f'Backup copy failed verification: {name}')

            published = []
            try:
                for name in ordered:
                    os.replace(incoming / name, staging / name)
                    published.append(name)
            except OSError:
                for name in reversed(published):
                    if name in previous_files:
                        shutil.copy2(previous / name, incoming / name)
                        os.replace(incoming / name, staging / name)
                    else:
                        (staging / name).unlink()
                raise
    return staging


def generation_key(yacht, generator, source_hash, pairs):
    executable = shutil.which(yacht[0])
    if executable is None:
        raise ValueError(f'YACHT executable is unavailable: {yacht[0]}')
    files = {Path(executable), generator}
    if pairs:
        files.add(pairs)
    for token in [executable, *yacht]:
        path = Path(token)
        if path.is_file():
            path = path.resolve()
            files.add(path)
            files.update(p for p in (path.parent / name for name in ('yacht.dll', 'YACHT.Core.dll', 'YARG.Core.dll'))
                         if p.is_file())
    # Includes the selected generator code, tool, pair policy, and exact input package.
    key = [list(yacht), GENERATION_OPTIONS, pairs is not None, source_hash,
           [digest(file) for file in sorted(files)]]
    return hashlib.sha256(json.dumps(key).encode()).hexdigest()


def generate(yacht, generator, reference, pairs, output):
    logfile = output.parent / 'generation.log'
    print(f'Generating Elite Drums charts in {output}. Progress: {logfile}', flush=True)
    pair_options = ['--pairs', pairs] if pairs else []
    with logfile.open('w', encoding='utf-8') as log:
        result = subprocess.run([
            sys.executable, generator, '--sources', reference,
            '--output', output, '--yacht', shlex.join(yacht), *pair_options, *GENERATION_OPTIONS,
        ], stdout=log, stderr=subprocess.STDOUT)
    # Generator exit 1 can contain explicitly permitted, recorded per-song failures.
    if result.returncode not in (0, 1):
        raise ValueError(f'Generator exited {result.returncode}. See {logfile}')
    return result.returncode


def load_index(path, dta):
    contents, _ = dta.decode(path.read_bytes(), 'auto')
    return dta.scan_entries(contents)


def refresh(args, dta):
    yacht = shlex.split(args.yacht)
    if not yacht:
        raise ValueError('--yacht requires an executable')
    if not args.staging.is_dir():
        raise ValueError(f'Release staging directory is unavailable: {args.staging}')
    with tempfile.TemporaryDirectory(dir=args.work, prefix='prepare-') as run:
        return prepare(args, Path(run), yacht, dta)


def generation_reference(sources):
    return sources / REFERENCE_DRUM_PARTS


def prepare(args, work, yacht, dta):
    original = generation_reference(args.sources)
    snapshot = work / original.name
    shutil.copyfile(original, snapshot)
    source_hash = digest(snapshot)
    subprocess.run([*yacht, 'supplement', 'verify', snapshot], check=True)
    with ZipFile(snapshot) as package:
        if json.loads(package.read('manifest.json'))['Kind'] != 'updates':
            raise ValueError(f'Expected an Updates package: {original}')
    base_metadata = song_metadata(yacht, snapshot)
    if args.generated_snapshot:
        print(f'Reusing generated snapshot: {args.generated_snapshot}', flush=True)
        generated = args.generated_snapshot
        result = None
    else:
        generator = work / 'gen_elite_updates_from_sources.py'
        shutil.copyfile(args.yacht_root / 'tools' / generator.name, generator)
        pairs = None
        if args.pairs:
            pairs = work / 'pairs.csv'
            shutil.copyfile(args.pairs, pairs)
        key = generation_key(yacht, generator, source_hash, pairs)
        generated = args.work / key / 'generated'
        generated.parent.mkdir(parents=True, exist_ok=True)
        # Materialize the package once. Each selective CLI MIDI read would reopen it.
        reference = snapshot.with_suffix('')
        subprocess.run([*yacht, 'supplement', 'extract', snapshot, '--output', reference], check=True)
        result = generate(yacht, generator, reference, pairs, generated)

    report = json.loads((generated / 'generation_report.json').read_text(encoding='utf-8'))
    if (report['schema'] != 'yacht.elite-source-generation' or report['version'] != 2
            or result == 1 and report['status'] != 'failed'):
        raise ValueError(f'Invalid generation report in {generated}')
    omitted = checked_omissions(report, {key.casefold() for key in args.allow_missing})
    generated_entries = load_index(generated / 'songs_updates.dta', dta)
    generated_ids = {key.casefold() for key, _ in generated_entries}
    skipped = {song['id'].casefold() for song in report['songs'] if song['outcome'] == 'skipped-no-drums'}
    missing = base_metadata.keys() - generated_ids - {key.casefold() for key in omitted} - skipped
    if missing or generated_ids != report['provenance'].keys() or any(
        item['mode'] not in ('source', 'community') for item in report['provenance'].values()
    ):
        raise ValueError(f'Generated snapshot does not cover the source package or has inconsistent provenance. Missing: {sorted(missing)}')
    if omitted:
        print('Omitted from generated charts: ' + ', '.join(omitted), flush=True)

    authored = work / 'authored'
    subprocess.run([
        *yacht, 'reaper-build', args.reaper_projects, '--format', 'upgrades', '--part', 'ELITE_DRUMS',
        '--cache-dir', args.reaper_cache, '--output', authored,
    ], check=True)
    authored_entries = load_index(authored / 'upgrades.dta', dta)
    if not authored_entries:
        raise ValueError('No hand-edited charts were built')
    combined = work / 'combined'
    combined.mkdir()
    entries = overlay(generated_entries, authored_entries)
    for key, _, hand_edited in entries:
        dta.safe_key(key)
        filename = f'{key}_plus.mid'
        source = authored / filename if hand_edited else generated / key / f'{key}_update.mid'
        shutil.copyfile(source, combined / filename)
    (combined / 'upgrades.dta').write_text(''.join(entry + '\n' for _, entry, _ in entries),
                                           encoding='utf-8-sig', newline='\n')
    write_playlist(work / RELEASE_FILES[2], authored, authored_entries, base_metadata, yacht)
    for source, name in zip((authored, combined), RELEASE_FILES[:2]):
        subprocess.run([*yacht, 'supplement', 'pack', '--kind', 'upgrades', '--dta',
                        source / 'upgrades.dta', '-o', work / name], check=True)
    if digest(original) != source_hash:
        raise ValueError('The drum reference changed during the release run. Retry with stable inputs.')
    return stage_release(work, args.staging)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sources', type=Path, default=Path('/Volumes/unity-nas/update-files'),
                        help='Directory containing reference_drum_parts.yargup')
    parser.add_argument('--reaper-projects', type=Path, default=EDIT_ROOT / 'reaper_projects/_playable')
    parser.add_argument('--reaper-cache', type=Path, default=EDIT_ROOT / 'reaper_cache/_playable')
    parser.add_argument('--pairs', type=Path, help='Optional YACHT 1x/2x pairs CSV for additional song IDs')
    parser.add_argument('--work', type=Path, default=EDIT_ROOT / 'elite-release-work')
    parser.add_argument('--staging', type=Path, default=STAGING)
    parser.add_argument('--yacht-root', type=Path, default=YACHT_ROOT)
    parser.add_argument('--yacht', help='Shell-quoted command prefix for an installed or built YACHT CLI')
    parser.add_argument('--generated-snapshot', type=Path, help='Reuse an explicitly selected generated-only repository')
    parser.add_argument('--allow-missing', action='append', default=[], metavar='SONG_ID',
                        help='Permit this recorded per-song conversion failure. Repeat for more songs.')
    args = parser.parse_args(argv)
    if args.yacht is None:
        args.yacht = shlex.join(['/opt/homebrew/bin/dotnet',
                                 str(args.yacht_root / 'YACHT.Cli/bin/Debug/net10.0/yacht.dll')])
    try:
        sys.path.insert(0, str(args.yacht_root / 'tools'))
        import copy_updates_to_upgrades as dta

        args.work = args.work.resolve()
        args.work.mkdir(parents=True, exist_ok=True)
        with (args.work / 'stage-elite-drums.lock').open('a') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            destination = refresh(args, dta)
        print(f'Staged {destination}', flush=True)
        return 0
    except (BadZipFile, ImportError, KeyError, OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f'Elite Drums staging failed: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
