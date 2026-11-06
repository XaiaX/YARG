# pattern: Functional Core
"""Conservative Unity raw framing and main-thread cadence calculations.

Framing reference: wotakuro/ProfilerBinarylogSplit (MIT, Yusuke Kurokawa).
See profiler_raw.LICENSE.txt. This implements only the observed unaligned,
20220328 protocol; it does not decode arbitrary profiler sample messages.
"""
from dataclasses import dataclass
import math
import statistics
import struct

GLOBAL_THREAD = 2**64 - 1


@dataclass(frozen=True)
class Frame:
    number: int
    offset: int
    time_ns: int


@dataclass(frozen=True)
class Block:
    offset: int
    sequence: int
    thread: int
    payload_size: int

    @property
    def size(self):
        return self.payload_size + 28


def parse_header(data):
    if (len(data) != 56 or data[:6] != b'PD3U\x01\x00'
            or struct.unpack_from('<I', data, 8)[0] != 0x20220328):
        raise ValueError('unsupported raw header: expected little-endian, unaligned 20220328 protocol')
    if struct.unpack_from('<QQ', data, 12) != (1, 1):
        raise ValueError('unsupported timestamp conversion: expected nanoseconds (1/1)')
    return struct.unpack_from('<Q', data, 28)[0], struct.unpack_from('<5I', data, 36)


def parse_block(data, offset, file_size):
    if len(data) != 20:
        raise ValueError(f'truncated block header at byte {offset}')
    signature, sequence, thread, length = struct.unpack('<IIQI', data)
    if signature != 0xb10c7ead or length > 16 * 1024 * 1024 or offset + length + 28 > file_size:
        raise ValueError(f'invalid or truncated block at byte {offset}')
    return Block(offset, sequence, thread, length)


def check_footer(data, block):
    if len(data) != 8:
        raise ValueError(f'truncated footer at byte {block.offset}')
    sequence, signature = struct.unpack('<II', data)
    if signature != 0xb10cf007 or sequence != (block.sequence + 1) % 2**32:
        raise ValueError(f'invalid footer at byte {block.offset}')


def parse_frame(data, block, main_thread):
    # Frame markers occupy their own 14-byte block. A continuation payload can
    # happen to begin with 34; never interpret that as a frame boundary.
    if block.thread != main_thread or block.payload_size != 14 or len(data) != 14:
        return None
    kind, number, timestamp = struct.unpack('<HIQ', data)
    return Frame(number, block.offset, timestamp) if kind == 34 else None


def intervals(frames):
    for left, right in zip(frames, frames[1:]):
        if right.number <= left.number or right.time_ns <= left.time_ns:
            raise ValueError('frame numbers and timestamps must strictly increase')
        if right.number == left.number + 1:
            yield left, (right.time_ns - left.time_ns) / 1_000_000


def summarize(frames):
    times = sorted(ms for _, ms in intervals(frames))
    missing = sum(right.number - left.number - 1 for left, right in zip(frames, frames[1:]))
    result = {'frame_markers': len(frames), 'interval_count': len(times),
              'missing_frame_numbers': missing,
              'duration_seconds': (frames[-1].time_ns - frames[0].time_ns) / 1e9 if frames else 0}
    if not times:
        return result
    result.update(average_fps=1000 * len(times) / sum(times), median_ms=statistics.median(times),
                  p95_ms=times[math.ceil(len(times)*.95)-1], p99_ms=times[math.ceil(len(times)*.99)-1],
                  worst_ms=times[-1])
    for limit in (20, 25, 33.34, 50, 100, 250):
        result[f'over_{limit}_ms'] = sum(t > limit for t in times)
    return result
