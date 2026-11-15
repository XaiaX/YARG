#!/usr/bin/env python3
# pattern: Imperative Shell
"""Refresh the player Updates package and the internal Elite Drums reference."""

import argparse
from datetime import datetime, timezone
import fcntl
import json
import os
from pathlib import Path
import shlex
import shutil
import stat
import subprocess
import sys
import tempfile
from uuid import uuid4
from zipfile import BadZipFile, ZipFile

from stage_elite_drums import (
    PLAYER_UPDATES_NAME, REFERENCE_DRUM_PARTS, STAGING, YACHT_ROOT, digest, release_archive, release_state,
)


SOURCE_FILES = ('rb3dx_base_updates.yargup', 'expert_plus_updates.yargup')
OUTPUT_NAME = PLAYER_UPDATES_NAME
RECORD_NAME = 'rb3dx_plus_updates_sources.json'
ORIGINAL_DRUMS = 'rock_band_source_exports_drums.yargup'
REFERENCE_RECORD = 'reference_drum_parts_sources.json'
REFERENCE_RECORD_VERSION = 2
SUPERSEDED_FILES = ('combined_updates.yargup', 'combined_updates_sources.json', 'rb3dx_base_updates.yargup')


def stamp(path):
    info = path.stat()
    if not stat.S_ISREG(info.st_mode):
        raise ValueError(f'Expected a regular file: {path}')
    return {
        'path': str(path),
        'modified_utc': datetime.fromtimestamp(info.st_mtime, timezone.utc).isoformat(),
        'mtime_ns': info.st_mtime_ns,
        'size_bytes': info.st_size,
    }


def fingerprint(path, previous):
    current = stamp(path)
    if all(previous.get(field) == current[field] for field in ('path', 'mtime_ns', 'size_bytes')):
        current['sha256'] = previous['sha256']
    else:
        current['sha256'] = digest(path)
        if stamp(path) != {key: value for key, value in current.items() if key != 'sha256'}:
            raise ValueError(f'{path} changed while being hashed. Retry with stable sources.')
    return current


def read_record(path):
    if not path.exists():
        return {'version': 1, 'sources': [], 'output': {}}
    previous = json.loads(path.read_text(encoding='utf-8'))
    if previous['version'] not in (1, REFERENCE_RECORD_VERSION) or not isinstance(previous['sources'], list) or not isinstance(previous['output'], dict):
        raise ValueError(f'Unsupported source record: {path}')
    return previous


def write_record(path, state):
    with path.open('x', encoding='utf-8') as stream:
        json.dump(state, stream, indent=2)
        stream.write('\n')
        stream.flush()
        os.fsync(stream.fileno())


def copy_for_publication(source, target):
    with source.open('rb') as incoming, target.open('xb') as outgoing:
        shutil.copyfileobj(incoming, outgoing)
        outgoing.flush()
        os.fsync(outgoing.fileno())


def run_yacht(yacht, *args):
    subprocess.run([*yacht, *args], check=True)


def compose_release(yacht, rb3dx, expert_plus, output):
    print(f'Combining {rb3dx.name} with {expert_plus.name}', flush=True)
    run_yacht(yacht, 'supplement', 'merge', '--replace-files', '--base', rb3dx, '--override', expert_plus, '-o', output)


def compose_reference(yacht, original_drums, updates, output):
    print(f'Resolving drum parts from {original_drums.name} and {updates.name}', flush=True)
    run_yacht(yacht, 'supplement', 'drum-reference', '--base', original_drums, '--updates', updates, '-o', output)
    verify_reference_inventory(output)


def verify_reference_inventory(output):
    with ZipFile(output) as package:
        manifest = json.loads(package.read('manifest.json'))
    expected = {f'{song}/{song}_update.mid' for song in manifest['Definitions']}
    actual = {asset['Path'] for asset in manifest['Assets']}
    if (manifest['Kind'] != 'updates' or not expected or expected != actual
            or len(expected) != len(manifest['Definitions']) or len(actual) != len(manifest['Assets'])):
        raise ValueError('Expected exactly one drum MIDI for each reference entry.')


def build_package(yacht, paths, sources, output, compose):
    snapshots = []
    for path, entry in zip(paths, sources, strict=True):
        target = output.parent / path.name
        shutil.copyfile(path, target)
        if digest(target) != entry['sha256']:
            raise ValueError(f'{path} changed while being copied. Retry with stable sources.')
        snapshots.append(target)
    compose(yacht, *snapshots, output)
    run_yacht(yacht, 'supplement', 'verify', output)


