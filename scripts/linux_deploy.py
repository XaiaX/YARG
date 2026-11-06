#!/usr/bin/env python3
# pattern: Imperative Shell
"""Publish, verify, install and retain complete Linux nightlies. Cleanup defaults to a dry run."""
import argparse
from contextlib import contextmanager
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sys
import uuid

MANIFEST = 'nightly-manifest.json'
LATEST = 'YOLO-nightly'
NAME = re.compile(r'linux-\d{8}-\d{6}-[0-9a-f]{6}\Z')
EXCLUDED = {MANIFEST, '.keep', '.deck-install.json'}


def require_build(path):
    path = Path(path)
    if path.is_symlink() or not path.is_dir() or not NAME.fullmatch(path.name):
        raise RuntimeError(f'not a dated Linux build directory: {path}')
    info = path / 'build-info.json'
    if info.is_symlink():
        raise RuntimeError(f'build receipt cannot be a symlink: {info}')
    try:
        report = json.loads(info.read_text())
    except (OSError, ValueError) as error:
        raise RuntimeError(f'missing or invalid build receipt: {info}') from error
    if report.get('build_succeeded') is not True or report.get('mac_restored') is not True:
        raise RuntimeError(f'build/restoration not successful: {path}')
    for name in ('YARG.x86_64', 'UnityPlayer.so', 'YARG_Data'):
        entry = path / name
        if entry.is_symlink() or (not entry.is_dir() if name == 'YARG_Data' else not entry.is_file()):
            raise RuntimeError(f'missing or linked player component: {entry}')
    if not (path / 'YARG_Data').is_dir():
        raise RuntimeError(f'player data is not a directory: {path}')
    return report


def inventory(path):
    files = {}
    for directory, dirs, names in os.walk(path, followlinks=False):
        for name in dirs + names:
            entry = Path(directory) / name
            relative = entry.relative_to(path).as_posix()
            if entry.is_symlink():
                raise RuntimeError(f'linked payload is not supported: {entry}')
            if relative in EXCLUDED or entry.is_dir():
                continue
            if not entry.is_file():
                raise RuntimeError(f'non-regular payload file: {entry}')
            digest = hashlib.sha256()
            with entry.open('rb') as source:
                for block in iter(lambda: source.read(1024 * 1024), b''):
                    digest.update(block)
            files[relative] = {'sha256': digest.hexdigest(), 'bytes': entry.stat().st_size}
    return dict(sorted(files.items()))


def write_json(path, data):
    temporary = path.with_name('.' + path.name + '-' + uuid.uuid4().hex)
    try:
        temporary.write_text(json.dumps(data, indent=2) + '\n')
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def publish(build):
    build = Path(build)
    with locked(build.parent):
        return _publish_locked(build)


def _publish_locked(build):
    build = Path(build)
    require_build(build)
    result = {'schema': 1, 'build': build.name, 'files': inventory(build)}
    target = build / MANIFEST
    if target.exists() and json.loads(target.read_text()) != result:
        raise RuntimeError(f'published build changed; create a new build instead: {build}')
    write_json(target, result)
    return result


def verify(build, expected_name=None):
    build = Path(build)
    manifest_path = build / MANIFEST
    if manifest_path.is_symlink():
        raise RuntimeError(f'linked manifest: {manifest_path}')
    try:
        manifest = json.loads(manifest_path.read_text())
    except (OSError, ValueError) as error:
        raise RuntimeError(f'build has no valid publication manifest: {build}') from error
    if manifest.get('schema') != 1 or manifest.get('build') != (expected_name or build.name):
        raise RuntimeError(f'manifest identity mismatch: {build}')
    actual = inventory(build)
    if actual != manifest.get('files'):
        raise RuntimeError(f'checksum/file inventory mismatch: {build}')
    return manifest


def candidates(root):
    result = []
    for path in sorted(root.iterdir()):
        if not NAME.fullmatch(path.name) or path.is_symlink():
            continue
        try:
            require_build(path)
        except RuntimeError:
            continue  # Unknown/incomplete directories never enter deletion plans.
        result.append(path)
    return result


