using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using YARG.Audio.PitchDetection;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Song;
using YARG.Core.Song.Cache;

namespace YARG.Tests
{
    /// <summary>
    /// Offline lead-vocals replay. Input and output paths must be outside the Unity project.
    /// PCM is caller-decoded 44.1 kHz mono f32le; FLAC is metadata only, never opened.
    /// </summary>
    public static class VocalsPhraseReplay
    {
        private const int SAMPLE_RATE = 44100;
        private const int CHUNK_SAMPLES = 1024;
        private const double FRAME_SECONDS = 0.04;
        private static readonly float[] STAR_THRESHOLDS = { 0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f };
        private static readonly float[] SOLO_THRESHOLDS = { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f };
        private static readonly Difficulty[] DIFFICULTIES =
            { Difficulty.Beginner, Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert };

        [Serializable]
        public sealed class Manifest { public ReplayCase[] cases; }

        [Serializable]
        public sealed class ReplayCase
        {
            public string id;
            public string flac; // provenance/pairing only; never read
            public string con;
            public string songName; // optional disambiguator for a multi-song CON
            public string pcm;
            public string pcmSha256;
            public int sampleRate = SAMPLE_RATE;
            public int channels = 1;
            public string format = "f32le";
            public float sensitivity = 1f;
            public double pcmToChartSeconds; // explicit alignment: chart time = PCM time + this offset
            public bool alignmentConfirmed; // false permits exploratory replay; do not interpret scores as validated alignment
            public int expectedLeadPhrases; // 0 disables count assertion
        }

        [Serializable]
        public sealed class PhraseResult
        {
            public int index;
            public uint tick;
            public uint tickEnd;
            public double start;
            public double end;
            public bool hit;
            public bool missed;
            public bool percussion;
            public bool empty; // no non-percussion ticks; Core auto-hits without phrase score
        }

        [Serializable]
        public sealed class ScoringResult
        {
            public string preset;
            public string difficulty;
            public int phrasesHit;
            public int phrasesMissed;
            public PhraseResult[] phrases;
        }

        [Serializable]
        public sealed class CaseResult
        {
            public string id;
            public string flac;
            public string con;
            public string songName;
            public double metadataSongOffsetSeconds;
            public double pcmToChartSeconds;
            public bool alignmentConfirmed;
            public string pcmSha256;
            public int sampleCount;
            public int outputFrames;
            public int leadPhrases;
            public int scoredPhrases; // excludes empty auto-hit phrases
            public ScoringResult[] scores;
        }

        [Serializable]
        public sealed class Report
        {
            public string clock = "PCM sample zero = 0 s; processor read-before clock = first sample index / 44100; backlog = 0; Core input time = processor frame time + pcmToChartSeconds; no latency compensation; update at each read end and after final phrase. Treat scores as exploratory unless alignmentConfirmed is independently verified, including CON metadata offset.";
            public CaseResult[] results;
        }

