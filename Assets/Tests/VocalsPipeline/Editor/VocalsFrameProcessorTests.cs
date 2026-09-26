using System;
using System.Collections.Generic;
using NUnit.Framework;
using YARG.Audio.PitchDetection;
using YARG.Core.Audio;

namespace YARG.Tests.VocalsPipeline
{
    public sealed class VocalsFrameProcessorTests
    {
        private const int SAMPLE_RATE = 48000;
        private const int FRAME_SAMPLES = SAMPLE_RATE * MicDevice.RECORD_PERIOD_MS / 1000;
        private const float GATE_DB = 2f;

        [Test]
        public void PartialReadsProduceOneFrameWithCompletionReadClock()
        {
            var processor = new VocalsFrameProcessor(SAMPLE_RATE);
            var diagnostics = new List<FrameDiagnostic>();
            var outputs = new List<MicOutputFrame>();
            processor.Diagnostics = diagnostics.Add;
            float[] quiet = new float[FRAME_SAMPLES];
            processor.Process(quiet, 500, 500 * sizeof(float), 10.0, GATE_DB,
                (frame, _) => outputs.Add(frame));
            Assert.That(diagnostics, Is.Empty);
            processor.Process(quiet, FRAME_SAMPLES - 500, 2500 * sizeof(float), 11.0, GATE_DB,
                (frame, _) => outputs.Add(frame));
            Assert.That(diagnostics, Has.Count.EqualTo(1));
            Assert.That(diagnostics[0].FrameEndTime,
                Is.EqualTo(11.0 - (2500 - (FRAME_SAMPLES - 500)) / (double) SAMPLE_RATE).Within(1e-9));
            Assert.That(diagnostics[0].Amplitude, Is.EqualTo(-160f));
            Assert.That(diagnostics[0].GateOpen, Is.False);
            Assert.That(diagnostics[0].Detection, Is.EqualTo("Silent"));
            Assert.That(diagnostics[0].OutputCount, Is.Zero);
            Assert.That(outputs, Is.Empty);
        }

        [Test]
        public void MultiFrameReadPreservesBacklogTimingAndEmitsHitAtMidpoint()
        {
            var processor = new VocalsFrameProcessor(SAMPLE_RATE);
            var diagnostics = new List<FrameDiagnostic>();
            var outputs = new List<(MicOutputFrame Frame, FrameDiagnostic Diagnostic)>();
            processor.Diagnostics = diagnostics.Add;
            float[] samples = new float[FRAME_SAMPLES * 2];
            for (int i = FRAME_SAMPLES; i < samples.Length; i++) samples[i] = 0.1f;
            processor.Process(samples, samples.Length, (samples.Length + FRAME_SAMPLES) * sizeof(float),
                10.0, 50f, (frame, diagnostic) => outputs.Add((frame, diagnostic)));

            Assert.That(diagnostics, Has.Count.EqualTo(2));
            Assert.That(diagnostics[0].FrameEndTime, Is.EqualTo(9.92).Within(1e-9));
            Assert.That(diagnostics[1].FrameEndTime, Is.EqualTo(9.96).Within(1e-9));
            Assert.That(diagnostics[0].Hit, Is.False);
            Assert.That(diagnostics[1].Hit, Is.True);
            Assert.That(diagnostics[1].GateOpen, Is.False);
            Assert.That(diagnostics[1].Detection, Is.EqualTo("Silent"));
            Assert.That(diagnostics[1].OutputCount, Is.EqualTo(1));
            Assert.That(diagnostics[1].Amplitude, Is.EqualTo(20f * (float) Math.Log10(18.0)).Within(0.01f));
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].Frame.IsHit, Is.True);
            Assert.That(outputs[0].Frame.Time, Is.EqualTo(9.94).Within(1e-9));
            Assert.That(outputs[0].Frame.Pitch, Is.EqualTo(-1f));
            Assert.That(outputs[0].Frame.Volume, Is.EqualTo(-1f));
            Assert.That(outputs[0].Diagnostic.FrameEndTime, Is.EqualTo(diagnostics[1].FrameEndTime));
        }

        [Test]
        public void ResetClearsPartialFrameAndPreviousAmplitude()
        {
            var processor = new VocalsFrameProcessor(SAMPLE_RATE);
            var diagnostics = new List<FrameDiagnostic>();
            var outputs = new List<MicOutputFrame>();
            processor.Diagnostics = diagnostics.Add;
            float[] loud = new float[FRAME_SAMPLES];
            Array.Fill(loud, 0.1f);
            processor.Process(loud, loud.Length, loud.Length * sizeof(float), 1.0, 50f,
                (frame, _) => outputs.Add(frame));
            processor.Process(loud, FRAME_SAMPLES / 2, FRAME_SAMPLES * sizeof(float), 2.0, 50f,
                (frame, _) => outputs.Add(frame));
            processor.Reset();
            float[] silence = new float[FRAME_SAMPLES];
            processor.Process(silence, FRAME_SAMPLES / 2, FRAME_SAMPLES * sizeof(float), 3.0, 50f,
                (frame, _) => outputs.Add(frame));
            Assert.That(diagnostics, Has.Count.EqualTo(1), "Reset must discard partial samples");
            processor.Process(silence, FRAME_SAMPLES / 2, FRAME_SAMPLES * sizeof(float), 3.04, 50f,
                (frame, _) => outputs.Add(frame));
            Assert.That(diagnostics, Has.Count.EqualTo(2));
            Assert.That(diagnostics[1].Hit, Is.False);
            Assert.That(diagnostics[1].Amplitude, Is.EqualTo(-160f));
            Assert.That(outputs, Is.Empty);
        }

        [Test]
        public void SyntheticSineProducesFreshPitchAndSubsequentFramesRemainVoiced()
        {
            var processor = new VocalsFrameProcessor(SAMPLE_RATE);
            var diagnostics = new List<FrameDiagnostic>();
            var outputs = new List<MicOutputFrame>();
            processor.Diagnostics = diagnostics.Add;
            const double frequency = 220.0;
            for (int block = 0; block < 12; block++)
            {
                float[] samples = new float[FRAME_SAMPLES];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = 0.2f * (float) Math.Sin(2 * Math.PI * frequency * (block * FRAME_SAMPLES + i) / SAMPLE_RATE);
                processor.Process(samples, samples.Length, samples.Length * sizeof(float), 10.0 + block * 0.04,
                    GATE_DB, (frame, _) => outputs.Add(frame));
            }

            Assert.That(diagnostics, Has.Count.EqualTo(12));
            Assert.That(diagnostics, Has.Some.Matches<FrameDiagnostic>(d => d.GateOpen && d.Detection == "Fresh"));
            Assert.That(outputs, Has.Some.Matches<MicOutputFrame>(f => !f.IsHit && f.Pitch > 180f && f.Pitch < 260f));
            foreach (FrameDiagnostic diagnostic in diagnostics)
                Assert.That(diagnostic.OutputCount, Is.InRange(0, 2));
        }

        [Test]
        public void InvalidArgumentsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new VocalsFrameProcessor(0));
            var processor = new VocalsFrameProcessor(SAMPLE_RATE);
            Action<MicOutputFrame, FrameDiagnostic> ignore = (_, _) => { };
            Assert.Throws<ArgumentNullException>(() => processor.Process(null, 0, 0, 0, 0, ignore));
            Assert.Throws<ArgumentOutOfRangeException>(() => processor.Process(new float[1], 2, 0, 0, 0, ignore));
            Assert.Throws<ArgumentOutOfRangeException>(() => processor.Process(new float[1], 0, -1, 0, 0, ignore));
        }
    }
}
