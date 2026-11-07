// pattern: Imperative Shell
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class CpuPhaseCaptureTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

        [Test]
        public void CpuCaptureKeepsNativeAndAllocationFrameWindowsSeparate()
        {
            var sampleType = Production("YARG.Persistent.FrameDiagnosticSample");
            var field = sampleType.GetField("CpuPhases");
            Assert.That(field, Is.Not.Null, "Capture CPU phases separately from overlapping render waits.");
            var captureType = Production("YARG.Persistent.FrameTimingCapture");
            string path = Path.Combine(Path.GetTempPath(), "yarg-cpu-phase-" + Guid.NewGuid() + ".csv");
            var capture = Activator.CreateInstance(captureType, new object[] { path, true });
            var completion = (Task)captureType.GetProperty("Completion").GetValue(capture);
            try
            {
                var sample = Activator.CreateInstance(sampleType);
                var phase = Activator.CreateInstance(field.FieldType);
                void Set(string name, object value) => field.FieldType.GetField(name).SetValue(phase, value);
                Set("Enabled", true); Set("Frame", 42); Set("AvailableMask", 4095);
                Set("BehaviourUpdateNanoseconds", 2500000L);
                Set("SampledMask", 64 | (1 << 11));
                Assert.That(field.FieldType.GetField("UrpRenderNanoseconds"), Is.Not.Null);
                Set("UrpRenderNanoseconds", 4500000L);
                Set("MainAllocatedBytesAvailable", true); Set("MainAllocatedBytesSampled", true);
                Set("MainAllocatedBytes", 861183L); Set("AllocationStartFrame", 42); Set("AllocationEndFrame", 43);
                field.SetValue(sample, phase);
                captureType.GetMethod("Add").Invoke(capture, new[] { sample });
                var targetedField = sampleType.GetField("Targeted");
                var targeted = Activator.CreateInstance(targetedField.FieldType);
                targetedField.FieldType.GetField("Enabled").SetValue(targeted, true);
                var hitField = targetedField.FieldType.GetField("EliteDrumHit");
                var hit = Activator.CreateInstance(hitField.FieldType);
                hitField.FieldType.GetField("Calls").SetValue(hit, 1L);
                hitField.FieldType.GetField("MaxContext").SetValue(hit, 36240);
                hitField.SetValue(targeted, hit);
                targetedField.SetValue(sample, targeted);
                captureType.GetMethod("Add").Invoke(capture, new[] { sample });
                captureType.GetMethod("Complete").Invoke(capture, null);
                Assert.That(completion.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(captureType.GetProperty("Error").GetValue(capture), Is.Null);
                var lines = File.ReadAllLines(path); var header = lines[0].Split(','); var cells = lines[1].Split(',');
                string Cell(string name) => cells[Array.IndexOf(header, name)];
                Assert.That(cells.Length, Is.EqualTo(header.Length));
                Assert.That(Cell("schema"), Is.EqualTo("10"));
                Assert.That(Cell("cpuUrpRenderMs"), Is.EqualTo("4.5"));
                Assert.That(Cell("cpuUrpMainCameraMs"), Is.Empty);
                Assert.That(Cell("eliteDrumHitMs"), Is.Empty);
                var active = lines[2].Split(',');
                Assert.That(active.Length, Is.EqualTo(header.Length));
                Assert.That(active[Array.IndexOf(header, "eliteDrumHitCalls")], Is.EqualTo("1"));
                Assert.That(active[Array.IndexOf(header, "eliteDrumHitMaxContext")], Is.EqualTo("36240"));
                Assert.That(active[Array.IndexOf(header, "classicDrumHitMaxContext")], Is.Empty);
                Assert.That(Cell("cpuPhaseFrame"), Is.EqualTo("42"));
                Assert.That(Cell("cpuBehaviourUpdateMs"), Is.EqualTo("2.5"));
                Assert.That(Cell("cpuInputUpdateMs"), Is.Empty);
                Assert.That(Cell("mainAllocatedBytes"), Is.EqualTo("861183"));
                Assert.That(Cell("mainAllocationStartFrame"), Is.EqualTo("42"));
                Assert.That(Cell("mainAllocationEndFrame"), Is.EqualTo("43"));
            }
            finally
            {
                captureType.GetMethod("Complete").Invoke(capture, null); completion.Wait(TimeSpan.FromSeconds(5));
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
