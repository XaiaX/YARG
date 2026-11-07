// pattern: Imperative Shell
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

namespace YARG.Tests.PlayMode
{
    public sealed class GcDiagnosticPlayModeTests
    {
        [UnityTest]
        public IEnumerator RecordersIdentifyCompletedFrameAndReleaseOnDispose()
        {
            var type = Type.GetType("YARG.Persistent.GcFrameDiagnostics, Assembly-CSharp", true);
            var instance = (IDisposable)Activator.CreateInstance(type);
            try
            {
                object Read() => type.GetMethod("Read").Invoke(instance, null);
                var first = Read();
                Assert.That(first.GetType().GetField("SampledMask").GetValue(first), Is.EqualTo(0));
                yield return null;
                yield return null;
                var data = new byte[8192];
                GC.KeepAlive(data);
                yield return null;
                var sample = Read();
                Assert.That(sample.GetType().GetField("Frame").GetValue(sample), Is.EqualTo(Time.frameCount - 1));
                Debug.Log("[GC test capabilities] " + type.GetProperty("Capabilities").GetValue(instance));
                int mask = (int)sample.GetType().GetField("SampledMask").GetValue(sample);
                if ((mask & 2) != 0)
                    Assert.That((long)sample.GetType().GetField("MainAllocationCalls").GetValue(sample), Is.GreaterThanOrEqualTo(1));
                instance.Dispose();
                var disposed = Read();
                Assert.That(disposed.GetType().GetField("Enabled").GetValue(disposed), Is.False);
                instance.Dispose();
            }
            finally { instance.Dispose(); }
        }

        [UnityTest]
        public IEnumerator SumRecorderProducesZeroForAnIdleFrameRatherThanRepeatingLastEvent()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.Name.Contains("GC") || d.Name.Contains("Garbage"))
                    Debug.Log("[GC available] " + d.Name + ": " + d.UnitType);
            }
            var marker = new ProfilerMarker("YARG.Tests.GcDiagnosticPulse");
            using var recorder = new ProfilerRecorder(marker, 1,
                ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.Default |
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            yield return null;
            yield return null;
            double deadline = Time.realtimeSinceStartupAsDouble + 2;
            int pulseFrame = 0;
            do
            {
                pulseFrame = Time.frameCount;
                using (marker.Auto()) System.Threading.Thread.SpinWait(10000);
                yield return null;
            } while (recorder.LastValue == 0 && Time.realtimeSinceStartupAsDouble < deadline);
            Assert.That(recorder.Count, Is.GreaterThan(0));
            Assert.That(recorder.LastValue, Is.GreaterThan(0));
            Debug.Log($"[GC marker alignment] lastPulseFrame={pulseFrame}; positiveReadFrame={Time.frameCount}");
            deadline = Time.realtimeSinceStartupAsDouble + 2;
            while (recorder.LastValue != 0 && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(recorder.LastValue, Is.Zero, "A completed idle frame must not repeat an earlier GC duration.");
            Debug.Log($"[GC marker alignment] idleReadFrame={Time.frameCount}");
        }
    }
}
