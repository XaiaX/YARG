#!/usr/bin/env python3
# pattern: Imperative Shell
"""Build a normal nightly to the NAS, verify/install it on the Deck, then retain five NAS builds."""
import argparse
import json
import os
from pathlib import Path
import shlex
import subprocess
import sys

import linux_deploy as deploy

SCRIPTS = Path(__file__).resolve().parent


def build_new(args):
    command = [sys.executable, str(SCRIPTS / 'build_linux.py'), '--output', str(args.output),
               '--project', str(args.project)]
    if args.profile:
        command.append('--profile')
    summary = None
    with subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True) as process:
        for line in process.stdout:
            print(line, end='', flush=True)
            if line.startswith('Summary: '):
                summary = Path(line[len('Summary: '):].strip())
        code = process.wait()
    if code != 0 or summary is None:
        raise RuntimeError('build/restoration failed; nothing deployed or pruned')
    report = json.loads(summary.read_text())
    if report.get('build_succeeded') is not True or report.get('mac_restored') is not True:
        raise RuntimeError('build summary is not successful')
    build = Path(report['output'])
    if build.parent.resolve() != args.output.resolve():
        raise RuntimeError('build output is outside the selected NAS root')
    return build


def remote_install(args, build):
    if args.host.startswith('-'):
        raise RuntimeError('invalid SSH host')
    remote = ['python3', '-', 'install', '--build', str(args.deck_nas_root / build.name),
              '--root', str(args.deck_installs), '--apply']
    command = ['ssh', '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10', '-o', 'IdentitiesOnly=yes',
               '-o', 'IdentityAgent=none', '-i', str(args.key.expanduser()), args.host, shlex.join(remote)]
    print(f'Installing verified build on {args.host}: {args.deck_installs / build.name}', flush=True)
    result = subprocess.run(command, input=(SCRIPTS / 'linux_deploy.py').read_text(),
                            capture_output=True, text=True, timeout=900)
    if result.returncode:
        raise RuntimeError(f'Deck installation failed; NAS retained: {result.stderr.strip()}')
    receipt = json.loads(result.stdout)
    print(json.dumps(receipt, indent=2), flush=True)
    return receipt


def execute(args):
    args.output = args.output.absolute()
    if not args.output.is_dir() or args.output.is_symlink():
        raise RuntimeError('NAS output root must be an existing real directory')
    if args.output.parts[:2] == ('/', 'Volumes') and not os.path.ismount(Path(*args.output.parts[:3])):
        raise RuntimeError('NAS volume is not mounted')
    if args.keep < 1:
        raise ValueError('retention must keep at least one successful build')
    if args.existing_build:
        if not deploy.NAME.fullmatch(args.existing_build):
            raise RuntimeError('existing build must be a dated build folder name, not a path')
        build = args.output / args.existing_build
    else:
        build = build_new(args)
    # Same NAS lock as standalone retention, held while the Deck reads its source.
    with deploy.locked(args.output):
        deploy._publish_locked(build)
        receipt = {'build': str(build), 'deck': remote_install(args, build)}
        receipt['nas_retention'] = deploy._prune_locked(args.output, keep=args.keep, apply=True, protect={build.name})
        receipts = args.output / 'deployment-receipts'
        receipts.mkdir(exist_ok=True)
        deploy.write_json(receipts / (build.name + '.json'), receipt)
    print(f'Deployed: {args.deck_installs / deploy.LATEST}\nReceipt: {receipts / (build.name + ".json")}', flush=True)
    return receipt


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument('--output', type=Path, required=True, help='Existing mounted NAS build directory on this computer.')
    result.add_argument('--project', type=Path, default=SCRIPTS.parent)
    result.add_argument('--existing-build', help='Retry installation of this successful dated folder without rebuilding.')
    result.add_argument('--profile', action='store_true', help='Explicitly build a profiling nightly instead of normal.')
    result.add_argument('--keep', type=int, default=5)
    result.add_argument('--host', default='deck@192.168.1.157')
    result.add_argument('--key', type=Path, default=Path.home()/'.ssh/deck_ed25519')
    result.add_argument('--deck-nas-root', type=Path, default=Path('/home/deck/NAS/Songs for YARG/Builds'))
    result.add_argument('--deck-installs', type=Path, default=Path('/home/deck/.local/share/YARC/YARG Installs'))
    return result


def main():
    try:
        execute(parser().parse_args())
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(f'nightly deployment failed: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__': sys.exit(main())
