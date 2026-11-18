// pattern: Imperative Shell
#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using ManagedBass;
using Newtonsoft.Json;
using UnityEngine;
using YARG.Audio.BASS;
using YARG.Audio.PitchDetection;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Core.Song.Cache;
using YARG.Helpers;
using YARG.Settings;

namespace YARG.Editor
{
    public static class VocalStemBaseline
    {
        private static readonly SongStem[] IgnoredStems = Enum.GetValues(typeof(SongStem))
            .Cast<SongStem>().Where(stem => stem != SongStem.Vocals).ToArray();
        private const int SCORE_UPDATES_PER_SECOND = 120;

        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run the vocal baseline in a separate Unity batch process.");

            var args = Environment.GetCommandLineArgs();
            string path = Path.GetFullPath(Required(args, "-vocalSong"));
            string reportPath = Path.GetFullPath(Required(args, "-vocalReport"));
            if (File.Exists(reportPath))
                throw new IOException($"The report already exists: {reportPath}");
            string? title = Option(args, "-vocalTitle");
            string detector = (Option(args, "-vocalDetector") ?? "legacy").ToLowerInvariant();
            if (detector is not ("legacy" or "fft"))
                throw new ArgumentException("-vocalDetector must be legacy or fft.");
            string part = (Option(args, "-vocalPart") ?? "solo").ToLowerInvariant();
            if (part is not ("solo" or "harm1" or "harm2" or "harm3" or "all"))
                throw new ArgumentException("-vocalPart must be solo, harm1, harm2, harm3 or all.");

            double offsetMs = Number(args, "-vocalOffsetMs", 0);
            double inputGainDb = Number(args, "-vocalGainDb", 0);
            double sensitivityDb = Number(args, "-vocalSensitivityDb", 2);
            if (Math.Abs(offsetMs) > 5000 || Math.Abs(inputGainDb) > 60 || Math.Abs(sensitivityDb) > 50)
                throw new ArgumentException("Offset, gain or sensitivity is outside the supported range.");

            PathHelper.Init();
            using var audioLogs = new ConsoleYargLogListener();
            YargLogger.AddLogListener(audioLogs);
            try
            {
                VocalsTimingTrace.Initialize();
                GlobalAudioHandler.Initialize<BassAudioManager>();
                SettingsManager.LoadSettings();
                SettingsManager.Settings.EnableNormalization.SetValueWithoutNotify(false);
                SettingsManager.Settings.MicrophoneSensitivity.SetValueWithoutNotify((float) sensitivityDb);
                var song = FindSong(path, title);
                var chart = song.LoadChart() ?? throw new InvalidOperationException("YARG could not load the chart.");
                var tracks = SelectTracks(chart, part);
                using var mixer = song.LoadAudio(1, 0, false, IgnoredStems);
                if (mixer?[SongStem.Vocals] is not BassStemChannel stem ||
                    mixer.Channels.Any(channel => channel.Stem != SongStem.Vocals))
                    throw new InvalidOperationException("YARG could not load an isolated vocals stem from this song.");

                var source = new StemSource(stem, inputGainDb);
                bool useFft = detector == "fft";
                var frames = Analyze(source, useFft ? new FftPitchTracker(source.SampleRate) : null);
                double chartOffset = offsetMs / 1000 - song.SongOffsetSeconds;
                var results = tracks.Select(track => Score(track.Part, track.Name, chart.SyncTrack, frames,
                    chartOffset, source.Duration)).ToArray();
                var report = new
                {
                    song = song.Name.Original,
                    artist = song.Artist.Original,
                    path = song.ActualLocation,
                    chartHash = song.Hash.ToString(),
                    detector = useFft ? "FFT" : "Legacy",
                    fftWindowMs = useFft ? FftPitchTracker.WINDOW_MS : (int?) null,
                    fftHopMs = useFft ? FftPitchTracker.HOP_MS : (int?) null,
                    pitchPublicationMs = MicDevice.RECORD_PERIOD_MS,
                    preset = "Default Expert",
                    scoringUpdatesPerSecond = SCORE_UPDATES_PER_SECOND,
                    source.SampleRate,
                    source.Channels,
                    source.Duration,
                    source.Peak,
                    sensitivityDb,
                    inputGainDb,
                    songOffsetMs = song.SongOffsetMilliseconds,
                    additionalInputOffsetMs = offsetMs,
                    pitchFrames = frames.Count(frame => !frame.IsHit),
                    pitchTrace = frames.Where(frame => !frame.IsHit).Select(frame => new
                    {
                        audioTime = frame.Time,
                        chartTime = frame.Time + chartOffset,
                        hz = frame.Pitch,
                        midi = frame.PitchAsMidiNote,
                    }).ToArray(),
                    percussionInputs = frames.Count(frame => frame.IsHit),
                    results,
                };
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                using (var file = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(file))
                    writer.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
                Debug.Log($"Vocal stem baseline saved to {reportPath}.");
            }
            finally
            {
                VocalsTimingTrace.Shutdown();
                GlobalAudioHandler.Close();
                YargLogger.RemoveLogListener(audioLogs);
            }
        }

