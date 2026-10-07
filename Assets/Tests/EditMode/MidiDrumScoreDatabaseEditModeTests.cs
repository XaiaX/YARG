using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class MidiDrumScoreDatabaseEditModeTests
    {
        // Fixed pre-category schema: do not generate this from the current ORM mappings.
        private const string LEGACY_GAME_SCHEMA = @"CREATE TABLE GameRecords (
            Id INTEGER PRIMARY KEY AUTOINCREMENT, Date BIGINT, SongChecksum BLOB,
            GameVersion VARCHAR, SongName VARCHAR, SongArtist VARCHAR, SongCharter VARCHAR,
            ReplayFileName VARCHAR, ReplayChecksum BLOB, BandScore INTEGER, BandStars INTEGER,
            SongSpeed FLOAT, PlayedWithReplay INTEGER, HasBots INTEGER)";
        private const string LEGACY_SCORE_SCHEMA = @"CREATE TABLE PlayerScores (
            Id INTEGER PRIMARY KEY AUTOINCREMENT, GameRecordId INTEGER, PlayerId VARCHAR,
            Instrument INTEGER, Difficulty INTEGER, EnginePresetId VARCHAR, Score INTEGER,
            Stars INTEGER, NotesHit INTEGER, NotesMissed INTEGER, IsFc INTEGER,
            IsReplay INTEGER, Percent FLOAT)";
        private const string LEGACY_PLAYER_SCHEMA = "CREATE TABLE Players (Id VARCHAR PRIMARY KEY, Name VARCHAR)";
        private const string CATEGORY_INDEX = "IX_PlayerScores_PlayerInstrumentCategoryReplayGameRecord";

        private string _directory;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "YARG-score-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "scores.db");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void LegacyUpgrade_PreservesRecordsAndReplayReferences_AndReopensIdempotently(int isolatedRun)
        {
            Assert.That(isolatedRun, Is.GreaterThan(0));
            using (var legacy = OpenNative())
            {
                Execute(legacy, LEGACY_GAME_SCHEMA);
                Execute(legacy, LEGACY_SCORE_SCHEMA);
                Execute(legacy, LEGACY_PLAYER_SCHEMA);
                Execute(legacy, @"INSERT INTO GameRecords
                    (Id, SongChecksum, ReplayFileName, ReplayChecksum, BandScore)
                    VALUES (41, X'01020304', 'preserved.replay', X'05060708', 12345)");
                foreach (var pair in new[] { (71, "FourLaneDrums"), (72, "EliteDrums"), (73, "ProDrums"), (74, "FiveLaneDrums") })
                {
                    Execute(legacy, @"INSERT INTO PlayerScores
                        (Id, GameRecordId, PlayerId, Instrument, Difficulty, Score, NotesHit, NotesMissed, IsReplay, Percent)
                        VALUES (?, 41, ?, ?, 3, 12345, 90, 10, ?, 0.9)",
                        pair.Item1, Guid.Empty.ToString(), Instrument(pair.Item2), pair.Item1 == 72 ? 1 : 0);
                }
                Assert.That(Scalar(legacy, "PRAGMA user_version"), Is.Zero);
            }

            for (int reopen = 0; reopen < 2; reopen++)
            {
                using (var database = OpenDatabase(_path))
                {
                    var connection = Connection(database);
                    Assert.That(Scalar(connection, "PRAGMA user_version"), Is.EqualTo(1));
                    Assert.That(Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('PlayerScores') WHERE name = 'DrumScoreCategory'"), Is.EqualTo(1));
                    Assert.That(Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = ?", CATEGORY_INDEX), Is.EqualTo(1));
                    Assert.That(Scalar(connection, "SELECT COUNT(*) FROM PlayerScores WHERE GameRecordId = 41 AND Score = 12345"), Is.EqualTo(4));
                    Assert.That(Scalar(connection, "SELECT COUNT(*) FROM GameRecords WHERE Id = 41 AND ReplayFileName = 'preserved.replay' AND ReplayChecksum = X'05060708' AND SongChecksum = X'01020304'"), Is.EqualTo(1));
                    var rows = ((IEnumerable)Call(database, "QueryAllPlayerScoreRecords")).Cast<object>().ToArray();
                    Assert.That(rows.Select(row => Get<int>(row, "Id")), Is.EqualTo(new[] { 71, 72, 73, 74 }));
                    Assert.That(rows.Select(row => Convert.ToInt32(Get<object>(row, "DrumScoreCategory"))), Is.EqualTo(new[] { 0, 4, 0, 0 }));
                    Assert.That(Get<bool>(rows[1], "IsReplay"), Is.True);
                }
            }
        }

        [Test]
        public void FreshDatabase_OrmRoundTripsEveryCategory_AndSeparatePathsRemainIsolated()
        {
            using (var database = OpenDatabase(_path))
            {
                var recordType = ProductionType("YARG.Scores.PlayerScoreRecord");
                var records = Array.CreateInstance(recordType, 5);
                for (int category = 0; category < 5; category++)
                {
                    var record = Activator.CreateInstance(recordType);
                    Set(record, "GameRecordId", 100 + category);
                    Set(record, "PlayerId", Guid.Empty);
                    Set(record, "Instrument", Enum.ToObject(ProductionType("YARG.Core.Instrument"), Instrument("EliteDrums")));
                    Set(record, "DrumScoreCategory", Enum.ToObject(recordType.GetProperty("DrumScoreCategory").PropertyType, category));
                    Set(record, "NotesHit", 10);
                    Set(record, "Percent", 1f);
                    records.SetValue(record, category);
                }
                Call(database, "InsertSoloRecords", records);
                using (var separate = OpenDatabase(Path.Combine(_directory, "separate.db")))
                {
                    Assert.That(Scalar(Connection(separate), "SELECT COUNT(*) FROM PlayerScores"), Is.Zero);
                }
            }
            using (var reopened = OpenDatabase(_path))
            {
                var rows = ((IEnumerable)Call(reopened, "QueryAllPlayerScoreRecords")).Cast<object>().ToArray();
                Assert.That(rows.Select(row => Convert.ToInt32(Get<object>(row, "DrumScoreCategory"))), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
                Assert.That(rows.Select(row => Get<int>(row, "Id")), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            }
        }

        [Test]
        public void FailedUpgrade_RollsBackCategoryAndVersion_AndReleasesConnection()
        {
            using (var legacy = OpenNative())
            {
                Execute(legacy, LEGACY_SCORE_SCHEMA);
                // The migration's index cannot be created over an existing table with this name.
                Execute(legacy, "CREATE TABLE " + CATEGORY_INDEX + " (Id INTEGER)");
            }
            Assert.Throws<TargetInvocationException>(() => OpenDatabase(_path));
            using (var connection = OpenNative())
            {
                Assert.That(Scalar(connection, "PRAGMA user_version"), Is.Zero);
                Assert.That(Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('PlayerScores') WHERE name = 'DrumScoreCategory'"), Is.Zero);
                Execute(connection, "DROP TABLE " + CATEGORY_INDEX);
            }
            using (var database = OpenDatabase(_path))
            {
                Assert.That(Scalar(Connection(database), "PRAGMA user_version"), Is.EqualTo(1));
            }
        }

        [Test]
        public void FutureSchema_IsRejectedWithoutDowngrading()
        {
            using (var connection = OpenNative())
            {
                Execute(connection, "PRAGMA user_version = 99");
            }
            var exception = Assert.Throws<TargetInvocationException>(() => OpenDatabase(_path));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            using (var connection = OpenNative())
            {
                Assert.That(Scalar(connection, "PRAGMA user_version"), Is.EqualTo(99));
                Assert.That(Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'PlayerScores'"), Is.Zero);
            }
        }

        private IDisposable OpenNative()
        {
            try
            {
                var connection = (IDisposable)Activator.CreateInstance(ProductionType("SQLite.SQLiteConnection"),
                    new object[] { _path, true });
                Assert.That(Scalar(connection, "SELECT 1"), Is.EqualTo(1));
                return connection;
            }
            catch (Exception exception)
            {
                Assert.Fail("Real native SQLite must be available; this fixture cannot be skipped or mocked. " + exception);
                throw;
            }
        }

        private static IDisposable OpenDatabase(string path) =>
            (IDisposable)Activator.CreateInstance(ProductionType("YARG.Scores.ScoreDatabase"), path);
        private static object Connection(object database) => database.GetType().GetField("_db").GetValue(database);
        private static int Instrument(string name) => Convert.ToInt32(Enum.Parse(ProductionType("YARG.Core.Instrument"), name));
        private static T Get<T>(object value, string property) => (T)value.GetType().GetProperty(property).GetValue(value);
        private static void Set(object value, string property, object data) => value.GetType().GetProperty(property).SetValue(value, data);
        private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method).Invoke(value, args);
        private static void Execute(object connection, string sql, params object[] args) =>
            connection.GetType().GetMethod("Execute", new[] { typeof(string), typeof(object[]) }).Invoke(connection, new object[] { sql, args });
        private static int Scalar(object connection, string sql, params object[] args) =>
            (int)connection.GetType().GetMethods().Single(method => method.Name == "ExecuteScalar" && method.IsGenericMethodDefinition)
                .MakeGenericMethod(typeof(int)).Invoke(connection, new object[] { sql, args });

        private static Type ProductionType(string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name)).FirstOrDefault(value => value != null);
            Assert.That(type, Is.Not.Null, "Required production type is unavailable: " + name);
            return type;
        }
    }
}
