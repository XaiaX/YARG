// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace YARG.Persistent
{
    [Serializable]
    public struct CpuPhaseDiagnosticSample
    {
        public bool Enabled;
        public int Frame, AvailableMask, SampledMask;
        public long InputUpdateNanoseconds, NewInputUpdateNanoseconds, LegacyInputUpdateNanoseconds;
        public long PlayerConnectionNanoseconds, PollPlayerConnectionNanoseconds, ProfilerMemoryNanoseconds;
        public long BehaviourUpdateNanoseconds, BehaviourLateUpdateNanoseconds, DelayedTasksNanoseconds;
        public long FinishFrameRenderingNanoseconds, UpdateAllRenderersNanoseconds;
        public long UrpRenderNanoseconds, UrpMainCameraNanoseconds,
            RenderGraphCompileNanoseconds, RenderGraphExecuteNanoseconds, RenderContextSubmitNanoseconds;
        public bool MainAllocatedBytesAvailable, MainAllocatedBytesSampled;
        public long MainAllocatedBytes;
        public int AllocationStartFrame, AllocationEndFrame;
        public long AllocationStartTicks, AllocationEndTicks;
    }

    /// <summary>
    /// Opt-in main-thread phase attribution. Construct, read and reset on the Unity main thread.
    /// Native timings describe the previous native interval; allocation deltas span Read calls.
    /// </summary>
    public sealed class CpuPhaseDiagnostics : IDisposable
    {
        private static readonly string[] METRICS =
        {
            "InputUpdate",
            "PreUpdate.NewInputUpdate",
            "EarlyUpdate.UpdateInputManager",
            "PlayerConnection.Poll",
            "EarlyUpdate.PollPlayerConnection",
            "Profiler.FlushMemoryCounters",
            "Update.ScriptRunBehaviourUpdate",
            "PreLateUpdate.ScriptRunBehaviourLateUpdate",
            "Update.ScriptRunDelayedTasks",
            "PostLateUpdate.FinishFrameRendering",
            "PostLateUpdate.UpdateAllRenderers",
            "Inl_UniversalRenderTotal",
            "Inl_UniversalRenderPipeline.RenderSingleCameraInternal: Main Camera",
            "Inl_CompileRenderGraph",
            "Inl_ExecuteRenderGraph",
            "Inl_ScriptableRenderContext.Submit"
        };

        private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[METRICS.Length];
        private readonly int _ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly bool _allocationAvailable;
        private int _startedFrame;
        private bool _disposed, _hasAllocationBaseline;
        private int _lastAllocationFrame;
        private long _lastAllocatedBytes, _lastAllocationTicks;
        public string Capabilities { get; }

        [Serializable]
        private sealed class Description
        {
            public bool developmentBuild, profilerEnabled, mainAllocatedBytesAvailable;
            public long allocationTimerFrequency;
            public int allocationProbeBytes;
            public long allocationProbeObservedBytes;
            public string nativeWindow = "previous completed native recording interval; Frame = Time.frameCount - 1";
            public string allocationWindow = "current-main-thread probe-to-probe Read delta; not the native interval; first and reset samples omitted";
            public string timingSemantics = "inclusive main-thread nanoseconds; nested scopes overlap, do not sum; missing metrics remain unavailable";
            public string allocationApi = "GC.GetAllocatedBytesForCurrentThread";
            public string allocationAvailabilityReason;
            public string[] metrics, relatedAvailableMarkers;
        }

        public CpuPhaseDiagnostics()
        {
            _startedFrame = Time.frameCount;
            var descriptions = new string[METRICS.Length];
            for (int i = 0; i < METRICS.Length; i++)
                descriptions[i] = $"bit {i}: {METRICS[i]} unavailable";
            var related = new List<string>();
            var handles = new List<ProfilerRecorderHandle>();
            try
            {
                ProfilerRecorderHandle.GetAvailable(handles);
                foreach (var handle in handles)
                {
                    var info = ProfilerRecorderHandle.GetDescription(handle);
                    if (info.Name.Contains("Input") || info.Name.Contains("PlayerConnection") ||
                        info.Name.StartsWith("Profiler.", StringComparison.Ordinal) ||
                        info.Name.StartsWith("Update.", StringComparison.Ordinal) ||
                        info.Name.StartsWith("PreLateUpdate.", StringComparison.Ordinal) ||
                        info.Name.StartsWith("PostLateUpdate.", StringComparison.Ordinal) ||
                        info.Name.Contains("RenderPipeline"))
                        related.Add(info.Name + ": " + info.UnitType);
                    if (info.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                    for (int i = 0; i < METRICS.Length; i++)
                    {
                        if (info.Name != METRICS[i] || _recorders[i].Valid) continue;
                        _recorders[i] = new ProfilerRecorder(handle, 1,
                            ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.Default |
                            ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                        descriptions[i] = $"bit {i}: {info.Name}; nanoseconds; main thread; valid={_recorders[i].Valid}";
                        break;
                    }
                }

                const int ALLOCATION_PROBE_BYTES = 8192;
                string allocationReason;
                long observedBytes = 0;
                try
                {
                    // Some Unity/Mono backends expose this API as a nonthrowing zero-returning stub.
                    // Validate it once at startup; never advertise those zeroes as measured bytes.
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    var probe = new byte[ALLOCATION_PROBE_BYTES];
                    long after = GC.GetAllocatedBytesForCurrentThread();
                    GC.KeepAlive(probe);
                    observedBytes = after - before;
                    _allocationAvailable = before >= 0 && observedBytes >= ALLOCATION_PROBE_BYTES;
                    allocationReason = _allocationAvailable ? "observed probe allocation" :
                        "allocation counter did not observe probe allocation";
                }
                catch (Exception e) when (e is NotSupportedException || e is NotImplementedException ||
                    e is MissingMethodException)
                {
                    _allocationAvailable = false;
                    allocationReason = e.GetType().Name;
                }
                Capabilities = JsonUtility.ToJson(new Description
                {
                    developmentBuild = UnityEngine.Debug.isDebugBuild,
                    profilerEnabled = Profiler.enabled,
                    mainAllocatedBytesAvailable = _allocationAvailable,
                    allocationTimerFrequency = Stopwatch.Frequency,
                    allocationProbeBytes = ALLOCATION_PROBE_BYTES,
                    allocationProbeObservedBytes = observedBytes,
                    allocationAvailabilityReason = allocationReason,
                    metrics = descriptions,
                    relatedAvailableMarkers = related.ToArray()
                }, true);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public CpuPhaseDiagnosticSample Read()
        {
            if (_disposed) return default;
            CheckThread();
            int frame = Time.frameCount;
            var sample = new CpuPhaseDiagnosticSample { Enabled = true, Frame = frame - 1 };
            ReadAllocation(frame, ref sample);
            sample.InputUpdateNanoseconds = ReadMetric(0, frame, ref sample);
            sample.NewInputUpdateNanoseconds = ReadMetric(1, frame, ref sample);
            sample.LegacyInputUpdateNanoseconds = ReadMetric(2, frame, ref sample);
            sample.PlayerConnectionNanoseconds = ReadMetric(3, frame, ref sample);
            sample.PollPlayerConnectionNanoseconds = ReadMetric(4, frame, ref sample);
            sample.ProfilerMemoryNanoseconds = ReadMetric(5, frame, ref sample);
            sample.BehaviourUpdateNanoseconds = ReadMetric(6, frame, ref sample);
            sample.BehaviourLateUpdateNanoseconds = ReadMetric(7, frame, ref sample);
            sample.DelayedTasksNanoseconds = ReadMetric(8, frame, ref sample);
            sample.FinishFrameRenderingNanoseconds = ReadMetric(9, frame, ref sample);
            sample.UpdateAllRenderersNanoseconds = ReadMetric(10, frame, ref sample);
            sample.UrpRenderNanoseconds = ReadMetric(11, frame, ref sample);
            sample.UrpMainCameraNanoseconds = ReadMetric(12, frame, ref sample);
            sample.RenderGraphCompileNanoseconds = ReadMetric(13, frame, ref sample);
            sample.RenderGraphExecuteNanoseconds = ReadMetric(14, frame, ref sample);
            sample.RenderContextSubmitNanoseconds = ReadMetric(15, frame, ref sample);
            return sample;
        }

        private long ReadMetric(int index, int frame, ref CpuPhaseDiagnosticSample sample)
        {
            var recorder = _recorders[index];
            if (!recorder.Valid || !recorder.IsRunning) return 0;
            sample.AvailableMask |= 1 << index;
            if (frame <= _startedFrame + 1 || recorder.Count == 0) return 0;
            sample.SampledMask |= 1 << index;
            return recorder.LastValue;
        }

        private void ReadAllocation(int frame, ref CpuPhaseDiagnosticSample sample)
        {
            sample.MainAllocatedBytesAvailable = _allocationAvailable;
            if (!_allocationAvailable || (_hasAllocationBaseline && frame == _lastAllocationFrame)) return;
            long ticks = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            if (_hasAllocationBaseline && frame > _lastAllocationFrame && allocated >= _lastAllocatedBytes)
            {
                sample.MainAllocatedBytesSampled = true;
                sample.MainAllocatedBytes = allocated - _lastAllocatedBytes;
                sample.AllocationStartFrame = _lastAllocationFrame;
                sample.AllocationEndFrame = frame;
                sample.AllocationStartTicks = _lastAllocationTicks;
                sample.AllocationEndTicks = ticks;
            }
            // A backwards frame or counter starts a new window instead of emitting a false delta.
            _hasAllocationBaseline = allocated >= 0;
            _lastAllocatedBytes = allocated;
            _lastAllocationFrame = frame;
            _lastAllocationTicks = ticks;
        }

        /// <summary>Discard partial windows when a capture is restarted or its measurement boundary changes.</summary>
        public void Reset()
        {
            if (_disposed) return;
            CheckThread();
            _startedFrame = Time.frameCount;
            _hasAllocationBaseline = false;
            for (int i = 0; i < _recorders.Length; i++)
            {
                if (!_recorders[i].Valid) continue;
                // Reset stops collection as well as clearing samples; resume before the next native frame.
                _recorders[i].Reset();
                _recorders[i].Start();
            }
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread)
                throw new InvalidOperationException("CPU phase diagnostics must be read and reset on their creating Unity main thread.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _hasAllocationBaseline = false;
            for (int i = 0; i < _recorders.Length; i++) _recorders[i].Dispose();
        }
    }
}
