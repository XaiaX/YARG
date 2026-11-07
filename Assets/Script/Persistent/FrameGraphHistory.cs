// pattern: Functional Core
using System;

namespace YARG.Persistent
{
    public enum FrameGraphSource { None, Native, UnityDelta }

    /// <summary>Frame durations only; late-Update scheduling intervals remain in the separate probe history.</summary>
    public sealed class FrameGraphHistory
    {
        public double[] History { get; } = new double[StutterProbe.HISTORY_SIZE];
        public int Count { get; private set; }
        public int Head { get; private set; }
        public int Revision { get; private set; }
        public FrameGraphSource Source { get; private set; }
        public double LatestMs { get; private set; }
        private int _lastFrame = -1;

        public void Add(FrameDiagnosticSample sample)
        {
            // Both native and Unity delta describe a completed frame. Repeated reads must
            // not give that frame extra weight, even if the available source changes.
            if (sample.Frame <= _lastFrame) return;
            _lastFrame = sample.Frame;
            if (sample.PacingChanged)
            {
                Clear();
                return;
            }
            var native = sample.RenderDiagnostics;
            bool hasNative = native.Enabled && (native.AvailableMask & native.SampledMask & 1) != 0 &&
                native.Frame == sample.Frame - 1 && native.MainThreadNanoseconds > 0;
            double ms = hasNative ? native.MainThreadNanoseconds / 1_000_000.0 : sample.UnityMs;
            if (ms <= 0 || double.IsNaN(ms) || double.IsInfinity(ms))
            {
                Clear();
                return;
            }
            var source = hasNative ? FrameGraphSource.Native : FrameGraphSource.UnityDelta;
            if (source != Source) Clear();
            Source = source;
            History[Head] = LatestMs = ms;
            Head = (Head + 1) % History.Length;
            Count = Math.Min(Count + 1, History.Length);
            Revision++;
        }

        public void Reset()
        {
            _lastFrame = -1;
            Clear();
        }

        private void Clear()
        {
            Count = Head = 0;
            Source = FrameGraphSource.None;
            LatestMs = 0;
            Revision++;
        }
    }
}
