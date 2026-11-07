// pattern: Imperative Shell
using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace YARG.Tests.PlayMode
{
    public sealed class CpuPhaseDiagnosticPlayModeTests
    {
        private static Type Production()
        {
            var type = Type.GetType("YARG.Persistent.CpuPhaseDiagnostics, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, "CPU phase attribution must exist before capture integration.");
            return type;
        }

        private static T Field<T>(object sample, string name) =>
            (T)sample.GetType().GetField(name).GetValue(sample);

        [Test]
        public void AllocationAvailabilityRequiresObservedBytesForKnownAllocation()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var bytes = new byte[8192];
            long after = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(bytes);
            bool working = after - before >= 8192;
            var type = Production();
            using var instance = (IDisposable)Activator.CreateInstance(type);
            var sample = type.GetMethod("Read").Invoke(instance, null);
            Assert.That(Field<bool>(sample, "MainAllocatedBytesAvailable"), Is.EqualTo(working),
                "A nonthrowing zero-returning runtime stub must not be advertised as an allocation measurement.");
        }

        [UnityTest]
        public IEnumerator NativePhasesWarmUpAndResetWithoutStaleSamples()
        {
            var type = Production();
            using var instance = (IDisposable)Activator.CreateInstance(type);
            object Read() => type.GetMethod("Read").Invoke(instance, null);
            Assert.That(Field<int>(Read(), "SampledMask"), Is.Zero);
            yield return null;
            yield return null;
            var sample = Read();
            Assert.That(Field<int>(sample, "Frame"), Is.EqualTo(Time.frameCount - 1));
            int available = Field<int>(sample, "AvailableMask");
            int sampled = Field<int>(sample, "SampledMask");
            Assert.That(sampled & ~available, Is.Zero);
            Assert.That(available & (1 << 6), Is.Not.Zero, "Editor supports the script Update marker.");
            Assert.That(sampled & (1 << 6), Is.Not.Zero);
            Assert.That(Field<long>(sample, "BehaviourUpdateNanoseconds"), Is.GreaterThan(0));
            type.GetMethod("Reset").Invoke(instance, null);
            Assert.That(Field<int>(Read(), "SampledMask"), Is.Zero);
            yield return null;
            yield return null;
            Assert.That(Field<int>(Read(), "SampledMask") & (1 << 6), Is.Not.Zero);
            instance.Dispose();
            Assert.That(Field<bool>(Read(), "Enabled"), Is.False);
        }

        [UnityTest]
        public IEnumerator MainAllocationDeltaExcludesWorkerAndSkipsFirstAndDuplicateReads()
        {
            var type = Production();
            using var instance = (IDisposable)Activator.CreateInstance(type);
            object Read() => type.GetMethod("Read").Invoke(instance, null);
            var first = Read();
            Assert.That(Field<bool>(first, "MainAllocatedBytesSampled"), Is.False);
            int start = Time.frameCount;
            var bytes = new byte[8192];
            GC.KeepAlive(bytes);
            yield return null;
            var allocated = Read();
            if (!Field<bool>(first, "MainAllocatedBytesAvailable"))
            {
                Assert.That(Field<bool>(allocated, "MainAllocatedBytesSampled"), Is.False,
                    "An unsupported API must leave every allocation interval unsampled, rather than emit zero bytes.");
                Assert.That(type.GetProperty("Capabilities").GetValue(instance).ToString(),
                    Does.Contain("allocation counter did not observe probe allocation"));
                type.GetMethod("Reset").Invoke(instance, null);
                yield return null;
                Assert.That(Field<bool>(Read(), "MainAllocatedBytesSampled"), Is.False);
                yield break;
            }
            Assert.That(Field<bool>(allocated, "MainAllocatedBytesSampled"), Is.True);
            Assert.That(Field<long>(allocated, "MainAllocatedBytes"), Is.GreaterThanOrEqualTo(8192));
            Assert.That(Field<int>(allocated, "AllocationStartFrame"), Is.EqualTo(start));
            Assert.That(Field<int>(allocated, "AllocationEndFrame"), Is.EqualTo(Time.frameCount));
            Assert.That(Field<bool>(Read(), "MainAllocatedBytesSampled"), Is.False,
                "A duplicate read must not split the probe-to-probe allocation window.");

            // Rebase after normal frame work, then allocate a much larger buffer only on a worker.
            type.GetMethod("Reset").Invoke(instance, null);
            Read();
            var worker = new Thread(() =>
            {
                var workerBytes = new byte[8 * 1024 * 1024];
                GC.KeepAlive(workerBytes);
            }) { IsBackground = true };
            worker.Start();
            Assert.That(worker.Join(2000), Is.True);
            yield return null;
            Assert.That(Field<long>(Read(), "MainAllocatedBytes"), Is.LessThan(8 * 1024 * 1024),
                "Worker allocation must not be attributed to the main thread.");
            type.GetMethod("Reset").Invoke(instance, null);
            Assert.That(Field<bool>(Read(), "MainAllocatedBytesSampled"), Is.False);
        }

        [Test]
        public void RepeatedReadsDoNotAllocateManagedMemory()
        {
            var type = Production();
            using var instance = (IDisposable)Activator.CreateInstance(type);
            // Bind once; reflection boxing is outside the measured loop.
            var method = typeof(CpuPhaseDiagnosticPlayModeTests).GetMethod(nameof(MeasureReads),
                BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(type.GetMethod("Read").ReturnType);
            long allocated = (long)method.Invoke(null, new object[] { instance, type.GetMethod("Read") });
            Assert.That(allocated, Is.Zero);
        }

        private static long MeasureReads<T>(object instance, MethodInfo method)
        {
            var read = (Func<T>)method.CreateDelegate(typeof(Func<T>), instance);
            for (int i = 0; i < 8; i++) read();
            // Use native allocation events: the managed byte API is a zero-returning stub on some backends.
            // Without SumAllSamplesInFrame, events become observable synchronously within this scope.
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 1,
                ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            if (!recorder.Valid) return -1;
            var control = new byte[8192];
            GC.KeepAlive(control);
            if (recorder.Count == 0) return -2;
            recorder.Reset();
            recorder.Start();
            for (int i = 0; i < 128; i++) read();
            recorder.Stop();
            return recorder.Count;
        }
    }
}
