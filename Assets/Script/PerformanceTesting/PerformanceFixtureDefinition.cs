// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace YARG.PerformanceTesting
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PerformanceFixtureDefinition
    {
        [JsonProperty("version")] public int Version { get; set; } = 1;
        [JsonProperty("mode")] public string Mode { get; set; } = "run";
        [JsonProperty("song")] public SongSelector Song { get; set; } = new();
        [JsonProperty("songFolders")] public List<string> SongFolders { get; set; } = new();
        [JsonProperty("updatesDtaPath")] public string UpdatesDtaPath { get; set; }
        [JsonProperty("upgradeDtaPath")] public string UpgradeDtaPath { get; set; }
        [JsonProperty("bots")] public List<FixtureBot> Bots { get; set; } = new();
        [JsonProperty("settings")] public JObject Settings { get; set; } = new();
        [JsonProperty("songSpeed")] public double SongSpeed { get; set; } = 1;
        [JsonProperty("warmupSeconds")] public double WarmupSeconds { get; set; } = 10;
        [JsonProperty("durationSeconds")] public double? DurationSeconds { get; set; }
        [JsonProperty("loadTimeoutSeconds")] public double LoadTimeoutSeconds { get; set; } = 300;
        [JsonProperty("runTimeoutSeconds")] public double RunTimeoutSeconds { get; set; } = 1200;
        [JsonProperty("deferTimingWrites")] public bool DeferTimingWrites { get; set; }
        [JsonProperty("skipDrumHitWarmup")] public bool SkipDrumHitWarmup { get; set; }
        [JsonProperty("warmEliteHitLighting")] public bool WarmEliteHitLighting { get; set; }
        [JsonProperty("warmProDrumHitLighting")] public bool WarmProDrumHitLighting { get; set; }
        [JsonProperty("requireFocus")] public bool RequireFocus { get; set; } = true;

        public static PerformanceFixtureDefinition Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Fixture JSON must not be empty.", nameof(json));
            try
            {
                var document = JObject.Parse(json, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Error,
                    TypeNameHandling = TypeNameHandling.None,
                    MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = 32
                });
                var fixture = document.ToObject<PerformanceFixtureDefinition>(serializer);
                if (fixture == null) throw new ArgumentException("Fixture must be an object.");
                fixture.Validate();
                return fixture;
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("Invalid performance fixture JSON: " + exception.Message, nameof(json), exception);
            }
        }

        public void Validate()
        {
            if (Version != 1) throw new ArgumentException("Only fixture version 1 is supported.");
            if (Mode != "run" && Mode != "discover") throw new ArgumentException("mode must be run or discover.");
            if (Song == null) throw new ArgumentException("song must be an object.");
            if (SongFolders == null || SongFolders.Count == 0) throw new ArgumentException("songFolders must contain at least one absolute path.");
            foreach (var folder in SongFolders) RequireAbsolutePath(folder, "songFolders", false);
            RequireAbsolutePath(UpdatesDtaPath, "updatesDtaPath", true);
            RequireAbsolutePath(UpgradeDtaPath, "upgradeDtaPath", true);
            RequireAbsolutePath(Song.Directory, "song.directory", true);
            if (Mode == "run" && string.IsNullOrWhiteSpace(Song.Hash)) throw new ArgumentException("run mode requires song.hash; use discover to select it.");
            if (!string.IsNullOrEmpty(Song.Hash) &&
                (Song.Hash.Length != 40 || !Song.Hash.All(Uri.IsHexDigit)))
                throw new ArgumentException("song.hash must contain exactly 40 hexadecimal characters.");
            if (Mode == "run" && (!string.IsNullOrEmpty(Song.Name) || !string.IsNullOrEmpty(Song.Artist)))
                throw new ArgumentException("song.name and song.artist are discovery filters; run mode uses hash and optional directory.");
            if (Settings == null) throw new ArgumentException("settings must be an object.");
            if (Bots == null) throw new ArgumentException("bots must be an array.");
            foreach (var bot in Bots) PerformanceFixtureCatalog.ValidateBot(bot);
            RequireRange(SongSpeed, 0, 10, "songSpeed", false);
            RequireRange(WarmupSeconds, 0, 86400, "warmupSeconds", true);
            if (DurationSeconds.HasValue) RequireRange(DurationSeconds.Value, 0, 86400, "durationSeconds", false);
            RequireRange(LoadTimeoutSeconds, 0, 86400, "loadTimeoutSeconds", false);
            RequireRange(RunTimeoutSeconds, 0, 86400, "runTimeoutSeconds", false);
            if (WarmupSeconds >= RunTimeoutSeconds) throw new ArgumentException("warmupSeconds must be less than runTimeoutSeconds.");
        }

        private static void RequireRange(double value, double minimum, double maximum, string name, bool includeMinimum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value > maximum ||
                (includeMinimum ? value < minimum : value <= minimum))
                throw new ArgumentException($"{name} must be finite, {(includeMinimum ? ">=" : ">")} {minimum}, and <= {maximum}.");
        }

        private static void RequireAbsolutePath(string value, string name, bool optional)
        {
            if (optional && string.IsNullOrEmpty(value)) return;
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
                throw new ArgumentException(name + " must be an absolute path on the test machine.");
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SongSelector
    {
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("directory")] public string Directory { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("artist")] public string Artist { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FixtureBot
    {
        [JsonProperty("instrument")] public string Instrument { get; set; }
        [JsonProperty("difficulty")] public string Difficulty { get; set; } = "Expert";
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("required")] public bool Required { get; set; }
        [JsonProperty("requireNativeElite")] public bool RequireNativeElite { get; set; } = true;
        [JsonProperty("profileOverrides")] public JObject ProfileOverrides { get; set; } = new();
    }
}