@contextmanager
def locked(root):
    if root.is_symlink() or not root.is_dir():
        raise RuntimeError(f'expected an existing real install/build root: {root}')
    lock_path = root / '.nightly-deploy.lock'
    # O_NOFOLLOW prevents an existing symlink from redirecting a lock-file write.
    fd = os.open(lock_path, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, 'a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise RuntimeError(f'another deployment/cleanup owns {root}') from None
        yield


def player_running(proc_root=Path('/proc')):
    if sys.platform != 'linux':
        return False
    for process in proc_root.iterdir():
        if not process.name.isdigit():
            continue
        identities = []
        # SteamOS restricts some /proc/exe links even for the same uid. Read the
        # independent name/cmdline sources too, rather than treating that as YARG.
        readers = (
            lambda: os.readlink(process / 'exe').removesuffix(' (deleted)'),
            lambda: os.fsdecode((process / 'cmdline').read_bytes().split(b'\0', 1)[0]),
            lambda: (process / 'comm').read_text().strip(),
        )
        for read in readers:
            try:
                value = read()
                if value:
                    identities.append(Path(value).name)
            except (FileNotFoundError, PermissionError, ProcessLookupError):
                continue
        if 'YARG.x86_64' in identities:
            return True
        if not identities:
            try:
                if process.stat().st_uid == os.getuid():
                    raise RuntimeError(f'cannot inspect own process: {process}')
            except FileNotFoundError:
                pass  # Process exited while enumerating.
    return False


def require_stopped(running):
    if running():
        raise RuntimeError('YARG is running; close it and retry deployment')


def remove_build(path):
    require_build(path)
    if (path / '.keep').exists():
        raise RuntimeError(f'build was pinned after planning: {path}')
    shutil.rmtree(path)


def validate_latest(latest):
    if latest.exists() and not latest.is_symlink():
        raise RuntimeError(f'refusing to replace non-symlink {latest}')
    if latest.is_symlink() and not NAME.fullmatch(os.readlink(latest)):
        raise RuntimeError(f'latest link has an unmanaged target: {latest}')


def install(build, root, apply=False, copy=shutil.copytree, running=player_running):
    build, root = Path(build), Path(root)
    require_build(build)
    if (root.is_symlink() or not root.is_dir() or root.resolve().is_relative_to(build.resolve())
            or build.resolve().is_relative_to(root.resolve())):
        raise RuntimeError('install root must be an existing directory separate from the source')
    require_stopped(running)
    manifest = verify(build)
    latest = root / LATEST
    validate_latest(latest)
    destination = root / build.name
    old = [p.name for p in candidates(root) if p.name != build.name and not (p / '.keep').exists()]
    plan = {'build': build.name, 'root': str(root), 'latest': str(latest), 'remove': old, 'applied': apply}
    if not apply:
        return plan
    with locked(root):
        require_stopped(running)
        validate_latest(latest)
        stage = root / ('.stage-' + build.name + '-' + uuid.uuid4().hex)
        temporary_link = root / ('.latest-' + uuid.uuid4().hex)
        try:
            if destination.exists() or destination.is_symlink():
                require_build(destination)
                if verify(destination) != manifest:
                    raise RuntimeError(f'existing build name has different content: {destination}')
            else:
                needed = sum(item['bytes'] for item in manifest['files'].values()) + 64 * 1024 * 1024
                if shutil.disk_usage(root).free < needed:
                    raise RuntimeError(f'insufficient free space to stage {build.name}')
                copy(build, stage)
                if verify(stage, expected_name=build.name) != manifest:
                    raise RuntimeError('staged manifest differs from publication')
                (stage / 'YARG.x86_64').chmod((stage / 'YARG.x86_64').stat().st_mode | 0o111)
                require_stopped(running)
                os.rename(stage, destination)
            require_stopped(running)
            validate_latest(latest)
            temporary_link.symlink_to(build.name)
            os.replace(temporary_link, latest)
            write_json(destination / '.deck-install.json', {'build': build.name, 'source': str(build), 'verified': True})
            # Re-read eligible directories under the lock; only prune after promotion.
            for path in candidates(root):
                if path.name != build.name and not (path / '.keep').exists():
                    require_stopped(running)
                    remove_build(path)
            return plan
        finally:
            temporary_link.unlink(missing_ok=True)
            if stage.exists():
                shutil.rmtree(stage)


def _prune_locked(root, keep=5, apply=False, protect=()):
    if keep < 1:
        raise ValueError('retention must keep at least one successful build')
    complete = candidates(root)
    retained = {p.name for p in complete[-keep:]} | set(protect)
    latest = root / LATEST
    if latest.is_symlink():
        retained.add(Path(os.readlink(latest)).name)
    result = {'root': str(root), 'keep': keep, 'applied': apply,
              'remove': [p.name for p in complete if p.name not in retained and not (p / '.keep').exists()]}
    if apply:
        for name in result['remove']:
            remove_build(root / name)
    return result


def prune(root, keep=5, apply=False, protect=()):
    root = Path(root)
    if root.is_symlink() or not root.is_dir():
        raise RuntimeError('retention root must be an existing real directory')
    if not apply:
        return _prune_locked(root, keep, False, protect)
    with locked(root):
        return _prune_locked(root, keep, True, protect)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    subs = parser.add_subparsers(dest='command', required=True)
    for name in ('publish', 'verify'):
        sub = subs.add_parser(name); sub.add_argument('--build', type=Path, required=True)
    sub = subs.add_parser('install')
    sub.add_argument('--build', type=Path, required=True)
    sub.add_argument('--root', type=Path, required=True)
    sub.add_argument('--apply', action='store_true')
    sub = subs.add_parser('prune')
    sub.add_argument('--root', type=Path, required=True)
    sub.add_argument('--keep', type=int, default=5)
    sub.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    try:
        if args.command == 'publish':
            result = publish(args.build); result = {'build': result['build'], 'files': len(result['files'])}
        elif args.command == 'verify':
            result = verify(args.build); result = {'build': result['build'], 'verified_files': len(result['files'])}
        elif args.command == 'install': result = install(args.build, args.root, args.apply)
        else: result = prune(args.root, args.keep, args.apply)
        print(json.dumps(result, indent=2))
        return 0
    except (OSError, ValueError, RuntimeError) as error:
        print(f'deployment refused: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__': sys.exit(main())
