// pattern: Imperative Shell
using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace YARG.Tests.PlayMode
{
    public sealed class RenderDiagnosticPlayModeTests
    {
        [UnityTest]
        public IEnumerator MinimalRecorderKeepsMainTimingAcrossDetailedToggle()
        {
            var type = Type.GetType("YARG.Persistent.RenderFrameDiagnostics, Assembly-CSharp", true);
            var constructor = type.GetConstructor(new[] { typeof(bool) });
            Assert.That(constructor, Is.Not.Null, "Graph-only sampling needs a minimal recorder independent of targeted checks.");
            var instance = (IDisposable)constructor.Invoke(new object[] { false });
            try
            {
                object Read() => type.GetMethod("Read").Invoke(instance, null);
                int Mask(object sample, string name) => (int)sample.GetType().GetField(name).GetValue(sample);
                yield return null;
                yield return null;
                var minimal = Read();
                Assert.That(Mask(minimal, "AvailableMask"), Is.EqualTo(1), "Editor provides Main Thread; detailed counters should remain off.");
                Assert.That(Mask(minimal, "SampledMask"), Is.EqualTo(1));
                type.GetMethod("SetDetailed").Invoke(instance, new object[] { true });
                Assert.That(Mask(Read(), "SampledMask") & 1, Is.EqualTo(1), "Toggling detailed counters must not restart Main Thread.");
                yield return null;
                yield return null;
                Assert.That(Mask(Read(), "AvailableMask") & ~1, Is.Not.Zero);
                type.GetMethod("SetDetailed").Invoke(instance, new object[] { false });
                Assert.That(Mask(Read(), "AvailableMask"), Is.EqualTo(1));
                Assert.That(Mask(Read(), "SampledMask"), Is.EqualTo(1));
            }
            finally { instance.Dispose(); }
        }

        [UnityTest]
        public IEnumerator RenderRecordersSkipStartupAndDisposeSafely()
        {
            var type = Type.GetType("YARG.Persistent.RenderFrameDiagnostics, Assembly-CSharp", true);
            var instance = (IDisposable)Activator.CreateInstance(type);
            try
            {
                object Read() => type.GetMethod("Read").Invoke(instance, null);
                var first = Read();
                Assert.That(first.GetType().GetField("SampledMask").GetValue(first), Is.EqualTo(0));
                yield return null;
                yield return null;
                var sample = Read();
                Assert.That(sample.GetType().GetField("Frame").GetValue(sample), Is.EqualTo(Time.frameCount - 1));
                Debug.Log("[Render test capabilities] " + type.GetProperty("Capabilities").GetValue(instance));
                instance.Dispose();
                var disposed = Read();
                Assert.That(disposed.GetType().GetField("Enabled").GetValue(disposed), Is.False);
                instance.Dispose();
            }
            finally { instance.Dispose(); }
        }

        [UnityTest]
        public IEnumerator WorkerMarkerIsVisibleOnlyToUnrestrictedRecorder()
        {
            var marker = new ProfilerMarker("YARG.Tests.RenderWorkerPulse");
            using var all = new ProfilerRecorder(marker, 1,
                ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.Default);
            using var main = new ProfilerRecorder(marker, 1,
                ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.Default |
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            yield return null;
            yield return null;
            int pulseFrame = Time.frameCount;
            var worker = new Thread(() =>
            {
                using (marker.Auto()) Thread.SpinWait(100000);
            });
            worker.Start();
            Assert.That(worker.Join(2000), Is.True);
            yield return null;
            Assert.That(all.LastValue, Is.GreaterThan(0), "Render-thread markers must not be restricted to the polling main thread.");
            Assert.That(main.LastValue, Is.Zero);
            Debug.Log($"[Render worker alignment] pulseFrame={pulseFrame}; readFrame={Time.frameCount}");
            yield return null;
            Assert.That(all.LastValue, Is.Zero, "Idle native frames must not repeat a previous wait.");
        }
    }
}
