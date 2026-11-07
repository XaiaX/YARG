// pattern: Imperative Shell
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class RenderDiagnosticCaptureTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

        [Test]
        public void RenderCapturePreservesThreadWaitUnitsAndMissingSamples()
        {
            var captureType = Production("YARG.Persistent.FrameTimingCapture");
            var sampleType = Production("YARG.Persistent.FrameDiagnosticSample");
            var field = sampleType.GetField("RenderDiagnostics");
            Assert.That(field, Is.Not.Null, "Render wait measurements must be separately identifiable.");
            string path = Path.Combine(Path.GetTempPath(), "yarg-render-" + Guid.NewGuid() + ".csv");
            var capture = Activator.CreateInstance(captureType, new object[] { path, true });
            var completion = (Task)captureType.GetProperty("Completion").GetValue(capture);
            try
            {
                var sample = Activator.CreateInstance(sampleType);
                var render = Activator.CreateInstance(field.FieldType);
                void Set(string name, object value) => field.FieldType.GetField(name).SetValue(render, value);
                Set("Enabled", true); Set("Frame", 42); Set("AvailableMask", 1023); Set("SampledMask", 24);
                Set("PresentWaitNanoseconds", 2500000L);
                field.SetValue(sample, render);
                sampleType.GetField("HasTiming").SetValue(sample, true);
                sampleType.GetField("CpuTimerFrequency").SetValue(sample, 1000000000UL);
                sampleType.GetField("PresentCalledTimestamp").SetValue(sample, 2000000001UL);
                captureType.GetMethod("Add").Invoke(capture, new[] { sample });
                captureType.GetMethod("Complete").Invoke(capture, null);
                Assert.That(completion.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(captureType.GetProperty("Error").GetValue(capture), Is.Null);
                var lines = File.ReadAllLines(path);
                var header = lines[0].Split(','); var cells = lines[1].Split(',');
                string Cell(string name) => cells[Array.IndexOf(header, name)];
                Assert.That(cells.Length, Is.EqualTo(header.Length));
                Assert.That(Cell("schema"), Is.EqualTo("10"));
                Assert.That(Cell("renderProfilerFrame"), Is.EqualTo("42"));
                Assert.That(Cell("nativePresentWaitMs"), Is.EqualTo("2.5"));
                Assert.That(Cell("nativeTargetFpsWaitMs"), Is.EqualTo("0"));
                Assert.That(Cell("nativePresentMs"), Is.Empty);
                Assert.That(Cell("timingCpuTimerFrequency"), Is.EqualTo("1000000000"));
                Assert.That(Cell("timingPresentCalledTimestamp"), Is.EqualTo("2000000001"));
                Assert.That(Cell("timingFrameCompleteTimestamp"), Is.Empty, "Unsupported zero timestamps stay blank.");
            }
            finally
            {
                captureType.GetMethod("Complete").Invoke(capture, null);
                completion.Wait(TimeSpan.FromSeconds(5));
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
