// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Song;

namespace YARG.PerformanceTesting
{
    public static class PerformanceFixtureCatalog
    {
        private static readonly Instrument[] DEFAULT_INSTRUMENTS =
        {
            Instrument.PartyVocals, Instrument.FiveFretGuitar, Instrument.FiveFretBass,
            Instrument.Keys, Instrument.ProKeys, Instrument.ProDrums, Instrument.EliteDrums,
            Instrument.FiveFretRhythm, Instrument.FiveFretCoopGuitar,
            Instrument.SixFretGuitar, Instrument.SixFretBass, Instrument.SixFretRhythm,
            Instrument.SixFretCoopGuitar
        };

        public static FixtureSongInfo Describe(SongEntry song)
        {
            if (song == null) throw new ArgumentNullException(nameof(song));
            var result = new FixtureSongInfo
            {
                Hash = song.Hash.ToString(), Directory = song.SortBasedLocation,
                Name = song.Name.ToString(), Artist = song.Artist.ToString()
            };
            foreach (var instrument in DEFAULT_INSTRUMENTS)
            {
                var difficulties = ((Difficulty[]) Enum.GetValues(typeof(Difficulty)))
                    .Where(difficulty => IsAvailable(song, instrument, difficulty, true))
                    .Select(difficulty => difficulty.ToString()).ToList();
                if (difficulties.Count > 0)
                    result.Parts.Add(new PartInfo { Instrument = instrument.ToString(), Difficulties = difficulties });
            }
            return result;
        }

