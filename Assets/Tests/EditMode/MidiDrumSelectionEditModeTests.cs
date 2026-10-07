using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.IO;
using YARG.Core.Song;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class MidiDrumSelectionEditModeTests
    {
        // The runtime menu assembly is not referenced by the EditMode assembly.
        // Reflection calls its real public adapter; only song facts are injected.
        private static Type RuntimeType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static object Call(object target, string method, params object[] arguments) =>
            target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(info => info.Name == method && info.GetParameters().Length == arguments.Length)
                .Invoke(target, arguments);

        private static T Property<T>(object target, string name) =>
            (T)target.GetType().GetProperty(name).GetValue(target);

        private static object Staged(object session, Guid id) =>
            ((IEnumerable)Property<object>(session, "Players")).Cast<object>()
                .Single(player => Property<Guid>(player, "ProfileId") == id);

        private static YargProfile Profile(Instrument output = Instrument.EliteDrums) =>
            new YargProfile(Guid.NewGuid())
            {
                GameMode = GameMode.EliteDrums,
                CurrentInstrument = output,
                PreferredInstrument = output,
                CurrentDifficulty = Difficulty.Hard,
                IsBot = true,
            };

        private static object Player(YargProfile profile) => Activator.CreateInstance(
            RuntimeType("YARG.Player.YargPlayer"), new object[] { profile, null });

        private static object Session(SongEntry[] songs, params object[] players)
        {
            var roster = Array.CreateInstance(RuntimeType("YARG.Player.YargPlayer"), players.Length);
            for (int index = 0; index < players.Length; index++) roster.SetValue(players[index], index);
            return RuntimeType("YARG.Menu.Maestro.MaestroSetupSession").GetMethod("Create")
                .Invoke(null, new object[] { roster, songs, 0, Guid.Empty });
        }

        private static T[] Available<T>(object session, string method, Guid id) =>
            ((IEnumerable)Call(session, method, id)).Cast<T>().ToArray();

        private static SongEntry Song(params DrumSourceTierFacts[] facts) => new SelectionSong(facts);

        private static DrumSourceTierFacts Fact(DrumSourceFormat source, Difficulty tier,
            bool classicEligible = true) => new DrumSourceTierFacts(source, tier,
                true, true, true, false, classicEligible);

        private static SongEntry[] MixedShow() => new[]
        {
            Song(Fact(DrumSourceFormat.FourLane, Difficulty.Easy),
                Fact(DrumSourceFormat.FourLane, Difficulty.Hard)),
            Song(Fact(DrumSourceFormat.FiveLane, Difficulty.Hard),
                Fact(DrumSourceFormat.FiveLane, Difficulty.Expert)),
        };

        [TestCase(Instrument.FourLaneDrums)]
        [TestCase(Instrument.ProDrums)]
        [TestCase(Instrument.FiveLaneDrums)]
        [TestCase(Instrument.EliteDrums)]
        public void MixedShow_AllOutputs_OnlyOffersCommonBaseTier(Instrument output)
        {
            var profile = Profile(output);
            var type = RuntimeType("YARG.Menu.Maestro.MaestroSetupSession");
            var active = type.GetProperty("Active").GetValue(null);
            var session = Session(MixedShow(), Player(profile));
            Assert.That(type.GetProperty("Active").GetValue(null), Is.SameAs(active));
            Assert.That(Available<Instrument>(session, "GetAvailableInstruments", profile.Id),
                Is.EqualTo(new[] { Instrument.FourLaneDrums, Instrument.ProDrums,
                    Instrument.FiveLaneDrums, Instrument.EliteDrums }));
            Assert.That(Available<Difficulty>(session, "GetAvailableDifficulties", profile.Id),
                Is.EqualTo(new[] { Difficulty.Hard }));
            Assert.That(Available<Instrument>(session, "GetAvailableEliteDrumsDownchartTargets", profile.Id), Is.Empty);
            Call(session, "StageDifficulty", profile.Id, Difficulty.ExpertPlus);
            Assert.That(Property<Difficulty>(Staged(session, profile.Id), "Difficulty"), Is.EqualTo(Difficulty.Hard));
            Assert.That(profile.CurrentModifiers, Is.EqualTo(Modifier.None), "Availability must not initialize live preferences.");
        }

        [Test]
        public void DisabledUpconversion_RemovesEliteButKeepsThreeClassicOutputs()
        {
            var profile = Profile(Instrument.ProDrums);
            var session = Session(MixedShow(), Player(profile));
            Call(session, "StageModifier", profile.Id, Modifier.EnableEliteUpconversion, false);
            Assert.That(Available<Instrument>(session, "GetAvailableInstruments", profile.Id),
                Is.EqualTo(new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums }));
            Call(session, "StageInstrument", profile.Id, Instrument.EliteDrums);
            Assert.That(Property<Instrument>(Staged(session, profile.Id), "Instrument"), Is.EqualTo(Instrument.ProDrums));
            Assert.That(Property<Instrument>(Staged(session, profile.Id), "PreferredInstrument"), Is.EqualTo(Instrument.ProDrums));
        }

        [Test]
        public void SourcePreference_NativeWins_AndOnlyChangesNonNativeFallback()
        {
            var resolve = RuntimeType("YARG.Menu.Maestro.MaestroSelectionRules").GetMethod("ResolveMidiDrums");
            var all = Song(Fact(DrumSourceFormat.FourLane, Difficulty.Hard),
                Fact(DrumSourceFormat.FiveLane, Difficulty.Hard), Fact(DrumSourceFormat.Elite, Difficulty.Hard));
            foreach (bool prefer in new[] { false, true })
            {
                var flags = Modifier.EnableEliteUpconversion | (prefer ? Modifier.PreferEliteDowncharts : Modifier.None);
                foreach (var pair in new[]
                {
                    (Instrument.FourLaneDrums, DrumSourceFormat.FourLane),
                    (Instrument.ProDrums, DrumSourceFormat.FourLane),
                    (Instrument.FiveLaneDrums, DrumSourceFormat.FiveLane),
                    (Instrument.EliteDrums, DrumSourceFormat.Elite),
                })
                {
                    var result = resolve.Invoke(null, new object[] { all, pair.Item1, Difficulty.Hard, flags });
                    Assert.That(Property<DrumSourceFormat>(result, "SourceFormat"), Is.EqualTo(pair.Item2));
                }
            }
            var fallback = Song(Fact(DrumSourceFormat.FiveLane, Difficulty.Hard), Fact(DrumSourceFormat.Elite, Difficulty.Hard));
            var ordinary = resolve.Invoke(null, new object[] { fallback, Instrument.ProDrums, Difficulty.Hard, Modifier.None });
            var preferred = resolve.Invoke(null, new object[] { fallback, Instrument.ProDrums, Difficulty.Hard, Modifier.PreferEliteDowncharts });
            Assert.That(Property<DrumSourceFormat>(ordinary, "SourceFormat"), Is.EqualTo(DrumSourceFormat.FiveLane));
            Assert.That(Property<DrumSourceFormat>(preferred, "SourceFormat"), Is.EqualTo(DrumSourceFormat.Elite));
        }

        [Test]
        public void PopulatedCommit_PersistsExplicitOutputTierAndSavedMidiPreferences()
        {
            var profile = Profile();
            var session = Session(MixedShow(), Player(profile));
            Call(session, "StageInstrument", profile.Id, Instrument.FiveLaneDrums);
            Call(session, "StageModifier", profile.Id, Modifier.Enable2xKicks, true);
            Call(session, "StageModifier", profile.Id, Modifier.PreferEliteDowncharts, true);
            Call(session, "StageModifier", profile.Id, Modifier.EnableEliteUpconversion, false);
            var expected = Modifier.Enable2xKicks | Modifier.PreferEliteDowncharts;
            Assert.That(profile.PreferredInstrument, Is.EqualTo(Instrument.EliteDrums));
            var result = Call(session, "TryCommit");
            Assert.That(Property<bool>(result, "Success"), Is.True, Property<string>(result, "GlobalError"));
            Assert.That(profile.CurrentInstrument, Is.EqualTo(Instrument.FiveLaneDrums));
            Assert.That(profile.PreferredInstrument, Is.EqualTo(Instrument.FiveLaneDrums));
            Assert.That(profile.CurrentDifficulty, Is.EqualTo(Difficulty.Hard));
            Assert.That(profile.DifficultyFallback, Is.EqualTo(Difficulty.Hard));
            Assert.That(profile.EliteDrumsDownchartTarget, Is.Null);
            Assert.That(profile.CurrentModifiers, Is.EqualTo(expected));
            profile.RestoreSavedModifiers();
            profile.InitializeLiveMidiDrumModifiers();
            Assert.That(profile.CurrentModifiers, Is.EqualTo(expected), "Explicit off must remain off after saved preferences are restored.");
            var next = Session(MixedShow(), Player(profile));
            Assert.That(Property<Instrument>(Staged(next, profile.Id), "Instrument"), Is.EqualTo(Instrument.FiveLaneDrums));
            Assert.That(Property<Modifier>(Staged(next, profile.Id), "Modifiers"), Is.EqualTo(expected));
        }

        [Test]
        public void PopulatedRejectedCommit_DoesNotPartiallyApplyOtherPlayers()
        {
            var valid = Profile(Instrument.ProDrums);
            var invalid = Profile();
            var session = Session(MixedShow(), Player(valid), Player(invalid));
            Call(session, "StageInstrument", valid.Id, Instrument.FiveLaneDrums);
            Call(session, "StageModifier", valid.Id, Modifier.Enable2xKicks, true);
            // Disable conversion without normalizing the explicit Elite request: validation
            // must reject it, not partially commit the preceding eligible player.
            var staged = Staged(session, invalid.Id);
            staged.GetType().GetProperty("Modifiers").SetValue(staged, Modifier.None);
            var result = Call(session, "TryCommit");
            Assert.That(Property<bool>(result, "Success"), Is.False);
            Assert.That(((IDictionary)Property<object>(result, "PlayerErrors")).Contains(invalid.Id), Is.True);
            Assert.That(valid.CurrentInstrument, Is.EqualTo(Instrument.ProDrums));
            Assert.That(valid.PreferredInstrument, Is.EqualTo(Instrument.ProDrums));
            Assert.That(valid.CurrentModifiers, Is.EqualTo(Modifier.None));
            Assert.That(invalid.CurrentInstrument, Is.EqualTo(Instrument.EliteDrums));
            Assert.That(invalid.CurrentModifiers, Is.EqualTo(Modifier.None));
        }

        [Test]
        public void EliteIneligibleForClassic_StillOffersElite_AndSnapshotRestoreIsIdempotent()
        {
            var profile = Profile();
            var player = Player(profile);
            var session = Session(new[] { Song(Fact(DrumSourceFormat.Elite, Difficulty.Hard, false)) }, player);
            Assert.That(Available<Instrument>(session, "GetAvailableInstruments", profile.Id), Is.EqualTo(new[] { Instrument.EliteDrums }));
            var snapshotType = session.GetType().GetNestedType("ProfileSnapshot", BindingFlags.NonPublic);
            var snapshot = Activator.CreateInstance(snapshotType, new[] { player });
            profile.InitializeLiveMidiDrumModifiers();
            profile.AddSingleModifier(Modifier.Enable2xKicks);
            profile.PreferredInstrument = Instrument.ProDrums;
            profile.EliteDrumsDownchartTarget = Instrument.ProDrums;
            for (int index = 0; index < 2; index++)
            {
                Call(snapshot, "Restore");
                Assert.That(profile.CurrentModifiers, Is.EqualTo(Modifier.None));
                Assert.That(profile.PreferredInstrument, Is.EqualTo(Instrument.EliteDrums));
                Assert.That(profile.EliteDrumsDownchartTarget, Is.Null);
            }
            profile.InitializeLiveMidiDrumModifiers();
            Assert.That(profile.CurrentModifiers, Is.EqualTo(Modifier.EnableEliteUpconversion));
        }

        [Test]
        public void DifficultySelectComponent_RefreshPreservesClassicEliteDownchartTarget()
        {
            var song = new SelectionSong(new[] { Fact(DrumSourceFormat.Elite, Difficulty.Hard) });
            var parts = AvailableParts.Default;
            parts.EliteDrumsDownchart.ActivateDifficulty(Difficulty.Hard);
            typeof(SongEntry).GetField("_parts", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(song, parts);

            var profile = new YargProfile(Guid.NewGuid())
            {
                GameMode = GameMode.FourLaneDrums,
                CurrentInstrument = Instrument.FourLaneDrums,
                PreferredInstrument = Instrument.FourLaneDrums,
                CurrentDifficulty = Difficulty.Hard,
            };
            var playerType = RuntimeType("YARG.Player.YargPlayer");
            var player = Activator.CreateInstance(playerType, new object[] { profile, null });
            var roster = Array.CreateInstance(playerType, 1);
            roster.SetValue(player, 0);
            var gameObject = new GameObject("DifficultySelectSelectionTest");
            try
            {
                var menu = gameObject.AddComponent(RuntimeType("YARG.Menu.DifficultySelect.DifficultySelectMenu"));
                Call(menu, "InitializeSelection", roster, new List<SongEntry> { song }, 0);
                Call(menu, "RefreshSelectionAvailability");

                var targets = (IEnumerable)menu.GetType().GetField("_eliteDrumsDownchartTargets",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
                Assert.That(targets.Cast<Instrument>(), Does.Contain(Instrument.FourLaneDrums),
                    "Classic four-lane selection should retain the generated Elite downchart choice.");

                profile.EliteDrumsDownchartTarget = Instrument.FourLaneDrums;
                Call(menu, "RefreshSelectionAvailability");
                Assert.That(profile.EliteDrumsDownchartTarget, Is.EqualTo(Instrument.FourLaneDrums),
                    "Refreshing production availability must preserve an offered explicit target.");
                Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(profile), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private sealed class SelectionSong : SongEntry
        {
            public SelectionSong(DrumSourceTierFacts[] facts)
            {
                typeof(SongEntry).GetField("_authoredDrumSourceFacts", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(this, Array.AsReadOnly(facts));
            }

            public override EntryType SubType => EntryType.Ini;
            public override string SortBasedLocation => "selection-test";
            public override string ActualLocation => "selection-test";
            public override DateTime GetLastWriteTime() => DateTime.UnixEpoch;
            public override SongChart LoadChart(IReadOnlyCollection<Instrument> eliteDrumsDownchartOutputs = null) => null;
            public override StemMixer LoadAudio(float speed, double volume, bool enableCensoring, params SongStem[] ignoreStems) => null;
            public override StemMixer LoadPreviewAudio(float speed, bool enableCensoring) => null;
            public override YARGImage LoadAlbumData() => null;
            public override BackgroundResult LoadBackground(bool censoringEnabled, bool excludeYarground = false) => null;
            public override FixedArray<byte> LoadMiloData() => null;
            public override FixedArray<byte> LoadVocData() => null;
        }
    }
}
