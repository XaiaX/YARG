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
                        new[] { hash, (object)player, output, expert, Enum.ToObject(categoryType, category), history });
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
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
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