def archive_legacy(staging):
    for name in SUPERSEDED_FILES:
        legacy = staging / name
        if not legacy.is_file():
            continue
        archive = release_archive(staging) / 'previous'
        archive.mkdir(parents=True, exist_ok=True)
        target = archive / legacy.name
        if target.exists():
            target = archive / f'{legacy.stem}-{uuid4().hex[:8]}{legacy.suffix}'
        legacy.rename(target)
        print(f'Archived {target}', flush=True)


def refresh(source_dir, staging, yacht):
    source_dir, staging = source_dir.resolve(), staging.resolve()
    if not staging.is_dir():
        raise ValueError(f'Release staging directory is unavailable: {staging}')
    if source_dir == staging:
        raise ValueError('The source packages and release staging must be in separate folders.')
    paths = [source_dir / name for name in SOURCE_FILES]
    output = staging / OUTPUT_NAME
    state = release_state(staging)
    state.mkdir(exist_ok=True)
    record = state / RECORD_NAME
    with (state / '.combined-updates.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        legacy_record = staging / RECORD_NAME
        if not record.exists() and legacy_record.is_file():
            legacy_record.rename(record)
        release_built = refresh_locked(paths, output, record, yacht, compose_release)
        archive_legacy(staging)
        reference_built = refresh_locked(
            [source_dir / ORIGINAL_DRUMS, output], source_dir / REFERENCE_DRUM_PARTS,
            source_dir / REFERENCE_RECORD, yacht, compose_reference, record_version=REFERENCE_RECORD_VERSION,
        )
        return release_built, reference_built


def refresh_locked(paths, output, record, yacht, compose, *, record_version=1):
    previous = read_record(record)
    source_by_path = {entry['path']: entry for entry in previous['sources']}
    sources = [fingerprint(path, source_by_path.get(str(path), {})) for path in paths]
    previous_output = previous['output']
    current_output = fingerprint(output, previous_output) if output.is_file() else {}
    new_inputs = [(entry['path'], entry['sha256']) for entry in sources]
    old_inputs = [(entry['path'], entry['sha256']) for entry in previous['sources']]
    rebuild = (previous['version'] != record_version or new_inputs != old_inputs or not current_output
               or current_output['sha256'] != previous_output.get('sha256'))
    state = {'version': record_version, 'sources': sources, 'output': current_output}
    if rebuild or state != previous:
        with tempfile.TemporaryDirectory(prefix='.combined-updates-', dir=record.parent) as stage:
            pending = Path(stage)
            if rebuild:
                with tempfile.TemporaryDirectory(prefix='combined-updates-') as directory:
                    combined = Path(directory) / output.name
                    build_package(yacht, paths, sources, combined, compose)
                    candidate = pending / output.name
                    copy_for_publication(combined, candidate)
                    candidate_hash = digest(candidate)
                    if candidate_hash != digest(combined):
                        raise ValueError('The staged package differs from the verified combination.')
                    # SMB may settle the new timestamp on the first readback.
                    state['output'] = {**stamp(candidate), 'path': str(output), 'sha256': candidate_hash}

            pending_record = pending / record.name
            write_record(pending_record, state)
            for path, entry in zip(paths, sources):
                if stamp(path) != {key: value for key, value in entry.items() if key != 'sha256'}:
                    raise ValueError(f'{path} changed during the refresh. Retry with stable sources.')
            if rebuild:
                os.replace(candidate, output)
            os.replace(pending_record, record)
    return rebuild


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sources', type=Path, default=Path('/Volumes/unity-nas/update-files'))
    parser.add_argument('--staging', type=Path, default=STAGING)
    parser.add_argument('--yacht', default=shlex.join(['/opt/homebrew/bin/dotnet',
                        str(YACHT_ROOT / 'YACHT.Cli/bin/Debug/net10.0/yacht.dll')]),
                        help='Shell-quoted prefix for YACHT with supplement merge --replace-files and drum-reference')
    args = parser.parse_args(argv)
    try:
        yacht = shlex.split(args.yacht)
        if not yacht:
            raise ValueError('--yacht requires an executable')
        release_built, reference_built = refresh(args.sources, args.staging, yacht)
        print(f'{"Staged" if release_built else "Up to date"}: {args.staging / OUTPUT_NAME}', flush=True)
        print(f'{"Refreshed" if reference_built else "Up to date"}: {args.sources / REFERENCE_DRUM_PARTS}', flush=True)
        return 0
    except (BadZipFile, KeyError, OSError, TypeError, ValueError, subprocess.CalledProcessError) as error:
        print(f'Combined updates staging failed: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