        public static void Run()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string manifest = Argument(args, "-vocalsReplayManifest");
                string output = Argument(args, "-vocalsReplayOutput");
                if (string.IsNullOrWhiteSpace(manifest) || string.IsNullOrWhiteSpace(output))
                    throw new ArgumentException("Supply -vocalsReplayManifest and -vocalsReplayOutput as absolute paths outside the Unity project.");
                ValidateExternal(manifest);
                ValidateExternal(output);
                Report report = RunManifest(manifest);
                Directory.CreateDirectory(output);
                string path = Path.Combine(output, "phrase-replay.json");
                File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                Debug.Log($"VOCALS_REPLAY_OK cases={report.results.Length} report={path}");
            }
            catch (Exception exception)
            {
                Debug.LogError("VOCALS_REPLAY_FAILED " + exception);
                throw;
            }
        }

        public static Report RunManifest(string path)
        {
            ValidateExternal(path);
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (manifest?.cases == null || manifest.cases.Length == 0)
                throw new InvalidDataException("Manifest requires cases.");
            var results = new List<CaseResult>();
            foreach (ReplayCase entry in manifest.cases)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || string.IsNullOrWhiteSpace(entry.flac) ||
                    string.IsNullOrWhiteSpace(entry.con) || string.IsNullOrWhiteSpace(entry.pcm) ||
                    string.IsNullOrWhiteSpace(entry.pcmSha256) || entry.sampleRate != SAMPLE_RATE ||
                    entry.channels != 1 || entry.format != "f32le" || entry.expectedLeadPhrases < 0 ||
                    float.IsNaN(entry.sensitivity) || float.IsInfinity(entry.sensitivity) ||
                    double.IsNaN(entry.pcmToChartSeconds) || double.IsInfinity(entry.pcmToChartSeconds) ||
                    entry.pcmToChartSeconds < 0)
                    throw new InvalidDataException("Each case needs id, FLAC/CON/PCM paths, PCM SHA256 and mono 44100 f32le settings.");
                ValidateExternal(entry.flac);
                ValidateExternal(entry.con);
                ValidateExternal(entry.pcm);
                results.Add(RunCase(entry));
            }
            return new Report { results = results.ToArray() };
        }

        public static CaseResult RunCase(ReplayCase entry)
        {
            ValidateExternal(entry.pcm);
            ValidateExternal(entry.con);
            float[] samples = ReadPcm(entry.pcm, entry.pcmSha256);
            SongEntry song = LoadConEntry(entry.con, entry.songName);
            SongChart chart = song.LoadChart() ?? throw new InvalidDataException("Core failed to load the CON chart.");
            CaseResult result = Replay(samples, chart, entry.id, entry.sensitivity, entry.pcmToChartSeconds,
                entry.expectedLeadPhrases);
            result.flac = entry.flac;
            result.con = entry.con;
            result.songName = song.Name.ToString();
            result.metadataSongOffsetSeconds = song.SongOffsetSeconds;
            result.pcmToChartSeconds = entry.pcmToChartSeconds;
            result.alignmentConfirmed = entry.alignmentConfirmed;
            result.pcmSha256 = entry.pcmSha256;
            return result;
        }

        private static SongEntry LoadConEntry(string path, string requestedSongName)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            // Core's full scan applies CON DTA settings, updates and upgrades before LoadChart.
            // CacheHandler scans the containing directory; its cache/bad-song reports live
            // alongside the external source, never inside Assets or the YARG checkout.
            string cachePath = Path.Combine(directory, ".vocals-replay-cache");
            string badPath = Path.Combine(directory, ".vocals-replay-bad-songs.txt");
            var cache = CacheHandler.RunScan(false, cachePath, badPath, false, new List<string> { directory });
            var matches = cache.Entries.Values.SelectMany(group => group)
                .Where(song => song is RBCONEntry &&
                    string.Equals(Path.GetFullPath(song.ActualLocation), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(requestedSongName) ||
                     string.Equals(song.Name.ToString(), requestedSongName, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Expected one scanned song in CON {path} (songName={requestedSongName}), found {matches.Length}.");
            return matches[0];
        }

        public static CaseResult Replay(float[] samples, SongChart chart, string id = "synthetic",
            float sensitivity = 1f, double songOffsetSeconds = 0, int expectedLeadPhrases = 0)
        {
            if (samples == null || chart == null) throw new ArgumentNullException(samples == null ? nameof(samples) : nameof(chart));
            if (double.IsNaN(songOffsetSeconds) || double.IsInfinity(songOffsetSeconds) || songOffsetSeconds < 0 ||
                float.IsNaN(sensitivity) || float.IsInfinity(sensitivity) || expectedLeadPhrases < 0)
                throw new ArgumentOutOfRangeException(nameof(songOffsetSeconds));
            if (chart.Vocals.Parts.Count == 0) throw new InvalidDataException("Chart has no lead vocal part.");
            int phraseCount = chart.Vocals.Parts[0].NotePhrases.Count(phrase => !phrase.PhraseParentNote.IsPercussionPhrase);
            if (expectedLeadPhrases > 0 && phraseCount != expectedLeadPhrases)
                throw new InvalidDataException($"Lead phrase count {phraseCount} differs from expected {expectedLeadPhrases}.");
            var engines = new List<(string Preset, Difficulty Difficulty, YargVocalsEngine Engine,
                InstrumentDifficulty<VocalNote> Track)>();
            foreach (EnginePreset preset in new[] { EnginePreset.Default, EnginePreset.Casual,
                EnginePreset.Precision, EnginePreset.SoloTaps })
                foreach (Difficulty difficulty in DIFFICULTIES)
                {
                    var track = chart.Vocals.Parts[0].CloneAsInstrumentDifficulty();
                    var parameters = preset.Vocals.Create(STAR_THRESHOLDS, SOLO_THRESHOLDS, difficulty,
                        MicDevice.UPDATES_PER_SECOND, false);
                    var engine = new YargVocalsEngine(track, chart.SyncTrack, parameters, false);
                    engines.Add((preset.Name, difficulty, engine, track));
                }
            var processor = new VocalsFrameProcessor(SAMPLE_RATE);
            var buffer = new float[CHUNK_SAMPLES];
            int frameCount = 0;
            for (int offset = 0; offset < samples.Length; offset += CHUNK_SAMPLES)
            {
                int count = Math.Min(CHUNK_SAMPLES, samples.Length - offset);
                Array.Copy(samples, offset, buffer, 0, count);
                processor.Process(buffer, count, 0, offset / (double) SAMPLE_RATE, sensitivity,
                    (MicOutputFrame frame, FrameDiagnostic _) =>
                    {
                        frameCount++;
                        // Exactly MicInputContext.GetInputsFromMic's two GameInput conversions.
                        var input = frame.IsHit
                            ? GameInput.Create(frame.Time + songOffsetSeconds, VocalsAction.Hit, true)
                            : GameInput.Create(frame.Time + songOffsetSeconds, VocalsAction.Pitch, frame.PitchAsMidiNote);
                        foreach (var item in engines)
                        {
                            var copy = input;
                            item.Engine.QueueInput(ref copy);
                        }
                    });
                // The read-end clock is monotonic and >= the processor's read-before clock.
                // Clamp negative input times to chart zero by beginning playback at zero.
                double now = songOffsetSeconds + (offset + count) / (double) SAMPLE_RATE;
                foreach (var item in engines) item.Engine.Update(Math.Max(0, now));
            }
            double end = chart.Vocals.Parts[0].NotePhrases.Count == 0 ? 0 :
                chart.Vocals.Parts[0].NotePhrases.Max(phrase => phrase.PhraseParentNote.TimeEnd);
            foreach (var item in engines) item.Engine.Update(Math.Max(0, Math.Max(samples.Length / (double) SAMPLE_RATE + songOffsetSeconds, end + FRAME_SECONDS)));
            var scores = new List<ScoringResult>();
            foreach (var item in engines)
            {
                var phrases = item.Track.Notes.Select((note, index) => new PhraseResult
                {
                    index = index, tick = note.Tick, tickEnd = note.TickEnd,
                    start = note.Time, end = note.TimeEnd, percussion = note.IsPercussionPhrase,
                    empty = !note.ChildNotes.Any(child => !child.IsPercussion && note.GetTicksForNote(child) > 0),
                    hit = note.WasHit, missed = note.WasMissed
                }).ToArray();
                scores.Add(new ScoringResult
                {
                    preset = item.Preset, difficulty = item.Difficulty.ToString(), phrases = phrases,
                    phrasesHit = phrases.Count(phrase => !phrase.percussion && !phrase.empty && phrase.hit),
                    phrasesMissed = phrases.Count(phrase => !phrase.percussion && !phrase.empty && phrase.missed)
                });
            }
            return new CaseResult { id = id, sampleCount = samples.Length, outputFrames = frameCount,
                leadPhrases = phraseCount,
                scoredPhrases = scores[0].phrases.Count(phrase => !phrase.percussion && !phrase.empty),
                scores = scores.ToArray() };
        }

        private static float[] ReadPcm(string path, string expectedDigest)
        {
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length == 0 || stream.Length % sizeof(float) != 0 || stream.Length / sizeof(float) > int.MaxValue)
                    throw new InvalidDataException("PCM must contain complete mono float samples.");
                using (var sha = SHA256.Create())
                {
                    string digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(digest, expectedDigest, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("PCM SHA256 mismatch.");
                }
                stream.Position = 0;
                var samples = new float[stream.Length / sizeof(float)];
                using (var reader = new BinaryReader(stream))
                    for (int i = 0; i < samples.Length; i++)
                    {
                        byte[] bytes = reader.ReadBytes(sizeof(float));
                        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
                        samples[i] = BitConverter.ToSingle(bytes, 0);
                        if (float.IsNaN(samples[i]) || float.IsInfinity(samples[i]) || Math.Abs(samples[i]) > 1f)
                            throw new InvalidDataException("PCM contains invalid samples.");
                    }
                return samples;
            }
        }

        private static void ValidateExternal(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new ArgumentException("All replay paths must be absolute.");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (full.StartsWith(project, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Replay media and reports must remain outside YARG: " + path);
        }

        private static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
