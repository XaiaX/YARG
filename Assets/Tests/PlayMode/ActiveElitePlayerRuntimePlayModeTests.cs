using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;
using Object = UnityEngine.Object;

namespace YARG.Tests.PlayMode
{
    /// <summary>
    /// Exercises an active production Elite player under an explicitly initialized gameplay manager.
    /// The manager/player lifecycle and engine input path are real; full song-scene audio and HUD
    /// initialization are intentionally not part of this focused runtime fixture.
    /// </summary>
    public sealed class ActiveElitePlayerRuntimePlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly List<GameObject> _ownedObjects = new();
        private readonly Dictionary<object, SongChart> _chartsBySession = new();
        private readonly List<object> _sessions = new();
        private readonly Type _gameManagerType = ProductionType("YARG.Gameplay.GameManager");
        private object _gameManager;
        private GameObject _managerObject;
        private EngineManager _engineManager;
        private int _originalHighwayCount;

        private static Type ProductionType(string name)
        {
            var type = Type.GetType(name + ", Assembly-CSharp");
            if (type == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(name);
                    if (type != null)
                    {
                        break;
                    }
                }
            }

            Assert.That(type, Is.Not.Null, name + " must exist in loaded Unity assemblies");
            return type;
        }

        [UnityTest]
        public IEnumerator ActivePlayerRuntime_ConsumesPreparedNotesScoresRoutesWildcardAndResetsTwice()
        {
            _originalHighwayCount = GetHighwayCount();
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null),
                "This active production-camera fixture requires a graphics-enabled batchmode run.");

            for (int run = 0; run < 2; run++)
            {
                var chart = CreateChart();
                var nativeProfile = NewProfile(Instrument.EliteDrums, Difficulty.Hard, Modifier.Enable2xKicks);
                var nativeSession = CreatePreparedSession(nativeProfile, chart, Instrument.EliteDrums);
                var kickOnProfile = NewProfile(Instrument.FiveLaneDrums, Difficulty.Hard, Modifier.Enable2xKicks);
                var kickOnSession = CreatePreparedSession(kickOnProfile, chart, Instrument.FiveLaneDrums);
                var kickOffProfile = NewProfile(Instrument.FiveLaneDrums, Difficulty.Hard, Modifier.None);
                var kickOffSession = CreatePreparedSession(kickOffProfile, chart, Instrument.FiveLaneDrums);
                var noExtraProfile = NewProfile(Instrument.FiveLaneDrums, Difficulty.Medium, Modifier.Enable2xKicks);
                var noExtraSession = CreatePreparedSession(noExtraProfile, chart, Instrument.FiveLaneDrums);

                // Highway ordering is profile state read during real player initialization.
                SetupConditionalKickOrdering(kickOnProfile);
                SetupConditionalKickOrdering(kickOffProfile);
                SetupConditionalKickOrdering(noExtraProfile);

                _engineManager = new EngineManager();
                _managerObject = Own(new GameObject("Explicit gameplay runtime " + run));
                _managerObject.SetActive(false);
                _gameManager = _managerObject.AddComponent(_gameManagerType);
                var beatHandler = Activator.CreateInstance(
                    ProductionType("YARG.Playback.BeatEventHandler"), chart.SyncTrack);
                var roster = (System.Collections.IList) Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(ProductionType("YARG.Player.YargPlayer")));
                foreach (var session in new[] { nativeSession, kickOnSession, kickOffSession, noExtraSession })
                {
                    roster.Add(session);
                }
                _gameManagerType.GetMethod("InitializeRuntime").Invoke(_gameManager, new object[]
                {
                    null, null, chart, roster,
                    _engineManager, beatHandler, null, false, true
                });
                _managerObject.SetActive(true);

                // A real ThemeManager provides the themed note prefab the players' real
                // SetupTheme path consumes; its Start populates default theme containers.
                var themeManagerObject = Own(new GameObject("ThemeManager"));
                themeManagerObject.AddComponent(ProductionType("YARG.Themes.ThemeManager"));

                // Real TrackPlayer.Initialize parents players under a "Visuals" object and
                // consumes a TrackView; provide both without scene, menu, or audio bootstrap.
                Own(new GameObject("Visuals"));
                // Instantiate from a deactivated source: the TrackView prefab contains HUD
                // GameplayBehaviours whose Awake subscribes to SongStarted (invoked
                // immediately when the runtime says the song already started) and needs
                // scene UI this fixture intentionally does not build.
                var trackViewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Gameplay/HUD/TrackView.prefab");
                Assert.That(trackViewPrefab, Is.Not.Null, "The production TrackView prefab is required.");
                bool trackViewWasActive = trackViewPrefab.activeSelf;
                trackViewPrefab.SetActive(false);
                Component trackView;
                try
                {
                    var trackViewObject = Own(Object.Instantiate(trackViewPrefab));
                    trackViewObject.SetActive(false);
                    trackView = trackViewObject.GetComponentInChildren(
                        ProductionType("YARG.Gameplay.HUD.TrackView"), true);
                }
                finally
                {
                    trackViewPrefab.SetActive(trackViewWasActive);
                }
                Assert.That(trackView, Is.Not.Null, "The TrackView prefab must contain a TrackView.");

                // Beatlines and the manager must exist before players; players must be
                // ACTIVE before Initialize so GameplayBehaviour.Awake resolves the
                // manager and subscribes its lifecycle events, exactly as in production.
                // Let ThemeManager.Start populate containers before players exist.
                yield return null;

                var nativeObject = ClonePlayerPrefab("Active native Elite player " + run,
                    out var nativePlayer);
                var kickOnObject = CloneClassicDrumsPrefab("2x lane on player " + run, out var kickOnPlayer);
                var kickOffObject = CloneClassicDrumsPrefab("2x lane off player " + run, out var kickOffPlayer);
                var noExtraObject = CloneClassicDrumsPrefab("No extra content player " + run, out var noExtraPlayer);

                chart.SyncTrack.GenerateBeatlines(10d, true);
                // Activate and initialize in the SAME frame: Awake resolves the manager
                // synchronously, while Start (which reads Player) is deferred until after
                // this frame's initialization has set it.
                nativeObject.SetActive(true);
                kickOnObject.SetActive(true);
                kickOffObject.SetActive(true);
                noExtraObject.SetActive(true);

                InitializePlayer(nativePlayer, nativeSession, 0, trackView, chart);
                InitializePlayer(kickOnPlayer, kickOnSession, 1, trackView, chart);
                InitializePlayer(kickOffPlayer, kickOffSession, 2, trackView, chart);
                InitializePlayer(noExtraPlayer, noExtraSession, 3, trackView, chart);

                yield return null;

                Assert.That(nativePlayer, Is.Not.Null, "The active prefab player must survive Awake/Start.");
                Assert.That(nativePlayer.gameObject.activeInHierarchy, Is.True);
                Assert.That(((Behaviour) nativePlayer).enabled, Is.True);
                var resolvedPlayback = (ResolvedDrumPlayback) nativeSession.GetType()
                    .GetProperty("ResolvedDrumPlayback").GetValue(nativeSession);
                Assert.That(resolvedPlayback, Is.Not.Null);
                Assert.That(resolvedPlayback.RequestedOutput, Is.EqualTo(Instrument.EliteDrums));
                var preparedElite = (InstrumentDifficulty<EliteDrumNote>) nativeSession.GetType()
                    .GetProperty("PlayableEliteDrumDifficulty").GetValue(nativeSession);
                Assert.That(preparedElite.Notes, Has.Count.EqualTo(3));

                var engine = GetPlayerEngine(nativePlayer);
                Assert.That(engine, Is.Not.Null,
                    "Real initialization must create and subscribe the engine itself.");
                QueuePlayerInput(nativePlayer, EliteDrumsAction.EliteSnare, 1d);
                UpdatePlayerEngine(nativePlayer, 1d);
                Assert.That(preparedElite.Notes[0].WasHit, Is.True);
                Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
                Assert.That(engine.EngineStats.CommittedScore, Is.GreaterThan(0));

                var route = nativePlayer.GetType().GetMethod("AnimateUnmatchedAction", INSTANCE);
                Assert.That((int) EliteDrumsAction.WildcardPad, Is.EqualTo(13));
                Assert.That((int) EliteDrumPad.Wildcard, Is.EqualTo(10));
                Assert.That((int) nativePlayer.GetType().GetMethod("GetLane").Invoke(null,
                    new object[] { (int) EliteDrumPad.Tom1 }), Is.EqualTo(2),
                    "The wildcard action's fallback physical feedback must map to the center fret.");
                // Wildcards accept any compatible physical strike. Use an allowed native Elite
                // action so the production InterceptInput path routes it to the engine.
                QueuePlayerInput(nativePlayer, EliteDrumsAction.EliteSnare, 2d);
                UpdatePlayerEngine(nativePlayer, 2d);
                Assert.That(preparedElite.Notes[1].WasHit, Is.True,
                    "Wildcard feedback must follow the real accepted-action -> scoring -> pad-hit callback path.");
                Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
                Assert.That(GetFretArray(nativeObject).GetComponentsInChildren(
                    ProductionType("YARG.Gameplay.Visuals.Fret"), true).Length, Is.EqualTo(5));
                var colors = new ColorProfile("Overhit effects");
                colors.EliteDrums.LeftCrashInputEffect = System.Drawing.Color.Red;
                colors.EliteDrums.Tom1InputEffect = System.Drawing.Color.Blue;
                colors.EliteDrums.RideInputEffect = System.Drawing.Color.Green;
                nativeSession.GetType().GetProperty("ColorProfile").SetValue(nativeSession, colors);
                var pending = nativePlayer.GetType().GetField("_pendingOverhitAction", INSTANCE);
                var overhit = nativePlayer.GetType().GetMethod("OnEliteOverhit", INSTANCE);
                var frets = (IDictionary) GetFretArray(nativeObject).GetType().GetField("_frets", INSTANCE)
                    .GetValue(GetFretArray(nativeObject));
                void AssertMissEffect(EliteDrumsAction action, EliteDrumPad pad, System.Drawing.Color expected)
                {
                    pending.SetValue(nativePlayer, (EliteDrumsAction?) action);
                    Assert.DoesNotThrow(() => overhit.Invoke(nativePlayer, null), action.ToString());
                    Assert.That(pending.GetValue(nativePlayer), Is.Null);
                    var fret = (Component) frets[(int) pad];
                    var theme = fret.GetType().GetProperty("ThemeBind").GetValue(fret);
                    var missEffect = (Component) theme.GetType().GetProperty("MissEffect").GetValue(theme);
                    var particle = missEffect.GetComponentsInChildren<ParticleSystem>(true).First();
                    var effectParticle = particle.GetComponent(ProductionType("YARG.Helpers.Authoring.EffectParticle"));
                    var actual = (UnityEngine.Color) effectParticle.GetType().GetProperty("InitialColor", INSTANCE)
                        .GetValue(effectParticle);
                    Assert.That(actual.r, Is.EqualTo(expected.R / 255f).Within(0.005f), action.ToString());
                    Assert.That(actual.g, Is.EqualTo(expected.G / 255f).Within(0.005f), action.ToString());
                    Assert.That(actual.b, Is.EqualTo(expected.B / 255f).Within(0.005f), action.ToString());
                    Assert.That(particle.isPlaying, Is.True, "Overhits retain miss particle feedback.");
                    Assert.That(preparedElite.Notes[2].WasHit, Is.False, "The overhit occurs within the chart.");
                }
                AssertMissEffect(EliteDrumsAction.EliteLeftCrash, EliteDrumPad.LeftCrash, System.Drawing.Color.Red);
                AssertMissEffect(EliteDrumsAction.EliteTom1, EliteDrumPad.Tom1, System.Drawing.Color.Blue);
                AssertMissEffect(EliteDrumsAction.FourLaneBlueDrum, EliteDrumPad.Ride, System.Drawing.Color.Green);
                Assert.That(colors.EliteDrums.LeftCrashTom1Particles.ToArgb(),
                    Is.Not.EqualTo(colors.EliteDrums.Tom1InputEffect.ToArgb()));

                Assert.That(GetResolvedPlayback(kickOnSession).EffectiveExtraContent, Is.True);
                Assert.That(GetResolvedPlayback(kickOffSession).EffectiveExtraContent, Is.False);
                Assert.That(GetResolvedPlayback(noExtraSession).EffectiveExtraContent, Is.False);
                Assert.That(GetLaneCount(kickOnPlayer), Is.EqualTo(7));
                Assert.That(GetLaneCount(kickOffPlayer), Is.EqualTo(6));
                Assert.That(GetLaneCount(noExtraPlayer), Is.EqualTo(6));

                UnregisterPlayerEngine(nativePlayer);
                _engineManager.Reset();
                _managerObject.SetActive(false);
                foreach (var playerObject in new[] { nativeObject, kickOnObject, kickOffObject, noExtraObject })
                {
                    playerObject.SetActive(false);
                }
                yield return null;
                DestroyOwnedObjects();
                DisposeSessions();
                SetHighwayCount(_originalHighwayCount);
                Assert.That(GetHighwayCount(), Is.EqualTo(_originalHighwayCount),
                    "TrackPlayer.HighwayCount must return to its pre-fixture value after each pass.");
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_engineManager != null)
            {
                _engineManager.Reset();
            }

            foreach (var value in _ownedObjects)
            {
                if (value != null)
                {
                    value.SetActive(false);
                    Object.Destroy(value);
                }
            }
            _ownedObjects.Clear();
            yield return null;
            DisposeSessions();
            SetHighwayCount(_originalHighwayCount);
        }

        private SongChart CreateChart()
        {
            var chart = new SongChart(480);
            chart.SyncTrack.Tempos.Add(new TempoChange(120, 0, 0));
            chart.SyncTrack.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0, 0, 0, 0, 16, 4));

            var elite = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Hard);
            elite.Notes.Add(new EliteDrumNote(EliteDrumPad.Snare, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, 1d, 480, false));
            elite.Notes.Add(new EliteDrumNote(EliteDrumPad.Wildcard, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, 2d, 960, false));
            elite.Notes.Add(new EliteDrumNote(EliteDrumPad.Kick, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, 3d, 1440, true));

            var fiveLaneExtra = new InstrumentDifficulty<DrumNote>(Instrument.FiveLaneDrums, Difficulty.Hard);
            fiveLaneExtra.Notes.Add(new DrumNote(FiveLaneDrumPad.Kick, DrumNoteType.Neutral,
                DrumNoteFlags.None, NoteFlags.None, 1d, 480));
            fiveLaneExtra.Notes.Add(new DrumNote(FiveLaneDrumPad.Kick, DrumNoteType.Neutral,
                DrumNoteFlags.None, NoteFlags.None, 1.5d, 720, true));
            fiveLaneExtra.Notes.Add(new DrumNote(FiveLaneDrumPad.Red, DrumNoteType.Neutral,
                DrumNoteFlags.None, NoteFlags.None, 2d, 960));

            var fiveLaneNoExtra = new InstrumentDifficulty<DrumNote>(Instrument.FiveLaneDrums, Difficulty.Medium);
            fiveLaneNoExtra.Notes.Add(new DrumNote(FiveLaneDrumPad.Kick, DrumNoteType.Neutral,
                DrumNoteFlags.None, NoteFlags.None, 1d, 480));
            fiveLaneNoExtra.Notes.Add(new DrumNote(FiveLaneDrumPad.Red, DrumNoteType.Neutral,
                DrumNoteFlags.None, NoteFlags.None, 2d, 960));

            var sources = new AuthoredDrumSourceCollection();
            AddAuthoredSource(sources, new DrumSourceTierFacts(DrumSourceFormat.Elite, Difficulty.Hard,
                true, true, true, false, true), null, elite);
            AddAuthoredSource(sources, new DrumSourceTierFacts(DrumSourceFormat.FiveLane, Difficulty.Hard,
                true, true, true, false, true), fiveLaneExtra, null);
            AddAuthoredSource(sources, new DrumSourceTierFacts(DrumSourceFormat.FiveLane, Difficulty.Medium,
                true, false, true, false, true), fiveLaneNoExtra, null);
            typeof(SongChart).GetProperty(nameof(SongChart.AuthoredDrumSources), INSTANCE)
                .GetSetMethod(true).Invoke(chart, new object[] { sources });
            return chart;
        }

        private static void AddAuthoredSource(AuthoredDrumSourceCollection collection,
            DrumSourceTierFacts facts, InstrumentDifficulty<DrumNote> classic,
            InstrumentDifficulty<EliteDrumNote> elite)
        {
            var sourceType = typeof(AuthoredDrumSourceTier);
            var constructor = sourceType.GetConstructor(INSTANCE, null, new[]
            {
                typeof(DrumSourceTierFacts), typeof(InstrumentDifficulty<DrumNote>),
                typeof(InstrumentDifficulty<EliteDrumNote>), typeof(IEnumerable<uint>)
            }, null);
            var source = constructor.Invoke(new object[] { facts, classic, elite, Array.Empty<uint>() });
            typeof(AuthoredDrumSourceCollection).GetMethod("Add", INSTANCE).Invoke(collection,
                new[] { source });
        }

        private YargProfile NewProfile(Instrument instrument, Difficulty difficulty, Modifier modifiers)
        {
            var profile = new YargProfile
            {
                GameMode = GameMode.EliteDrums,
                CurrentInstrument = instrument,
                PreferredInstrument = instrument,
                CurrentDifficulty = difficulty,
                UseCymbalModels = true,
            };
            if (modifiers != Modifier.None)
            {
                profile.AddSingleModifier(modifiers);
            }

            return profile;
        }

        private object CreatePreparedSession(YargProfile profile, SongChart chart, Instrument output)
        {
            var bindings = Activator.CreateInstance(ProductionType("YARG.Input.ProfileBindings"), profile);
            var session = Activator.CreateInstance(ProductionType("YARG.Player.YargPlayer"), profile, bindings);
            session.GetType().GetMethod("RefreshPresets").Invoke(session, null);
            var facts = chart.AuthoredDrumSources.Tiers.Values.Select(source => source.Facts).ToArray();
            var resolved = DrumOutputResolver.Resolve(facts, output, profile.CurrentDifficulty,
                profile.IsModifierActive(Modifier.EnableEliteUpconversion),
                profile.IsModifierActive(Modifier.Enable2xKicks),
                profile.IsModifierActive(Modifier.PreferEliteDowncharts));
            Assert.That(resolved, Is.Not.Null, $"Expected resolved {output} playback at {profile.CurrentDifficulty}.");
            var prepared = DrumPlaybackPreparer.Prepare(chart, resolved,
                notes => profile.ApplyModifiers(notes, chart.SyncTrack),
                notes => profile.ApplyModifiers(notes, chart.SyncTrack));
            session.GetType().GetMethod("SetDrumPlayback").Invoke(session,
                new object[] { resolved, prepared.Classic, prepared.Elite });
            _chartsBySession.Add(session, chart);
            _sessions.Add(session);
            return session;
        }

        private GameObject ClonePlayerPrefab(string name, out Component player)
        {
            var clone = CloneDrumsPrefab(name, out var stockPlayer);
            var nativeType = ProductionType("YARG.Gameplay.Player.EliteDrumsPlayer");
            var trackPlayerType = ProductionType("YARG.Gameplay.Player.TrackPlayer");
            player = clone.AddComponent(nativeType);
            // Copy serialized references from the shared base hierarchy only
            // (TrackPlayer<TEngine,TNote> and below): DrumsPlayer-declared fields do
            // not exist on EliteDrumsPlayer, but the generic base declares pools/HUD
            // references the non-generic base does not, and the real Initialize path
            // dereferences them.
            var copied = new HashSet<string>();
            for (var level = stockPlayer.GetType().BaseType; level != null && level != typeof(MonoBehaviour);
                level = level.BaseType)
            {
                foreach (var field in level.GetFields(INSTANCE))
                {
                    if (!field.IsDefined(typeof(SerializeField), true) || !copied.Add(field.Name))
                    {
                        continue;
                    }

                    field.SetValue(player, field.GetValue(stockPlayer));
                }
            }

            nativeType.GetMethod("SetTrackCameraForNativeElite").Invoke(player,
                new[] { trackPlayerType.GetProperty("TrackCamera").GetValue(stockPlayer) });
            nativeType.GetMethod("SetDrumComponentsForNativeElite").Invoke(player, new[]
            {
                clone.GetComponentInChildren(ProductionType("YARG.Gameplay.Visuals.FretArray"), true),
                clone.GetComponentInChildren(ProductionType("YARG.Gameplay.Visuals.KickFretFlash"), true)
            });
            Object.DestroyImmediate(stockPlayer);
            DisableUnneededPrefabBehaviours(clone, player);
            return clone;
        }

        private GameObject CloneClassicDrumsPrefab(string name, out Component player)
        {
            var clone = CloneDrumsPrefab(name, out player);
            DisableUnneededPrefabBehaviours(clone, player);
            return clone;
        }

        private GameObject CloneDrumsPrefab(string name, out Component player)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/FiveLaneDrumsVisual.prefab");
            Assert.That(prefab, Is.Not.Null, "The production five-lane player prefab is required.");
            bool wasActive = prefab.activeSelf;
            prefab.SetActive(false);
            GameObject clone;
            try
            {
                clone = Own(Object.Instantiate(prefab));
            }
            finally
            {
                prefab.SetActive(wasActive);
            }

            clone.name = name;
            clone.SetActive(false);
            player = clone.GetComponent(ProductionType("YARG.Gameplay.Player.DrumsPlayer"));
            return clone;
        }

        private static void DisableUnneededPrefabBehaviours(GameObject clone, Component player)
        {
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != player && behaviour.GetType().Name != "FretArray" &&
                    behaviour.GetType().Name != "KickFretFlash")
                {
                    behaviour.enabled = false;
                }
            }
        }

        /// <summary>
        /// Invokes the REAL production TrackPlayer.Initialize path: the player wires its own
        /// session, chart, prepared note track, engine, callbacks, and combo meter. No
        /// reflection-set engines and no cleared callbacks.
        /// </summary>
        private static void InitializePlayer(Component player, object session, int index,
            Component trackView, SongChart chart)
        {
            player.GetType().GetMethod("Initialize", INSTANCE, null, new[]
            {
                typeof(int), ProductionType("YARG.Player.YargPlayer"), typeof(SongChart),
                ProductionType("YARG.Gameplay.HUD.TrackView"), typeof(StemMixer), typeof(int?)
            }, null).Invoke(player, new object[] { index, session, chart, trackView, null, null });
        }

        private static EliteDrumsEngine GetPlayerEngine(Component player) =>
            (EliteDrumsEngine) player.GetType().BaseType.GetProperty("Engine", INSTANCE).GetValue(player);

        private static void SetupConditionalKickOrdering(YargProfile profile)
        {
            profile.FiveLaneDrumsHighwayOrdering = new[]
            {
                DrumsHighwayItem.Kick1x, DrumsHighwayItem.Kick2xConditional, DrumsHighwayItem.Red,
                DrumsHighwayItem.Yellow, DrumsHighwayItem.Blue, DrumsHighwayItem.Orange,
                DrumsHighwayItem.Green
            };
            profile.FiveLaneDrumsHighwayOrderingLength = profile.FiveLaneDrumsHighwayOrdering.Length;
        }

        private static int GetLaneCount(Component player)
        {
            // Real initialization has already built the highway ordering.
            var drumsPlayerType = ProductionType("YARG.Gameplay.Player.DrumsPlayer");
            return (int) drumsPlayerType.GetProperty("LaneCount").GetValue(player);
        }

        private static void QueuePlayerInput(Component player, EliteDrumsAction action, double time)
        {
            var input = GameInput.Create(time, action, 0.8f);
            ProductionType("YARG.Gameplay.Player.BasePlayer").GetMethod("OnGameInput", INSTANCE)
                .Invoke(player, new object[] { input });
        }

        private static void UpdatePlayerEngine(Component player, double time)
        {
            ProductionType("YARG.Gameplay.Player.BasePlayer").GetMethod("UpdateInputs", INSTANCE)
                .Invoke(player, new object[] { time });
        }

        private void UnregisterPlayerEngine(Component player)
        {
            var basePlayer = ProductionType("YARG.Gameplay.Player.BasePlayer");
            var engineContainer = basePlayer.GetField("EngineContainer", INSTANCE).GetValue(player);
            if (engineContainer == null)
            {
                return;
            }

            var manager = (EngineManager) _gameManagerType.GetProperty("EngineManager").GetValue(_gameManager);
            var unregister = manager.GetType().GetMethods().Single(method => method.Name == "Unregister" &&
                method.GetParameters().Length == 1);
            unregister.Invoke(manager, new[] { engineContainer });
            basePlayer.GetField("EngineContainer", INSTANCE).SetValue(player, null);
        }

        private static object GetField(Type type, object target, string name)
        {
            var field = type.GetField(name, INSTANCE);
            Assert.That(field, Is.Not.Null, name);
            return field.GetValue(target);
        }

        private static Component GetFretArray(GameObject playerObject) =>
            playerObject.GetComponentInChildren(ProductionType("YARG.Gameplay.Visuals.FretArray"), true);

        private static Component GetFretArray(Component player) => GetFretArray(player.gameObject);

        private static ResolvedDrumPlayback GetResolvedPlayback(object session) =>
            (ResolvedDrumPlayback) session.GetType().GetProperty("ResolvedDrumPlayback").GetValue(session);

        private static int GetHighwayCount() => (int) ProductionType("YARG.Gameplay.Player.TrackPlayer")
            .GetField("HighwayCount", BindingFlags.Static | BindingFlags.Public).GetValue(null);

        private static void SetHighwayCount(int value) => ProductionType("YARG.Gameplay.Player.TrackPlayer")
            .GetField("HighwayCount", BindingFlags.Static | BindingFlags.Public).SetValue(null, value);

        private GameObject Own(GameObject gameObject)
        {
            _ownedObjects.Add(gameObject);
            return gameObject;
        }

        private void DestroyOwnedObjects()
        {
            foreach (var value in _ownedObjects)
            {
                if (value != null)
                {
                    Object.Destroy(value);
                }
            }
            _ownedObjects.Clear();
            _managerObject = null;
            _gameManager = null;
        }

        private void DisposeSessions()
        {
            foreach (var session in _sessions)
            {
                if (session != null)
                {
                    ((IDisposable) session).Dispose();
                }
            }
            _sessions.Clear();
        }
    }
}
