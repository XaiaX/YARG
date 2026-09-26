using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using YARG.Audio.PitchDetection;
using YARG.Core.Audio;

namespace YARG.Tests
{
    /// <summary>Offline Editor-only processor runner. PCM files must remain outside Assets.</summary>
    public static class VocalsMediaRunner
    {
        [Serializable]
        public sealed class Provenance
        {
            public string origin;
            public string rights;
        }

        [Serializable]
        public sealed class Case
        {
            public string id;
            public string pcm;
            public string format;
            public int channels;
            public int sampleRate;
            public string sha256;
            public Provenance provenance;
            public float sensitivity = 1f;
            public int chunkSamples = 1024;
            public string sourceSha256;
            public float sourceDuration;
            public string ffmpegCommand;
            public Interval[] intervals;
            public Perturbation perturbation;
        }

        [Serializable]
        public sealed class Interval
        {
            public double onset;
            public double stableStart;
            public double end;
            public float referenceHz;
        }

        [Serializable]
        public sealed class Perturbation
        {
            // Exactly one perturbation per case. Zero-valued parameters are valid controls.
            public string type;
            public int seed;
            public float noiseAmplitude;
            public float reverbWet;
            public float reverbDelaySeconds = 0.08f;
            public float cents;
            public float shiftSeconds;
        }

        [Serializable]
        public sealed class StageMetrics
        {
            public int stableFrames;
            public int coveredFrames;
            public int dropouts;
            public int pitchClassErrors;
            public int octaveErrors;
            public float coverage;
            public bool lagAvailable;
            public double onsetToStableLag;
            public double lagUncertaintySeconds;
        }

        [Serializable]
        public sealed class IntervalMetrics
        {
            public Interval interval;
            public StageMetrics metrics;
        }

        [Serializable]
        public sealed class Manifest
        {
            public Case[] cases;
        }

        [Serializable]
        public sealed class Observation
        {
            public double time;
            public bool isHit;
            public float pitch;
            public float volume;
        }

        [Serializable]
        public sealed class DiagnosticObservation
        {
            public bool gateOpen;
            public string detection;
            public bool hit;
            public int outputCount;
            public float amplitude;
            public double frameEndTime;
            public double audioSampleTime;
            public double simulatedAvailabilityTime;
            public double estimatedTimestamp;
            public float hz;
            public float midi;
            public int pitchClass = -1;
            public string state;
        }

        [Serializable]
        public sealed class Result
        {
            public string id;
            public Provenance provenance;
            public string sha256;
            public string sourceSha256;
            public float sourceDuration;
            public string ffmpegCommand;
            public Perturbation perturbation;
            public IntervalMetrics[] intervals;
            public StageMetrics stableSolo;
            public int sampleCount;
            public int frameCount;
            public int diagnosticCount;
            public Observation[] frames;
            public DiagnosticObservation[] diagnostics;
        }

        [Serializable]
        public sealed class Report
        {
            public int sampleRate;
            public Result[] results;
        }

        public static void Run()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string manifest = Argument(args, "-vocalsManifest");
                string outputDirectory = Argument(args, "-vocalsOutput");
                if (string.IsNullOrWhiteSpace(manifest) || string.IsNullOrWhiteSpace(outputDirectory) ||
                    !Path.IsPathRooted(manifest) || !Path.IsPathRooted(outputDirectory))
                    throw new ArgumentException("Specify absolute -vocalsManifest and -vocalsOutput directory paths.");
                Report report = RunManifest(manifest);
                Directory.CreateDirectory(outputDirectory);
                string reportPath = Path.Combine(outputDirectory, "report.json");
                File.WriteAllText(reportPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                WriteCsv(Path.Combine(outputDirectory, "diagnostics.csv"), report);
                Debug.Log($"VOCALS_MEDIA_OK cases={report.results.Length} report={reportPath}");
            }
            catch (Exception exception)
            {
                Debug.LogError("VOCALS_MEDIA_FAILED " + exception);
                throw;
            }
        }

