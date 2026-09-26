using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using YARG.Tests;

namespace YARG.Tests.VocalsMedia
{
    public sealed class VocalsMediaTests
    {
        private const int SAMPLE_RATE = 44100;

        [Test]
        public void SilenceEmitsDiagnosticsWithoutVoicedOutput()
        {
            var report = VocalsMediaRunner.RunSamples(new float[SAMPLE_RATE], "silence", "synthetic", 1024);
            Assert.That(report.diagnosticCount, Is.EqualTo(25));
            Assert.That(report.diagnostics, Has.All.Matches<VocalsMediaRunner.DiagnosticObservation>(
                diagnostic => !diagnostic.gateOpen && diagnostic.detection == "Silent"));
            Assert.That(report.frames, Has.None.Matches<VocalsMediaRunner.Observation>(
                frame => !frame.isHit));
        }

        [Test]
        public void ToneProducesFiniteChronologicalDiagnosticsAndPitch()
        {
            var samples = new float[SAMPLE_RATE * 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = 0.4f * (float) Math.Sin(2 * Math.PI * 220 * i / SAMPLE_RATE);
            var report = VocalsMediaRunner.RunSamples(samples, "tone", "synthetic", 127);
            Assert.That(report.diagnosticCount, Is.EqualTo(50));
            double previous = double.NegativeInfinity;
            foreach (var diagnostic in report.diagnostics)
            {
                Assert.That(diagnostic.frameEndTime, Is.GreaterThanOrEqualTo(previous));
                Assert.That(double.IsNaN(diagnostic.frameEndTime), Is.False);
                previous = diagnostic.frameEndTime;
            }
            Assert.That(report.frames, Has.Some.Matches<VocalsMediaRunner.Observation>(
                frame => !frame.isHit && frame.pitch > 200 && frame.pitch < 240));
        }

        [Test]
        public void SeededNoiseIsReproducibleAndDoesNotModifyInput()
        {
            var input = Tone();
            var perturbation = new VocalsMediaRunner.Perturbation { type = "noise", seed = 51, noiseAmplitude = 0.08f };
            var first = VocalsMediaRunner.RunSamples(input, "noise", "synthetic", 127, 1f, null, perturbation);
            var second = VocalsMediaRunner.RunSamples(input, "noise", "synthetic", 127, 1f, null, perturbation);
            Assert.That(first.diagnosticCount, Is.EqualTo(second.diagnosticCount));
            for (int i = 0; i < first.diagnosticCount; i++)
            {
                Assert.That(first.diagnostics[i].amplitude, Is.EqualTo(second.diagnostics[i].amplitude));
                Assert.That(first.diagnostics[i].hz, Is.EqualTo(second.diagnostics[i].hz));
            }
            Assert.That(input[0], Is.EqualTo(0f));
            perturbation.seed++;
            var different = VocalsMediaRunner.RunSamples(input, "noise", "synthetic", 127, 1f, null, perturbation);
            Assert.That(first.diagnostics[0].amplitude, Is.Not.EqualTo(different.diagnostics[0].amplitude));
        }

        [Test]
        public void StableSoloMetricsExcludeUnannotatedFrames()
        {
            var intervals = new[] { new VocalsMediaRunner.Interval
            {
                onset = 0, stableStart = 0.2, end = 0.6, referenceHz = 220
            } };
            var result = VocalsMediaRunner.RunSamples(Tone(), "annotated", "synthetic", 1024, 1f, intervals);
            Assert.That(result.diagnosticCount, Is.EqualTo(25));
            Assert.That(result.stableSolo.stableFrames, Is.EqualTo(10));
            Assert.That(result.stableSolo.coveredFrames + result.stableSolo.dropouts, Is.EqualTo(10));
            Assert.That(result.intervals[0].metrics.lagUncertaintySeconds, Is.EqualTo(0.04));
            Assert.That(result.diagnostics[0].simulatedAvailabilityTime, Is.GreaterThanOrEqualTo(0.04));
            Assert.That(result.diagnostics[0].estimatedTimestamp, Is.EqualTo(result.diagnostics[0].frameEndTime));
        }

        [Test]
        public void LegacyManifestPreservesProvenanceAndRejectsMissingInput()
        {
            string directory = Path.Combine(Path.GetTempPath(), "vocals-schema-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            try
            {
                string pcm = Path.Combine(directory, "tone.f32le");
                var bytes = new byte[SAMPLE_RATE * sizeof(float)];
                float[] tone = Tone();
                Buffer.BlockCopy(tone, 0, bytes, 0, bytes.Length);
                File.WriteAllBytes(pcm, bytes);
                string digest;
                using (var sha = SHA256.Create())
                    digest = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                string manifestPath = Path.Combine(directory, "manifest.json");
                var manifest = new VocalsMediaRunner.Manifest { cases = new[] { new VocalsMediaRunner.Case
                {
                    id = "legacy", pcm = pcm, format = "f32le", channels = 1, sampleRate = SAMPLE_RATE,
                    sha256 = digest, provenance = new VocalsMediaRunner.Provenance { origin = "generated", rights = "owned" }
                } } };
                File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest));
                var result = VocalsMediaRunner.RunManifest(manifestPath).results[0];
                Assert.That(result.provenance.rights, Is.EqualTo("owned"));
                Assert.That(result.sha256, Is.EqualTo(digest));
                Assert.That(result.diagnosticCount, Is.EqualTo(25));
                manifest.cases[0].sourceSha256 = digest;
                manifest.cases[0].sourceDuration = 1;
                manifest.cases[0].ffmpegCommand = "ffmpeg -ac 1";
                File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest));
                var sourced = VocalsMediaRunner.RunManifest(manifestPath).results[0];
                Assert.That(sourced.sourceSha256, Is.EqualTo(digest));
                Assert.That(sourced.sourceDuration, Is.EqualTo(1));
                Assert.That(sourced.ffmpegCommand, Is.EqualTo("ffmpeg -ac 1"));
                File.Delete(pcm);
                Assert.Throws<FileNotFoundException>(() => VocalsMediaRunner.RunManifest(manifestPath));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static float[] Tone()
        {
            var samples = new float[SAMPLE_RATE];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = 0.4f * (float) Math.Sin(2 * Math.PI * 220 * i / SAMPLE_RATE);
            return samples;
        }

        [Test]
        public void PartialPcmSampleIsRejected()
        {
            string path = Path.Combine(Path.GetTempPath(), "vocals-invalid-" + Guid.NewGuid() + ".f32le");
            try
            {
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                Assert.Throws<InvalidDataException>(() => VocalsMediaRunner.RunPcm(path, "invalid", "synthetic"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
