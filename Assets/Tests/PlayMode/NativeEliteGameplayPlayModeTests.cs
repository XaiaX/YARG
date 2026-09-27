using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    /// <summary>
    /// Prefab-backed native component/engine tests. The PlayMode test asmdef cannot reference
    /// Assembly-CSharp, so production Unity types are resolved at runtime. This does not
    /// initialize the full gameplay scene, subscribe device input, or exercise visual seek.
    /// </summary>
    public sealed class NativeEliteGameplayPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _clone;
        private GameObject _managerObject;
        private Component _player;
        private Component _manager;
        private object _session;
        private Type _nativeType;
        private Type _basePlayerType;
        private Type _trackPlayerType;
        private Type _typedTrackPlayerType;
        private SongChart _chart;
        private EliteDrumNote _note;

        private static Type ProductionType(string name)
        {
            var type = Type.GetType(name + ", Assembly-CSharp");
            if (type == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(name);
                    if (type != null) break;
                }
            }
            Assert.That(type, Is.Not.Null, name + " must exist in loaded Unity assemblies");
            return type;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _nativeType = ProductionType("YARG.Gameplay.Player.EliteDrumsPlayer");
            _trackPlayerType = ProductionType("YARG.Gameplay.Player.TrackPlayer");
            _basePlayerType = ProductionType("YARG.Gameplay.Player.BasePlayer");
            _typedTrackPlayerType = _nativeType.BaseType;
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/FiveLaneDrumsVisual.prefab");
#else
            var prefab = Resources.Load<GameObject>("FiveLaneDrumsVisual");
