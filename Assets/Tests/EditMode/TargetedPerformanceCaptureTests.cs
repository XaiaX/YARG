// pattern: Imperative Shell
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class TargetedPerformanceCaptureTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

        [Test]
        public void CsvDistinguishesDisabledFromEnabledIdleAndPreservesCounts()
        {
            var sampleType = Production("YARG.Persistent.FrameDiagnosticSample");
            var targetedField = sampleType.GetField("Targeted");
            Assert.That(targetedField, Is.Not.Null, "Frame log needs a targeted interval snapshot.");
            var captureType = Production("YARG.Persistent.FrameTimingCapture");
            string path = Path.Combine(Path.GetTempPath(), "yarg-targeted-" + Guid.NewGuid() + ".csv");
            var capture = Activator.CreateInstance(captureType, new object[] { path });
            try
            {
                var sample = Activator.CreateInstance(sampleType);
                sampleType.GetField("Scene").SetValue(sample, "Menu");
                var snapshot = Activator.CreateInstance(targetedField.FieldType);
                targetedField.SetValue(sample, snapshot);
                captureType.GetMethod("Add").Invoke(capture, new[] { sample });
                snapshot.GetType().GetField("Enabled").SetValue(snapshot, true);
                snapshot.GetType().GetField("Generation").SetValue(snapshot, 3L);
                targetedField.SetValue(sample, snapshot);
                captureType.GetMethod("Add").Invoke(capture, new[] { sample });
                snapshot.GetType().GetField("LaneLookups").SetValue(snapshot, 123L);
                var timingField = snapshot.GetType().GetField("HiHatDiscovery");
                var timing = Activator.CreateInstance(timingField.FieldType);
                timingField.FieldType.GetField("Calls").SetValue(timing, 2L);
                timingField.FieldType.GetField("ElapsedTicks").SetValue(timing, System.Diagnostics.Stopwatch.Frequency);
                timingField.FieldType.GetField("MaxTicks").SetValue(timing, System.Diagnostics.Stopwatch.Frequency / 2);
                timingField.SetValue(snapshot, timing);
                targetedField.SetValue(sample, snapshot);
                sampleType.GetField("TargetedChanged").SetValue(sample, true);
                sampleType.GetField("IntervalStartFrame").SetValue(sample, 17);
                captureType.GetMethod("Add").Invoke(capture, new[] { sample });
                captureType.GetMethod("Complete").Invoke(capture, null);
                Assert.That(((Task)captureType.GetProperty("Completion").GetValue(capture)).Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(captureType.GetProperty("Error").GetValue(capture), Is.Null);
                var lines = File.ReadAllLines(path);
                var header = lines[0].Split(',');
                string Cell(int row, string column) => lines[row].Split(',')[Array.IndexOf(header, column)];
                Assert.That(Cell(1, "targetedEnabled"), Is.EqualTo("False"));
                Assert.That(Cell(1, "rollScheduleMs"), Is.Empty);
                Assert.That(Cell(2, "rollScheduleMs"), Is.EqualTo("0"));
                Assert.That(Cell(3, "laneLookups"), Is.EqualTo("123"));
                Assert.That(Cell(3, "hiHatDiscoveryCalls"), Is.EqualTo("2"));
                Assert.That(Cell(3, "hiHatDiscoveryMs"), Is.EqualTo("1000"));
                Assert.That(Cell(3, "hiHatDiscoveryMaxMs"), Is.EqualTo("500"));
                Assert.That(Cell(3, "targetedGeneration"), Is.EqualTo("3"));
                Assert.That(Cell(3, "targetedChanged"), Is.EqualTo("True"));
                Assert.That(Cell(3, "intervalStartFrame"), Is.EqualTo("17"));
                foreach (var line in lines.Skip(1).Take(3))
                    Assert.That(line.Split(',').Length, Is.EqualTo(header.Length));
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