        public static SongEntry[] Find(IEnumerable<SongEntry> songs, SongSelector selector, bool discovery)
        {
            if (songs == null) throw new ArgumentNullException(nameof(songs));
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (!discovery && string.IsNullOrWhiteSpace(selector.Hash)) throw new ArgumentException("run mode requires a song hash.");
            return songs.Where(song => song != null &&
                (string.IsNullOrEmpty(selector.Hash) || string.Equals(song.Hash.ToString(), selector.Hash, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(selector.Directory) || string.Equals(song.SortBasedLocation, selector.Directory, StringComparison.Ordinal)) &&
                (!discovery || (Contains(song.Name.ToString(), selector.Name) && Contains(song.Artist.ToString(), selector.Artist))))
                .OrderBy(song => song.SortBasedLocation, StringComparer.Ordinal).ToArray();
        }

        public static List<YargProfile> BuildBots(SongEntry song, List<FixtureBot> bots)
        {
            if (song == null) throw new ArgumentNullException(nameof(song));
            if (bots == null) throw new ArgumentNullException(nameof(bots));
            var requests = bots.Count == 0
                ? DEFAULT_INSTRUMENTS.Select(instrument => new FixtureBot { Instrument = instrument.ToString(), Required = false }).ToList()
                : bots;
            var result = new List<YargProfile>();
            foreach (var bot in requests)
            {
                ValidateBot(bot);
                var instrument = ParseInstrument(bot.Instrument);
                var difficulty = ParseDifficulty(bot.Difficulty);
                if (!IsAvailable(song, instrument, difficulty, bot.RequireNativeElite))
                {
                    if (!bot.Required) continue;
                    throw new ArgumentException($"Required fixture part {instrument}/{difficulty} is unavailable" +
                        (instrument == Instrument.EliteDrums && bot.RequireNativeElite ? " as a native Elite chart." : "."));
                }
                var profile = new YargProfile
                {
                    Name = string.IsNullOrWhiteSpace(bot.Name) ? $"Fixture {result.Count + 1}: {instrument}" : bot.Name,
                    IsBot = true,
                    MaestroSittingOut = false,
                    GameMode = instrument.ToNativeGameMode(),
                    CurrentInstrument = instrument,
                    PreferredInstrument = instrument,
                    CurrentDifficulty = difficulty,
                    DifficultyFallback = difficulty,
                    PartyVocalsChartPreference = PartyVocalsChartPreference.Harmony
                };
                if (instrument == Instrument.EliteDrums && !bot.RequireNativeElite)
                    profile.AddSingleModifier(Modifier.EnableEliteUpconversion);
                ApplyProfileOverrides(bot.ProfileOverrides, profile);
                result.Add(profile);
            }
            if (result.Count == 0) throw new ArgumentException("The fixture has no available bot parts at the requested difficulties.");
            return result;
        }

        public static void ValidateBot(FixtureBot bot)
        {
            if (bot == null) throw new ArgumentException("Each bots entry must be an object.");
            ParseInstrument(bot.Instrument);
            ParseDifficulty(bot.Difficulty);
            if (bot.ProfileOverrides == null) throw new ArgumentException("profileOverrides must be an object.");
            ApplyProfileOverrides(bot.ProfileOverrides, null);
        }

        // Validation also runs for unavailable optional requests, so misspelled or unsafe
        // overrides never become accepted merely because a particular song omits that part.
        private static void ApplyProfileOverrides(JObject overrides, YargProfile profile)
        {
            foreach (var property in overrides.Properties())
            {
                switch (property.Name)
                {
                    case nameof(YargProfile.NoteSpeed):
                    case nameof(YargProfile.HighwayLength):
                        var number = PositiveFloat(property);
                        if (profile != null)
                        {
                            if (property.Name == nameof(YargProfile.NoteSpeed)) profile.NoteSpeed = number;
                            else profile.HighwayLength = number;
                        }
                        break;
                    case nameof(YargProfile.LeftyFlip):
                    case nameof(YargProfile.UseCymbalModels):
                    case nameof(YargProfile.KeepOriginalCymbalColors):
                    case nameof(YargProfile.OneFretHiHatSustains):
                    case nameof(YargProfile.HideHiHatSustains):
                        if (property.Value.Type != JTokenType.Boolean)
                            throw new ArgumentException("profileOverrides." + property.Name + " must be a boolean.");
                        var enabled = property.Value.Value<bool>();
                        if (profile == null) break;
                        switch (property.Name)
                        {
                            case nameof(YargProfile.LeftyFlip): profile.LeftyFlip = enabled; break;
                            case nameof(YargProfile.UseCymbalModels): profile.UseCymbalModels = enabled; break;
                            case nameof(YargProfile.KeepOriginalCymbalColors): profile.KeepOriginalCymbalColors = enabled; break;
                            case nameof(YargProfile.OneFretHiHatSustains): profile.OneFretHiHatSustains = enabled; break;
                            case nameof(YargProfile.HideHiHatSustains): profile.HideHiHatSustains = enabled; break;
                        }
                        break;
                    default:
                        throw new ArgumentException("Unsupported profile override: " + property.Name);
                }
            }
        }

        private static float PositiveFloat(JProperty property)
        {
            if (property.Value.Type != JTokenType.Integer && property.Value.Type != JTokenType.Float)
                throw new ArgumentException("profileOverrides." + property.Name + " must be a number.");
            double number;
            try { number = property.Value.Value<double>(); }
            catch (OverflowException exception)
            {
                throw new ArgumentException("profileOverrides." + property.Name + " is outside the supported numeric range.", exception);
            }
            var result = (float) number;
            if (float.IsNaN(result) || float.IsInfinity(result) || result <= 0)
                throw new ArgumentException("profileOverrides." + property.Name + " must be a finite positive float.");
            return result;
        }

        public static Instrument ParseInstrument(string value)
        {
            if (!Enum.GetNames(typeof(Instrument)).Contains(value, StringComparer.Ordinal) ||
                !Enum.TryParse(value, out Instrument result) || !DEFAULT_INSTRUMENTS.Contains(result))
                throw new ArgumentException("Unknown or unsupported fixture instrument: " + value);
            return result;
        }

        public static Difficulty ParseDifficulty(string value)
        {
            if (!Enum.GetNames(typeof(Difficulty)).Contains(value, StringComparer.Ordinal) ||
                !Enum.TryParse(value, out Difficulty result))
                throw new ArgumentException("Unknown fixture difficulty: " + value);
            return result;
        }

        private static bool IsAvailable(SongEntry song, Instrument instrument, Difficulty difficulty, bool requireNativeElite)
        {
            if (instrument is Instrument.Vocals or Instrument.Harmony or Instrument.PartyVocals)
                return difficulty != Difficulty.ExpertPlus && song.HasInstrument(instrument);
            if (instrument == Instrument.ProKeys && difficulty == Difficulty.Beginner) return false;
            if (instrument == Instrument.EliteDrums && !requireNativeElite)
                return DrumDifficultySelector.HasNativeEliteCandidate(song, difficulty);
            return song[instrument][difficulty];
        }

        private static bool Contains(string value, string filter) => string.IsNullOrEmpty(filter) ||
            value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FixtureSongInfo
    {
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("directory")] public string Directory { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("artist")] public string Artist { get; set; }
        [JsonProperty("parts")] public List<PartInfo> Parts { get; set; } = new();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PartInfo
    {
        [JsonProperty("instrument")] public string Instrument { get; set; }
        [JsonProperty("difficulties")] public List<string> Difficulties { get; set; } = new();
    }
}
