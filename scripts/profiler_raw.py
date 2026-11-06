#!/usr/bin/env python3
# pattern: Imperative Shell
"""Stream-index Unity .raw captures and extract bounded frame ranges.

No Unity installation or third-party Python package needed for indexing.
Extracted .raw ranges should be opened with the matching Unity version.
"""
import argparse
import csv
from dataclasses import dataclass
import json
from pathlib import Path
import shutil
import sys
from profiler_raw_format import (
    GLOBAL_THREAD, Frame, check_footer, intervals, parse_block, parse_frame,
    parse_header, summarize,
)


@dataclass
class Scan:
    frames: list
    globals: list
    byte_count: int
    block_count: int
    main_thread: int
    unity_version: tuple


def scan(stream):
    stream.seek(0, 2)
    size = stream.tell()
    stream.seek(0)
    main, version = parse_header(stream.read(56))
    frames, globals_ = [], []
    position, count = 56, 0
    while position < size:
        stream.seek(position)
        block = parse_block(stream.read(20), position, size)
        if block.thread == main and block.payload_size == 14:
            frame = parse_frame(stream.read(14), block, main)
            if frame is not None:
                if frames and (frame.number <= frames[-1].number or frame.time_ns <= frames[-1].time_ns):
                    raise ValueError(f'non-increasing frame marker at byte {position}')
                frames.append(frame)
        if block.thread == GLOBAL_THREAD:
            globals_.append(block)
        stream.seek(position + 20 + block.payload_size)
        check_footer(stream.read(8), block)
        position += block.size
        count += 1
    return Scan(frames, globals_, size, count, main, version)


def copy_range(source, destination, offset, length):
    source.seek(offset)
    while length:
        data = source.read(min(length, 1024 * 1024))
        if not data:
            raise ValueError('source became truncated during extraction')
        destination.write(data)
        length -= len(data)


def extract(source, destination, start, end):
    source, destination = Path(source), Path(destination)
    if source.resolve() == destination.resolve() or destination.exists():
        raise FileExistsError(f'refusing to overwrite {destination}')
    if start < 1 or end < start:
        raise ValueError('expected 1 <= start <= end')
    with source.open('rb') as src:
        result = scan(src)
        by_number = {f.number: f for f in result.frames}
        # Include leading/trailing context, but do not claim complete worker
        # scopes: asynchronous samples may cross either boundary or span many
        # frames. Exact timing validation applies to main-thread intervals only.
        if any(n not in by_number for n in (start, end, end + 1, end + 2)):
            raise ValueError('range needs two subsequent frame markers; cannot extract final two frames')
        leading = start - 1 if start - 1 in by_number else start
        begin, finish = by_number[leading].offset, by_number[end + 2].offset
        metadata = [b for b in result.globals if b.offset < begin]
        expected = 56 + sum(b.size for b in metadata) + finish - begin
        if shutil.disk_usage(destination.parent).free < expected + 256 * 1024 * 1024:
            raise OSError(f'insufficient space: extraction requires {expected} bytes plus 256 MiB reserve')
        created = False
        try:
            with destination.open('xb') as dst:
                created = True
                copy_range(src, dst, 0, 56)
                for block in metadata:
                    copy_range(src, dst, block.offset, block.size)
                copy_range(src, dst, begin, finish - begin)
            with destination.open('rb') as dst:
                checked = scan(dst)
            if [f.number for f in checked.frames] != [f.number for f in result.frames if leading <= f.number <= end + 1]:
                raise ValueError('extracted frame sequence differs from source')
        except BaseException:
            if created:
                destination.unlink(missing_ok=True)
            raise
    return {'requested_first': start, 'requested_last': end,
            'leading_context_frame': leading if leading < start else None,
            'trailing_context_frame': end + 1, 'bytes': expected,
            'limitations': 'both boundaries and cross-boundary worker/render scopes may be incomplete; exact validation is for requested main-thread intervals only',
            'source_first_timestamp_ns': by_number[start].time_ns,
            'source_offset_begin': begin, 'source_offset_end_exclusive': finish}


def analyze(source, output):
    output = Path(output)
    if output.exists():
        raise FileExistsError(f'refusing to overwrite report directory {output}')
    with Path(source).open('rb') as stream:
        result = scan(stream)
    if len(result.frames) < 2:
        raise ValueError('capture contains fewer than two complete frame markers')
    summary = summarize(result.frames)
    summary.update(source=str(Path(source).resolve()), bytes=result.byte_count,
                   blocks=result.block_count, unity_version=list(result.unity_version),
                   first_frame=result.frames[0].number, last_frame=result.frames[-1].number,
                   first_timestamp_ns=result.frames[0].time_ns,
                   measurement='main-thread frame-boundary intervals; not display presentation or GPU duration')
    output.mkdir(parents=True)
    with (output / 'frames.csv').open('x', newline='') as file:
        writer = csv.writer(file)
        writer.writerow(['frame', 'seconds_from_first_marker', 'interval_ms', 'source_offset', 'timestamp_ns'])
        for frame, ms in intervals(result.frames):
            writer.writerow([frame.number, (frame.time_ns-result.frames[0].time_ns)/1e9, ms, frame.offset, frame.time_ns])
    with (output / 'slowest.csv').open('x', newline='') as file:
        writer = csv.writer(file)
        writer.writerow(['frame', 'seconds_from_first_marker', 'interval_ms'])
        for frame, ms in sorted(intervals(result.frames), key=lambda entry: entry[1], reverse=True)[:100]:
            writer.writerow([frame.number, (frame.time_ns-result.frames[0].time_ns)/1e9, ms])
    (output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    return summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    report = commands.add_parser('analyze')
    report.add_argument('source', type=Path)
    report.add_argument('output', type=Path, help='new report directory')
    part = commands.add_parser('extract')
    part.add_argument('source', type=Path)
    part.add_argument('destination', type=Path, help='new .raw file')
    part.add_argument('--start', type=int, required=True, help='original frame number, inclusive')
    part.add_argument('--end', type=int, required=True, help='original frame number, inclusive')
    args = parser.parse_args()
    try:
        result = analyze(args.source, args.output) if args.command == 'analyze' else extract(args.source, args.destination, args.start, args.end)
        print(json.dumps(result, indent=2))
    except (OSError, ValueError) as error:
        parser.exit(1, f'failed to {args.command} capture: {error}\n')


if __name__ == '__main__':
    main()
