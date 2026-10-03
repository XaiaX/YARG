using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;

namespace YARG.Tests.VocalsMedia
{
    public sealed class VocalsPhraseReplayTests
    {
        private const int SAMPLE_RATE = 44100;

        [Test]
        public void SyntheticSilenceResolvesEveryPhraseForAllPresetsAndDifficulties()
        {
            var chart = MakeChart();
            var samples = new float[SAMPLE_RATE * 2];
            var first = VocalsPhraseReplay.Replay(samples, chart, expectedLeadPhrases: 2);
            var second = VocalsPhraseReplay.Replay(samples, chart, expectedLeadPhrases: 2);
            Assert.That(first.scores.Length, Is.EqualTo(20));
            Assert.That(first.outputFrames, Is.Zero);
            foreach (var score in first.scores)
            {
                Assert.That(score.phrases.Length, Is.EqualTo(2));
                Assert.That(score.phrasesHit, Is.Zero);
                Assert.That(score.phrasesMissed, Is.EqualTo(2));
                Assert.That(score.phrases[0].missed && score.phrases[1].missed, Is.True);
            }
            for (int i = 0; i < first.scores.Length; i++)
            {
                Assert.That(first.scores[i].preset, Is.EqualTo(second.scores[i].preset));
                Assert.That(first.scores[i].difficulty, Is.EqualTo(second.scores[i].difficulty));
                Assert.That(first.scores[i].phrasesMissed, Is.EqualTo(second.scores[i].phrasesMissed));
            }
            Assert.That(chart.Vocals.Parts[0].NotePhrases[0].PhraseParentNote.WasMissed, Is.False,
                "Replay must clone chart notes for each engine run.");
        }

        [Test]
        public void SyntheticToneProducesInputsAndRejectsWrongPhraseCount()
        {
            var samples = new float[SAMPLE_RATE * 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = 0.4f * (float) Math.Sin(2 * Math.PI * 220 * i / SAMPLE_RATE);
            var report = VocalsPhraseReplay.Replay(samples, MakeChart(), expectedLeadPhrases: 2);
            Assert.That(report.outputFrames, Is.GreaterThan(0));
            Assert.That(report.scores.Length, Is.EqualTo(20));
            Assert.Throws<InvalidDataException>(() => VocalsPhraseReplay.Replay(samples, MakeChart(),
                expectedLeadPhrases: 3));
        }

        private static SongChart MakeChart()
        {
            var chart = new SongChart(480);
            chart.SyncTrack.Tempos.Add(new TempoChange(120, 0, 0));
            var phrases = new List<VocalsPhrase>();
            for (int i = 0; i < 2; i++)
            {
                uint tick = (uint) (i * 960);
                var parent = new VocalNote(NoteFlags.None, false, i, 0.5, tick, 480);
                parent.AddChildNote(new VocalNote(57, 0, VocalNoteType.Lyric, i, 0.5, tick, 480));
                phrases.Add(new VocalsPhrase(i, 0.5, tick, 480, parent, new()));
            }
            chart.Vocals.Parts.Add(new VocalsPart(false, phrases, new(), new(), new(), new()));
            return chart;
        }
    }
}