#endif
            Assert.That(prefab, Is.Not.Null, "The production five-lane highway prefab is required.");
            bool wasActive = prefab.activeSelf;
            prefab.SetActive(false);
            try
            {
                _clone = UnityEngine.Object.Instantiate(prefab);
            }
            finally
            {
                prefab.SetActive(wasActive);
            }
            Assert.That(_clone.activeSelf, Is.False);
            var stockType = ProductionType("YARG.Gameplay.Player.DrumsPlayer");
            var stock = _clone.GetComponent(stockType);
            Assert.That(stock, Is.Not.Null);
            _player = _clone.AddComponent(_nativeType);
            foreach (var field in _trackPlayerType.GetFields(INSTANCE | BindingFlags.Public))
            {
                if (field.IsDefined(typeof(SerializeField), true))
                    field.SetValue(_player, field.GetValue(stock));
            }
            _nativeType.GetMethod("SetTrackCameraForNativeElite").Invoke(_player,
                new[] { _trackPlayerType.GetProperty("TrackCamera").GetValue(stock) });
            var fretArray = _clone.GetComponentInChildren(ProductionType("YARG.Gameplay.Visuals.FretArray"), true);
            var kickFlash = _clone.GetComponentInChildren(ProductionType("YARG.Gameplay.Visuals.KickFretFlash"), true);
            _nativeType.GetMethod("SetDrumComponentsForNativeElite").Invoke(_player,
                new object[] { fretArray, kickFlash });
            UnityEngine.Object.DestroyImmediate(stock);

            var profile = new YargProfile { GameMode = GameMode.EliteDrums,
                CurrentInstrument = Instrument.EliteDrums, CurrentDifficulty = Difficulty.Expert };
            var bindings = Activator.CreateInstance(ProductionType("YARG.Input.ProfileBindings"), profile);
            var sessionType = ProductionType("YARG.Player.YargPlayer");
            _session = Activator.CreateInstance(sessionType, profile, bindings);
            sessionType.GetMethod("RefreshPresets").Invoke(_session, null);
            SetField(_basePlayerType, _player, "<Player>k__BackingField", _session);

            _chart = new SongChart(480);
            _chart.SyncTrack.Tempos.Add(new TempoChange(120, 0, 0));
            _note = new EliteDrumNote(EliteDrumPad.Snare, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, 1d, 480, false);
            var native = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert);
            native.Notes.Add(_note);
            _chart.EliteDrums.AddDifficulty(Difficulty.Expert, native);

            _managerObject = new GameObject("NativeEliteTestManager");
            _managerObject.SetActive(false);
            var managerType = ProductionType("YARG.Gameplay.GameManager");
            _manager = _managerObject.AddComponent(managerType);
            SetField(managerType, _manager, "<EngineManager>k__BackingField", new EngineManager());
            var beatHandlerType = ProductionType("YARG.Playback.BeatEventHandler");
            SetField(managerType, _manager, "<BeatEventHandler>k__BackingField",
                Activator.CreateInstance(beatHandlerType, _chart.SyncTrack));
            SetField(ProductionType("YARG.Gameplay.GameplayBehaviour"), _player,
                "<GameManager>k__BackingField", _manager);
            SetField(_basePlayerType, _player, "<SyncTrack>k__BackingField", _chart.SyncTrack);
            SetField(_typedTrackPlayerType, _player, "<NoteTrack>k__BackingField", native);
            SetField(_typedTrackPlayerType, _player, "Chart", _chart);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_player != null && _manager != null)
            {
                var container = (EngineManager.EngineContainer) GetField(
                    _basePlayerType, _player, "EngineContainer");
                if (container != null)
                {
                    ((EngineManager) _manager.GetType().GetProperty("EngineManager").GetValue(_manager))
                        .Unregister(container);
                    SetField(_basePlayerType, _player, "EngineContainer", null);
                }
            }
            if (_clone != null) UnityEngine.Object.Destroy(_clone);
            if (_managerObject != null) UnityEngine.Object.Destroy(_managerObject);
            yield return null;
            if (_session != null) _session.GetType().GetMethod("Dispose").Invoke(_session, null);
        }

        [UnityTest]
        public IEnumerator PrefabSwap_PreservesWiringAndUsesTypedNativePlayer()
        {
            Assert.That(_clone.GetComponent(ProductionType("YARG.Gameplay.Player.DrumsPlayer")), Is.Null);
            Assert.That(_clone.GetComponent(_nativeType), Is.SameAs(_player));
            Assert.That(GetField(_trackPlayerType, _player, "LanePool"), Is.Not.Null);
            Assert.That(GetField(_trackPlayerType, _player, "NotePool"), Is.Not.Null);
            Assert.That(_clone.GetComponentInChildren(ProductionType("YARG.Gameplay.Visuals.FretArray"), true),
                Is.Not.Null);
            Assert.That((bool) _nativeType.GetProperty("ShouldUpdateInputsOnResume").GetValue(_player), Is.False);
            yield return null;
            Assert.That(_player, Is.Not.Null, "The inactive prefab clone must survive a frame.");
        }

        [UnityTest]
        public IEnumerator NativeDifficultySelection_RetainsEliteNotesRatherThanDowncharting()
        {
            var selected = (InstrumentDifficulty<EliteDrumNote>) _nativeType.GetMethod("GetNotes", INSTANCE)
                .Invoke(_player, new object[] { _chart });
            Assert.That(selected.Instrument, Is.EqualTo(Instrument.EliteDrums));
            Assert.That(selected.Notes, Has.Count.EqualTo(1));
            Assert.That(selected.Notes[0].Pad, Is.EqualTo((int) EliteDrumPad.Snare));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CreateEngine_RegistersNativeEngineAndUsesProfileParameters()
        {
            var engine = CreateRegisteredEngine();
            Assert.That(GetField(_typedTrackPlayerType, _player, "<Engine>k__BackingField"), Is.SameAs(engine));
            Assert.That(_nativeType.GetProperty("EngineParams").GetValue(_player), Is.Not.Null);
            var manager = (EngineManager) _manager.GetType().GetProperty("EngineManager").GetValue(_manager);
            Assert.That(manager.Engines, Has.Count.EqualTo(1));
            Assert.That(manager.Engines[0].Instrument, Is.EqualTo(Instrument.EliteDrums));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RegisteredEngine_NativeSnareInputScores_AndResetClearsHit()
        {
            var engine = CreateRegisteredEngine();
            // Full visual/audio callbacks need TrackView and a mixer; only engine scoring is tested.
            ClearDelegate(typeof(BaseEngine<EliteDrumNote, DrumsEngineParameters, DrumsStats>),
                engine, "OnNoteHit");
            ClearDelegate(typeof(BaseEngine<EliteDrumNote, DrumsEngineParameters, DrumsStats>),
                engine, "OnNoteMissed");
            ClearDelegate(typeof(EliteDrumsEngine), engine, "OnPadHit");
            var input = GameInput.Create(1d, EliteDrumsAction.EliteSnare, 0.8f);
            engine.QueueInput(ref input);
            engine.Update(1d);
            Assert.That(_note.WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.CommittedScore, Is.GreaterThan(0));
            engine.Reset();
            Assert.That(_note.WasHit, Is.False);
            Assert.That(engine.EngineStats.NotesHit, Is.Zero);
            yield return null;
        }

        private EliteDrumsEngine CreateRegisteredEngine()
        {
            var engine = (EliteDrumsEngine) _nativeType.GetMethod("CreateEngine", INSTANCE)
                .Invoke(_player, null);
            SetField(_typedTrackPlayerType, _player, "<Engine>k__BackingField", engine);
            return engine;
        }

        private static void ClearDelegate(Type declaringType, object target, string name)
        {
            var field = declaringType.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, null);
        }

        private static object GetField(Type declaringType, object target, string name)
        {
            var field = declaringType.GetField(name, INSTANCE);
            Assert.That(field, Is.Not.Null, name);
            return field.GetValue(target);
        }

        private static void SetField(Type declaringType, object target, string name, object value)
        {
            var field = declaringType.GetField(name, INSTANCE);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
