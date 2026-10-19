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
        // Reflection calls its real public adapter; only source-presence masks are injected.
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

        private static SongEntry Song(params SourceTier[] sources) => new SelectionSong(sources);

        private readonly struct SourceTier
        {
            public readonly DrumSourceFormat Source;
            public readonly Difficulty Tier;
            public readonly bool ClassicEligible;

            public SourceTier(DrumSourceFormat source, Difficulty tier, bool classicEligible)
            {
                Source = source;
                Tier = tier;
                ClassicEligible = classicEligible;
            }
        }

        private static SourceTier Fact(DrumSourceFormat source, Difficulty tier,
            bool classicEligible = true) => new SourceTier(source, tier, classicEligible);

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

        [TestCase(Instrument.FourLaneDrums)]
        [TestCase(Instrument.ProDrums)]
        [TestCase(Instrument.FiveLaneDrums)]
        [TestCase(Instrument.EliteDrums)]
        public void DifficultySelectComponent_MixedShow_UsesPresenceWithoutLoadingCharts(Instrument output)
        {
            var profile = Profile(output);
            var playerType = RuntimeType("YARG.Player.YargPlayer");
            var roster = Array.CreateInstance(playerType, 1);
            roster.SetValue(Player(profile), 0);
            var gameObject = new GameObject("DifficultySelectPresenceTest");
            try
            {
                var menu = gameObject.AddComponent(RuntimeType("YARG.Menu.DifficultySelect.DifficultySelectMenu"));
                var songs = MixedShow().ToList();
                Call(menu, "InitializeSelection", roster, songs, 0);
                Call(menu, "RefreshSelectionAvailability");
                var difficulties = (IEnumerable)menu.GetType().GetField("_possibleDifficulties",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
                Assert.That(difficulties.Cast<Difficulty>(), Is.EqualTo(new[] { Difficulty.Hard }));
                Assert.That(profile.CurrentInstrument, Is.EqualTo(output));

                string label = (string)Call(menu, "GetDifficultyLabel", Difficulty.Hard);
                Assert.That(label, Does.Not.Contain("Kick").And.Not.Contain("kicks:"));
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                profile.AddSingleModifier(Modifier.Enable2xKicks);
                Assert.That((string)Call(menu, "GetDifficultyLabel", Difficulty.Hard), Is.EqualTo(label));
                Call(menu, "RefreshSelectionAvailability");
                Assert.That(difficulties.Cast<Difficulty>(), Is.EqualTo(new[] { Difficulty.Hard }),
                    "The 2x adjustment must not alter base-tier availability.");
                Assert.That(songs.Cast<SelectionSong>().Select(song => song.LoadCount), Is.All.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void GeneratedClassicParts_DoNotMasqueradeAsAuthoredSources()
        {
            var song = Song(Fact(DrumSourceFormat.Elite, Difficulty.Hard));
            var parts = (AvailableParts)typeof(SongEntry).GetField("_parts",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(song);
            parts.FourLaneDrums.ActivateDifficulty(Difficulty.Hard);
            parts.ProDrums.ActivateDifficulty(Difficulty.Hard);
            parts.FiveLaneDrums.ActivateDifficulty(Difficulty.Hard);
            typeof(SongEntry).GetField("_parts", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(song, parts);
            var resolve = RuntimeType("YARG.Menu.Maestro.MaestroSelectionRules").GetMethod("ResolveMidiDrums");
            foreach (var output in new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums })
            {
                var candidate = resolve.Invoke(null, new object[] { song, output, Difficulty.Hard, Modifier.None });
                Assert.That(Property<DrumSourceFormat>(candidate, "SourceFormat"), Is.EqualTo(DrumSourceFormat.Elite));
                Assert.That(Property<Difficulty>(candidate, "BaseDifficulty"), Is.EqualTo(Difficulty.Hard));
            }
        }

        [Test]
        public void EmptyPresence_DoesNotOfferOutputsOrDifficulties()
        {
            var profile = Profile();
            var session = Session(new[] { Song() }, Player(profile));
            Assert.That(Available<Instrument>(session, "GetAvailableInstruments", profile.Id), Is.Empty);
            Assert.That(Available<Difficulty>(session, "GetAvailableDifficulties", profile.Id), Is.Empty);
        }

        [Test]
        public void ExpertPlusPresence_OffersOnlyExpertBaseTier_RegardlessOf2xAdjustment()
        {
            var profile = Profile();
            var session = Session(new[] { Song(Fact(DrumSourceFormat.FourLane, Difficulty.ExpertPlus)) }, Player(profile));
            foreach (bool enabled in new[] { false, true })
            {
                Call(session, "StageModifier", profile.Id, Modifier.Enable2xKicks, enabled);
                Assert.That(Available<Difficulty>(session, "GetAvailableDifficulties", profile.Id),
                    Is.EqualTo(new[] { Difficulty.Expert }));
            }
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

        [Test]
        public void DifficultyLabels_AllTiersAndOutputs_UseAuthoredKickContentWithOneLoad()
        {
            foreach (var output in new[] { Instrument.FourLaneDrums, Instrument.ProDrums,
                         Instrument.FiveLaneDrums, Instrument.EliteDrums })
            foreach (bool ordinary in new[] { false, true })
            foreach (bool extra in new[] { false, true })
            foreach (bool other in new[] { false, true })
            {
                var facts = (from source in new[] { DrumSourceFormat.FourLane, DrumSourceFormat.FiveLane, DrumSourceFormat.Elite }
                             from tier in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert }
                             select new DrumSourceTierFacts(source, tier, ordinary, extra, other, false, true)).ToArray();
                var song = AuthoredSong(facts);
                var profile = Profile(output);
                WithMenu(profile, new List<SongEntry> { song }, menu =>
                {
                    Call(menu, "RefreshSelectionAvailability");
                    Assert.That(song.LoadCount, Is.Zero, "Availability must remain parse-free.");
                    Call(menu, "LoadSelectedDrumSourcesForLabels");
                    foreach (bool enabled in new[] { false, true })
                    {
                        if (enabled) profile.AddSingleModifier(Modifier.Enable2xKicks);
                        foreach (var tier in new[] { Difficulty.Beginner, Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert })
                        {
                            string label = (string)Call(menu, "GetDifficultyLabel", tier);
                            string suffix = !extra ? "" : enabled ? " (2x Kick)" : ordinary ? " (1x Kick)" : " (0x Kick)";
                            if (!ordinary && !extra && !other) suffix = "";
                            if (suffix.Length == 0) Assert.That(label, Does.Not.Contain("Kick"));
                            else Assert.That(label, Does.EndWith(suffix));
                            Assert.That(label, Does.Not.Contain("kicks:"));
                        }
                        Call(menu, "LoadSelectedDrumSourcesForLabels");
                    }
                    Assert.That(song.LoadCount, Is.EqualTo(1));
                });
            }
        }

        [Test]
        public void DifficultyLabels_OutputTierAndPreferenceChanges_ResolveAgainWithoutLoading()
        {
            var song = AuthoredSong(
                new DrumSourceTierFacts(DrumSourceFormat.Elite, Difficulty.Easy, false, true, true, false, true),
                new DrumSourceTierFacts(DrumSourceFormat.FiveLane, Difficulty.Easy, true, true, true, false, true),
                new DrumSourceTierFacts(DrumSourceFormat.Elite, Difficulty.Hard, true, false, true, false, true));
            var profile = Profile(Instrument.ProDrums);
            WithMenu(profile, new List<SongEntry> { song }, menu =>
            {
                profile.AddSingleModifier(Modifier.EnableEliteUpconversion);
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                Assert.That((string)Call(menu, "GetDifficultyLabel", Difficulty.Beginner), Does.EndWith(" (1x Kick)"));
                profile.AddSingleModifier(Modifier.PreferEliteDowncharts);
                Assert.That((string)Call(menu, "GetDifficultyLabel", Difficulty.Easy), Does.EndWith(" (0x Kick)"));
                profile.CurrentInstrument = Instrument.FiveLaneDrums;
                Assert.That((string)Call(menu, "GetDifficultyLabel", Difficulty.Easy), Does.EndWith(" (1x Kick)"));
                profile.CurrentInstrument = Instrument.EliteDrums;
                Assert.That((string)Call(menu, "GetDifficultyLabel", Difficulty.Easy), Does.EndWith(" (0x Kick)"));
                Assert.That((string)Call(menu, "GetDifficultyLabel", Difficulty.Hard), Does.Not.Contain("Kick"));
                Assert.That(song.LoadCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void DifficultyLabels_LoadScopeAndLifecycle_ReuseOnlyWithinSingleSongContext()
        {
            var first = AuthoredSong(new DrumSourceTierFacts(DrumSourceFormat.Elite, Difficulty.Hard, true, true, true, false, true));
            var second = AuthoredSong(new DrumSourceTierFacts(DrumSourceFormat.Elite, Difficulty.Hard, false, true, true, false, true));
            var profile = Profile();
            var songs = new List<SongEntry> { first };
            WithMenu(profile, songs, menu =>
            {
                Assert.That((string)Call(menu, "GetDifficultyLabel", Difficulty.Hard), Does.Not.Contain("Kick"));
                Assert.That(first.LoadCount, Is.Zero, "Reading labels must not load charts.");
                profile.GameMode = GameMode.FourLaneDrums;
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                Assert.That(first.LoadCount, Is.Zero);
                profile.GameMode = GameMode.EliteDrums;
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                Assert.That(first.LoadCount, Is.EqualTo(1));
                songs.Add(second);
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                Assert.That(second.LoadCount, Is.Zero);
                Assert.That((string)Call(menu, "GetDifficultyLabel", Difficulty.Hard), Does.Not.Contain("Kick"));
                songs.RemoveAt(0);
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                Assert.That(second.LoadCount, Is.EqualTo(1));
                Assert.That((string)Call(menu, "GetDifficultyLabel", Difficulty.Hard), Does.EndWith(" (0x Kick)"));
                Call(menu, "OnDisable");
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                Assert.That(second.LoadCount, Is.EqualTo(2));
                var roster = Array.CreateInstance(RuntimeType("YARG.Player.YargPlayer"), 2);
                roster.SetValue(Player(profile), 0);
                roster.SetValue(Player(Profile()), 1);
                Call(menu, "InitializeSelection", roster, songs, 0);
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                Assert.That(second.LoadCount, Is.EqualTo(3), "Explicit initialization starts a new lifecycle.");
                menu.GetType().GetField("_playerIndex", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(menu, 1);
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                Assert.That((string)Call(menu, "GetDifficultyLabel", Difficulty.Hard), Does.EndWith(" (0x Kick)"));
                Assert.That(second.LoadCount, Is.EqualTo(3), "Players share selected-song analysis within a lifecycle.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DifficultyLabels_EmptyOrFailedLoad_IsPlainAndNotRetried(bool fail)
        {
            var song = AuthoredSong();
            song.FailLoad = fail;
            WithMenu(Profile(), new List<SongEntry> { song }, menu =>
            {
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                Call(menu, "LoadSelectedDrumSourcesForLabels");
                Assert.That((string)Call(menu, "GetDifficultyLabel", Difficulty.Hard), Does.Not.Contain("Kick"));
                Assert.That(song.LoadCount, Is.EqualTo(1));
            });
        }

        private static void WithMenu(YargProfile profile, List<SongEntry> songs, Action<object> test)
        {
            var roster = Array.CreateInstance(RuntimeType("YARG.Player.YargPlayer"), 1);
            roster.SetValue(Player(profile), 0);
            var gameObject = new GameObject("DifficultyLabelTest");
            gameObject.SetActive(false);
            try
            {
                var menu = gameObject.AddComponent(RuntimeType("YARG.Menu.DifficultySelect.DifficultySelectMenu"));
                Call(menu, "InitializeSelection", roster, songs, 0);
                test(menu);
            }
            finally { UnityEngine.Object.DestroyImmediate(gameObject); }
        }

        private static SelectionSong AuthoredSong(params DrumSourceTierFacts[] facts)
        {
            var song = new SelectionSong(facts.Select(fact => Fact(fact.Source, fact.Tier)).ToArray());
            song.Chart = new SongChart(480);
            var collection = song.Chart.AuthoredDrumSources;
            foreach (var fact in facts)
            {
                var tier = (AuthoredDrumSourceTier)Activator.CreateInstance(typeof(AuthoredDrumSourceTier),
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new object[] { fact, null, null, Array.Empty<uint>() }, null);
                typeof(AuthoredDrumSourceCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(collection, new object[] { tier });
            }
            return song;
        }

        private sealed class SelectionSong : SongEntry
        {
            public SelectionSong(SourceTier[] sources)
            {
                var parts = AvailableParts.Default;
                foreach (var source in sources)
                {
                    switch (source.Source)
                    {
                        case DrumSourceFormat.FourLane:
                            parts.NativeFourLaneDrums |= BaseMask(source.Tier);
                            parts.FourLaneDrums.ActivateDifficulty(source.Tier);
                            parts.ProDrums.ActivateDifficulty(source.Tier);
                            break;
                        case DrumSourceFormat.FiveLane:
                            parts.NativeFiveLaneDrums |= BaseMask(source.Tier);
                            parts.FiveLaneDrums.ActivateDifficulty(source.Tier);
                            break;
                        case DrumSourceFormat.Elite:
                            parts.NativeEliteDrums |= BaseMask(source.Tier);
                            parts.EliteDrums.ActivateDifficulty(source.Tier);
                            if (source.ClassicEligible)
                                parts.EliteDrumsDownchart.ActivateDifficulty(source.Tier);
                            break;
                    }
                }
                typeof(SongEntry).GetField("_parts", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(this, parts);
            }

            private static DifficultyMask BaseMask(Difficulty tier) =>
                (DifficultyMask)(1 << (int)(tier == Difficulty.ExpertPlus ? Difficulty.Expert : tier));

            public override EntryType SubType => EntryType.Ini;
            public override string SortBasedLocation => "selection-test";
            public override string ActualLocation => "selection-test";
            public override DateTime GetLastWriteTime() => DateTime.UnixEpoch;
            public SongChart Chart;
            public int LoadCount;
            public bool FailLoad;
            public override SongChart LoadChart(IReadOnlyCollection<Instrument> eliteDrumsDownchartOutputs = null)
            {
                LoadCount++;
                if (FailLoad || Chart == null)
                    throw new InvalidOperationException("Selection chart load unavailable.");
                return Chart;
            }
            public override StemMixer LoadAudio(float speed, double volume, bool enableCensoring, params SongStem[] ignoreStems) => null;
            public override StemMixer LoadPreviewAudio(float speed, bool enableCensoring) => null;
            public override YARGImage LoadAlbumData() => null;
            public override BackgroundResult LoadBackground(bool censoringEnabled, bool excludeYarground = false) => null;
            public override FixedArray<byte> LoadMiloData() => null;
            public override FixedArray<byte> LoadVocData() => null;
        }
    }
}
