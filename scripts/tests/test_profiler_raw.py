# pattern: Imperative Shell
"""Real binary fixtures exercise bounded I/O and fail-closed validation."""
import io
from pathlib import Path
import struct
import sys
import tempfile
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import profiler_raw


def header():
    data = bytearray(56)
    data[:6] = b'PD3U\x01\x00'
    struct.pack_into('<I', data, 8, 0x20220328)
    struct.pack_into('<QQ', data, 12, 1, 1)
    struct.pack_into('<Q', data, 28, 123)
    struct.pack_into('<5I', data, 36, 6000, 3, 5, 2, 2)
    return bytes(data)


def block(thread, payload, sequence=0):
    return (struct.pack('<IIQI', 0xb10c7ead, sequence, thread, len(payload))
            + payload + struct.pack('<II', sequence + 1, 0xb10cf007))


def fixture():
    return header() + block(2**64-1, b'metadata') + b''.join(
        block(123, struct.pack('<HIQ', 34, frame, ns))
        + block(123, b'sample data')
        for frame, ns in enumerate([1000000000, 1010000000, 1030000000, 1040000000, 1060000000], 1))


class CaptureTests(unittest.TestCase):
    def test_frame_timestamps_keep_nanosecond_precision(self):
        result = profiler_raw.scan(io.BytesIO(fixture()))
        self.assertEqual([f.number for f in result.frames], [1, 2, 3, 4, 5])
        self.assertEqual([f.time_ns for f in result.frames], [1000000000, 1010000000, 1030000000, 1040000000, 1060000000])

    def test_rejects_truncated_packet(self):
        with self.assertRaises(ValueError): profiler_raw.scan(io.BytesIO(fixture()[:-1]))

    def test_rejects_wrong_signature_or_version(self):
        for offset in [0, 8, 56]:
            data = bytearray(fixture()); data[offset] ^= 0xff
            with self.subTest(offset=offset), self.assertRaises(ValueError):
                profiler_raw.scan(io.BytesIO(data))

    def test_rejects_other_timestamp_units(self):
        data = bytearray(fixture())
        struct.pack_into('<QQ', data, 12, 1000, 3)
        with self.assertRaises(ValueError): profiler_raw.scan(io.BytesIO(data))

    def test_rejects_damaged_footer(self):
        data = bytearray(fixture()); data[-1] ^= 0xff
        with self.assertRaises(ValueError): profiler_raw.scan(io.BytesIO(data))

    def test_nonframe_payload_is_not_a_frame(self):
        data = header() + block(123, struct.pack('<HIQ', 34, 999, 10) + b'continuation')
        self.assertEqual(profiler_raw.scan(io.BytesIO(data)).frames, [])

    def test_missing_frames_are_reported_not_scored_as_one_slow_frame(self):
        data = header() + block(123, struct.pack('<HIQ',34,1,1000000000)) + block(123,struct.pack('<HIQ',34,4,1060000000))
        result = profiler_raw.scan(io.BytesIO(data))
        summary = profiler_raw.summarize(result.frames)
        self.assertIn('missing_frame_numbers', summary)
        self.assertEqual(summary['missing_frame_numbers'], 2)
        self.assertEqual(summary['interval_count'], 0)

    def test_source_and_existing_destination_are_never_overwritten(self):
        with tempfile.TemporaryDirectory() as tmp:
            source=Path(tmp)/'source.raw'; source.write_bytes(fixture())
            target=Path(tmp)/'exists.raw'; target.write_bytes(b'keep me')
            for path in [source,target]:
                with self.assertRaises((ValueError,FileExistsError)):
                    profiler_raw.extract(source,path,2,3)
            self.assertEqual(source.read_bytes(),fixture())
            self.assertEqual(target.read_bytes(),b'keep me')

    def test_slice_preserves_metadata_and_requested_frames(self):
        with tempfile.TemporaryDirectory() as tmp:
            source=Path(tmp)/'source.raw'; source.write_bytes(fixture())
            target=Path(tmp)/'slice.raw'
            profiler_raw.extract(source,target,2,3)
            self.assertTrue(target.exists(), 'slice must be created')
            result=profiler_raw.scan(io.BytesIO(target.read_bytes()))
            self.assertEqual([f.number for f in result.frames],[1,2,3,4])
            self.assertIn(b'metadata',target.read_bytes())
            self.assertEqual(source.read_bytes(),fixture())

    def test_scan_never_reads_sample_payload(self):
        class BoundedRead(io.BytesIO):
            def read(self,size=-1):
                if size < 0 or size > 56: raise AssertionError('unbounded read')
                return super().read(size)
        self.assertEqual(len(profiler_raw.scan(BoundedRead(fixture())).frames),5)

    def test_statistics_expose_uneven_sixty_fps_cadence(self):
        frames=[profiler_raw.Frame(i,i,ns) for i,ns in enumerate([0,8333333,33333333,41666666,66666666])]
        result=profiler_raw.summarize(frames)
        self.assertIn('average_fps', result)
        self.assertAlmostEqual(result['average_fps'],60,places=4)
        self.assertAlmostEqual(result['p95_ms'],25,places=4)
        self.assertEqual(result['over_20_ms'],2)

if __name__ == '__main__': unittest.main()
