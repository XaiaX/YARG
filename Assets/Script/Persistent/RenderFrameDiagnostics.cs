// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace YARG.Persistent
{
    [Serializable]
    public struct RenderDiagnosticSample
    {
        public bool Enabled;
        public int Frame, AvailableMask, SampledMask;
        public long MainThreadNanoseconds, RenderThreadNanoseconds, PlayerLoopNanoseconds;
        public long PresentWaitNanoseconds, TargetFpsWaitNanoseconds, PresentNanoseconds;
        public long RenderCommandsWaitNanoseconds, RenderThreadWaitNanoseconds, FrameStartWaitNanoseconds;
        public long ProcessCommandsNanoseconds;
    }

    /// <summary>Inclusive native timings, not GPU or display presentation timestamps.</summary>
    public sealed class RenderFrameDiagnostics : IDisposable
    {
        private static readonly (string Name, string Alias, bool MainThread)[] METRICS =
        {
            ("Main Thread", null, false),
            ("Render Thread", null, false),
            ("PlayerLoop", null, true),
            ("Gfx.WaitForPresentOnGfxThread", null, true),
            ("WaitForTargetFPS", null, true),
            ("Gfx.PresentFrame", null, false),
            ("Gfx.WaitForCommands", "Gfx.WaitForGfxCommandsFromMainThread", false),
            ("Gfx.WaitForRenderThread", null, true),
            ("WaitForLastPresentationAndUpdateTime", "TimeUpdate.WaitForLastPresentationAndUpdateTime", true),
            ("Gfx.ProcessCommands", null, false)
        };
        private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[METRICS.Length];
        private readonly int[] _startedFrames = new int[METRICS.Length];
        private readonly List<ProfilerRecorderHandle> _handles = new();
        private readonly string[] _descriptions = new string[METRICS.Length];
        private readonly List<string> _related = new();
        private bool _disposed;
        public string Capabilities { get; private set; }
        public bool Detailed { get; private set; }

        [Serializable]
        private sealed class Description
        {
            public bool developmentBuild, profilerEnabled, graphicsMultiThreaded;
            public string graphicsApi;
            public int maxQueuedFrames;
            public string valueFrame = "Previous completed native recording interval; render work can belong to a queued frame. Inclusive/all-thread timings overlap; do not sum.";
            public string[] metrics, relatedAvailableMarkers;
        }

        public RenderFrameDiagnostics() : this(true) { }

        public RenderFrameDiagnostics(bool detailed)
        {
            ProfilerRecorderHandle.GetAvailable(_handles);
            foreach (var handle in _handles)
            {
                var info = ProfilerRecorderHandle.GetDescription(handle);
                if (info.Name.StartsWith("Gfx.", StringComparison.Ordinal) ||
                    info.Name.Contains("Present") || info.Name.Contains("WaitForTarget") ||
                    info.Name == "Main Thread" || info.Name == "Render Thread")
                    _related.Add(info.Name + ": " + info.UnitType);
            }
            try
            {
                StartMetric(0);
                for (int i = 1; i < METRICS.Length; i++)
                    _descriptions[i] = $"bit {i}: {METRICS[i].Name} disabled";
                if (detailed) SetDetailed(true);
                else UpdateCapabilities();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // Main Thread stays running when targeted diagnostics change, so the graph
        // never loses its source merely because extra instrumentation was toggled.
        public void SetDetailed(bool detailed)
        {
            if (_disposed || Detailed == detailed) return;
            Detailed = detailed;
            for (int i = 1; i < METRICS.Length; i++)
            {
                if (detailed) StartMetric(i);
                else
                {
                    _recorders[i].Dispose();
                    _recorders[i] = default;
                    _descriptions[i] = $"bit {i}: {METRICS[i].Name} disabled";
                }
            }
            UpdateCapabilities();
        }

        private void StartMetric(int index)
        {
            var metric = METRICS[index];
            _startedFrames[index] = Time.frameCount;
            _descriptions[index] = $"bit {index}: {metric.Name} unavailable";
            foreach (var handle in _handles)
            {
                var info = ProfilerRecorderHandle.GetDescription(handle);
                if ((info.Name != metric.Name && info.Name != metric.Alias) ||
                    info.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                var options = ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.Default;
                if (metric.MainThread) options |= ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                _recorders[index] = new ProfilerRecorder(handle, 1, options);
                _descriptions[index] = $"bit {index}: {info.Name}; nanoseconds; " +
                    $"{(metric.MainThread ? "main thread" : "all threads")}; valid={_recorders[index].Valid}";
                break;
            }
        }

        private void UpdateCapabilities()
        {
            Capabilities = JsonUtility.ToJson(new Description
            {
                developmentBuild = Debug.isDebugBuild, profilerEnabled = Profiler.enabled,
                graphicsMultiThreaded = SystemInfo.graphicsMultiThreaded,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                maxQueuedFrames = QualitySettings.maxQueuedFrames,
                metrics = _descriptions, relatedAvailableMarkers = _related.ToArray()
            }, true);
        }

        public RenderDiagnosticSample Read()
        {
            if (_disposed) return default;
            var s = new RenderDiagnosticSample { Enabled = true, Frame = Time.frameCount - 1 };
            s.MainThreadNanoseconds = Read(0, ref s);
            s.RenderThreadNanoseconds = Read(1, ref s);
            s.PlayerLoopNanoseconds = Read(2, ref s);
            s.PresentWaitNanoseconds = Read(3, ref s);
            s.TargetFpsWaitNanoseconds = Read(4, ref s);
            s.PresentNanoseconds = Read(5, ref s);
            s.RenderCommandsWaitNanoseconds = Read(6, ref s);
            s.RenderThreadWaitNanoseconds = Read(7, ref s);
            s.FrameStartWaitNanoseconds = Read(8, ref s);
            s.ProcessCommandsNanoseconds = Read(9, ref s);
            return s;
        }

        private long Read(int index, ref RenderDiagnosticSample sample)
        {
            var recorder = _recorders[index];
            if (!recorder.Valid || !recorder.IsRunning) return 0;
            sample.AvailableMask |= 1 << index;
            if (Time.frameCount <= _startedFrames[index] + 1 || recorder.Count == 0) return 0;
            sample.SampledMask |= 1 << index;
            return recorder.LastValue;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _recorders.Length; i++) _recorders[i].Dispose();
        }
    }
}
