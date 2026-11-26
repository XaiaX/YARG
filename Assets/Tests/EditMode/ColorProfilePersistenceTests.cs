using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class ColorProfilePersistenceTests
    {
        private static readonly Type ContainerType = Type.GetType(
            "YARG.Settings.Customization.ColorProfileContainer, Assembly-CSharp", true);
        private static readonly PropertyInfo DataPath = Type.GetType(
            "YARG.Helpers.PathHelper, Assembly-CSharp", true).GetProperty("PersistentDataPath");
        private static readonly Color CustomKick = Color.FromArgb(255, 35, 117, 201);
        private static readonly Color CustomFret = Color.FromArgb(255, 74, 41, 153);
        private string _originalDataPath;
        private string _root;
        private object _container;

        [SetUp]
        public void SetUp()
        {
            _originalDataPath = (string) DataPath.GetValue(null);
            _root = Path.Combine(Path.GetTempPath(), "yarg-colors-" + Guid.NewGuid().ToString("N"));
            DataPath.SetValue(null, _root);
            OpenStore();
        }

        [TearDown]
        public void TearDown()
        {
            DataPath.SetValue(null, _originalDataPath);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void OpenStore()
        {
            _container = Activator.CreateInstance(ContainerType);
            Call("Initialize");
        }

        private object Call(string method, params object[] args) =>
            ContainerType.GetMethod(method).Invoke(_container, args);

        private ColorProfile Get(Guid id) => (ColorProfile) Call("GetPresetById", id);

        private string BaseFile(ColorProfile profile) => Path.Combine(_root, "custom", "colors",
            $"{profile.Name}.{profile.Id.ToString()[..8]}.json");

        private string EliteFile(ColorProfile profile) =>
            Path.Combine(_root, "custom", "elite-colors", $"{profile.Id:D}.json");

        // An explicit old timestamp detects an identical-byte rewrite without depending on timer resolution.
        private static DateTime MarkFile(string path)
        {
            File.SetLastWriteTimeUtc(path, new DateTime(2005, 8, 19, 14, 00, 00, DateTimeKind.Utc));
            return File.GetLastWriteTimeUtc(path);
        }

        private ColorProfile Add(string name)
        {
            var profile = new ColorProfile(name);
            profile.EliteDrums.KickNote = CustomKick;
            Call("AddPreset", profile);
            return profile;
        }

        [TestCase("ColorProfile", "ColorProfileContainer")]
        [TestCase("HighwayPreset", "HighwayPresetContainer")]
        [TestCase("CameraPreset", "CameraSettingsContainer")]
        [TestCase("EnginePreset", "EnginePresetContainer")]
        [TestCase("RockMeterPreset", "RockMeterPresetContainer")]
        public void ImportPickerReadsDestinationFromArchive(string type, string expectedContainer)
        {
            string path = Path.Combine(_root, "unrelated-name.preset");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("preset.json").Open()))
            {
                writer.Write($"{{\"Type\":\"{type}\"}}");
            }

            var actions = Type.GetType("YARG.Menu.Settings.PresetActions, Assembly-CSharp", true);
            var read = actions.GetMethod("ReadImportDestination", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(read.Invoke(null, new object[] { path }).GetType().Name, Is.EqualTo(expectedContainer));
        }

        [Test]
        public void FirstLoadProtectsInlinePaletteBeforeAnOlderBuildRewritesTheProfile()
        {
            var profile = new ColorProfile("Tour");
            string common = $"{{\"Type\":\"ColorProfile\",\"Name\":\"Tour\",\"Id\":\"{profile.Id}\"," +
                "\"FiveFretGuitar\":{\"RedFret\":\"4A2999FF\"}";
            string legacy = common + ",\"EliteDrums\":{\"KickNote\":\"2375C9FF\"}}";
            string path = BaseFile(profile);
            File.WriteAllText(path, legacy);
            var timestamp = MarkFile(path);

            OpenStore();
            Assert.That(Get(profile.Id).EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomKick.ToArgb()));
            Call("SaveAll");
            Assert.That(File.ReadAllText(path), Is.EqualTo(legacy));
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(timestamp));
            Assert.That(File.Exists(EliteFile(profile)), Is.True);

            // The older writer recognizes the guitar section and omits EliteDrums.
            File.WriteAllText(path, common + "}");
            OpenStore();
            var loaded = Get(profile.Id);
            Assert.That(loaded.EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomKick.ToArgb()));
            Assert.That(loaded.FiveFretGuitar.RedFret.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));
        }

        [Test]
        public void OnlyTheEditedSectionOfTheEditedProfileIsWritten()
        {
            var edited = Add("Edited");
            var other = Add("Other");
            string path = BaseFile(edited);
            string elite = EliteFile(edited);
            string otherPath = BaseFile(other);
            string otherElite = EliteFile(other);
            Assert.That(File.ReadAllText(path), Does.Not.Contain("\"EliteDrums\""));
            var baseTime = MarkFile(path);
            var eliteTime = MarkFile(elite);
            var otherTime = MarkFile(otherPath);
            var otherEliteTime = MarkFile(otherElite);

            Call("SaveAll");
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(baseTime));
            Assert.That(File.GetLastWriteTimeUtc(elite), Is.EqualTo(eliteTime));

            edited.EliteDrums.SnareNote = CustomFret;
            Call("SaveAll");
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(baseTime));
            OpenStore();
            edited = Get(edited.Id);
            Assert.That(edited.EliteDrums.SnareNote.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));

            eliteTime = MarkFile(elite);
            edited.FiveFretGuitar.RedFret = CustomFret;
            Call("SaveAll");
            Assert.That(File.GetLastWriteTimeUtc(elite), Is.EqualTo(eliteTime));
            Assert.That(File.GetLastWriteTimeUtc(otherPath), Is.EqualTo(otherTime));
            Assert.That(File.GetLastWriteTimeUtc(otherElite), Is.EqualTo(otherEliteTime));
            OpenStore();
            Assert.That(Get(edited.Id).FiveFretGuitar.RedFret.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));
        }

        [Test]
        public void UnreadableAndForeignFilesSurviveAlongsideAUsableProfile()
        {
            string folder = Path.Combine(_root, "custom", "colors");
            string malformed = Path.Combine(folder, "broken.json");
            string incompatible = Path.Combine(folder, "foreign.json");
            string foreignJson = $"{{\"Id\":\"{Guid.NewGuid()}\",\"Name\":\"Other\",\"Type\":\"FuturePreset\"}}";
            File.WriteAllText(malformed, "{");
            File.WriteAllText(incompatible, foreignJson);
            var good = Add("Good");
            OpenStore();
            var loaded = Get(good.Id);
            Assert.That(loaded, Is.Not.Null);
            loaded.FiveFretGuitar.RedFret = CustomFret;
            Call("SaveAll");
            Assert.That(File.ReadAllText(malformed), Is.EqualTo("{"));
            Assert.That(File.ReadAllText(incompatible), Is.EqualTo(foreignJson));
            OpenStore();
            Assert.That(Get(good.Id).FiveFretGuitar.RedFret.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));
        }

        [Test]
        public void EditingAClassicColorPreservesUnknownRootAndInstrumentFields()
        {
            var profile = new ColorProfile("Forward");
            string path = BaseFile(profile);
            File.WriteAllText(path,
                $"{{\"Type\":\"ColorProfile\",\"Name\":\"Forward\",\"Id\":\"{profile.Id}\"," +
                "\"FutureInstrument\":{\"Rim\":\"future-rim-value\"}," +
                "\"FiveFretGuitar\":{\"FutureFret\":\"future-fret-value\",\"RedFret\":\"2375C9FF\"}}");
            OpenStore();
            Get(profile.Id).FiveFretGuitar.RedFret = CustomFret;
            Call("SaveAll");
            var saved = File.ReadAllText(path);
            Assert.That(saved, Does.Contain("\"FutureInstrument\"").And.Contain("\"future-rim-value\""));
            Assert.That(saved, Does.Contain("\"FutureFret\"").And.Contain("\"future-fret-value\""));
            OpenStore();
            Assert.That(Get(profile.Id).FiveFretGuitar.RedFret.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));
        }

        [Test]
        public void ReloadAndPassiveSavePreserveAnUnreadablePalette()
        {
            var broken = Add("Broken");
            var healthy = Add("Healthy");
            string path = EliteFile(broken);
            File.WriteAllText(path, "{");
            var timestamp = MarkFile(path);

            OpenStore();
            Get(broken.Id).FiveFretGuitar.RedFret = CustomFret;
            Get(broken.Id).EliteDrums.KickNote = CustomFret;
            Call("SaveAll");
            OpenStore();
            Assert.That(Get(broken.Id).FiveFretGuitar.RedFret.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));
            Assert.That(Get(healthy.Id).EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomKick.ToArgb()));
            Assert.That(File.ReadAllText(path), Is.EqualTo("{"));
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(timestamp));
        }

        [Test]
        public void DuplicateExportWaitsForConfirmationThenRestoresBothPalettes()
        {
            var original = Add("Restore");
            var archive = Path.Combine(_root, "backup.preset");
            Call("ExportPreset", original, archive);
            original.EliteDrums.KickNote = CustomFret;
            original.FiveFretGuitar.RedFret = CustomFret;
            Call("SaveAll");
            var baseFile = BaseFile(original);
            var baseTime = MarkFile(baseFile);
            var eliteTime = MarkFile(EliteFile(original));

            string pending = null;
            Action<ColorProfile, string> prompt = (existing, json) =>
            {
                Assert.That(existing, Is.SameAs(original));
                pending = json;
            };
            Assert.That(Call("ImportWithConflictHandler", archive, prompt), Is.Null);
            Assert.That(pending, Is.Not.Null);
            Assert.That(File.GetLastWriteTimeUtc(baseFile), Is.EqualTo(baseTime));
            Assert.That(File.GetLastWriteTimeUtc(EliteFile(original)), Is.EqualTo(eliteTime));
            Assert.That(Get(original.Id).EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));

            Call("OverwriteImportedProfile", original, pending);
            OpenStore();
            Assert.That(Get(original.Id).EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomKick.ToArgb()));
            Assert.That(Get(original.Id).FiveFretGuitar.RedFret.ToArgb(),
                Is.EqualTo(new ColorProfile("Default").FiveFretGuitar.RedFret.ToArgb()));
        }

        [Test]
        public void OverwritingFromAClassicArchivePreservesEliteAndRollsBackOnWriteFailure()
        {
            var original = Add("Classic");
            string json = $"{{\"Id\":\"{original.Id}\",\"Type\":\"ColorProfile\",\"Name\":\"Restored\"," +
                "\"FiveFretGuitar\":{\"RedFret\":\"4A2999FF\"}}";
            var replaced = (ColorProfile)Call("OverwriteImportedProfile", original, json);
            Assert.That(replaced.EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomKick.ToArgb()));
            Assert.That(replaced.FiveFretGuitar.RedFret.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));
            var path = replaced.Path;
            var saved = File.ReadAllBytes(path);
            var side = EliteFile(replaced);
            File.Delete(side);
            Directory.CreateDirectory(side);
            string incoming = json.Replace("Restored", "Another").TrimEnd('}') +
                "},\"EliteDrums\":{\"KickNote\":\"2375C9FF\"}}";
            Assert.Throws<TargetInvocationException>(() => Call("OverwriteImportedProfile", replaced, incoming));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
            Assert.That(Get(original.Id), Is.SameAs(replaced));
        }

        [Test]
        public void MixedCaseLegacyKeysKeepTheirPaletteAndClassicEdits()
        {
            var profile = new ColorProfile("Mixed");
            var path = BaseFile(profile);
            File.WriteAllText(path, $"{{\"id\":\"{profile.Id}\",\"name\":\"Mixed\",\"type\":\"ColorProfile\"," +
                "\"eliteDrums\":{\"KickNote\":\"2375C9FF\"},\"fiveFretGuitar\":{\"redFret\":\"2375C9FF\"}}");
            OpenStore();
            var loaded = Get(profile.Id);
            Assert.That(loaded.EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomKick.ToArgb()));
            loaded.FiveFretGuitar.RedFret = CustomFret;
            Call("SaveAll");
            Assert.That(File.ReadAllText(path), Does.Contain("redFret").And.Not.Contain("RedFret"));
            Assert.That(File.Exists(EliteFile(profile)), Is.True);
            OpenStore();
            Assert.That(Get(profile.Id).FiveFretGuitar.RedFret.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));
        }

        [Test]
        public void ImportAfterOlderBuildDeletedBaseRecoversTheSurvivingPalette()
        {
            var profile = Add("Recover");
            string archive = Path.Combine(_root, "older.zip");
            Call("ExportPreset", profile, archive);
            profile.EliteDrums.KickNote = CustomFret;
            Call("SaveAll");
            File.Delete(BaseFile(profile));
            OpenStore();
            Call("ImportPreset", archive);
            Assert.That(Get(profile.Id).EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));
            Call("RenamePreset", Get(profile.Id), "RECOVER");
            OpenStore();
            Assert.That(Get(profile.Id).Name, Is.EqualTo("RECOVER"));
            Assert.That(Get(profile.Id).EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));
        }

        [Test]
        public void RenameCopyExportImportAndDeleteFollowThePaletteId()
        {
            var original = Add("First");
            string originalPath = BaseFile(original);
            Call("RenamePreset", original, "Renamed");
            Assert.That(File.Exists(originalPath), Is.False);
            OpenStore();
            original = Get(original.Id);
            Assert.That(original.Name, Is.EqualTo("Renamed"));
            Assert.That(original.EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomKick.ToArgb()));

            var copy = (ColorProfile) original.CopyWithNewName("Copy");
            Call("CopyPreset", original, copy);
            copy.EliteDrums.KickNote = CustomFret;
            Call("SaveAll");
            string archive = Path.Combine(_root, "palette.zip");
            Call("ExportPreset", copy, archive);
            Call("DeletePreset", copy);
            Assert.That(File.Exists(BaseFile(copy)), Is.False);
            Assert.That(File.Exists(EliteFile(copy)), Is.False);

            Call("ImportPreset", archive);
            OpenStore();
            Assert.That(Get(copy.Id).EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomFret.ToArgb()));
            Assert.That(Get(original.Id).EliteDrums.KickNote.ToArgb(), Is.EqualTo(CustomKick.ToArgb()));
        }
    }
}
