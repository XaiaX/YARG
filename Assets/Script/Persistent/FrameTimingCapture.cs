// pattern: Imperative Shell
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using YARG.Core.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YARG.Persistent
{
    /// <summary>Bounded, non-blocking handoff. Formatting and disk writes run off the game thread.</summary>
    public sealed class FrameTimingCapture
    {
        private readonly BlockingCollection<FrameDiagnosticSample> _pending = new(4096);
        private int _droppedRows;
        private volatile string _error;
        public string Path { get; }
        public string Error => _error;
        public int DroppedRows => Volatile.Read(ref _droppedRows);
        public Task Completion { get; }

        public FrameTimingCapture(string path)
        {
            Path = path;
            Completion = Task.Run(Write);
        }

        // Called only by the main-thread owner; never blocks on disk or a full queue.
        public void Add(FrameDiagnosticSample sample)
        {
            if (_error != null || !_pending.TryAdd(sample)) Interlocked.Increment(ref _droppedRows);
        }

        public void Complete() => _pending.CompleteAdding();

        private void Write()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                using var writer = new StreamWriter(Path, false, new UTF8Encoding(false), 65536);
                writer.WriteLine("schema,frame,monotonicSeconds,wallMs,unityMs,scene,focused,quality,requestedVsync,requestedFps,backgroundFps,effectiveVsync,effectiveTargetFps,width,height,refreshHz,graphicsApi,budgetMs,timingEnabled,timingAvailable,timingFresh,timingFrameStartTimestamp,cpuMs,mainMs,renderMs,presentWaitMs,gpuMs,gc0,gc1,gc2,visualTime,visualDelta,songSpeed,pacingChanged,droppedRows,intervalStartFrame,targetedEnabled,targetedChanged,targetedGeneration,rollScheduleCalls,rollScheduleMs,rollScheduleMaxMs,rollResolveCalls,rollResolveMs,rollResolveMaxMs,laneMapBuildCalls,laneMapBuildMs,laneMapBuildMaxMs,hiHatActivationCalls,hiHatActivationMs,hiHatActivationMaxMs,hiHatDiscoveryCalls,hiHatDiscoveryMs,hiHatDiscoveryMaxMs,poolPrewarmCalls,poolPrewarmMs,poolPrewarmMaxMs,poolCreateCalls,poolCreateMs,poolCreateMaxMs,scheduleNotesExamined,resolveNotesExamined,laneLookups,laneMembers,deadlinesQueued,continuations");
                var row = new StringBuilder(512);
                int sinceFlush = 0;
                foreach (var s in _pending.GetConsumingEnumerable())
                {
                    row.Clear();
                    row.Append("3,");
                    Add(row, s.Frame); Add(row, s.Time); Add(row, s.WallMs); Add(row, s.UnityMs);
                    Text(row, s.Scene); Add(row, s.Focused); Add(row, s.Quality); Add(row, s.RequestedVSync);
                    Add(row, s.RequestedFps); Add(row, s.BackgroundFps); Add(row, s.VSync); Add(row, s.TargetFps);
                    Add(row, s.Width); Add(row, s.Height); Add(row, s.RefreshHz); Text(row, s.GraphicsApi); Add(row, s.BudgetMs);
                    Add(row, s.TimingEnabled); Add(row, s.HasTiming); Add(row, s.FreshTiming);
                    Optional(row, s.HasTiming, s.TimingTimestamp);
                    Optional(row, s.HasTiming, s.CpuMs); Optional(row, s.HasTiming, s.MainMs);
                    Optional(row, s.HasTiming, s.RenderMs); Optional(row, s.HasTiming, s.PresentWaitMs);
                    Optional(row, s.HasGpu, s.GpuMs);
                    Add(row, s.GC0); Add(row, s.GC1); Add(row, s.GC2);
                    Optional(row, s.HasGameplay, s.VisualTime); Optional(row, s.HasGameplay, s.VisualDelta);
                    Optional(row, s.HasGameplay, s.SongSpeed);
                    Add(row, s.PacingChanged);
                    Add(row, DroppedRows);
                    Add(row, s.IntervalStartFrame);
                    Add(row, s.Targeted.Enabled); Add(row, s.TargetedChanged); Add(row, s.Targeted.Generation);
                    AppendTargeted(row, s.Targeted);
                    writer.WriteLine(row);
                    if (++sinceFlush >= 120) { writer.Flush(); sinceFlush = 0; }
                }
                writer.WriteLine($"# capture complete; droppedRows={DroppedRows}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                _error = e.Message;
            }
        }

        private static void AppendTargeted(StringBuilder row, TargetedPerformanceSnapshot s)
        {
            Timing(row, s.Enabled, s.RollSchedule); Timing(row, s.Enabled, s.RollResolve);
            Timing(row, s.Enabled, s.LaneMapBuild); Timing(row, s.Enabled, s.HiHatActivation);
            Timing(row, s.Enabled, s.HiHatDiscovery); Timing(row, s.Enabled, s.PoolPrewarm);
            Timing(row, s.Enabled, s.PoolCreate);
            Optional(row, s.Enabled, s.ScheduleNotesExamined); Optional(row, s.Enabled, s.ResolveNotesExamined);
            Optional(row, s.Enabled, s.LaneLookups); Optional(row, s.Enabled, s.LaneMembers);
            Optional(row, s.Enabled, s.DeadlinesQueued);
            if (s.Enabled) row.Append(s.Continuations.ToString(CultureInfo.InvariantCulture));
        }

        private static void Timing(StringBuilder row, bool enabled, TargetedTiming value)
        {
            const double MILLISECONDS_PER_SECOND = 1000.0;
            Optional(row, enabled, value.Calls);
            Optional(row, enabled, value.ElapsedTicks * (MILLISECONDS_PER_SECOND / Stopwatch.Frequency));
            Optional(row, enabled, value.MaxTicks * (MILLISECONDS_PER_SECOND / Stopwatch.Frequency));
        }

        private static void Add<T>(StringBuilder row, T value) where T : IConvertible
            => row.Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');
        private static void Optional<T>(StringBuilder row, bool present, T value) where T : IConvertible
        {
            if (present) row.Append(value.ToString(CultureInfo.InvariantCulture));
            row.Append(',');
        }
        private static void Text(StringBuilder row, string value)
            => row.Append('"').Append((value ?? string.Empty).Replace("\"", "\"\"")).Append("\",");
    }
}