        public static Report RunManifest(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Manifest missing", path);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (manifest == null || manifest.cases == null || manifest.cases.Length == 0)
                throw new InvalidDataException("Manifest requires nonempty cases.");
            var results = new List<Result>();
            foreach (Case entry in manifest.cases)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || string.IsNullOrWhiteSpace(entry.pcm) ||
                    entry.provenance == null || string.IsNullOrWhiteSpace(entry.provenance.origin) ||
                    string.IsNullOrWhiteSpace(entry.provenance.rights) || entry.format != "f32le" ||
                    entry.channels != 1 || entry.sampleRate != 44100 || string.IsNullOrWhiteSpace(entry.sha256))
                    throw new InvalidDataException("Each case requires id, mono f32le/44100, SHA256 and origin/rights.");
                if (!Path.IsPathRooted(entry.pcm)) throw new InvalidDataException("PCM paths must be absolute.");
                if (entry.chunkSamples <= 0 || entry.chunkSamples > 44100 * 60 ||
                    float.IsNaN(entry.sensitivity) || float.IsInfinity(entry.sensitivity))
                    throw new InvalidDataException("Chunk count must be 1..2646000 and sensitivity finite.");
                string digest;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(entry.pcm))
                    digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(digest, entry.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("PCM SHA256 mismatch: " + entry.id);
                ValidateAnnotations(entry.intervals, entry.perturbation);
                Result result = RunPcm(entry.pcm, entry.id, entry.provenance.origin + "; " + entry.provenance.rights,
                    entry.chunkSamples, entry.sensitivity, entry.intervals, entry.perturbation);
                result.provenance = entry.provenance;
                result.sha256 = digest;
                result.sourceSha256 = entry.sourceSha256;
                result.sourceDuration = entry.sourceDuration;
                result.ffmpegCommand = entry.ffmpegCommand;
                results.Add(result);
            }
            return new Report { sampleRate = 44100, results = results.ToArray() };
        }

        public static Result RunPcm(string path, string id, string provenance, int chunkSamples = 1024,
            float sensitivity = 1f, Interval[] intervals = null, Perturbation perturbation = null)
        {
            if (chunkSamples <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSamples));
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(provenance))
                throw new ArgumentException("Case id and provenance must be nonempty.");
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length == 0 || stream.Length % sizeof(float) != 0 || stream.Length / sizeof(float) > int.MaxValue)
                    throw new InvalidDataException("Expected nonempty mono f32le with whole samples.");
                var samples = new float[stream.Length / sizeof(float)];
                using (var reader = new BinaryReader(stream))
                    for (int i = 0; i < samples.Length; i++)
                    {
                        byte[] bytes = reader.ReadBytes(sizeof(float));
                        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
                        samples[i] = BitConverter.ToSingle(bytes, 0);
                        if (float.IsNaN(samples[i]) || float.IsInfinity(samples[i]) || Math.Abs(samples[i]) > 1f)
                            throw new InvalidDataException("Non-finite or out-of-range PCM sample.");
                    }
                return RunSamples(samples, id, provenance, chunkSamples, sensitivity, intervals, perturbation);
            }
        }

        public static Result RunSamples(float[] samples, string id, string provenance, int chunkSamples = 1024,
            float sensitivity = 1f, Interval[] intervals = null, Perturbation perturbation = null)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (chunkSamples <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSamples));
            ValidateAnnotations(intervals, perturbation);
            if (perturbation != null && string.IsNullOrEmpty(perturbation.type)) perturbation = null;
            float[] input = Perturb(samples, perturbation);
            var processor = new VocalsFrameProcessor(44100);
            var frames = new List<Observation>();
            var diagnostics = new List<DiagnosticObservation>();
            var buffer = new float[chunkSamples];
            int frameIndex = 0;
            float pendingPitch = 0f;
            processor.Diagnostics = diagnostic =>
            {
                frameIndex++;
                // Each completed 40 ms frame spans precisely 1764 samples at 44.1 kHz.
                int endSample = frameIndex * 1764;
                double audioSampleTime = endSample / 44100.0;
                float hz = pendingPitch > 0f ? pendingPitch : 0f;
                float midi = hz > 0f ? (float) (69 + 12 * Math.Log(hz / 440.0, 2)) : 0f;
                diagnostics.Add(new DiagnosticObservation
                {
                    gateOpen = diagnostic.GateOpen,
                    detection = diagnostic.Detection,
                    hit = diagnostic.Hit,
                    outputCount = diagnostic.OutputCount,
                    amplitude = diagnostic.Amplitude,
                    frameEndTime = diagnostic.FrameEndTime,
                    audioSampleTime = audioSampleTime,
                    simulatedAvailabilityTime = Math.Min(input.Length, ((endSample - 1) / chunkSamples + 1) * (long) chunkSamples) / 44100.0,
                    estimatedTimestamp = diagnostic.FrameEndTime,
                    hz = hz,
                    midi = midi,
                    pitchClass = hz > 0f ? ((int) Math.Round(midi) % 12 + 12) % 12 : -1,
                    state = diagnostic.Detection
                });
                pendingPitch = 0f;
            };
            for (int offset = 0; offset < input.Length; offset += chunkSamples)
            {
                int count = Math.Min(chunkSamples, input.Length - offset);
                Array.Copy(input, offset, buffer, 0, count);
                double readTime = offset / 44100.0;
                processor.Process(buffer, count, 0L, readTime, sensitivity,
                    (MicOutputFrame frame, FrameDiagnostic diagnostic) =>
                    {
                        frames.Add(new Observation
                        {
                            time = frame.Time, isHit = frame.IsHit, pitch = frame.Pitch, volume = frame.Volume
                        });
                        if (!frame.IsHit) pendingPitch = frame.Pitch;
                    });
            }
            IntervalMetrics[] intervalMetrics = MeasureIntervals(intervals, diagnostics);
            return new Result
            {
                id = id,
                provenance = new Provenance { origin = provenance, rights = "caller supplied" },
                perturbation = perturbation,
                intervals = intervalMetrics,
                stableSolo = Aggregate(intervalMetrics),
                sampleCount = input.Length,
                frameCount = frames.Count,
                diagnosticCount = diagnostics.Count,
                frames = frames.ToArray(),
                diagnostics = diagnostics.ToArray()
            };
        }

        private static void ValidateAnnotations(Interval[] intervals, Perturbation perturbation)
        {
            if (intervals != null)
                foreach (Interval interval in intervals)
                    if (interval == null || double.IsNaN(interval.onset) || double.IsInfinity(interval.onset) ||
                        double.IsNaN(interval.stableStart) || double.IsInfinity(interval.stableStart) ||
                        double.IsNaN(interval.end) || double.IsInfinity(interval.end) ||
                        interval.onset < 0 || interval.stableStart < interval.onset || interval.end <= interval.stableStart ||
                        float.IsNaN(interval.referenceHz) || float.IsInfinity(interval.referenceHz) || interval.referenceHz <= 0)
                        throw new InvalidDataException("Intervals require finite onset <= stableStart < end and positive referenceHz.");
            if (perturbation == null || string.IsNullOrEmpty(perturbation.type)) return;
            if (perturbation.type != "noise" && perturbation.type != "reverb" &&
                perturbation.type != "cents" && perturbation.type != "temporal")
                throw new InvalidDataException("Unknown perturbation type.");
            if (!Finite(perturbation.noiseAmplitude) || !Finite(perturbation.reverbWet) ||
                !Finite(perturbation.reverbDelaySeconds) || !Finite(perturbation.cents) ||
                !Finite(perturbation.shiftSeconds) || perturbation.noiseAmplitude < 0 ||
                perturbation.noiseAmplitude > 1 || perturbation.reverbWet < 0 || perturbation.reverbWet > 1 ||
                perturbation.reverbDelaySeconds < 0 || perturbation.reverbDelaySeconds > 2 ||
                Math.Abs(perturbation.cents) > 1200 || Math.Abs(perturbation.shiftSeconds) > 60)
                throw new InvalidDataException("Perturbation parameters out of range.");
            if ((perturbation.type != "noise" && perturbation.noiseAmplitude != 0) ||
                (perturbation.type != "reverb" && perturbation.reverbWet != 0) ||
                (perturbation.type != "cents" && perturbation.cents != 0) ||
                (perturbation.type != "temporal" && perturbation.shiftSeconds != 0))
                throw new InvalidDataException("Only one perturbation may be applied per case.");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float[] Perturb(float[] source, Perturbation perturbation)
        {
            if (perturbation == null || source.Length == 0) return source;
            var output = new float[source.Length];
            uint state = unchecked((uint) perturbation.seed) ^ 0x9E3779B9u;
            if (state == 0) state = 0xA341316Cu;
            int delay = Math.Max(1, (int) Math.Round(perturbation.reverbDelaySeconds * 44100));
            int shift = (int) Math.Round(perturbation.shiftSeconds * 44100);
            double pitchRatio = Math.Pow(2, perturbation.cents / 1200.0);
            for (int i = 0; i < output.Length; i++)
            {
                double value;
                switch (perturbation.type)
                {
                    case "noise":
                        state ^= state << 13;
                        state ^= state >> 17;
                        state ^= state << 5;
                        value = source[i] + perturbation.noiseAmplitude * (2.0 * state / uint.MaxValue - 1);
                        break;
                    case "reverb":
                        value = (1 - perturbation.reverbWet) * source[i] +
                            (i >= delay ? perturbation.reverbWet * source[i - delay] : 0);
                        break;
                    case "cents":
                        // Fractional resampling with wrap preserves clip length, but creates a seam at the boundary.
                        double index = i * pitchRatio % source.Length;
                        int left = (int) index;
                        value = source[left] + (source[(left + 1) % source.Length] - source[left]) * (index - left);
                        break;
                    default:
                        int from = i - shift;
                        value = from >= 0 && from < source.Length ? source[from] : 0;
                        break;
                }
                output[i] = (float) Math.Max(-1, Math.Min(1, value));
            }
            return output;
        }

        private static IntervalMetrics[] MeasureIntervals(Interval[] intervals, List<DiagnosticObservation> diagnostics)
        {
            if (intervals == null) return new IntervalMetrics[0];
            var results = new IntervalMetrics[intervals.Length];
            for (int i = 0; i < intervals.Length; i++)
            {
                Interval interval = intervals[i];
                var metrics = new StageMetrics();
                double referenceMidi = 69 + 12 * Math.Log(interval.referenceHz / 440.0, 2);
                double? firstStable = null;
                foreach (DiagnosticObservation frame in diagnostics)
                {
                    // Frame-end sample clock; intervals describe the source audio, never wall time.
                    if (frame.audioSampleTime <= interval.stableStart || frame.audioSampleTime > interval.end)
                        continue;
                    metrics.stableFrames++;
                    if (!frame.gateOpen || frame.hz <= 0 || frame.state == "Silent")
                    {
                        metrics.dropouts++;
                        continue;
                    }
                    metrics.coveredFrames++;
                    int semitones = (int) Math.Round(frame.midi - referenceMidi);
                    if (((semitones % 12) + 12) % 12 != 0) metrics.pitchClassErrors++;
                    else if (semitones != 0) metrics.octaveErrors++;
                    if (!firstStable.HasValue && Math.Abs(frame.midi - referenceMidi) <= 0.5)
                        firstStable = frame.audioSampleTime;
                }
                metrics.coverage = metrics.stableFrames == 0 ? 0f : (float) metrics.coveredFrames / metrics.stableFrames;
                metrics.lagAvailable = firstStable.HasValue;
                if (firstStable.HasValue) metrics.onsetToStableLag = Math.Max(0, firstStable.Value - interval.onset);
                // Frame quantization plus the completing read's scheduling uncertainty.
                metrics.lagUncertaintySeconds = 0.04;
                results[i] = new IntervalMetrics { interval = interval, metrics = metrics };
            }
            return results;
        }

        private static StageMetrics Aggregate(IntervalMetrics[] intervals)
        {
            var aggregate = new StageMetrics();
            double lagSum = 0;
            int lagCount = 0;
            foreach (IntervalMetrics interval in intervals)
            {
                StageMetrics part = interval.metrics;
                aggregate.stableFrames += part.stableFrames;
                aggregate.coveredFrames += part.coveredFrames;
                aggregate.dropouts += part.dropouts;
                aggregate.pitchClassErrors += part.pitchClassErrors;
                aggregate.octaveErrors += part.octaveErrors;
                if (part.lagAvailable) { lagSum += part.onsetToStableLag; lagCount++; }
            }
            aggregate.coverage = aggregate.stableFrames == 0 ? 0f : (float) aggregate.coveredFrames / aggregate.stableFrames;
            aggregate.lagAvailable = lagCount > 0;
            if (lagCount > 0) aggregate.onsetToStableLag = lagSum / lagCount;
            aggregate.lagUncertaintySeconds = lagCount > 0 ? 0.04 : 0;
            return aggregate;
        }

        private static void WriteCsv(string path, Report report)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("case_id,frame_index,audio_sample_time,simulated_availability_time,frame_end_time,gate_open,detection,hit,output_count,amplitude,estimated_timestamp,hz,midi,pitch_class,state");
                foreach (Result result in report.results)
                    for (int i = 0; i < result.diagnostics.Length; i++)
                    {
                        DiagnosticObservation diagnostic = result.diagnostics[i];
                        writer.WriteLine(string.Join(",", new[]
                        {
                            "\"" + result.id.Replace("\"", "\"\"") + "\"",
                            i.ToString(CultureInfo.InvariantCulture),
                            diagnostic.audioSampleTime.ToString("R", CultureInfo.InvariantCulture),
                            diagnostic.simulatedAvailabilityTime.ToString("R", CultureInfo.InvariantCulture),
                            diagnostic.frameEndTime.ToString("R", CultureInfo.InvariantCulture),
                            diagnostic.gateOpen ? "true" : "false",
                            diagnostic.detection,
                            diagnostic.hit ? "true" : "false",
                            diagnostic.outputCount.ToString(CultureInfo.InvariantCulture),
                            diagnostic.amplitude.ToString("R", CultureInfo.InvariantCulture),
                            diagnostic.estimatedTimestamp.ToString("R", CultureInfo.InvariantCulture),
                            diagnostic.hz.ToString("R", CultureInfo.InvariantCulture),
                            diagnostic.midi.ToString("R", CultureInfo.InvariantCulture),
                            diagnostic.pitchClass.ToString(CultureInfo.InvariantCulture),
                            diagnostic.state
                        }));
                    }
            }
        }

        private static string Argument(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }
    }
}
