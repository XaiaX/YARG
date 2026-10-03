using System;
using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Parsing;
using YARG.Core.Audio;

namespace YARG.Tests.VocalsMedia
{
    public sealed class VocalsLoopbackChartTests
    {
        [Test]
        public void GeneratedExternalChartHasExactGuideSchedule()
        {
            string directory = Environment.GetEnvironmentVariable("YARG_LOOPBACK_TEST_SONG");
            if (string.IsNullOrEmpty(directory))
            {
                Assert.Ignore("Set YARG_LOOPBACK_TEST_SONG to the generated external song directory.");
                return;
            }
            var settings = new ParseSettings();
            var chart = SongChart.FromFile(settings, Path.Combine(directory, "notes.mid"));
            Assert.That(chart.Vocals.Parts.Count, Is.EqualTo(1));
            Assert.That(chart.Vocals.Parts[0].NotePhrases.Count, Is.EqualTo(9));
            var builder = Type.GetType("YARG.Gameplay.Player.VocalToneSchedule, Assembly-CSharp", true);
            var schedule = (ToneSegment[]) builder.GetMethod("Build").Invoke(null,
                new object[] { chart.Vocals.Parts[0] });
            int[] pitches = { 60, 64, 67, 69, 60, 64, 67, 69, 60 };
            Assert.That(schedule.Length, Is.EqualTo(pitches.Length));
            for (int i = 0; i < schedule.Length; i++)
            {
                Assert.That(schedule[i].StartTime, Is.EqualTo(2 + 2 * i).Within(1e-9));
                Assert.That(schedule[i].EndTime, Is.EqualTo(2.5 + 2 * i).Within(1e-9));
                Assert.That(schedule[i].StartPitch, Is.EqualTo(pitches[i]));
                Assert.That(schedule[i].EndPitch, Is.EqualTo(pitches[i]));
            }
        }
    }
}
