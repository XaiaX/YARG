using System;
using System.Collections.Generic;
using NUnit.Framework;
using YARG.Audio.PitchDetection;
using YARG.Core.Audio;

namespace YARG.Tests.VocalsPipeline
{
    public sealed class VocalsAcceptanceTests
    {
        private const float GATE_DB = 2f;

        [TestCase(44100, 65.4064, TestName = "AC_Pitch_C2_44100")]
        [TestCase(44100, 261.6256, TestName = "AC_Pitch_C4_44100")]
        [TestCase(44100, 523.2511, TestName = "AC_Pitch_C5_44100")]
        [TestCase(48000, 65.4064, TestName = "AC_Pitch_C2_48000")]
        [TestCase(48000, 261.6256, TestName = "AC_Pitch_C4_48000")]
        [TestCase(48000, 523.2511, TestName = "AC_Pitch_C5_48000")]
        [TestCase(48000, 249.0, TestName = "AC_Pitch_Near250Low")]
        [TestCase(48000, 251.0, TestName = "AC_Pitch_Near250High")]
        public void SyntheticToneEventuallyDetectsFundamental(int sampleRate, double frequency)
        {
            var processor = new VocalsFrameProcessor(sampleRate);
            var voiced = new List<MicOutputFrame>();
            var diagnostics = new List<FrameDiagnostic>();
            processor.Diagnostics = diagnostics.Add;
            int length = sampleRate * MicDevice.RECORD_PERIOD_MS / 1000;
            for (int block = 0; block < 24; block++)
            {
                float[] samples = Sine(sampleRate, frequency, 0.25f, block * length, length);
                processor.Process(samples, length, length * sizeof(float), 10.0 + block * 0.04,
                    GATE_DB, (frame, _) => { if (!frame.IsHit) voiced.Add(frame); });
            }
            Assert.That(diagnostics, Has.Count.EqualTo(24));
            Assert.That(diagnostics, Has.Some.Matches<FrameDiagnostic>(d => d.Detection == "Fresh"));
            Assert.That(voiced, Has.Some.Matches<MicOutputFrame>(f =>
                Math.Abs(f.Pitch - frequency) / frequency < 0.08),
                "Expected at least one measured fundamental, tolerating detector startup lag");
        }

        [Test]
        public void AC_ZeroBacklogDelayedRead_UsesReadClockWithoutInventingLatency()
        {
            var processor = new VocalsFrameProcessor(48000);
            var diagnostics = new List<FrameDiagnostic>();
            processor.Diagnostics = diagnostics.Add;
            float[] silence = new float[1920];
            processor.Process(silence, silence.Length, 0, 42.25, GATE_DB, (_, _) => { });
            Assert.That(diagnostics, Has.Count.EqualTo(1));
            Assert.That(diagnostics[0].FrameEndTime, Is.EqualTo(42.25).Within(1e-9));
        }

        [Test]
        public void AC_WrongBacklog_ShiftsFrameTimeEarlierNotLater()
        {
            const int sampleRate = 48000;
            float[] silence = new float[1920];
            double Time(long backlog)
            {
                var processor = new VocalsFrameProcessor(sampleRate);
                double time = double.NaN;
                processor.Diagnostics = d => time = d.FrameEndTime;
                processor.Process(silence, silence.Length, backlog, 42.25, GATE_DB, (_, _) => { });
                return time;
            }
            double correct = Time(silence.Length * sizeof(float));
            double wrong = Time((silence.Length + 4800) * sizeof(float));
            Assert.That(correct, Is.EqualTo(42.25).Within(1e-9));
            Assert.That(wrong - correct, Is.EqualTo(-0.1).Within(1e-9));
        }

        [Test]
        public void AC_GateAndHeld_ClosedGateClearsHistoryAfterVoicedFrames()
        {
            const int sampleRate = 48000;
            const int length = 1920;
            var processor = new VocalsFrameProcessor(sampleRate);
            var diagnostics = new List<FrameDiagnostic>();
            var outputs = new List<MicOutputFrame>();
            processor.Diagnostics = diagnostics.Add;
            for (int block = 0; block < 20; block++)
            {
                float[] tone = Sine(sampleRate, 220, 0.2f, block * length, length);
                processor.Process(tone, length, length * sizeof(float), block * 0.04, GATE_DB,
                    (frame, _) => outputs.Add(frame));
            }
            Assert.That(diagnostics, Has.Some.Matches<FrameDiagnostic>(d => d.Detection == "Fresh"));
            Assert.That(outputs, Has.Some.Matches<MicOutputFrame>(f => !f.IsHit && f.Pitch > 0));
            int oldCount = outputs.Count;
            processor.Process(new float[length], length, length * sizeof(float), 20 * 0.04,
                GATE_DB, (frame, _) => outputs.Add(frame));
            Assert.That(diagnostics[diagnostics.Count - 1].GateOpen, Is.False);
            Assert.That(diagnostics[diagnostics.Count - 1].Detection, Is.EqualTo("Silent"));
            Assert.That(outputs.Count, Is.EqualTo(oldCount));
        }

        [Test]
        public void AC_Transition_StableSecondToneIsDetectedAfterFirstTone()
        {
            const int sampleRate = 48000;
            const int length = 1920;
            var processor = new VocalsFrameProcessor(sampleRate);
            var secondHalf = new List<float>();
            for (int block = 0; block < 40; block++)
            {
                double frequency = block < 20 ? 220 : 440;
                float[] samples = Sine(sampleRate, frequency, 0.2f, block * length, length);
                processor.Process(samples, length, length * sizeof(float), 10.0 + block * 0.04,
                    GATE_DB, (frame, diagnostic) =>
                    {
                        if (!frame.IsHit && block >= 20 && diagnostic.Detection == "Fresh")
                            secondHalf.Add(frame.Pitch);
                    });
            }
            Assert.That(secondHalf, Has.Some.InRange(400f, 480f),
                "Transition may lag but should converge within twenty frames");
        }

        [Test]
        public void AC_NegativeControls_SilenceAndSubGateToneHaveNoVoicedOutput()
        {
            var processor = new VocalsFrameProcessor(48000);
            var frames = new List<MicOutputFrame>();
            var diagnostics = new List<FrameDiagnostic>();
            processor.Diagnostics = diagnostics.Add;
            for (int block = 0; block < 12; block++)
            {
                float[] samples = block < 6 ? new float[1920] : Sine(48000, 220, 0.0001f, block * 1920, 1920);
                processor.Process(samples, samples.Length, samples.Length * sizeof(float), block * 0.04,
                    GATE_DB, (frame, _) => frames.Add(frame));
            }
            Assert.That(diagnostics, Has.Count.EqualTo(12));
            Assert.That(diagnostics.TrueForAll(d => !d.GateOpen && d.Detection == "Silent"), Is.True);
            Assert.That(frames.Exists(f => !f.IsHit), Is.False);
        }

        private static float[] Sine(int sampleRate, double frequency, float amplitude, int offset, int count)
        {
            var samples = new float[count];
            for (int i = 0; i < count; i++)
                samples[i] = amplitude * (float) Math.Sin(2 * Math.PI * frequency * (offset + i) / sampleRate);
            return samples;
        }
    }
}
