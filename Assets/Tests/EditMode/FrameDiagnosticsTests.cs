// pattern: Imperative Shell
using System;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class FrameDiagnosticsTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

        [Test]
        public void UnevenSixtyFpsCadenceRemainsVisibleInPercentileAndWorst()
        {
            var type = Production("YARG.Persistent.FrameTimingStatistics");
            Assert.That(type, Is.Not.Null, "Need interval statistics, not only averaged FPS.");
            // Both sequences average 60 fps; alternating delivery must not look smooth.
            var samples = new double[] { 8.333333, 25, 8.333333, 25 };
            var result = type.GetMethod("Calculate").Invoke(null, new object[] { samples, samples.Length, 16.666667 });
            Assert.That((double)type.GetField("AverageMs").GetValue(result), Is.EqualTo(16.666667).Within(0.001));
            Assert.That((double)type.GetField("P95Ms").GetValue(result), Is.EqualTo(25));
            Assert.That((double)type.GetField("WorstMs").GetValue(result), Is.EqualTo(25));
            Assert.That((int)type.GetField("OverBudget").GetValue(result), Is.EqualTo(2));
        }

        [Test]
        public void SamplingIsAvailableWithoutGameplayOrFileCapture()
        {
            var type = Production("YARG.Persistent.StutterProbe");
            var count = type.GetProperty("SampleCount");
            Assert.That(count, Is.Not.Null, "Menu diagnostics must expose samples independently of GameManager.");
            // Use the same explicit clock entry point as Update, with deterministic intervals.
            var sample = type.GetMethod("Sample");
            Assert.That(sample, Is.Not.Null);
            var host = new GameObject("menu-timing-test");
            host.SetActive(false);
            try
            {
                var probe = host.AddComponent(type);
                sample.Invoke(probe, new object[] { 1.0 });
                sample.Invoke(probe, new object[] { 1.01 });
                sample.Invoke(probe, new object[] { 1.04 });
                Assert.That(count.GetValue(probe), Is.EqualTo(2));
                var latest = type.GetProperty("Latest").GetValue(probe);
                Assert.That((double)latest.GetType().GetField("WallMs").GetValue(latest), Is.EqualTo(30).Within(0.001));
                Assert.That(latest.GetType().GetField("HasGameplay").GetValue(latest), Is.False);
                Assert.That(type.GetProperty("CapturePath").GetValue(probe), Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void PacingChangeStartsFreshHistoryInsteadOfGradingBackgroundFramesAgainstForegroundBudget()
        {
            int oldCap = Application.targetFrameRate;
            var type = Production("YARG.Persistent.StutterProbe");
            var sample = type.GetMethod("Sample");
            var host = new GameObject("pacing-history-test"); host.SetActive(false);
            try
            {
                var probe = host.AddComponent(type);
                Application.targetFrameRate = 10;
                sample.Invoke(probe, new object[] { 1.0 });
                sample.Invoke(probe, new object[] { 1.1 });
                Assert.That(type.GetProperty("SampleCount").GetValue(probe), Is.EqualTo(1));
                Application.targetFrameRate = 60;
                sample.Invoke(probe, new object[] { 1.2 });
                Assert.That(type.GetProperty("SampleCount").GetValue(probe), Is.Zero);
                var latest = type.GetProperty("Latest").GetValue(probe);
                Assert.That(latest.GetType().GetField("PacingChanged").GetValue(latest), Is.True);
                sample.Invoke(probe, new object[] { 1.216 });
                Assert.That(type.GetProperty("SampleCount").GetValue(probe), Is.EqualTo(1));
            }
            finally { Application.targetFrameRate = oldCap; UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void ConsecutivePacingChangesEachOmitTheCrossingInterval()
        {
            int oldCap = Application.targetFrameRate;
            var type = Production("YARG.Persistent.StutterProbe");
            var sample = type.GetMethod("Sample");
            var host = new GameObject("consecutive-pacing-test"); host.SetActive(false);
            try
            {
                var probe = host.AddComponent(type);
                Application.targetFrameRate = 10;
                sample.Invoke(probe, new object[] { 1.0 });
                sample.Invoke(probe, new object[] { 1.1 });
                Application.targetFrameRate = 60;
                sample.Invoke(probe, new object[] { 1.2 });
                Assert.That(type.GetProperty("SampleCount").GetValue(probe), Is.Zero);
                Application.targetFrameRate = 120;
                sample.Invoke(probe, new object[] { 1.216 });
                var latest = type.GetProperty("Latest").GetValue(probe);
                Assert.That(latest.GetType().GetField("PacingChanged").GetValue(latest), Is.True,
                    "An empty history after a transition must not hide the next pacing change.");
                Assert.That(type.GetProperty("SampleCount").GetValue(probe), Is.Zero);
                sample.Invoke(probe, new object[] { 1.224 });
                Assert.That(type.GetProperty("SampleCount").GetValue(probe), Is.EqualTo(1));
                latest = type.GetProperty("Latest").GetValue(probe);
                Assert.That(latest.GetType().GetField("PacingChanged").GetValue(latest), Is.False);
            }
            finally { Application.targetFrameRate = oldCap; UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void CsvDrainPreservesMenuSamplesAndMarksUnavailableTimingBlank()
        {
            string path = Path.Combine(Path.GetTempPath(), "yarg-frame-test-" + Guid.NewGuid() + ".csv");
            var type = Production("YARG.Persistent.FrameTimingCapture");
            var sampleType = Production("YARG.Persistent.FrameDiagnosticSample");
            var capture = Activator.CreateInstance(type, new object[] { path });
            try
            {
                var sample = Activator.CreateInstance(sampleType);
                sampleType.GetField("Scene").SetValue(sample, "Menu");
                sampleType.GetField("WallMs").SetValue(sample, 25.0);
                for (int i = 0; i < 240; i++)
                {
                    sampleType.GetField("Frame").SetValue(sample, i);
                    type.GetMethod("Add").Invoke(capture, new[] { sample });
                }
                type.GetMethod("Complete").Invoke(capture, null);
                Assert.That(((Task)type.GetProperty("Completion").GetValue(capture)).Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(type.GetProperty("Error").GetValue(capture), Is.Null);
                var rows = File.ReadAllLines(path);
                Assert.That(rows.Length, Is.EqualTo(242)); // Header, all rows, completion marker.
                var columns = rows[0].Split(','); var data = rows[240].Split(',');
                Assert.That(data[Array.IndexOf(columns, "frame")], Is.EqualTo("239"));
                Assert.That(data[Array.IndexOf(columns, "scene")], Is.EqualTo("\"Menu\""));
                Assert.That(data[Array.IndexOf(columns, "gpuMs")], Is.Empty);
                Assert.That(data[Array.IndexOf(columns, "cpuMs")], Is.Empty);
                Assert.That(data[Array.IndexOf(columns, "visualTime")], Is.Empty);
                Assert.That(rows.Last(), Does.Contain("capture complete; droppedRows=0"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void CaptureFailureIsReportedInsteadOfThrowingOnGameThread()
        {
            string file = Path.GetTempFileName(); // A file cannot be used as the capture directory.
            var type = Production("YARG.Persistent.FrameTimingCapture");
            try
            {
                var capture = Activator.CreateInstance(type, new object[] { Path.Combine(file, "capture.csv") });
                type.GetMethod("Complete").Invoke(capture, null);
                Assert.That(((Task)type.GetProperty("Completion").GetValue(capture)).Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(type.GetProperty("Error").GetValue(capture), Is.Not.Null);
            }
            finally { File.Delete(file); }
        }
    }
}