        private static SongEntry FindSong(string path, string? title)
        {
            bool isFile = File.Exists(path);
            if (!isFile && !Directory.Exists(path))
                throw new ArgumentException($"Song file or directory does not exist: {path}");

            string scratch = Path.Combine(Path.GetTempPath(), "yarg-vocal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                var cache = CacheHandler.RunScan(false, Path.Combine(scratch, "songs.bin"),
                    Path.Combine(scratch, "badsongs.txt"), false,
                    new List<string> { isFile ? Path.GetDirectoryName(path)! : path });
                var matches = cache.Entries.Values.SelectMany(entries => entries)
                    .Where(song => (!isFile || Path.GetFullPath(song.ActualLocation) == path) &&
                        (title == null || string.Equals(song.Name.Original, title, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException($"Expected one matching song, found {matches.Length}. " +
                        "Pass a song package, an unpacked song directory, or an exact -vocalTitle.");
                return matches[0];
            }
            finally
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        private static List<(string Name, VocalsPart Part)> SelectTracks(SongChart chart, string selection)
        {
            var tracks = new List<(string, VocalsPart)>();
            AddTracks(chart.Vocals, 1, _ => "solo");
            AddTracks(chart.Harmony, 3, i => $"harm{i + 1}");
            if (tracks.Count == 0)
                throw new InvalidOperationException($"No pitched vocal chart found for '{selection}'.");
            return tracks;

            void AddTracks(VocalsTrack track, int limit, Func<int, string> nameAt)
            {
                for (int i = 0; i < Math.Min(limit, track.Parts.Count); i++)
                {
                    var part = track.Parts[i];
                    string name = nameAt(i);
                    if ((selection == "all" || selection == name) &&
                        part.NotePhrases.Any(phrase => phrase.PhraseParentNote.ChildNotes
                            .Any(note => !note.IsPercussion && !note.IsNonPitched)))
                        tracks.Add((name, part));
                }
            }
        }

        private static List<MicOutputFrame> Analyze(StemSource source, FftPitchTracker? fftPitchTracker)
        {
            var frames = new List<MicOutputFrame>();
            using var analyzer = new BassMicAnalyzer(source, () => true, () => source.ReadTime,
                fftPitchTracker: fftPitchTracker);
            if (!SpinWait.SpinUntil(() => source.Finished || analyzer.HasFailed, TimeSpan.FromMinutes(5)))
                throw new TimeoutException("Vocal stem analysis did not finish.");
            // End of input is observed on the worker after processing the previous read.
            analyzer.StopAndJoin();
            if (analyzer.HasFailed || source.Duration <= 0)
                throw new InvalidOperationException("The microphone analyzer could not decode the vocal stem.");
            while (analyzer.DequeueOutputFrame(out var frame))
                frames.Add(frame);
            return frames;
        }

        private static object Score(VocalsPart part, string name, SyncTrack sync, List<MicOutputFrame> frames,
            double chartOffset, double duration)
        {
            var track = part.CloneAsInstrumentDifficulty(deepClone: true);
            var parameters = EnginePreset.Default.Vocals.Create(
                new[] { 0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f },
                new[] { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f },
                Difficulty.Expert, MicDevice.UPDATES_PER_SECOND, false);
            var engine = new YargVocalsEngine(track, sync, parameters, isBot: false);
            var phrases = new List<PhraseResult>();
            engine.OnPhraseHit += (fractionOfThreshold, hit, _) =>
            {
                var phrase = track.Notes[engine.NoteIndex - 1];
                phrases.Add(new PhraseResult(phrase, hit, fractionOfThreshold * parameters.PhraseHitPercent));
            };
            int taps = 0;
            engine.OnNoteHit += (_, note) => { if (note.IsPercussion) taps++; };

            // Advance the engine through silence as well as input, with the same schedule
            // for all detectors. Queue events before each update so Core sees their exact times.
            int nextFrame = 0;
            double end = Math.Max(duration + chartOffset, track.Notes.Max(note => note.TimeEnd));
            double finishBy = end + track.Notes.Count + 1;
            for (int update = 0; engine.NoteIndex < track.Notes.Count; update++)
            {
                double updateTime = update / (double) SCORE_UPDATES_PER_SECOND;
                if (updateTime > finishBy)
                    throw new InvalidOperationException($"Scoring did not finish chart part {name}.");

                while (nextFrame < frames.Count && frames[nextFrame].Time + chartOffset <= updateTime)
                {
                    var frame = frames[nextFrame++];
                    double time = frame.Time + chartOffset;
                    if (time < 0)
                        continue;
                    var input = frame.IsHit
                        ? GameInput.Create(time, VocalsAction.Hit, true)
                        : GameInput.Create(time, VocalsAction.Pitch, frame.PitchAsMidiNote);
                    engine.QueueInput(ref input);
                }
                engine.Update(updateTime);
            }

            var stats = engine.EngineStats;
            return new
            {
                part = name,
                stats.TotalScore,
                stats.TicksHit,
                stats.TicksMissed,
                pitchedOnlyPhrases = phrases.Count(p => p.PitchedNotes > 0 && p.Talkies == 0),
                pitchedOnlyPhrasesHit = phrases.Count(p => p.PitchedNotes > 0 && p.Talkies == 0 && p.Hit),
                percussionHits = taps,
                phrases,
                noteTiming = MeasureNoteTiming(part, frames, chartOffset, parameters.PitchWindowPerfect),
            };
        }

        // Use the chart as the timing reference. This measures published frames, including
        // retained pitch, and uses the same octave folding as the gameplay scorer.
        private static object[] MeasureNoteTiming(VocalsPart part, List<MicOutputFrame> frames, double offset,
            double pitchWindow)
        {
            var pitchedFrames = frames.Where(frame => !frame.IsHit).ToArray();
            var timing = new List<object>();
            var notes = part.NotePhrases.SelectMany(phrase => phrase.PhraseParentNote.ChildNotes)
                .Where(note => !note.IsNonPitched && !note.IsPercussion).OrderBy(note => note.Time);
            int firstFrame = 0;
            foreach (var note in notes)
            {
                while (firstFrame < pitchedFrames.Length && pitchedFrames[firstFrame].Time + offset < note.Time)
                    firstFrame++;

                double? firstInTuneMs = null;
                for (int i = firstFrame; i < pitchedFrames.Length; i++)
                {
                    var frame = pitchedFrames[i];
                    double time = frame.Time + offset;
                    if (time >= note.TotalTimeEnd)
                        break;

                    double distance = Math.Abs(frame.PitchAsMidiNote - note.PitchAtSongTime(time)) % 12;
                    if (Math.Min(distance, 12 - distance) <= pitchWindow)
                    {
                        firstInTuneMs = (time - note.Time) * 1000;
                        break;
                    }
                }

                timing.Add(new { start = note.Time, end = note.TotalTimeEnd, midi = note.Pitch, firstInTuneMs });
            }

            return timing.ToArray();
        }

        private sealed class PhraseResult
        {
            public double Start { get; }
            public double End { get; }
            public bool Hit { get; }
            public double RawCredit { get; }
            public int PitchedNotes { get; }
            public int Talkies { get; }

            public PhraseResult(VocalNote phrase, bool hit, double rawCredit)
            {
                Start = phrase.Time;
                End = phrase.TimeEnd;
                Hit = hit;
                RawCredit = rawCredit;
                PitchedNotes = phrase.ChildNotes.Count(note => !note.IsPercussion && !note.IsNonPitched);
                Talkies = phrase.ChildNotes.Count(note => !note.IsPercussion && note.IsNonPitched);
            }
        }

        private static string Required(string[] args, string name) =>
            Option(args, name) ?? throw new ArgumentException($"Missing {name}.");

        private static string? Option(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            if (i < 0)
                return null;
            if (i == args.Length - 1 || string.IsNullOrWhiteSpace(args[i + 1]))
                throw new ArgumentException($"Missing value for {name}.");
            return args[i + 1];
        }

        private static double Number(string[] args, string name, double fallback)
        {
            string? text = Option(args, name);
            if (text == null)
                return fallback;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException($"Expected a finite number for {name}.");
            return value;
        }

        private sealed class StemSource : IBassMicSampleSource
        {
            private readonly int _handle;
            private readonly float[] _buffer;
            private readonly float _gain;
            private int _pending;
            private long _framesRead;
            private volatile bool _finished;

            public StemSource(BassStemChannel stem, double gainDb)
            {
                _handle = stem.DecodedStemHandle;
                var info = Bass.ChannelGetInfo(_handle);
                SampleRate = info.Frequency;
                if (SampleRate <= 20_000 || info.Channels <= 0)
                    throw new InvalidOperationException("The vocal stem needs a usable channel layout and a sample rate above 20 kHz.");
                Channels = info.Channels;
                _buffer = new float[checked(SampleRate * MicDevice.RECORD_PERIOD_MS / 1000 * Channels)];
                _gain = (float) Math.Pow(10, gainDb / 20);
                if (!BassMicSignal.TryAddAnalysisEq(_handle))
                    throw new InvalidOperationException($"Failed to apply the microphone analysis EQ: {Bass.LastError}");
            }

            public int SampleRate { get; }
            public int Channels { get; }
            public bool IsValid => !_finished;
            public bool Finished => _finished;
            public double Duration => _framesRead / (double) SampleRate;
            public double ReadTime => (_framesRead + _pending) / (double) SampleRate;
            public float Peak { get; private set; }

            public int GetBacklogBytes()
            {
                int bytes = Bass.ChannelGetData(_handle, _buffer, _buffer.Length * sizeof(float) | (int) DataFlags.Float);
                if (bytes < 0 && Bass.LastError != Errors.Ended)
                    return -1;
                if (bytes <= 0)
                {
                    _finished = true;
                    return 0;
                }

                if (bytes % (sizeof(float) * Channels) != 0)
                    throw new InvalidDataException("The decoder returned an incomplete audio frame.");

                _pending = bytes / (sizeof(float) * Channels);
                return _pending * sizeof(float);
            }

            public int Read(Span<float> destination)
            {
                int frames = _pending;
                for (int frame = 0; frame < frames; frame++)
                {
                    float sum = 0;
                    for (int channel = 0; channel < Channels; channel++)
                        sum += _buffer[frame * Channels + channel];
                    float sample = sum / Channels * _gain;
                    destination[frame] = sample;
                    Peak = Math.Max(Peak, Math.Abs(sample));
                }

                _framesRead += frames;
                _pending = 0;
                return frames;
            }

            public bool ResetToLive() => false;
        }
    }
}
#endif
