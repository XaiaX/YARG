// pattern: Functional Core
using YARG.Core.Diagnostics;

namespace YARG.Persistent
{
    /// <summary>Value snapshot; delayed FrameTiming data is identified separately from this update.</summary>
    public struct FrameDiagnosticSample
    {
        public int Frame, Quality, VSync, TargetFps, RequestedFps, BackgroundFps, Width, Height;
        public int GC0, GC1, GC2;
        public bool Focused, RequestedVSync, TimingEnabled, HasTiming, FreshTiming, HasGpu, HasGameplay, PacingChanged;
        public string Scene, GraphicsApi;
        public double Time, WallMs, UnityMs, RefreshHz, BudgetMs, VisualTime, VisualDelta, SongSpeed;
        public ulong TimingTimestamp;
        public int IntervalStartFrame;
        public bool TargetedChanged;
        public TargetedPerformanceSnapshot Targeted;
        public GcDiagnosticSample GcDiagnostics;
        public RenderDiagnosticSample RenderDiagnostics;
        public CpuPhaseDiagnosticSample CpuPhases;
        public ulong CpuTimerFrequency, PresentCalledTimestamp, FrameCompleteTimestamp, FirstSubmitTimestamp;
        public double CpuMs, MainMs, RenderMs, PresentWaitMs, GpuMs;
    }
}
