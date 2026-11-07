// pattern: Imperative Shell
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class GcDiagnosticCaptureTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

        [Test]
        public void DeferredCaptureBoundsMemoryAndCompletesOnlyOnce()
        {
            var type = Production("YARG.Persistent.FrameTimingCapture");
            var sample = Activator.CreateInstance(Production("YARG.Persistent.FrameDiagnosticSample"));
            string path = Path.Combine(Path.GetTempPath(), "yarg-gc-bound-" + Guid.NewGuid() + ".csv");
            var capture = Activator.CreateInstance(type, new object[] { path, true });
            var add = type.GetMethod("Add");
            var complete = type.GetMethod("Complete");
            var completion = (Task)type.GetProperty("Completion").GetValue(capture);
            try
            {
                for (int i = 0; i < 32770; i++) add.Invoke(capture, new[] { sample });
                Assert.That(type.GetProperty("DroppedRows").GetValue(capture), Is.EqualTo(2));
                complete.Invoke(capture, null);
                complete.Invoke(capture, null);
                add.Invoke(capture, new[] { sample });
                Assert.That(type.GetProperty("DroppedRows").GetValue(capture), Is.EqualTo(3));
                Assert.That(completion.Wait(TimeSpan.FromSeconds(20)), Is.True);
                Assert.That(type.GetProperty("Error").GetValue(capture), Is.Null);
                Assert.That(File.ReadLines(path).Count(), Is.EqualTo(32770));
            }
            finally
            {
                complete.Invoke(capture, null);
                completion.Wait(TimeSpan.FromSeconds(20));
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void DeferredCaptureDoesNotFormatOrWriteUntilCompletedAndKeepsAvailability()
        {
            var captureType = Production("YARG.Persistent.FrameTimingCapture");
            var sampleType = Production("YARG.Persistent.FrameDiagnosticSample");
            var gcField = sampleType.GetField("GcDiagnostics");
            Assert.That(gcField, Is.Not.Null, "GC metrics must distinguish unavailable values from zero.");
            string path = Path.Combine(Path.GetTempPath(), "yarg-gc-" + Guid.NewGuid() + ".csv");
            var capture = Activator.CreateInstance(captureType, new object[] { path, true });
            try
            {
                var sample = Activator.CreateInstance(sampleType);
                var gc = Activator.CreateInstance(gcField.FieldType);
                gc.GetType().GetField("Enabled").SetValue(gc, true);
                gc.GetType().GetField("Frame").SetValue(gc, 42);
                gc.GetType().GetField("AvailableMask").SetValue(gc, 63);
                gc.GetType().GetField("SampledMask").SetValue(gc, 3);
                gc.GetType().GetField("AllocatedBytes").SetValue(gc, 4096L);
                gcField.SetValue(sample, gc);
                captureType.GetMethod("Add").Invoke(capture, new[] { sample });
                Assert.That(File.Exists(path), Is.False);
                Assert.That(((Task)captureType.GetProperty("Completion").GetValue(capture)).IsCompleted, Is.False);
                captureType.GetMethod("Complete").Invoke(capture, null);
                Assert.That(((Task)captureType.GetProperty("Completion").GetValue(capture)).Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(captureType.GetProperty("Error").GetValue(capture), Is.Null);
                var lines = File.ReadAllLines(path);
                var header = lines[0].Split(','); var cells = lines[1].Split(',');
                string Cell(string name) => cells[Array.IndexOf(header, name)];
                Assert.That(cells.Length, Is.EqualTo(header.Length));
                Assert.That(Cell("schema"), Is.EqualTo("10"));
                Assert.That(Cell("profilerFrame"), Is.EqualTo("42"));
                Assert.That(Cell("gcAllocatedBytes"), Is.EqualTo("4096"));
                Assert.That(Cell("gcMainAllocationCalls"), Is.EqualTo("0"));
                Assert.That(Cell("gcUsedBytes"), Is.Empty, "Available but not sampled must remain blank.");
                Assert.That(Cell("gcIncrementalMs"), Is.Empty);
            }
            finally
            {
                if (!((Task)captureType.GetProperty("Completion").GetValue(capture)).IsCompleted)
                    captureType.GetMethod("Complete").Invoke(capture, null);
                ((Task)captureType.GetProperty("Completion").GetValue(capture)).Wait(TimeSpan.FromSeconds(5));
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
