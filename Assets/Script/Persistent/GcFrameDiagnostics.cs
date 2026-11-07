// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Scripting;

namespace YARG.Persistent
{
    [Serializable]
    public struct GcDiagnosticSample
    {
        public bool Enabled;
        public int Frame, AvailableMask, SampledMask;
        public long AllocatedBytes, MainAllocationCalls, UsedBytes, ReservedBytes;
        public long IncrementalNanoseconds, CollectNanoseconds;
    }

    /// <summary>Opt-in native recorders. Values belong to the previous completed Unity frame.</summary>
    public sealed class GcFrameDiagnostics : IDisposable
    {
        private static readonly string[] NAMES = { "GC Allocated In Frame", "GC.Alloc", "GC Used Memory",
            "GC Reserved Memory", "GarbageCollector.CollectIncremental", "GC.Collect" };
        private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[NAMES.Length];
        private readonly int _startedFrame;
        private bool _disposed;
        public string Capabilities { get; }

        [Serializable]
        private sealed class Description
        {
            public bool developmentBuild, incremental;
            public ulong incrementalBudgetNanoseconds;
            public string valueFrame = "Time.frameCount - 1; not the enclosing update-to-update interval";
            public string[] metrics;
        }

        public GcFrameDiagnostics()
        {
            _startedFrame = Time.frameCount;
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var descriptions = new string[NAMES.Length];
            try
            {
                for (int i = 0; i < NAMES.Length; i++)
                {
                    descriptions[i] = $"bit {i}: {NAMES[i]} unavailable";
                    foreach (var handle in handles)
                    {
                        var info = ProfilerRecorderHandle.GetDescription(handle);
                        var expected = i != 1 && i < 4 ? ProfilerMarkerDataUnit.Bytes : ProfilerMarkerDataUnit.TimeNanoseconds;
                        if (info.Name != NAMES[i] || info.UnitType != expected) continue;
                        var options = ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.Default;
                        if (i == 1 || i >= 4) options |= ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                        _recorders[i] = new ProfilerRecorder(handle, 1, options);
                        descriptions[i] = $"bit {i}: {info.Name}; {info.UnitType}; " +
                            $"{(i == 1 || i >= 4 ? "main thread" : "all threads")}; " +
                            $"output={(i == 1 ? "allocation event count" : info.UnitType.ToString())}; valid={_recorders[i].Valid}";
                        break;
                    }
                }
                Capabilities = JsonUtility.ToJson(new Description
                {
                    developmentBuild = Debug.isDebugBuild, incremental = GarbageCollector.isIncremental,
                    incrementalBudgetNanoseconds = GarbageCollector.incrementalTimeSliceNanoseconds,
                    metrics = descriptions
                }, true);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public GcDiagnosticSample Read()
        {
            if (_disposed) return default;
            var sample = new GcDiagnosticSample { Enabled = true, Frame = Time.frameCount - 1 };
            sample.AllocatedBytes = Read(0, ref sample);
            sample.MainAllocationCalls = Read(1, ref sample);
            sample.UsedBytes = Read(2, ref sample);
            sample.ReservedBytes = Read(3, ref sample);
            sample.IncrementalNanoseconds = Read(4, ref sample);
            sample.CollectNanoseconds = Read(5, ref sample);
            return sample;
        }

        private long Read(int index, ref GcDiagnosticSample sample)
        {
            var recorder = _recorders[index];
            if (!recorder.Valid || !recorder.IsRunning) return 0;
            sample.AvailableMask |= 1 << index;
            if (Time.frameCount <= _startedFrame + 1 || recorder.Count == 0) return 0;
            sample.SampledMask |= 1 << index;
            return index == 1 ? recorder.GetSample(recorder.Count - 1).Count : recorder.LastValue;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _recorders.Length; i++) _recorders[i].Dispose();
        }
    }
}
