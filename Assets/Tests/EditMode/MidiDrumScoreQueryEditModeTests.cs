using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class MidiDrumScoreQueryEditModeTests
    {
        [TestCase(0, 11)]
        [TestCase(1, 12)]
        [TestCase(2, 12)]
        [TestCase(3, 11)]
        [TestCase(4, 12)]
        [TestCase(5, 12)]
        public void MidiQuery_AllHistoryModes_IsolateCategoryAndNormalizeExpertPlus(int mode, int expectedId)
        {
            string directory = Path.Combine(Path.GetTempPath(), "YARG-midi-query-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using (var database = (IDisposable)Activator.CreateInstance(TypeFor("YARG.Scores.ScoreDatabase"), Path.Combine(directory, "scores.db")))
                {
                    var connection = database.GetType().GetField("_db").GetValue(database);
                    var execute = connection.GetType().GetMethod("Execute", new[] { typeof(string), typeof(object[]) });
                    Action<string, object[]> sql = (text, args) => execute.Invoke(connection, new object[] { text, args });
                    var player = Guid.NewGuid();
                    var elite = Enum.Parse(TypeFor("YARG.Core.Instrument"), "EliteDrums");
                    var pro = Enum.Parse(TypeFor("YARG.Core.Instrument"), "ProDrums");
                    var expert = Enum.Parse(TypeFor("YARG.Core.Difficulty"), "Expert");
                    var expertPlus = Enum.Parse(TypeFor("YARG.Core.Difficulty"), "ExpertPlus");
                    var hard = Enum.Parse(TypeFor("YARG.Core.Difficulty"), "Hard");
                    var bytes = new byte[20];
                    bytes[0] = 1;
                    var hash = TypeFor("YARG.Core.Song.HashWrapper").GetMethod("Deserialize", new[] { typeof(Stream) })
                        .Invoke(null, new object[] { new MemoryStream(bytes) });
                    sql("INSERT INTO GameRecords(Id, SongChecksum) VALUES (41, ?), (42, ?)",
                        new object[] { bytes, new byte[] { 9 } });
                    Action<int, object, object, int, int, float, bool, Guid, int> insert =
                        (id, output, tier, category, score, percent, replay, owner, game) => sql(
                            "INSERT INTO PlayerScores(Id,GameRecordId,PlayerId,Instrument,Difficulty,DrumScoreCategory,Score,Percent,IsReplay) VALUES (?,?,?,?,?,?,?,?,?)",
                            new object[] { id, game, owner, output, tier, category, score, percent, replay });
                    insert(11, elite, hard, 1, 1000, 1f, false, player, 41);
                    insert(12, elite, expertPlus, 1, 300, .9f, false, player, 41);
                    insert(13, elite, expert, 1, 200, .8f, false, player, 41);
                    insert(14, elite, expert, 2, 9999, 1f, false, player, 41);
                    insert(15, elite, expert, 3, 9998, 1f, false, player, 41);
                    insert(16, elite, expert, 4, 9997, 1f, false, player, 41);
                    insert(17, pro, expert, 0, 9996, 1f, false, player, 41);
                    insert(18, elite, expert, 1, 9995, 1f, true, player, 41);
                    insert(19, elite, expert, 1, 9994, 1f, false, Guid.NewGuid(), 41);
                    insert(20, elite, expert, 1, 9993, 1f, false, player, 42);
                    var query = database.GetType().GetMethod("QueryPlayerMidiDrumScore");
                    var categoryType = TypeFor("YARG.Core.Game.DrumScoreCategory");
                    var history = Enum.ToObject(TypeFor("YARG.Scores.HighScoreHistoryMode"), mode);
                    Func<int, object, object> read = (category, output) => query.Invoke(database,
                        new[] { hash, (object)player, output, expert, Enum.ToObject(categoryType, category), history, null });
                    var result = read(1, elite);
                    Assert.That(Property<int>(result, "Id"), Is.EqualTo(expectedId));
                    if (expectedId == 12) Assert.That(Property<object>(result, "Difficulty"), Is.EqualTo(expertPlus), "Historical difficulty must not be mutated.");
                    Assert.That(Property<int>(read(2, elite), "Id"), Is.EqualTo(14));
                    Assert.That(Property<int>(read(3, elite), "Id"), Is.EqualTo(15));
                    Assert.That(Property<int>(read(4, elite), "Id"), Is.EqualTo(16));
                    Assert.That(Property<int>(read(0, pro), "Id"), Is.EqualTo(17));
                    Assert.That(read(0, elite), Is.Null);
                    Assert.That(Property<int>(read(1, elite), "Id"), Is.EqualTo(expectedId), "Switching category must not contaminate a later query.");
                    var classicQuery = database.GetType().GetMethod("QueryPlayerSongHighScore");
                    var classic = classicQuery.Invoke(database, new[] { hash, (object)player, elite, false, true, expert });
                    Assert.That(Property<object>(classic, "Difficulty"), Is.EqualTo(expert), "Classic equality remains unnormalized.");

                    insert(21, pro, expert, 0, 20000, 1f, false, player, 41);
                    insert(22, pro, expert, 0, 15000, .95f, false, player, 41);
                    sql("UPDATE PlayerScores SET DrumExtraKicks = 0 WHERE Id = 21", Array.Empty<object>());
                    sql("UPDATE PlayerScores SET DrumExtraKicks = 1 WHERE Id = 22", Array.Empty<object>());
                    var ordinaryPro = classicQuery.Invoke(database, new[] { hash, (object)player, pro, false, true, expert });
                    Assert.That(Property<int>(ordinaryPro, "Id"), Is.EqualTo(17), "Known Elite scores cannot enter ordinary output queries, even with Classic category.");
                    Assert.That(Property<int>(read(0, pro), "Id"), Is.EqualTo(mode == 1 || mode == 4 ? 22 : 21),
                        "Highest-tier history orders known plus above base; overall/current history compares score or percentage.");
                    var selectedBase = query.Invoke(database, new[] { hash, (object)player, pro, expert,
                        Enum.ToObject(categoryType, 0), history, (object)0 });
                    Assert.That(Property<int>(selectedBase, "Id"), Is.EqualTo(21));
                    var selectedPlus = query.Invoke(database, new[] { hash, (object)player, pro, expert,
                        Enum.ToObject(categoryType, 0), history, (object)1 });
                    Assert.That(Property<int>(selectedPlus, "Id"), Is.EqualTo(mode == 0 || mode == 2 ? 17 : 22),
                        "Selected-plus lookup retains ambiguous historical rows: their higher percentage wins percentage-led modes, while highest-tier modes rank known plus first.");
                    var ordinaryPercent = database.GetType().GetMethod("QueryPlayerSongHighestPercentage")
                        .Invoke(database, new[] { hash, (object)player, pro, false, true, expert });
                    Assert.That(Property<int>(ordinaryPercent, "Id"), Is.EqualTo(17));
                    var outputs = Array.CreateInstance(pro.GetType(), 1);
                    outputs.SetValue(pro, 0);
                    foreach (string method in new[] { "QueryPlayerSongHighScoreForInstruments", "QueryPlayerSongHighestPercentageForInstruments" })
                    {
                        var ordinary = database.GetType().GetMethod(method).Invoke(database,
                            new object[] { hash, player, outputs, false, false });
                        Assert.That(Property<int>(ordinary, "Id"), Is.EqualTo(17), method);
                    }
                    var hashes = Array.CreateInstance(hash.GetType(), 1);
                    hashes.SetValue(hash, 0);
                    foreach (string method in new[] { "QueryPlayerHighScores", "QueryPlayerHighestPercentages" })
                    {
                        var rows = ((System.Collections.IEnumerable)database.GetType().GetMethod(method)
                            .Invoke(database, new object[] { player, pro, false, true, expert, hashes, 1 }))
                            .Cast<object>().ToArray();
                        Assert.That(rows.Length, Is.EqualTo(1), method);
                        Assert.That(Property<int>(rows[0], "Id"), Is.EqualTo(17), method);
                    }
                    var profileType = TypeFor("YARG.Core.Game.YargProfile");
                    var profile = Activator.CreateInstance(profileType);
                    profileType.GetField("Id").SetValue(profile, player);
                    profileType.GetField("GameMode").SetValue(profile, Enum.Parse(TypeFor("YARG.Core.GameMode"), "FourLaneDrums"));
                    profileType.GetField("CurrentInstrument").SetValue(profile, pro);
                    profileType.GetField("CurrentDifficulty").SetValue(profile, expert);
                    foreach (string method in new[] { "QueryPlayerBestStars", "QueryPlayerBestStarsForInstruments" })
                    {
                        var args = method.EndsWith("ForInstruments")
                            ? new object[] { profile, outputs, history, hashes, 1 }
                            : new object[] { profile, history, hashes, 1 };
                        var rows = ((System.Collections.IEnumerable)database.GetType().GetMethod(method).Invoke(database, args)).Cast<object>().ToArray();
                        Assert.That(rows.Length, Is.EqualTo(1), method);
                        Assert.That(Property<int>(rows[0], "Id"), Is.EqualTo(17), method);
                    }
                    var counts = ((System.Collections.IEnumerable)database.GetType().GetMethod("QueryPlayerMostPlayedSongs")
                        .Invoke(database, new[] { profile, Enum.ToObject(TypeFor("YARG.Scores.SortOrdering"), 0) })).Cast<object>().ToArray();
                    Assert.That(counts.Sum(row => Property<int>(row, "Count")), Is.EqualTo(1), "Ordinary play count excludes known Elite scores.");
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SongContainer_ActualScoreAndPercentageSorts_RespectHistoryMode_AndRestoreStatics()
        {
            var songContainer = TypeFor("YARG.Song.SongContainer");
            var scoreContainer = TypeFor("YARG.Scores.ScoreContainer");
            var playerContainer = TypeFor("YARG.Player.PlayerContainer");
            var settingsType = TypeFor("YARG.Settings.SettingsManager");
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var settingsProperty = settingsType.GetProperty("Settings");
            var oldSettings = settingsProperty.GetValue(null);
            var songField = songContainer.GetField("_songs", flags);
            var oldSongs = songField.GetValue(null);
            var players = (System.Collections.IList)playerContainer.GetField("_players", flags).GetValue(null);
            var oldPlayers = players.Cast<object>().ToArray();
            var scoreFields = scoreContainer.GetFields(flags).Where(field => !field.IsLiteral).ToArray();
            var oldScoreState = scoreFields.ToDictionary(field => field, field => field.GetValue(null));
            var dictionaries = oldScoreState.Values.OfType<System.Collections.IDictionary>().Distinct().ToArray();
            var oldDictionaries = dictionaries.ToDictionary(dictionary => dictionary,
                dictionary => dictionary.Cast<System.Collections.DictionaryEntry>().ToArray());
            string path = Path.Combine(Path.GetTempPath(), "YARG-sort-" + Guid.NewGuid().ToString("N") + ".db");
            IDisposable database = null;
            try
            {
                var isolatedSettings = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(settingsProperty.PropertyType);
                var historyProperty = settingsProperty.PropertyType.GetProperty("HighScoreHistory");
                var historyType = historyProperty.PropertyType;
                var isolatedHistory = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(historyType);
                settingsProperty.PropertyType.GetField("<HighScoreHistory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(isolatedSettings, isolatedHistory);
                settingsProperty.GetSetMethod(true).Invoke(null, new[] { isolatedSettings });
                var profileType = TypeFor("YARG.Core.Game.YargProfile");
                var profile = Activator.CreateInstance(profileType);
                profileType.GetField("GameMode").SetValue(profile, Enum.Parse(TypeFor("YARG.Core.GameMode"), "EliteDrums"));
                var output = Enum.Parse(TypeFor("YARG.Core.Instrument"), "EliteDrums");
                var expert = Enum.Parse(TypeFor("YARG.Core.Difficulty"), "Expert");
                profileType.GetField("CurrentInstrument").SetValue(profile, output);
                profileType.GetField("CurrentDifficulty").SetValue(profile, expert);
                var playerType = TypeFor("YARG.Player.YargPlayer");
                var player = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(playerType);
                playerType.GetProperty("Profile").GetSetMethod(true).Invoke(player, new[] { profile });
                players.Clear();
                players.Add(player);
                var songType = TypeFor("YARG.Core.Song.SongEntry");
                var concreteSong = songType.Assembly.GetTypes().Single(type => type.Name == "UnpackedIniEntry");
                var songs = Array.CreateInstance(songType, 2);
                var hashes = new object[2];
                for (int i = 0; i < 2; i++)
                {
                    var song = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(concreteSong);
                    var partsType = TypeFor("YARG.Core.Song.AvailableParts");
                    var parts = partsType.GetField("Default").GetValue(null);
                    var native = partsType.GetField("NativeEliteDrums");
                    native.SetValue(parts, Enum.ToObject(native.FieldType, 1 << Convert.ToInt32(expert)));
                    songType.GetField("_parts", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(song, parts);
                    var bytes = new byte[20]; bytes[0] = (byte)(i + 1);
                    hashes[i] = TypeFor("YARG.Core.Song.HashWrapper").GetMethod("Deserialize", new[] { typeof(Stream) })
                        .Invoke(null, new object[] { new MemoryStream(bytes) });
                    songType.GetField("_hash", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(song, hashes[i]);
                    songs.SetValue(song, i);
                }
                songField.SetValue(null, songs);
                database = (IDisposable)Activator.CreateInstance(TypeFor("YARG.Scores.ScoreDatabase"), path);
                scoreContainer.GetField("_db", flags).SetValue(null, database);
                scoreContainer.GetMethod("InvalidateScoreCache").Invoke(null, null);
                var connection = database.GetType().GetField("_db").GetValue(database);
                var execute = connection.GetType().GetMethod("Execute", new[] { typeof(string), typeof(object[]) });
                Action<string, object[]> sql = (text, args) => execute.Invoke(connection, new object[] { text, args });
                var id = profileType.GetField("Id").GetValue(profile);
                for (int i = 0; i < 2; i++)
                {
                    sql("INSERT INTO GameRecords(Id, SongChecksum) VALUES (?, ?)", new[] { (object)(i + 1), hashes[i].GetType().GetProperty("HashBytes").GetValue(hashes[i]) });
                    sql("INSERT INTO PlayerScores(GameRecordId, PlayerId, Instrument, Difficulty, DrumScoreCategory, DrumExtraKicks, Score, Percent, IsReplay) VALUES (?, ?, ?, ?, 1, 0, ?, ?, 0)",
                        new[] { (object)(i + 1), id, output, expert, i == 0 ? 100000 : 200000, i == 0 ? .8f : .9f });
                }
                // The first song's lower base tier wins overall, but must not win current/highest tier.
                sql("INSERT INTO PlayerScores(GameRecordId, PlayerId, Instrument, Difficulty, DrumScoreCategory, DrumExtraKicks, Score, Percent, IsReplay) VALUES (1, ?, ?, ?, 1, 1, 500000, 1.0, 0)",
                    new[] { id, output, Enum.Parse(expert.GetType(), "Hard") });
                var settings = settingsProperty.GetValue(null);
                var history = settings.GetType().GetProperty("HighScoreHistory").GetValue(settings);
                var value = history.GetType().GetProperty("Value");
                foreach (int mode in new[] { 0, 1, 2, 3, 4, 5 })
                {
                    history.GetType().GetMethod("SetValueWithoutNotify").Invoke(history,
                        new[] { Enum.ToObject(value.PropertyType, mode) });
                    foreach (string method in new[] { "GetScore", "GetPercentage" })
                    {
                        var categories = (System.Collections.IEnumerable)songContainer.GetMethod(method, flags).Invoke(null, null);
                        var flattened = categories.Cast<object>().SelectMany(category =>
                            ((System.Collections.IEnumerable)category.GetType().GetProperty("Songs").GetValue(category)).Cast<object>()).ToArray();
                        Assert.That(flattened.Length, Is.EqualTo(2), method);
                        Assert.That(flattened[0], Is.SameAs(songs.GetValue(mode == 0 || mode == 3 ? 0 : 1)), method + " mode " + mode);
                    }
                }
            }
            finally
            {
                songField.SetValue(null, oldSongs);
                players.Clear(); foreach (var player in oldPlayers) players.Add(player);
                settingsProperty.GetSetMethod(true).Invoke(null, new[] { oldSettings });
                foreach (var pair in oldScoreState) if (!pair.Key.IsInitOnly) pair.Key.SetValue(null, pair.Value);
                foreach (var dictionary in dictionaries)
                {
                    dictionary.Clear();
                    foreach (var pair in oldDictionaries[dictionary]) dictionary.Add(pair.Key, pair.Value);
                }
                database?.Dispose();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void EliteDifficultyLabels_AndOrdering()
        {
            var recordType = TypeFor("YARG.Scores.PlayerScoreRecord");
            var difficultyType = TypeFor("YARG.Core.Difficulty");
            var label = recordType.GetMethod("GetDifficultyDisplayName", BindingFlags.Static | BindingFlags.Public);
            var order = recordType.GetMethod("GetDifficultyOrder", BindingFlags.Static | BindingFlags.Public);
            double previousPlus = double.NegativeInfinity;
            foreach (string tier in new[] { "Beginner", "Easy", "Medium", "Hard", "Expert" })
            {
                var difficulty = Enum.Parse(difficultyType, tier);
                Func<int?, bool, string> display = (kicks, detailed) =>
                    (string)label.Invoke(null, new object[] { difficulty, kicks, detailed });
                Func<int?, double> rank = kicks =>
                    (double)order.Invoke(null, new object[] { difficulty, kicks });
                Assert.That(display(0, true), Is.EqualTo(tier));
                Assert.That(display(1, true), Is.EqualTo(tier + "+"));
                Assert.That(display(null, true), Is.EqualTo(tier + " (extra kicks unknown)"));
                Assert.That(display(null, false), Is.EqualTo(tier));
                Assert.That(rank(0), Is.EqualTo(Convert.ToInt32(difficulty) * 2));
                Assert.That(rank(1), Is.EqualTo(Convert.ToInt32(difficulty) * 2 + 1));
                Assert.That(rank(1), Is.GreaterThan(rank(0)));
                Assert.That(rank(0), Is.GreaterThan(rank(null)), "Unknown follows both known variants in descending order.");
                Assert.That(rank(null), Is.GreaterThan(previousPlus), "Unknown stays within its base tier.");
                previousPlus = rank(1);
            }
            var legacy = Activator.CreateInstance(recordType);
            recordType.GetProperty("Difficulty").SetValue(legacy, Enum.Parse(difficultyType, "ExpertPlus"));
            Assert.That((string)recordType.GetMethod("GetDifficultyDisplayName", new[] { typeof(bool) })
                .Invoke(legacy, new object[] { true }), Is.EqualTo("Expert+ (extra kicks unknown)"));
            Assert.That(Property<double>(legacy, "DifficultyOrder"), Is.EqualTo(
                (double)order.Invoke(null, new object[] { Enum.Parse(difficultyType, "Expert"), null })));
            Assert.That(recordType.GetProperty("DifficultyOrder").GetCustomAttributes(false)
                .Any(attribute => attribute.GetType().FullName == "SQLite.IgnoreAttribute"), Is.True,
                "Display ordering must not add another persisted schema column.");
        }

        [TestCase("Classic", "Classic drums")]
        [TestCase("NativeElite", "Native MIDI Drumkit")]
        [TestCase("FourLaneDerivedElite", "4-Lane-derived MIDI Drumkit")]
        [TestCase("FiveLaneDerivedElite", "5-Lane-derived MIDI Drumkit")]
        [TestCase("LegacyUnknownElite", "Legacy MIDI Drumkit (source unknown)")]
        public void HistorySourceLabels_AreVisibleAndDistinct(string categoryName, string expectedLabel)
        {
            var localizationType = TypeFor("YARG.Localization.LocalizationManager");
            var map = (System.Collections.Generic.IDictionary<string, string>)localizationType
                .GetField("_localizationMap", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var priorMap = map.ToArray();
            var culture = (string)localizationType.GetProperty("CultureCode").GetValue(null);
            try
            {
                var load = localizationType.GetMethod("TryParseAndLoadLanguage", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That((bool)load.Invoke(null, new object[] { "en-US" }), Is.True,
                    "The real English language file must load for visible history labels.");
                var categoryType = TypeFor("YARG.Core.Game.DrumScoreCategory");
                var viewType = TypeFor("YARG.Menu.History.ReplayViewType");
                var labelMethod = viewType.GetMethod("GetDrumSourceLabel", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(labelMethod, Is.Not.Null, "History should map saved score provenance to visible text.");
                var label = (string)labelMethod.Invoke(null, new[] { Enum.Parse(categoryType, categoryName) });
                Assert.That(label, Is.EqualTo(expectedLabel));
            }
            finally
            {
                map.Clear();
                foreach (var entry in priorMap) map.Add(entry.Key, entry.Value);
                localizationType.GetProperty("CultureCode").GetSetMethod(true).Invoke(null, new object[] { culture });
            }
        }

        private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name).GetValue(value);
        private static Type TypeFor(string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name)).FirstOrDefault(value => value != null);
            Assert.That(type, Is.Not.Null, "Required real production/SQLite type is unavailable: " + name);
            return type;
        }
    }
}
