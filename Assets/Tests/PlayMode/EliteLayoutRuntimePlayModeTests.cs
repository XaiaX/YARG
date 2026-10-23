using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteLayoutRuntimePlayModeTests
    {
        private InitializedElitePlayerFixture _fixture;
        private object _originalSettings;
        private bool _originalSettingsInitialized;
        private bool _hasCapturedOriginalSettings;
        private bool _ownsAudioManager;
        private readonly List<string> _padHitDiagnostics = new();
        private readonly List<string> _inputSubmissionDiagnostics = new();
        private static Type Production(string name) => PreparedElitePlayerFixture.ProductionType(name);

        private void RecordPadHit(EliteDrumsAction action, bool noteWasHit, bool bonus,
            bool wasOverhitInLane, DrumNoteType type, float velocity)
        {
            var engine = GetEngine(_fixture.Player);
            double currentTime = (double) engine.GetType().GetProperty("CurrentTime").GetValue(engine);
            _padHitDiagnostics.Add($"time={currentTime:F6} action={action} noteWasHit={noteWasHit} " +
                $"bonus={bonus} overhitInLane={wasOverhitInLane} type={type} velocity={velocity:F3}");
        }

        private static void AddPadHitDiagnostic(object engine, EliteLayoutRuntimePlayModeTests owner)
        {
            var field = engine.GetType().GetField("OnPadHit", BindingFlags.Public | BindingFlags.Instance);
            var callback = Delegate.CreateDelegate(field.FieldType, owner,
                typeof(EliteLayoutRuntimePlayModeTests).GetMethod(nameof(RecordPadHit), PreparedElitePlayerFixture.INSTANCE));
            field.SetValue(engine, Delegate.Combine((Delegate) field.GetValue(engine), callback));
        }

        private string DescribeEngineInputState(Component player, object engine, IReadOnlyList<EliteDrumNote> notes)
        {
            var basePlayer = Production("YARG.Gameplay.Player.BasePlayer");
            bool gameStarted = (bool) _fixture.Manager.GetType().GetProperty("Started").GetValue(_fixture.Manager);
            bool gamePaused = (bool) _fixture.Manager.GetType().GetProperty("Paused").GetValue(_fixture.Manager);
            bool managerFailed = (bool) _fixture.Manager.GetType().GetProperty("PlayerHasFailed").GetValue(_fixture.Manager);
            bool playerFailed = (bool) basePlayer.GetField("PlayerHasFailed", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(player);
            var lastInputs = (IDictionary) basePlayer.GetProperty("LastInputs", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(player);
            var submittedInputs = string.Join("; ", lastInputs.Keys.Cast<object>().Select(key =>
            {
                var input = (GameInput) lastInputs[key];
                return $"actionKey={key} rawTime={input.Time:F6} action={input.GetAction<EliteDrumsAction>()}";
            }));
            var acceptedInputs = string.Join("; ", ((IEnumerable<GameInput>) basePlayer.GetProperty("ReplayInputs").GetValue(player))
                .Select(input => $"time={input.Time:F6} action={input.GetAction<EliteDrumsAction>()}"));
            var notesState = string.Join("; ", notes.Select((note, index) =>
                $"[{index}] pad={note.Pad} tick={note.Tick} chartTime={_fixture.Chart.SyncTrack.TickToTime(note.Tick):F6} " +
                $"preparedTime={note.Time:F6} hit={note.WasHit} missed={note.WasMissed} " +
                $"source=({note.SourceDefinition?.StartTick}->{note.SourceDefinition?.EndTick}," +
                $"{note.SourceDefinition?.StartTime:F6}->{note.SourceDefinition?.EndTime:F6})"));
            var profile = (YargProfile) _fixture.Session.GetType().GetProperty("Profile").GetValue(_fixture.Session);
            var engineType = engine.GetType();
            var engineBaseType = Production("YARG.Core.Engine.BaseEngine");
            var inputQueue = (System.Collections.ICollection) engineBaseType
                .GetField("InputQueue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(engine);
            var laneMap = engineType.GetField("_authoredLanes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(engine);
            var barrier = laneMap.GetType().GetField("_barrier", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(laneMap);
            string barrierState = barrier == null ? "none" :
                $"trigger={barrier.GetType().GetProperty("TriggerTimestamp").GetValue(barrier):F6} " +
                $"latchedUntil={barrier.GetType().GetProperty("LatchedUntil").GetValue(barrier):F6} " +
                $"recoveredAt={barrier.GetType().GetProperty("RecoveredAt").GetValue(barrier):F6}";
            return $"gameStarted={gameStarted} gamePaused={gamePaused} managerFailed={managerFailed} playerFailed={playerFailed} " +
                $"fixtureNoFail={_fixture.NoFailEnabled} happiness={_fixture.EngineManager.Happiness:F6} " +
                $"engineTime={engineType.GetProperty("CurrentTime").GetValue(engine):F6} " +
                $"lastUpdate={engineType.GetProperty("LastUpdateTime").GetValue(engine):F6} " +
                $"currentTick={engineType.GetProperty("CurrentTick").GetValue(engine)} " +
                $"noteIndex={engineType.GetProperty("NoteIndex").GetValue(engine)} " +
                $"lastQueuedInputTime={engineType.GetProperty("LastQueuedInputTime").GetValue(engine):F6} " +
                $"queuedInputCount={inputQueue.Count} " +
                $"inputCalibration={((double) basePlayer.GetProperty("InputCalibration").GetValue(player)):F6} " +
                $"bot={profile.IsBot} modifiers={profile.CurrentModifiers} barrier=[{barrierState}] " +
                $"callbackInputs=[{string.Join("; ", _padHitDiagnostics)}] " +
                $"inputSubmissions=[{string.Join("; ", _inputSubmissionDiagnostics)}] " +
                $"submittedInputs=[{submittedInputs}] acceptedReplayInputs=[{acceptedInputs}] " +
                $"preparedNotes=[{notesState}]";
        }

        [UnitySetUp]
        public IEnumerator SetUpSettingsAndAudio()
        {
            _hasCapturedOriginalSettings = false;
            _ownsAudioManager = false;
            _padHitDiagnostics.Clear();
            _inputSubmissionDiagnostics.Clear();

            var settingsManager = Production("YARG.Settings.SettingsManager");
            var settingContainer = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
            var initialized = settingContainer.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static);
            _originalSettings = settingsManager.GetProperty("Settings").GetValue(null);
            _originalSettingsInitialized = (bool) initialized.GetValue(null);
            _hasCapturedOriginalSettings = true;

            var audioHandler = Production("YARG.Core.Audio.GlobalAudioHandler");
            var audioInstance = audioHandler.GetField("_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (audioInstance.GetValue(null) == null)
            {
                Production("YARG.Helpers.PathHelper").GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                var bassAudioManager = Production("YARG.Audio.BASS.BassAudioManager");
                audioHandler.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)
                    .MakeGenericMethod(bassAudioManager).Invoke(null, null);
                _ownsAudioManager = true;
            }

            if (_originalSettings == null)
            {
                var temporarySettings = Activator.CreateInstance(settingContainer);
                settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null,
                    new[] { temporarySettings });
                initialized.SetValue(null, true);
            }

            yield return null;
        }
        private static EliteDrumNote Note(EliteDrumPad pad, bool doubleKick = false,
            double time = 1d, uint tick = 480) => new EliteDrumNote(
            pad, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp,
            false, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, tick, doubleKick);

        private static float ThemeGroupWidth(Component group)
        {
            var renderers = group.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty, "The source theme model must include renderers.");
            return renderers.Max(renderer => renderer.bounds.max.x) - renderers.Min(renderer => renderer.bounds.min.x);
        }

        private static void QueuePlayerInput(Component player, EliteDrumsAction action, double time,
            Component manager = null, List<string> diagnostics = null)
        {
            var basePlayer = Production("YARG.Gameplay.Player.BasePlayer");
            var lastInputs = (IDictionary) basePlayer.GetProperty("LastInputs", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(player);
            var replayInputs = (ICollection<GameInput>) basePlayer.GetProperty("ReplayInputs").GetValue(player);
            bool started = manager != null && (bool) manager.GetType().GetProperty("Started").GetValue(manager);
            bool paused = manager == null || (bool) manager.GetType().GetProperty("Paused").GetValue(manager);
            bool managerFailed = manager != null && (bool) manager.GetType().GetProperty("PlayerHasFailed").GetValue(manager);
            bool playerFailed = (bool) basePlayer.GetField("PlayerHasFailed", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(player);
            object engine = GetEngine(player);
            var engineType = engine.GetType();
            double beforeLastQueued = (double) engineType.GetProperty("LastQueuedInputTime").GetValue(engine);
            int beforeSubmitted = lastInputs.Count;
            int beforeReplay = replayInputs.Count;
            var input = GameInput.Create(time, action, 0.8f);
            basePlayer.GetMethod("OnGameInput", PreparedElitePlayerFixture.INSTANCE)
                .Invoke(player, new object[] { input });
            if (diagnostics != null)
            {
                double afterLastQueued = (double) engineType.GetProperty("LastQueuedInputTime").GetValue(engine);
                diagnostics.Add($"action={action} requestedTime={time:F6} started={started} paused={paused} " +
                    $"managerFailed={managerFailed} playerFailed={playerFailed} " +
                    $"lastInputs={beforeSubmitted}->{lastInputs.Count} replayInputs={beforeReplay}->{replayInputs.Count} " +
                    $"lastQueued={beforeLastQueued:F6}->{afterLastQueued:F6}");
            }
        }

        private static void UpdatePlayerEngine(Component player, double time) =>
            Production("YARG.Gameplay.Player.BasePlayer").GetMethod("UpdateInputs", PreparedElitePlayerFixture.INSTANCE)
                .Invoke(player, new object[] { time });

        private static Component GetReceptor(Component player, int key)
        {
            var array = player.GetComponentInChildren(Production("YARG.Gameplay.Visuals.FretArray"), true);
            var frets = (System.Collections.IDictionary) array.GetType()
                .GetField("_frets", PreparedElitePlayerFixture.INSTANCE).GetValue(array);
            return (Component) frets[key];
        }

        private IReadOnlyList<EliteDrumNote> GetPlayableNotes() =>
            ((InstrumentDifficulty<EliteDrumNote>) _fixture.Session.GetType()
                .GetProperty("PlayableEliteDrumDifficulty").GetValue(_fixture.Session)).Notes;

        private static object GetEngine(Component player) => player.GetType().BaseType
            .GetProperty("Engine", PreparedElitePlayerFixture.INSTANCE).GetValue(player);

        private static int GetNotesHit(object engine)
        {
            var stats = engine.GetType().GetField("EngineStats", BindingFlags.Public | BindingFlags.Instance)
                .GetValue(engine);
            return (int) stats.GetType().GetField("NotesHit", BindingFlags.Public | BindingFlags.Instance).GetValue(stats);
        }

        private static void AssertEffect(Component receptor, System.Drawing.Color expected, bool miss = false)
        {
            var theme = receptor.GetType().GetProperty("ThemeBind").GetValue(receptor);
            var effect = (Component) theme.GetType().GetProperty(miss ? "MissEffect" : "HitEffect").GetValue(theme);
            var particles = effect.GetComponentsInChildren(Production("YARG.Helpers.Authoring.EffectParticle"), true);
            Assert.That(particles, Is.Not.Empty, "The selected receptor must have its themed effect particles.");
            var effectParticle = particles[0];
            var actual = (Color) effectParticle.GetType().GetProperty("InitialColor", PreparedElitePlayerFixture.INSTANCE)
                .GetValue(effectParticle);
            Assert.That(actual.r, Is.EqualTo(expected.R / 255f).Within(0.005f));
            Assert.That(actual.g, Is.EqualTo(expected.G / 255f).Within(0.005f));
            Assert.That(actual.b, Is.EqualTo(expected.B / 255f).Within(0.005f));
            Assert.That(effectParticle.GetComponent<ParticleSystem>().isPlaying, Is.True,
                "The selected receptor's effect must actually play.");
        }
        private static YargProfile Profile(bool lefty) => new YargProfile
        {
            GameMode = GameMode.EliteDrums, CurrentInstrument = Instrument.EliteDrums,
            PreferredInstrument = Instrument.EliteDrums, CurrentDifficulty = Difficulty.Hard,
            UseCymbalModels = true, LeftyFlip = lefty
        };

        [UnityTest]
        public IEnumerator IndependentInitializedFixturesCleanUpPlayersAndReceptors()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                _fixture = new InitializedElitePlayerFixture();
                yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(Note(EliteDrumPad.Snare)),
                    Profile(false), EliteDrumsLayoutPreset.Compact);
                var owned = _fixture.Objects.ToArray();
                Assert.That(_fixture.Player.gameObject.activeInHierarchy, Is.True);
                yield return _fixture.Dispose();
                Assert.That(owned.All(item => item == null), Is.True,
                    "A fixture must release all owned player, theme, camera, and manager objects before the next instance.");
                _fixture = null;
            }
        }

        [Test]
        public void ExplicitLaneWidthsRemainIndependentOfClassicScaleCache()
        {
            var laneType = Production("YARG.Gameplay.Visuals.LaneElement");
            var cache = (System.Collections.IDictionary) laneType.GetField("_scaleByInstrument",
                BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            bool hadEntry = cache.Contains(Instrument.FiveLaneDrums);
            object original = hadEntry ? cache[Instrument.FiveLaneDrums] : null;
            var first = new GameObject("Five-lane width probe");
            var second = new GameObject("Nine-lane width probe");
            first.SetActive(false);
            second.SetActive(false);
            try
            {
                laneType.GetMethod("DefineLaneScale").Invoke(null,
                    new object[] { Instrument.FiveLaneDrums, 5, true });
                var firstLane = first.AddComponent(laneType);
                var secondLane = second.AddComponent(laneType);
                var appearance = laneType.GetMethod("SetAppearance", new[]
                    { typeof(Instrument), typeof(int), typeof(float), typeof(int), typeof(UnityEngine.Color), typeof(float) });
                appearance.Invoke(firstLane, new object[] { Instrument.FiveLaneDrums, 0, 0f, 5, UnityEngine.Color.red, 2f / 5f });
                appearance.Invoke(secondLane, new object[] { Instrument.FiveLaneDrums, 0, 0f, 9, UnityEngine.Color.red, 2f / 9f });
                var scale = laneType.GetField("_scale", PreparedElitePlayerFixture.INSTANCE);
                Assert.That((float)scale.GetValue(firstLane), Is.EqualTo(2f / 5f));
                Assert.That((float)scale.GetValue(secondLane), Is.EqualTo(2f / 9f));
                Assert.That((float)cache[Instrument.FiveLaneDrums], Is.EqualTo(2f / 5f),
                    "Explicit Elite widths must not overwrite the classic instrument cache.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                if (hadEntry) cache[Instrument.FiveLaneDrums] = original;
                else cache.Remove(Instrument.FiveLaneDrums);
            }
        }

        [UnityTest]
        public IEnumerator ActualDedicatedKickModelsFitResolvedColumnsAndMirrorIdentity()
        {
            foreach (bool legacyTheme in new[] { false, true })
            foreach (bool lefty in new[] { false, true })
            foreach (bool split in new[] { false, true })
            {
                var layout = EliteDrumsLayoutPreset.Full;
                layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(split ? EliteDrumsLayoutSlotKind.Kick1 : EliteDrumsLayoutSlotKind.Kick);
                if (split) layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
                var ordinary = Note(EliteDrumPad.Kick);
                var extra = Note(EliteDrumPad.Kick, true);
                var profile = Profile(lefty);
                profile.AddSingleModifier(Modifier.Enable2xKicks);
                _fixture = new InitializedElitePlayerFixture();
                yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(ordinary), profile, layout, legacyTheme);
                var player = _fixture.Player;
                if (split)
                    Assert.That(GetReceptor(player, 100), Is.Not.Null,
                        "The second split receptor must exist even when the prepared chart has no double-kick note.");
                foreach (var note in new[] { ordinary, extra })
                {
                    player.GetType().GetMethod("SpawnNote", PreparedElitePlayerFixture.INSTANCE).Invoke(player, new object[] { note });
                    var elements = player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true);
                    var element = elements.Single(e => ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), note));
                    var renderers = element.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.activeInHierarchy).ToArray();
                    Assert.That(renderers, Is.Not.Empty);
                    float width = renderers.Max(r => r.bounds.max.x) - renderers.Min(r => r.bounds.min.x);
                    float highwayWidth = (float) Production("YARG.Gameplay.Player.TrackPlayer").GetField("TRACK_WIDTH").GetRawConstantValue();
                    int lanes = (int) player.GetType().GetProperty("LaneCount").GetValue(player);
                    float scale = Mathf.Abs(element.transform.lossyScale.x);
                    Assert.That(width / scale, Is.EqualTo(highwayWidth / lanes).Within(0.01f));
                    int key = (int) player.GetType().GetMethod("GetReceptorKey").Invoke(player, new object[] { note });
                    Assert.That(key, Is.EqualTo(split && note.IsDoubleKick ? 100 : (int) EliteDrumPad.Kick));
                }
                yield return _fixture.Dispose();
                _fixture = null;
            }
        }

        [UnityTest]
        public IEnumerator SnareAndLeftCrashActualModelsUseSingleOrPairedResolvedLaneWidth()
        {
            // Collision pairs are unconditional; no display setting participates.
            try
            {
                foreach (bool legacyTheme in new[] { false, true })
                foreach (bool lefty in new[] { false, true })
                foreach (bool pairAtFirstLane in new[] { false, true })
                {
                    var layout = EliteDrumsLayoutPreset.Compact;
                    if (pairAtFirstLane)
                    {
                        layout.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", "left-crash");
                        layout.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom1", null);
                    }
                    else
                    {
                        layout.Slots[0] = EliteDrumsLayoutSlot.Ordinary("tom2", "ride");
                        layout.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom1", null);
                        layout.Slots[3] = EliteDrumsLayoutSlot.Ordinary("snare", "left-crash");
                    }

                    var standalone = Note(EliteDrumPad.Snare, false, 1d, 480);
                    var standaloneCrash = Note(EliteDrumPad.LeftCrash, false, 1d, 480);
                    var chordSnare = Note(EliteDrumPad.Snare, false, 2d, 960);
                    var chordCrash = Note(EliteDrumPad.LeftCrash, false, 2d, 960);
                    chordSnare.AddChildNote(chordCrash);
                    _fixture = new InitializedElitePlayerFixture();
                    yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(standalone, standaloneCrash, chordSnare),
                        Profile(lefty), layout, legacyTheme);
                    var player = _fixture.Player;
                    var spawn = player.GetType().GetMethod("SpawnNote", PreparedElitePlayerFixture.INSTANCE);
                    foreach (var note in new[] { standalone, standaloneCrash, chordSnare, chordCrash })
                        spawn.Invoke(player, new object[] { note });

                    var runtimeLayout = (EliteDrumsResolvedLayout) player.GetType().GetProperty("Layout").GetValue(player);
                    float highwayWidth = (float) Production("YARG.Gameplay.Player.TrackPlayer")
                        .GetField("TRACK_WIDTH").GetRawConstantValue();
                    float laneWidth = runtimeLayout.GetLaneWidth(highwayWidth);
                    var elementType = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
                    var template = player.GetComponentsInChildren(elementType, true).First();
                    var templateGroups = (Array) elementType.GetField("NoteGroups",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(template);
                    float[] authoredWidths = { ThemeGroupWidth((Component) templateGroups.GetValue(0)),
                        ThemeGroupWidth((Component) templateGroups.GetValue(1)) };
                    float parentScale = Mathf.Abs(player.transform.lossyScale.x);
                    foreach (var note in new[] { standalone, standaloneCrash, chordSnare, chordCrash })
                    {
                        var element = player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true)
                            .Single(e => ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), note));
                        var renderers = element.GetComponentsInChildren<Renderer>()
                            .Where(renderer => renderer.gameObject.activeInHierarchy).ToArray();
                        Assert.That(renderers, Is.Not.Empty,
                            $"An actual themed model must be active for pad {note.Pad}; legacy={legacyTheme}, lefty={lefty}.");
                        float left = renderers.Min(renderer => renderer.bounds.min.x);
                        float right = renderers.Max(renderer => renderer.bounds.max.x);
                        float scale = parentScale;
                        float actualWidth = (right - left) / scale;
                        float actualCenter = player.transform.InverseTransformPoint(new Vector3((left + right) / 2f, 0, 0)).x;
                        int lane = runtimeLayout.SourceToLane[note.Pad == (int) EliteDrumPad.Snare ? "snare" : "left-crash"];
                        float laneCenter = runtimeLayout.GetLaneX(lane, highwayWidth, lefty);
                        float expectedWidth = laneWidth;
                        float expectedCenter = laneCenter;
                        if (ReferenceEquals(note, chordSnare) || ReferenceEquals(note, chordCrash))
                        {
                            int columns = runtimeLayout.Lanes.Count;
                            expectedWidth = laneWidth * (columns - 1f) / columns;
                            float drumWidth = authoredWidths[0] * expectedWidth / (highwayWidth / 5f);
                            float crashWidth = authoredWidths[1] * expectedWidth / (highwayWidth / 5f);
                            float halfPair = (drumWidth + crashWidth) / 2f;
                            float midpoint = laneCenter / (highwayWidth / 2f - laneWidth / 2f) *
                                (highwayWidth / 2f - halfPair);
                            bool rightSide = (note.Pad == (int) EliteDrumPad.LeftCrash) != lefty;
                            float ownWidth = note.Pad == (int) EliteDrumPad.LeftCrash ? crashWidth : drumWidth;
                            expectedCenter = midpoint + (rightSide ? 1f : -1f) * (halfPair - ownWidth / 2f);
                        }
                        bool cymbalModel = note.Pad == (int) EliteDrumPad.LeftCrash;
                        float authoredWidth = authoredWidths[cymbalModel ? 1 : 0];
                        float expectedRenderedWidth = authoredWidth * expectedWidth / (highwayWidth / 5f);
                        Assert.That(actualWidth, Is.EqualTo(expectedRenderedWidth).Within(0.002f),
                            $"Actual model width must follow its measured five-lane authored baseline; pad={note.Pad}, lane={lane}, legacy={legacyTheme}, lefty={lefty}, firstLane={pairAtFirstLane}.");
                        Assert.That(actualCenter, Is.EqualTo(expectedCenter).Within(0.015f),
                            $"Actual renderer center; pad={note.Pad}, lane={lane}, legacy={legacyTheme}, lefty={lefty}, firstLane={pairAtFirstLane}.");
                    }
                    yield return _fixture.Dispose();
                    _fixture = null;
                }
            }
            finally
            {
                // Fixture cleanup is handled by TearDown if an assertion fails.
            }
        }

        [UnityTest]
        public IEnumerator StompAndSplashModelsUseAvailableNeighborColumnsWithoutShrinkingConcurrentKick()
        {
            EliteDrumNote Pedal(EliteDrumsHatPedalType type, double time, uint tick) => new(
                EliteDrumPad.HatPedal, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent, type,
                false, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, tick, false);

            foreach (int layoutKind in new[] { 0, 1, 2, 3, 4 })
            foreach (bool lefty in new[] { false, true })
            {
                var layout = EliteDrumsLayoutPreset.Compact;
                if (layoutKind == 1)
                {
                    layout = EliteDrumsLayoutPreset.Full;
                    layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
                }
                else if (layoutKind == 2)
                {
                    layout = EliteDrumsLayoutPreset.Full;
                    layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
                    layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
                }
                if (layoutKind is 3 or 4)
                {
                    layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Pedal edge test");
                    int hat = System.Array.FindIndex(layout.Slots,
                        slot => slot.Kind == EliteDrumsLayoutSlotKind.HiHat);
                    int edge = layoutKind == 3 ? 0 : 4;
                    (layout.Slots[hat], layout.Slots[edge]) = (layout.Slots[edge], layout.Slots[hat]);
                }
                var standaloneStomp = Pedal(EliteDrumsHatPedalType.Stomp, 1d, 480);
                var pairedStomp = Pedal(EliteDrumsHatPedalType.Stomp, 2d, 960);
                var pairedKick = Note(EliteDrumPad.Kick, false, 2d, 960);
                pairedStomp.AddChildNote(pairedKick);
                var standaloneSplash = Pedal(EliteDrumsHatPedalType.Splash, 3d, 1440);
                var pairedSplash = Pedal(EliteDrumsHatPedalType.Splash, 4d, 1920);
                var pairedSplashKick = Note(EliteDrumPad.Kick, false, 4d, 1920);
                pairedSplash.AddChildNote(pairedSplashKick);
                _fixture = new InitializedElitePlayerFixture();
                yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(
                    standaloneStomp, pairedStomp, standaloneSplash, pairedSplash), Profile(lefty), layout);
                var player = _fixture.Player;
                var spawn = player.GetType().GetMethod("SpawnNote", PreparedElitePlayerFixture.INSTANCE);
                foreach (var note in new[]
                {
                    standaloneStomp, pairedStomp, pairedKick, standaloneSplash, pairedSplash, pairedSplashKick,
                })
                    spawn.Invoke(player, new object[] { note });

                float highwayWidth = (float) Production("YARG.Gameplay.Player.TrackPlayer")
                    .GetField("TRACK_WIDTH").GetRawConstantValue();
                int laneCount = (int) player.GetType().GetProperty("LaneCount").GetValue(player);
                int sections = layoutKind is 3 or 4 ? 2 : 3;
                float pedalWidth = highwayWidth * sections / laneCount;
                foreach (var (note, expectedWidth) in new[]
                {
                    (standaloneStomp, pedalWidth),
                    (pairedStomp, pedalWidth),
                    (standaloneSplash, pedalWidth),
                    (pairedSplash, pedalWidth),
                })
                {
                    var element = player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true)
                        .Single(e => ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), note));
                    var renderers = element.GetComponentsInChildren<Renderer>()
                        .Where(renderer => renderer.gameObject.activeInHierarchy).ToArray();
                    Assert.That(renderers, Is.Not.Empty, "The real stomp/splash model must render.");
                    float width = renderers.Max(renderer => renderer.bounds.max.x) -
                        renderers.Min(renderer => renderer.bounds.min.x);
                    float parentScale = Mathf.Abs(player.transform.lossyScale.x);
                    Assert.That(width / parentScale, Is.EqualTo(expectedWidth).Within(0.02f),
                        $"layoutKind={layoutKind}, lefty={lefty}, pedalType={note.HatPedalType}, time={note.Time}");
                    float center = element.transform.localPosition.x;
                    float visualPedalLane = (int) player.GetType().GetMethod("GetVisualPosition", new[] { typeof(EliteDrumNote) })
                        .Invoke(player, new object[] { note });
                    float hiHatCenter = (visualPedalLane + 0.5f) * (highwayWidth / laneCount) - highwayWidth / 2f;
                    float expectedCenter = Mathf.Clamp(hiHatCenter, -highwayWidth / 2f + pedalWidth / 2f,
                        highwayWidth / 2f - pedalWidth / 2f);
                    Assert.That(center, Is.EqualTo(expectedCenter).Within(0.01f),
                        $"Pedals stay centered on the hat and its available immediate neighbors; layoutKind={layoutKind}, lefty={lefty}.");
                    Assert.That((renderers.Min(renderer => renderer.bounds.min.x) - player.transform.position.x) / parentScale,
                        Is.GreaterThanOrEqualTo(-highwayWidth / 2f - 0.01f));
                    Assert.That((renderers.Max(renderer => renderer.bounds.max.x) - player.transform.position.x) / parentScale,
                        Is.LessThanOrEqualTo(highwayWidth / 2f + 0.01f));
                }
                var resolvedLayout = player.GetType().GetProperty("Layout").GetValue(player);
                string kickMode = resolvedLayout.GetType().GetProperty("KickMode").GetValue(resolvedLayout).ToString();
                string expectedKickMode = layoutKind switch
                {
                    0 or 3 or 4 => "Bars",
                    1 => "Combined",
                    _ => "Split",
                };
                Assert.That(kickMode, Is.EqualTo(expectedKickMode),
                    $"layoutKind={layoutKind} must exercise the expected resolved kick mode.");
                float expectedKickWidth = kickMode == "Bars" ? highwayWidth : highwayWidth / laneCount;
                foreach (var kick in new[] { pairedKick, pairedSplashKick })
                {
                    var kickElement = player.GetComponentsInChildren(
                        Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true)
                        .Single(e => ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), kick));
                    var kickRenderers = kickElement.GetComponentsInChildren<Renderer>()
                        .Where(renderer => renderer.gameObject.activeInHierarchy).ToArray();
                    float kickWidth = kickRenderers.Max(renderer => renderer.bounds.max.x) -
                        kickRenderers.Min(renderer => renderer.bounds.min.x);
                    Assert.That(kickWidth / Mathf.Abs(player.transform.lossyScale.x),
                        Is.EqualTo(expectedKickWidth).Within(0.02f),
                        $"Kick geometry remains layout-specific when paired with a pedal; layoutKind={layoutKind}.");
                }
                yield return _fixture.Dispose();
                _fixture = null;
            }
        }

        [UnityTest]
        public IEnumerator GhostAndOrdinaryRendererRatiosMatchAuthoredModelsAcrossLaneCounts()
        {
            foreach (int count in new[] { 5, 8, 10 })
            {
                var layout = count == 5 ? EliteDrumsLayoutPreset.Compact : EliteDrumsLayoutPreset.Full;
                if (count == 8)
                {
                    layout.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", "left-crash");
                    layout.Slots[2] = new EliteDrumsLayoutSlot();
                    layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
                }
                else if (count == 10)
                {
                    layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
                    layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
                }
                var ordinary = Note(EliteDrumPad.Snare, false, 1d, 480);
                var ghost = new EliteDrumNote(EliteDrumPad.Snare, DrumNoteType.Ghost,
                    EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                    DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, 2d, 960, false);
                var starOrdinary = new EliteDrumNote(EliteDrumPad.Snare, DrumNoteType.Neutral,
                    EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                    DrumNoteFlags.None, NoteFlags.StarPower, EliteDrumsChannelFlag.None, 3d, 1440, false);
                var starGhost = new EliteDrumNote(EliteDrumPad.Snare, DrumNoteType.Ghost,
                    EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                    DrumNoteFlags.None, NoteFlags.StarPower, EliteDrumsChannelFlag.None, 4d, 1920, false);
                _fixture = new InitializedElitePlayerFixture();
                yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(ordinary, ghost, starOrdinary, starGhost),
                    Profile(false), layout);
                try
                {
                    var player = _fixture.Player;
                    var spawn = player.GetType().GetMethod("SpawnNote", PreparedElitePlayerFixture.INSTANCE);
                    var elementType = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
                    var template = player.GetComponentsInChildren(elementType, true).First();
                    var sourceGroups = (Array) elementType.GetField("NoteGroups",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(template);
                    float GroupWidth(Component group)
                    {
                        var renderers = group.GetComponentsInChildren<Renderer>(true);
                        Assert.That(renderers, Is.Not.Empty, "ThemeManager-created source model must render.");
                        return renderers.Max(r => r.bounds.max.x) - renderers.Min(r => r.bounds.min.x);
                    }
                    float authoredRatio = GroupWidth((Component) sourceGroups.GetValue(4)) /
                        GroupWidth((Component) sourceGroups.GetValue(0));
                    var starGroups = (Array) elementType.GetField("StarPowerNoteGroups",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(template);
                    float authoredStarRatio = GroupWidth((Component) starGroups.GetValue(4)) /
                        GroupWidth((Component) starGroups.GetValue(0));
                    Assert.That(authoredRatio, Is.GreaterThan(0f).And.LessThan(1f));
                    Assert.That(authoredStarRatio, Is.GreaterThan(0f).And.LessThan(1f));
                    starOrdinary.ActivateFlag(NoteFlags.StarPower);
                    starGhost.ActivateFlag(NoteFlags.StarPower);
                    spawn.Invoke(player, new object[] { ordinary });
                    spawn.Invoke(player, new object[] { ghost });
                    spawn.Invoke(player, new object[] { starOrdinary });
                    spawn.Invoke(player, new object[] { starGhost });
                    var elements = player.GetComponentsInChildren(elementType, true);
                    float Width(EliteDrumNote note)
                    {
                        var element = elements.Single(e => ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), note));
                        var renderers = element.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.activeInHierarchy).ToArray();
                        Assert.That(renderers, Is.Not.Empty);
                        return renderers.Max(r => r.bounds.max.x) - renderers.Min(r => r.bounds.min.x);
                    }
                    float ordinaryWidth = Width(ordinary);
                    float ghostWidth = Width(ghost);
                    float starOrdinaryWidth = Width(starOrdinary);
                    float starGhostWidth = Width(starGhost);
                    Assert.That(ghostWidth / ordinaryWidth, Is.EqualTo(authoredRatio).Within(0.015f),
                        $"The runtime ghost/normal renderer ratio must match the un-fitted ThemeManager model groups at {count} lanes.");
                    Assert.That(starGhostWidth / starOrdinaryWidth, Is.EqualTo(authoredStarRatio).Within(0.015f),
                        $"The runtime star-power ghost/normal renderer ratio must match the un-fitted theme groups at {count} lanes.");
                }
                catch { throw; }
                yield return _fixture.Dispose();
                _fixture = null;
            }
        }

        [UnityTest]
        public IEnumerator StompVisualLeadUsesPreparedSyncTrackWithoutChangingNoteTiming(
            [ValueSource(nameof(LeadCases))] (uint resolution, uint noteTick, double expectedDuration) testCase)
        {
            var note = new EliteDrumNote(EliteDrumPad.HatPedal, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, 0d, testCase.noteTick, false);
            var chart = PreparedElitePlayerFixture.CreateChart(note);
            chart.SyncTrack = new SyncTrack(testCase.resolution);
            chart.SyncTrack.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0, 0, 0, 0, 16, 4));
            chart.SyncTrack.Tempos.Clear();
            chart.SyncTrack.Tempos.Add(new TempoChange(120, 0, 0));
            chart.SyncTrack.Tempos.Add(new TempoChange(60, 1, testCase.resolution));
            _fixture = new InitializedElitePlayerFixture();
            yield return _fixture.Initialize(chart, Profile(false), EliteDrumsLayoutPreset.Compact, legacyTheme: true);
            try
            {
                var player = _fixture.Player;
                player.GetType().GetMethod("SpawnNote", PreparedElitePlayerFixture.INSTANCE)
                    .Invoke(player, new object[] { note });
                var element = player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true)
                    .Single(e => ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), note));
                var noteGroup = (Component) element.GetType().BaseType.GetField("NoteGroup",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(element);
                var transform = noteGroup.transform;
                double time = note.Time;
                uint tick = note.Tick;
                double elementTime = (double) element.GetType().GetProperty("ElementTime").GetValue(element);
                double syncDuration = chart.SyncTrack.TickToTime(note.Tick + 1) - chart.SyncTrack.TickToTime(note.Tick);
                Assert.That(syncDuration, Is.EqualTo(testCase.expectedDuration).Within(1e-6));
                float noteSpeed = System.Convert.ToSingle(player.GetType().GetProperty("NoteSpeed").GetValue(player));
                float expectedZ = -(float) (noteSpeed * syncDuration);
                float firstZ = transform.localPosition.z;
                Assert.That(firstZ, Is.EqualTo(expectedZ).Within(0.0001f));
                for (int frame = 0; frame < 3; frame++)
                {
                    element.GetType().GetMethod("UpdateElement", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(element, null);
                    Assert.That(transform.localPosition.z, Is.EqualTo(firstZ).Within(0.0001f));
                }
                Assert.That(note.Time, Is.EqualTo(time));
                Assert.That(note.Tick, Is.EqualTo(tick));
                Assert.That((double) element.GetType().GetProperty("ElementTime").GetValue(element), Is.EqualTo(elementTime));
            }
            catch { throw; }
            yield return _fixture.Dispose();
            _fixture = null;
        }

        private static IEnumerable<(uint resolution, uint noteTick, double expectedDuration)> LeadCases()
        {
            yield return (192, 0, 0.0026041666667);
            yield return (192, 192, 0.0052083333333);
            yield return (480, 0, 0.0010416666667);
            yield return (480, 480, 0.0020833333333);
        }

        [UnityTest]
        public IEnumerator ArbitraryPairAndNestedFlamsStayInsideHighwayForEveryGeometrySetting()
        {
            // Preserve the historical test identity; invalid flam-plus-hand chords are undefined.
            // Exercise valid flat/regular hand flams independently of collision pairs.
            try
            {
                foreach (bool legacyTheme in new[] { false, true })
                foreach (bool lefty in new[] { false, true })
                foreach (bool offset in new[] { false, true })
                foreach (bool split in new[] { false, true })
                foreach (int count in new[] { 5, 7, 10 })
                {
                    // Historical setting matrix now proves the same unconditional presentation.
                    var layout = count == 5 ? EliteDrumsLayoutPreset.Compact : EliteDrumsLayoutPreset.Full;
                    if (count == 5)
                    {
                        layout.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", "right-crash");
                        layout.Slots[4] = EliteDrumsLayoutSlot.Ordinary("tom3", null);
                    }
                    else if (count == 7)
                    {
                        layout.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", "right-crash");
                        layout.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom1", "left-crash");
                        layout.Slots[3] = EliteDrumsLayoutSlot.Ordinary("tom2", null);
                        layout.Slots[4] = EliteDrumsLayoutSlot.Ordinary("tom3", null);
                        layout.Slots[5] = EliteDrumsLayoutSlot.Ordinary(null, "ride");
                        layout.Slots[6] = new EliteDrumsLayoutSlot();
                        layout.Slots[7] = new EliteDrumsLayoutSlot();
                        layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
                    }
                    else
                    {
                        layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
                        layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
                    }
                    var snare = Note(EliteDrumPad.Snare);
                    snare.IsFlam = !split;
                    snare.IsFlatFlam = split;
                    var collision = Note(EliteDrumPad.Snare, false, 2d, 960);
                    var crash = Note(EliteDrumPad.RightCrash, false, 2d, 960);
                    collision.AddChildNote(crash);
                    _fixture = new InitializedElitePlayerFixture();
                    yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(snare, collision), Profile(lefty),
                        layout, legacyTheme);
                    var player = _fixture.Player;
                    foreach (var note in new[] { snare, collision, crash })
                        player.GetType().GetMethod("SpawnNote", PreparedElitePlayerFixture.INSTANCE)
                            .Invoke(player, new object[] { note });
                    var elements = player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true)
                        .Where(e => e.gameObject.activeInHierarchy).ToArray();
                    Assert.That(elements.Length, Is.GreaterThanOrEqualTo(2));
                    foreach (var element in elements)
                    {
                        var renderers = element.GetComponentsInChildren<Renderer>()
                            .Where(r => r.gameObject.activeInHierarchy).ToArray();
                        if (renderers.Length == 0) continue;
                        float parentScale = Mathf.Abs(element.transform.lossyScale.x);
                        float origin = element.transform.parent.position.x;
                        var note = (EliteDrumNote) element.GetType().GetProperty("NoteRef").GetValue(element);
                        int position = (int) player.GetType().GetMethod("GetVisualPosition", new[] { typeof(EliteDrumNote) })
                            .Invoke(player, new object[] { note });
                        float columnWidth = 2f;
                        float columnLeft = -1f;
                        var measured = ActiveMeshBounds(element, player.transform);
                        Assert.That(measured.min, Is.GreaterThanOrEqualTo(columnLeft - 0.001f));
                        Assert.That(measured.max, Is.LessThanOrEqualTo(columnLeft + columnWidth + 0.001f));
                        if (ReferenceEquals(note, snare))
                        {
                            var activeGroups = element.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
                                .Where(g => g.gameObject.activeInHierarchy).ToArray();
                            Assert.That(activeGroups, Has.Length.EqualTo(2), "Every eligible hand flam displays two physical gems.");
                            float before = measured.max - measured.min;
                            element.GetType().GetMethod("OnStarPowerUpdated").Invoke(element, null);
                            var after = ActiveMeshBounds(element, player.transform);
                            Assert.That(after.max - after.min, Is.EqualTo(before).Within(0.001f));
                            if (!legacyTheme && !offset)
                            {
                                CapturePairGeometry(element, 720, $"N{count}-lefty{lefty}-flat{split}");
                                CapturePairGeometry(element, 1080, $"N{count}-lefty{lefty}-flat{split}");
                            }
                        }
                    }
                    yield return _fixture.Dispose();
                    _fixture = null;
                }
            }
            finally
            {
                // Fixture cleanup is handled by TearDown if an assertion fails.
            }
        }

        [UnityTest]
        public IEnumerator ElitePairActualPreviewParityVisibilityAndPooling()
        {
            var previewFixture = new ElitePreviewGeometryPlayModeTests();
            var settings = Production("YARG.Settings.SettingsManager").GetProperty("Settings").GetValue(null);
            var layoutSetting = settings.GetType().GetProperty("EliteFlamLayout").GetValue(settings);
            var valueProperty = layoutSetting.GetType().GetProperty("Value");
            var originalLayout = valueProperty.GetValue(layoutSetting);
            try
            {
                foreach (string flamLayout in new[] { "V", "Bucket", "Parallel" })
                foreach (int columns in new[] { 5, 7, 9, 10 })
                foreach (bool lefty in new[] { false, true })
                foreach (bool cymbalModels in new[] { false, true })
                foreach (bool edge in new[] { false, true })
                {
                    valueProperty.SetValue(layoutSetting, Enum.Parse(valueProperty.PropertyType, flamLayout));
                    var layout = PairCoverageLayout(columns);
                    if (!edge)
                    {
                        int interior = columns == 5 ? 2 : 3;
                        var previous = layout.Slots[interior];
                        layout.Slots[interior] = layout.Slots[0];
                        layout.Slots[0] = previous;
                    }
                    var snare = Note(EliteDrumPad.Snare, false, 10d, 4800);
                    var crash = Note(EliteDrumPad.RightCrash, false, 10d, 4800);
                    snare.AddChildNote(crash);
                    var profile = Profile(lefty);
                    profile.UseCymbalModels = cymbalModels;
                    _fixture = new InitializedElitePlayerFixture();
                    yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(snare), profile, layout);
                    var player = _fixture.Player;
                    var resolved = (EliteDrumsResolvedLayout)player.GetType().GetProperty("Layout").GetValue(player);
                    Assert.That(resolved.Lanes.Count, Is.EqualTo(columns));
                    var (preview, previewPool) = previewFixture.CreatePreparedPlayer(layout);
                    preview.GetType().GetProperty("LeftyFlip").SetValue(preview, lefty);
                    preview.GetType().GetProperty("UseCymbalModels").SetValue(preview, cymbalModels);
                    foreach (string shape in new[] { "regular", "flat", "collision" })
                    {
                        bool collision = shape == "collision";
                        if (collision && columns == 10) continue; // Full ten-column layout separates all hand sources; merging a collision resolves to nine.
                        snare.IsFlam = shape == "regular";
                        snare.IsFlatFlam = shape == "flat";
                        // Remove the chord for valid standalone flams, retain its original collision identity separately.
                        var source = collision ? snare : Note(cymbalModels ? EliteDrumPad.RightCrash : EliteDrumPad.Snare, false, 10d, 4800);
                        source.IsFlam = shape == "regular";
                        source.IsFlatFlam = shape == "flat";
                        if (collision) { source.IsFlam = false; source.IsFlatFlam = false; }
                        source.ActivateFlag(NoteFlags.StarPower);
                        crash.ClearFlag(NoteFlags.StarPower);
                        var firstData = PairPreviewData(source, !collision && cymbalModels ? EliteDrumsColorRole.RightCrash : EliteDrumsColorRole.Snare);
                        var secondData = collision ? PairPreviewData(crash, EliteDrumsColorRole.RightCrash) : null;
                        if (collision)
                        {
                            firstData.GetType().GetField("ElitePairPartner").SetValue(firstData, secondData);
                            secondData.GetType().GetField("ElitePairPartner").SetValue(secondData, firstData);
                        }
                        SpawnCoverageNote(player, source);
                        if (collision) SpawnCoverageNote(player, crash);
                        preview.GetType().GetMethod("SpawnNote", PreparedElitePlayerFixture.INSTANCE).Invoke(preview, new[] { firstData });
                        if (collision) preview.GetType().GetMethod("SpawnNote", PreparedElitePlayerFixture.INSTANCE).Invoke(preview, new[] { secondData });
                        var gameplayFirst = FindCoverageElement(player, source);
                        var gameplaySecond = collision ? FindCoverageElement(player, crash) : null;
                        var previewNotes = ((IEnumerable)previewPool.GetType().GetProperty("AllSpawned").GetValue(previewPool)).Cast<Component>().ToArray();
                        var previewFirst = previewNotes.Single(n => ReferenceEquals(n.GetType().GetProperty("NoteRef").GetValue(n), firstData));
                        var previewSecond = collision ? previewNotes.Single(n => ReferenceEquals(n.GetType().GetProperty("NoteRef").GetValue(n), secondData)) : null;
                        for (int swap = 0; swap < 3; swap++)
                        {
                            bool star = swap != 1;
                            bool unchangedPartnerFlag = crash.IsStarPower;
                            if (star) source.ActivateFlag(NoteFlags.StarPower); else source.ClearFlag(NoteFlags.StarPower);
                            firstData.GetType().GetField("ForceStarPower").SetValue(firstData, (bool?)star);
                            gameplayFirst.GetType().GetMethod("UpdateElement", PreparedElitePlayerFixture.INSTANCE).Invoke(gameplayFirst, null);
                            if (collision)
                            {
                                Assert.That(crash.IsStarPower, Is.EqualTo(unchangedPartnerFlag), "Only the first member's SP flag changes.");
                                gameplaySecond.GetType().GetMethod("UpdateElement", PreparedElitePlayerFixture.INSTANCE).Invoke(gameplaySecond, null);
                            }
                            previewFirst.GetType().GetMethod("OnSettingChanged").Invoke(previewFirst, null);
                            if (collision) previewSecond.GetType().GetMethod("OnSettingChanged").Invoke(previewSecond, null);
                            if (shape == "regular")
                            {
                                if (swap == 0 || (flamLayout == "Parallel" && swap == 1))
                                {
                                    CapturePairGeometry(gameplayFirst, 720, $"{flamLayout}-N{columns}-lefty{lefty}-cymbals{cymbalModels}-edge{edge}-star{star}");
                                    CapturePairGeometry(gameplayFirst, 1080, $"{flamLayout}-N{columns}-lefty{lefty}-cymbals{cymbalModels}-edge{edge}-star{star}");
                                }
                                AssertFlamLayout(gameplayFirst, player.transform, flamLayout, lefty, cymbalModels, columns);
                                AssertFlamLayout(previewFirst, preview.transform, flamLayout, lefty, cymbalModels, columns);
                            }
                            var actual = PairMemberBounds(gameplayFirst, gameplaySecond, player.transform);
                            var expected = PairMemberBounds(previewFirst, previewSecond, preview.transform);
                            Assert.That(actual.Length, Is.EqualTo(2));
                            Assert.That(expected.Length, Is.EqualTo(2));
                            for (int member = 0; member < 2; member++)
                            {
                                Assert.That(actual[member].min, Is.EqualTo(expected[member].min).Within(0.001f),
                                    $"Actual gameplay/preview left mesh corner: N={columns}, lefty={lefty}, cymbals={cymbalModels}, {shape}, swap={swap}, member={member}");
                                Assert.That(actual[member].max, Is.EqualTo(expected[member].max).Within(0.001f));
                            }
                            float laneWidth = resolved.GetLaneWidth(2f);
                            float center = resolved.GetSourceX(!collision && cymbalModels ? "right-crash" : "snare", 2f, lefty);
                            float min = actual.Min(b => b.min);
                            float max = actual.Max(b => b.max);
                            float halfPair = (max - min) / 2f;
                            float midpoint = center / (1f - laneWidth / 2f) * (1f - halfPair);
                            if (shape != "regular" || (!cymbalModels && flamLayout == "V"))
                                Assert.That(actual[0].max, Is.EqualTo(actual[1].min).Within(0.001f), "Flat/original drum member bounds meet.");
                            Assert.That((min + max) / 2f, Is.EqualTo(midpoint).Within(0.001f));
                            if (edge && columns < 10) Assert.That(lefty ? max : min, Is.EqualTo(lefty ? 1f : -1f).Within(0.001f),
                                "The outside mesh corner of an edge pair is flush with the highway.");
                            var selected = (Array)gameplayFirst.GetType().GetField(collision ?
                                (star ? "StarPowerNoteGroups" : "NoteGroups") :
                                (star ? "_splitStarGemGroups" : "_splitGemGroups"), PreparedElitePlayerFixture.INSTANCE).GetValue(gameplayFirst);
                            Assert.That(selected.Cast<Component>().Count(g => g != null && g.gameObject.activeInHierarchy),
                                Is.EqualTo(collision ? 1 : 2), "The actual selected normal/SP variant must be visible after the flag transition.");
                            if (collision && swap == 0)
                            {
                                // Capture both independent actual members together, including mixed-SP identity.
                                var captureRoot = new GameObject("Mixed pair capture root");
                                captureRoot.transform.SetParent(player.transform, false);
                                var firstParent = gameplayFirst.transform.parent;
                                var secondParent = gameplaySecond.transform.parent;
                                gameplayFirst.transform.SetParent(captureRoot.transform, true);
                                gameplaySecond.transform.SetParent(captureRoot.transform, true);
                                try
                                {
                                    CapturePairGeometry(captureRoot.transform, 720, $"mixed-N{columns}-lefty{lefty}-cymbals{cymbalModels}-edge{edge}");
                                    CapturePairGeometry(captureRoot.transform, 1080, $"mixed-N{columns}-lefty{lefty}-cymbals{cymbalModels}-edge{edge}");
                                }
                                finally
                                {
                                    gameplayFirst.transform.SetParent(firstParent, true);
                                    gameplaySecond.transform.SetParent(secondParent, true);
                                    UnityEngine.Object.DestroyImmediate(captureRoot);
                                }
                            }
                        }
                        if (!collision) AssertGameplayPairPoolReset(player, gameplayFirst, source, flamLayout, lefty);
                        ReturnCoverageElement(gameplayFirst);
                        if (collision) ReturnCoverageElement(gameplaySecond);
                        previewPool.GetType().GetMethod("ReturnAllObjects").Invoke(previewPool, null);
                    }
                    UnityEngine.Object.Destroy(preview.gameObject);
                    yield return _fixture.Dispose();
                    _fixture = null;
                }
            }
            finally
            {
                valueProperty.SetValue(layoutSetting, originalLayout);
                previewFixture.DisposePreparedPlayers();
            }
        }

        private static EliteDrumsLayoutPreset PairCoverageLayout(int columns)
        {
            var layout = columns == 5 ? EliteDrumsLayoutPreset.Compact : EliteDrumsLayoutPreset.Full;
            layout.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", columns < 10 ? "right-crash" : null);
            if (columns == 5) layout.Slots[4] = EliteDrumsLayoutSlot.Ordinary("tom3", null);
            if (columns == 7)
            {
                layout.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom1", "left-crash");
                layout.Slots[3] = EliteDrumsLayoutSlot.Ordinary("tom2", null);
                layout.Slots[4] = EliteDrumsLayoutSlot.Ordinary("tom3", null);
                layout.Slots[5] = EliteDrumsLayoutSlot.Ordinary(null, "ride");
                layout.Slots[6] = new EliteDrumsLayoutSlot();
                layout.Slots[7] = new EliteDrumsLayoutSlot();
                layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
            }
            if (columns >= 9)
            {
                layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
                layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
                if (columns == 9) layout.Slots[7] = new EliteDrumsLayoutSlot();
            }
            return layout;
        }

        private static object PairPreviewData(EliteDrumNote source, EliteDrumsColorRole role)
        {
            var generator = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
            var type = Production("YARG.Themes.ThemeNoteType");
            var descriptor = generator.GetMethod("Describe").Invoke(null, new[] { (object)role, Enum.Parse(type, "Normal") });
            var data = generator.GetMethod("CreateRoleNote").Invoke(null, new[] { (object)source.Time, descriptor });
            data.GetType().GetField("EliteHandFlam").SetValue(data, source.IsFlam || source.IsFlatFlam);
            data.GetType().GetField("EliteFlatFlam").SetValue(data, source.IsFlatFlam);
            data.GetType().GetField("ForceStarPower").SetValue(data, (bool?)source.IsStarPower);
            return data;
        }

        private static void SpawnCoverageNote(Component player, EliteDrumNote note) => player.GetType()
            .GetMethod("SpawnNote", PreparedElitePlayerFixture.INSTANCE).Invoke(player, new object[] { note });

        private static Component FindCoverageElement(Component player, EliteDrumNote note) => player
            .GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true)
            .Single(e => e.gameObject.activeInHierarchy && ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), note));

        private static void ReturnCoverageElement(Component element)
        {
            var pool = element.GetType().GetProperty("ParentPool").GetValue(element);
            pool.GetType().GetMethod("Return").Invoke(pool, new object[] { element });
        }

        private static void AssertFlamLayout(Component element, Transform highway, string layout, bool lefty, bool cymbal, int columns)
        {
            var active = element.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
                .Where(g => g.gameObject.activeInHierarchy).ToArray();
            Assert.That(active, Has.Length.EqualTo(2));
            // The split arrays encode member identity. Spatial order is a separate
            // contract below, never the input used to decide expected rotation.
            var splitField = element.GetType().GetField("_splitGemGroups", PreparedElitePlayerFixture.INSTANCE);
            Component[] groups;
            if (splitField != null)
            {
                var regular = ((IEnumerable)splitField.GetValue(element)).Cast<Component>();
                var stars = ((IEnumerable)element.GetType().GetField("_splitStarGemGroups", PreparedElitePlayerFixture.INSTANCE).GetValue(element)).Cast<Component>();
                groups = regular.Concat(stars).Where(g => g != null && active.Contains(g)).ToArray();
            }
            else groups = active; // Preview creates the companion after the original.
            Assert.That(groups, Has.Length.EqualTo(2));
            string context = $"layout={layout}, lefty={lefty}, cymbal={cymbal}, element={element.name}, " +
                $"left={groups[0].name}@{groups[0].transform.localPosition}, right={groups[1].name}@{groups[1].transform.localPosition}";
            Debug.Log("Flam geometry: " + context);
            if (layout != "Parallel")
                Assert.That((ActiveMeshBounds(groups[0], highway).min + ActiveMeshBounds(groups[0], highway).max) * 0.5f,
                    Is.LessThan((ActiveMeshBounds(groups[1], highway).min + ActiveMeshBounds(groups[1], highway).max) * 0.5f),
                    "Stable left member must remain visibly left: " + context);
            else
            {
                Assert.That(groups[0].transform.localPosition.z, Is.EqualTo(groups[1].transform.localPosition.z).Within(0.00001f),
                    "Parallel members must have no relative depth: " + context);
                Assert.That(groups[0].transform.localPosition.y, Is.EqualTo(groups[1].transform.localPosition.y).Within(0.00001f));
            }
            var cache = (IDictionary)element.GetType().GetField("_geometry", PreparedElitePlayerFixture.INSTANCE).GetValue(element);
            for (int i = 0; i < groups.Length; i++)
            {
                var geometry = cache[groups[i]];
                var authored = (Quaternion)geometry.GetType().GetField("Rotation", PreparedElitePlayerFixture.INSTANCE).GetValue(geometry);
                float angle = layout == "Parallel" ? (lefty ? -25f : 25f) : (i == 0 ? 25f : -25f);
                var expected = Quaternion.AngleAxis(layout == "Bucket" ? -angle : angle,
                    layout == "Bucket" ? Vector3.forward : Vector3.up) * authored;
                Assert.That(Quaternion.Angle(groups[i].transform.localRotation, expected), Is.LessThan(0.001f),
                    $"{context}, member={i}, authored={authored.eulerAngles}, actual={groups[i].transform.localRotation.eulerAngles}, expected={expected.eulerAngles}");
            }
            if (layout == "Parallel")
            {
                var pairPoints = groups.SelectMany(g => VisibleCorners(g.transform, Matrix4x4.identity)).ToArray();
                var geometry = cache[groups[0]];
                var scale = (Vector3)geometry.GetType().GetField("Scale", PreparedElitePlayerFixture.INSTANCE).GetValue(geometry);
                var position = (Vector3)geometry.GetType().GetField("Position", PreparedElitePlayerFixture.INSTANCE).GetValue(geometry);
                var rotation = (Quaternion)geometry.GetType().GetField("Rotation", PreparedElitePlayerFixture.INSTANCE).GetValue(geometry);
                float laneScale = groups[0].transform.localScale.x / scale.x;
                // Undo pair narrowing independently; authored rotation can mix X into Z.
                float pairFactor = columns > 1 ? (columns - 1f) / columns : 0.5f;
                var ordinary = Matrix4x4.TRS(position, rotation, new Vector3(scale.x * laneScale / pairFactor, scale.y, scale.z));
                var ordinaryPoints = VisibleCorners(groups[0].transform, ordinary, true);
                float actualZ = (pairPoints.Min(p => p.z) + pairPoints.Max(p => p.z)) * 0.5f;
                float ordinaryZ = (ordinaryPoints.Min(p => p.z) + ordinaryPoints.Max(p => p.z)) * 0.5f;
                Assert.That(actualZ, Is.EqualTo(ordinaryZ).Within(0.00001f), "Parallel full visible centerZ must match ordinary gem: " + context);
                foreach (var member in groups)
                {
                    var points = VisibleCorners(member.transform, Matrix4x4.identity);
                    Assert.That((points.Min(p => p.z) + points.Max(p => p.z)) * 0.5f,
                        Is.EqualTo(ordinaryZ).Within(0.00001f), "Each Parallel member independently retains ordinary visible depth: " + context);
                }
            }
            if (!cymbal && layout != "Parallel") return; // V/Bucket drums retain clearance.
            var referenceGeometry = cache[groups[0]];
            var referenceRotation = (Quaternion)referenceGeometry.GetType().GetField("Rotation", PreparedElitePlayerFixture.INSTANCE).GetValue(referenceGeometry);
            Vector3 direction = layout == "Parallel"
                ? Quaternion.AngleAxis(lefty ? -25f : 25f, Vector3.up) * (referenceRotation * Vector3.forward) : Vector3.right;
            if (layout == "Parallel" && direction.z < 0f) direction = -direction;
            if (!cymbal)
            {
                var first = VisibleCorners(groups[0].transform, Matrix4x4.identity);
                var second = VisibleCorners(groups[1].transform, Matrix4x4.identity);
                Assert.That(ProjectedOverlap(first, second, 0f), Is.False, "Parallel geometry must not penetrate: " + context);
                Assert.That(ProjectedOverlap(first, second, -0.0001f), Is.True, "Any inward lateral squeeze must overlap: " + context);
                return;
            }
            var surfaces = groups.Select(g => BaseTriangles(g, highway)).ToArray();
            Assert.That(surfaces[0].Length, Is.GreaterThan(0), context);
            Assert.That(surfaces[1].Length, Is.GreaterThan(0), context);
            float separation = surfaces[1].Min(p => Vector3.Dot(p, direction)) -
                surfaces[0].Max(p => Vector3.Dot(p, direction));
            if (layout == "Parallel")
            {
                var geometryType = Production("YARG.Gameplay.Visuals.EliteDrumsRuntimeGeometry");
                float priorDelta = (float)geometryType.GetMethod("GetLateralContact", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { surfaces[0], surfaces[1] });
                float priorDistance = SurfaceDistance(surfaces[0], surfaces[1].Select(p => p + Vector3.right * priorDelta).ToArray());
                Debug.Log($"Prior projected-hull contact: lateralDelta={priorDelta:R}, complete3DDistance={priorDistance:R}; {context}");
                float outward = SurfaceDistance(surfaces[0], surfaces[1].Select(p => p + Vector3.right * 0.0001f).ToArray());
                Assert.That(outward, Is.GreaterThan(0.00001f), "Outward lateral movement must open the first-contact seam: " + context);
            }
            else Assert.That(separation, Is.EqualTo(0f).Within(0.00001f), "Nonpenetrating base supports: " + context);
            float distance = SurfaceDistance(surfaces[0], surfaces[1]);
            Assert.That(distance, Is.LessThan(0.00001f), $"Bases must physically touch in 3D, distance={distance}: {context}");
        }

        private static bool ProjectedOverlap(Vector3[] left, Vector3[] right, float shift)
        {
            var a = left.Select(p => new Vector2(p.x, p.z)).Distinct().ToArray();
            var b = right.Select(p => new Vector2(p.x + shift, p.z)).Distinct().ToArray();
            foreach (var points in new[] { a, b })
            for (int i = 0; i < points.Length; i++)
            for (int j = i + 1; j < points.Length; j++)
            {
                var edge = points[j] - points[i];
                if (edge.sqrMagnitude < 1e-12f) continue;
                var axis = new Vector2(-edge.y, edge.x).normalized;
                if (a.Max(p => Vector2.Dot(p, axis)) <= b.Min(p => Vector2.Dot(p, axis)) + 0.000001f ||
                    b.Max(p => Vector2.Dot(p, axis)) <= a.Min(p => Vector2.Dot(p, axis)) + 0.000001f) return false;
            }
            return true;
        }

        private static Vector3[] VisibleCorners(Transform group, Matrix4x4 replacement, bool replace = false)
        {
            var points = new System.Collections.Generic.List<Vector3>();
            foreach (var filter in group.GetComponentsInChildren<MeshFilter>(true))
            {
                var bounds = filter.sharedMesh.bounds;
                var child = group.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var matrix = (replace ? replacement : Matrix4x4.TRS(group.localPosition, group.localRotation, group.localScale)) * child;
                for (int i = 0; i < 8; i++)
                    points.Add(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
            }
            return points.ToArray();
        }

        private static Vector3[] BaseTriangles(Component group, Transform highway)
        {
            var result = new System.Collections.Generic.List<Vector3>();
            foreach (var filter in group.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                Assert.That(mesh.isReadable, Is.True, mesh.name);
                if (filter.transform.name == "Upper Cymbal" || filter.transform.parent.name == "Upper Cymbal") continue;
                int[] indices;
                if (filter.transform.name == "Base" || mesh.name == "Cymbal Base") indices = mesh.triangles;
                else if (mesh.name.Contains("Cymbal") && mesh.subMeshCount > 2) indices = mesh.GetTriangles(2);
                else continue;
                var points = mesh.vertices;
                var matrix = highway.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (int index in indices) result.Add(matrix.MultiplyPoint3x4(points[index]));
                Debug.Log($"Flam base mesh {group.name}/{filter.name}, mesh={mesh.name}, triangles={indices.Length / 3}, transform={matrix}");
            }
            return result.ToArray();
        }

        // Independent point/triangle distance: a shared projection is insufficient
        // when two support edges are staggered in height or depth.
        [Test]
        public void BaseSurfaceDistanceRejectsStaggeredCoplanarSupports()
        {
            var left = new[] { Vector3.zero, Vector3.up, Vector3.forward };
            var staggered = left.Select(p => p + Vector3.forward * 3f).ToArray();
            Assert.That(SurfaceDistance(left, staggered), Is.GreaterThan(1f),
                "Identical X support planes do not establish physical contact.");
            var touching = new[] { Vector3.zero, Vector3.down, Vector3.back };
            Assert.That(SurfaceDistance(left, touching), Is.Zero.Within(0.000001f));
        }

        private static float SurfaceDistance(Vector3[] left, Vector3[] right)
        {
            float result = float.PositiveInfinity;
            for (int side = 0; side < 2; side++)
            {
                var points = side == 0 ? left : right;
                var triangles = side == 0 ? right : left;
                foreach (var point in points)
                for (int i = 0; i < triangles.Length; i += 3)
                    result = Mathf.Min(result, PointTriangleDistance(point, triangles[i], triangles[i + 1], triangles[i + 2]));
            }
            float vertexDistance = result;
            for (int i = 0; i < left.Length; i += 3)
            for (int j = 0; j < right.Length; j += 3)
            for (int a = 0; a < 3; a++)
            for (int b = 0; b < 3; b++)
                result = Mathf.Min(result, SegmentDistance(left[i + a], left[i + (a + 1) % 3], right[j + b], right[j + (b + 1) % 3]));
            Debug.Log($"Base distance vertex/triangle={vertexDistance:R}, including edge/edge={result:R}");
            return result;
        }

        private static float SegmentDistance(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
        {
            var u = q - p;
            var v = s - r;
            var w = p - r;
            float a = Vector3.Dot(u, u), b = Vector3.Dot(u, v), c = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w), e = Vector3.Dot(v, w);
            float t = a * c - b * b;
            float x = t > 1e-20f ? Mathf.Clamp01((b * e - c * d) / t) : 0f;
            float y = c > 1e-20f ? (b * x + e) / c : 0f;
            if (y < 0f) { y = 0f; x = a > 1e-20f ? Mathf.Clamp01(-d / a) : 0f; }
            else if (y > 1f) { y = 1f; x = a > 1e-20f ? Mathf.Clamp01((b - d) / a) : 0f; }
            return Vector3.Distance(p + x * u, r + y * v);
        }

        [Test]
        public void BaseSurfaceDistanceIncludesInteriorEdgeContact()
        {
            var a = new[] { new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, -1f, 0f) };
            var b = new[] { new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f) };
            Assert.That(a.Min(p => PointTriangleDistance(p, b[0], b[1], b[2])), Is.GreaterThan(0.1f));
            Assert.That(b.Min(p => PointTriangleDistance(p, a[0], a[1], a[2])), Is.GreaterThan(0.1f));
            Assert.That(SurfaceDistance(a, b), Is.LessThan(0.00001f));
        }

        private static float PointTriangleDistance(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float Edge(Vector3 start, Vector3 end)
            {
                var edge = end - start;
                return Vector3.Distance(p, start + edge * Mathf.Clamp01(Vector3.Dot(p - start, edge) / Mathf.Max(edge.sqrMagnitude, 1e-20f)));
            }
            var normal = Vector3.Cross(b - a, c - a);
            float length = normal.magnitude;
            if (length > 1e-10f)
            {
                normal /= length;
                var q = p - normal * Vector3.Dot(p - a, normal);
                float ab = Vector3.Dot(Vector3.Cross(b - a, q - a), normal);
                float bc = Vector3.Dot(Vector3.Cross(c - b, q - b), normal);
                float ca = Vector3.Dot(Vector3.Cross(a - c, q - c), normal);
                if (ab >= 0f && bc >= 0f && ca >= 0f) return Mathf.Abs(Vector3.Dot(p - a, normal));
            }
            return Mathf.Min(Edge(a, b), Mathf.Min(Edge(b, c), Edge(c, a)));
        }

        private static void AssertReceptorFacingV(Component element, Transform highway)
        {
            var groups = element.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
                .Where(g => g.gameObject.activeInHierarchy).OrderBy(g => ActiveMeshBounds(g, highway).min).ToArray();
            Assert.That(groups, Has.Length.EqualTo(2));
            for (int member = 0; member < groups.Length; member++)
            {
                var cache = (IDictionary)element.GetType().GetField("_geometry", PreparedElitePlayerFixture.INSTANCE).GetValue(element);
                var geometry = cache[groups[member]];
                var authoredRotation = (Quaternion)geometry.GetType().GetField("Rotation", PreparedElitePlayerFixture.INSTANCE).GetValue(geometry);
                // Follow the actual rendered transform hierarchy, undoing only the
                // theme-authored rotation. X-extreme bounding corners are not the
                // horizontal axis: at narrow widths they can select depth/endcap
                // corners and reverse their apparent Z ordering despite a correct V.
                var localAuthoredHorizontal = Quaternion.Inverse(authoredRotation) * Vector3.right;
                var worldHorizontal = groups[member].transform.TransformDirection(localAuthoredHorizontal);
                var highwayHorizontal = highway.InverseTransformDirection(worldHorizontal).normalized;
                Assert.That(highwayHorizontal.x, Is.GreaterThan(0f), "Authored horizontal direction remains left-to-right.");
                float signedAngle = Mathf.Atan2(highwayHorizontal.z, highwayHorizontal.x) * Mathf.Rad2Deg;
                Assert.That(signedAngle, Is.EqualTo(member == 0 ? -25f : 25f).Within(0.001f),
                    $"Actual authored horizontal axis must put the inner end toward receptor (-Z), not a roof; " +
                    $"element={element.GetType().Name}, member={member}, direction={highwayHorizontal}.");
            }
        }

        private static (float min, float max)[] PairMemberBounds(Component first, Component second, Transform highway)
        {
            var groupType = Production("YARG.Gameplay.Visuals.NoteGroup");
            var groups = second == null ? first.GetComponentsInChildren(groupType).Where(g => g.gameObject.activeInHierarchy).ToArray()
                : new[] { first, second };
            return groups.Select(g => ActiveMeshBounds(g, highway)).OrderBy(b => b.min).ToArray();
        }

        private static void AssertGameplayPairPoolReset(Component player, Component element, EliteDrumNote source,
            string layout = "V", bool lefty = false)
        {
            foreach (string shape in new[] { "regular", "flat", "ordinary" })
            {
                ReturnCoverageElement(element);
                var note = Note(EliteDrumPad.Snare, false, source.Time, source.Tick);
                note.IsFlam = shape == "regular";
                note.IsFlatFlam = shape == "flat";
                SpawnCoverageNote(player, note);
                var reused = FindCoverageElement(player, note);
                Assert.That(reused, Is.SameAs(element), "Actual gameplay pool must reuse the returned element.");
                var groups = reused.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
                    .Where(g => g.gameObject.activeInHierarchy).ToArray();
                Assert.That(groups, Has.Length.EqualTo(shape == "ordinary" ? 1 : 2));
                var cache = (IDictionary)reused.GetType().GetField("_geometry", PreparedElitePlayerFixture.INSTANCE).GetValue(reused);
                var resolved = (EliteDrumsResolvedLayout)player.GetType().GetProperty("Layout").GetValue(player);
                float factor = 5f / resolved.Lanes.Count * (shape == "ordinary" ? 1f : (resolved.Lanes.Count - 1f) / resolved.Lanes.Count);
                var ordered = groups.OrderBy(g => ActiveMeshBounds(g, player.transform).min).ToArray();
                for (int i = 0; i < ordered.Length; i++)
                {
                    var geometry = cache[ordered[i]];
                    var authoredScale = (Vector3)geometry.GetType().GetField("Scale", PreparedElitePlayerFixture.INSTANCE).GetValue(geometry);
                    var authoredRotation = (Quaternion)geometry.GetType().GetField("Rotation", PreparedElitePlayerFixture.INSTANCE).GetValue(geometry);
                    var authoredPosition = (Vector3)geometry.GetType().GetField("Position", PreparedElitePlayerFixture.INSTANCE).GetValue(geometry);
                    var scale = ordered[i].transform.localScale;
                    Assert.That(scale.x, Is.EqualTo(authoredScale.x * factor).Within(0.00001f));
                    Assert.That(scale.y, Is.EqualTo(authoredScale.y).Within(0.00001f));
                    Assert.That(scale.z, Is.EqualTo(authoredScale.z).Within(0.00001f));
                    float angle = layout == "Parallel" ? (lefty ? -25f : 25f) : (i == 0 ? 25f : -25f);
                    var expectedRotation = shape == "regular" ? Quaternion.AngleAxis(layout == "Bucket" ? -angle : angle,
                        layout == "Bucket" ? Vector3.forward : Vector3.up) * authoredRotation : authoredRotation;
                    Assert.That(Quaternion.Angle(ordered[i].transform.localRotation, expectedRotation), Is.LessThan(0.001f),
                        "Pooled regular/flat/ordinary gems preserve authored rotation and never accumulate extra rotation.");
                    if (shape != "regular") Assert.That(ordered[i].transform.localPosition.z,
                        Is.EqualTo(authoredPosition.z).Within(0.00001f), "No temporal stagger survives pool reuse.");
                }
                element = reused;
            }
        }

        private static (float min, float max) ActiveMeshBounds(Component root, Transform highway)
        {
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            foreach (var mesh in root.GetComponentsInChildren<MeshFilter>())
            {
                if (!mesh.gameObject.activeInHierarchy || mesh.sharedMesh == null) continue;
                var bounds = mesh.sharedMesh.bounds;
                var matrix = highway.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    float x = matrix.MultiplyPoint3x4(p).x;
                    min = Mathf.Min(min, x);
                    max = Mathf.Max(max, x);
                }
            }
            Assert.That(float.IsFinite(min) && float.IsFinite(max), Is.True, "Active production meshes must be visible.");
            return (min, max);
        }

        private static void CapturePairGeometry(Component element, int height, string scenario)
        {
            var root = new GameObject("Pair geometry capture camera");
            var camera = root.AddComponent<Camera>();
            var render = new RenderTexture(height * 16 / 9, height, 24);
            var pixels = new Texture2D(render.width, render.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                var renderers = element.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                camera.transform.position = bounds.center + element.transform.up * 5f;
                camera.transform.LookAt(bounds.center, element.transform.forward);
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.8f + 0.1f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.targetTexture = render;
                camera.Render();
                RenderTexture.active = render;
                pixels.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0);
                pixels.Apply();
                Assert.That(pixels.GetPixels32().Any(p => p.r > 8 || p.g > 8 || p.b > 8), Is.True,
                    "Graphics readback must contain rendered geometry, not only inactive transform math.");
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    $"yarg-pair-{scenario}-{height}-{Guid.NewGuid():N}.png");
                System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
                Debug.Log($"Pair geometry capture: {path}");
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(pixels);
                render.Release();
                UnityEngine.Object.DestroyImmediate(render);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator DedicatedHiHatHoldSpawnsOneBoundedStripAndAssistedPedalSkipsTerminatorGem()
        {
            var pedal = new EliteDrumNote(EliteDrumPad.HatPedal, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, 1d, 480, false,
                new EliteDrumSourceDefinition("pedal-hold", 0, (int) EliteDrumPad.HatPedal, 480, 480, 1d, 1d));
            var release = new EliteDrumNote(EliteDrumPad.HatPedal, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.InvisibleTerminator, false,
                DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, 2d, 960, false);
            var layout = EliteDrumsLayoutPreset.Full;
            layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
            _fixture = new InitializedElitePlayerFixture();
            yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(pedal, release),
                Profile(false), layout);
            var player = _fixture.Player;
            player.GetType().GetMethod("SpawnNote", PreparedElitePlayerFixture.INSTANCE)
                .Invoke(player, new object[] { release });
            Assert.That(player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true)
                .Count(e => ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), release)), Is.Zero);
            player.GetType().GetMethod("OnNoteSpawned", PreparedElitePlayerFixture.INSTANCE)
                .Invoke(player, new object[] { pedal });
            var strips = player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.LaneElement"), true)
                .Where(e => e.gameObject.activeInHierarchy &&
                    Math.Abs((double) e.GetType().GetProperty("ElementTime").GetValue(e) - pedal.Time) < 0.001d &&
                    Math.Abs((double) e.GetType().GetField("EndTime").GetValue(e) - pedal.SourceDefinition.EndTime) < 0.001d)
                .ToArray();
            Assert.That(strips, Has.Length.EqualTo(1), "Only the authored pedal hold strip should match this time range.");
            var stripRenderers = strips[0].GetComponentsInChildren<Renderer>();
            Assert.That(stripRenderers, Is.Not.Empty);
            int laneCount = (int) player.GetType().GetProperty("LaneCount").GetValue(player);
            Assert.That(laneCount, Is.EqualTo(9), "This scenario requires Full with one explicit kick lane.");
            float minHighwayX = float.PositiveInfinity;
            float maxHighwayX = float.NegativeInfinity;
            var meshDiagnostics = new List<string>();
            foreach (var renderer in stripRenderers)
            {
                Assert.That(renderer, Is.InstanceOf<SkinnedMeshRenderer>(),
                    "The lane prefab's render geometry is expected to be skinned.");
                var skinnedRenderer = (SkinnedMeshRenderer) renderer;
                var bakedMesh = new Mesh();
                try
                {
                    skinnedRenderer.BakeMesh(bakedMesh, true);
                    var highwayFromRenderer = player.transform.worldToLocalMatrix *
                        renderer.transform.localToWorldMatrix;
                    foreach (var vertex in bakedMesh.vertices)
                    {
                        float highwayX = highwayFromRenderer.MultiplyPoint3x4(vertex).x;
                        minHighwayX = Mathf.Min(minHighwayX, highwayX);
                        maxHighwayX = Mathf.Max(maxHighwayX, highwayX);
                    }
                    var meshTransform = (Transform) strips[0].GetType()
                        .GetField("_meshTransform", PreparedElitePlayerFixture.INSTANCE).GetValue(strips[0]);
                    var cachedScale = strips[0].GetType()
                        .GetField("_scale", PreparedElitePlayerFixture.INSTANCE).GetValue(strips[0]);
                    meshDiagnostics.Add($"renderer={renderer.name} mesh={bakedMesh.name} sharedMesh={skinnedRenderer.sharedMesh.name} " +
                        $"vertices={bakedMesh.vertexCount} sharedBounds={skinnedRenderer.sharedMesh.bounds.size} " +
                        $"localBounds={renderer.localBounds.size} bakedBounds={bakedMesh.bounds.size} " +
                        $"transformScale={meshTransform.localScale} cachedLaneScale={cachedScale} " +
                        $"rendererLocalScale={renderer.transform.localScale} rendererLossyScale={renderer.transform.lossyScale} " +
                        "bones=[" + string.Join("; ", skinnedRenderer.bones.Select(bone =>
                            $"{bone.name}:local={bone.localScale}:lossy={bone.lossyScale}")) + "]");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(bakedMesh);
                }
            }
            float measuredWidth = maxHighwayX - minHighwayX;
            Assert.That(measuredWidth, Is.LessThanOrEqualTo(2f / laneCount + 0.001f),
                $"The baked skinned vertices must fit one highway-local lane. measuredWidth={measuredWidth:F6}; " +
                $"laneCount={laneCount}; " + string.Join("; ", meshDiagnostics));
            Production("YARG.Gameplay.Player.BasePlayer").GetMethod("UpdateInputs", PreparedElitePlayerFixture.INSTANCE)
                .Invoke(player, new object[] { 1d });
            var prepared = (InstrumentDifficulty<EliteDrumNote>) _fixture.Session.GetType()
                .GetProperty("PlayableEliteDrumDifficulty").GetValue(_fixture.Session);
            Assert.That(prepared.Notes[0].WasHit, Is.True, "The real engine invokes assisted-pedal feedback.");
            yield return _fixture.Dispose();
            _fixture = null;
        }

        [UnityTest]
        public IEnumerator SavedSlotRecolorAndBreDoNotReplaceSourceNoteColors()
        {
            _fixture = new InitializedElitePlayerFixture();
            yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(Note(EliteDrumPad.Snare)),
                Profile(true), EliteDrumsLayoutPreset.Full);
            var colors = new ColorProfile("Slot separation");
            colors.EliteDrums.Fret1 = System.Drawing.Color.Magenta;
            colors.EliteDrums.SnareNote = System.Drawing.Color.Cyan;
            colors.EliteDrums.SnareInputEffect = System.Drawing.Color.Lime;
            _fixture.Session.GetType().GetProperty("ColorProfile").SetValue(_fixture.Session, colors);
            var player = _fixture.Player;
            var receptor = GetReceptor(player, (int) EliteDrumPad.Snare);
            var array = player.GetComponentInChildren(Production("YARG.Gameplay.Visuals.FretArray"), true);
            var adapter = new ColorProfile.EliteDrumsFretColorAdapter(colors.EliteDrums);
            array.GetType().GetMethod("RecolorFrets").Invoke(array, new object[] { adapter, null });
            var slotColor = (UnityEngine.Color) receptor.GetType()
                .GetField("_originalUnityTopColor", PreparedElitePlayerFixture.INSTANCE).GetValue(receptor);
            Assert.That(slotColor.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(slotColor.g, Is.Zero.Within(0.001f));
            Assert.That(slotColor.b, Is.EqualTo(1f).Within(0.001f));
            var laneObject = new GameObject("BRE appearance probe");
            try
            {
                laneObject.SetActive(false);
                var lane = laneObject.AddComponent(Production("YARG.Gameplay.Visuals.LaneElement"));
                player.GetType().GetMethod("InitializeBRELane", PreparedElitePlayerFixture.INSTANCE)
                    .Invoke(player, new object[] { lane, 7 });
                var actual = (UnityEngine.Color) lane.GetType().GetField("_color", PreparedElitePlayerFixture.INSTANCE).GetValue(lane);
                Assert.That(actual.r, Is.EqualTo(1f).Within(0.001f));
                Assert.That(actual.b, Is.EqualTo(1f).Within(0.001f));
                Assert.That(colors.EliteDrums.GetNoteColor(EliteDrumsColorRole.Snare), Is.EqualTo(System.Drawing.Color.Cyan));
            }
            finally { UnityEngine.Object.Destroy(laneObject); }

            QueuePlayerInput(player, EliteDrumsAction.EliteSnare, 1d);
            UpdatePlayerEngine(player, 1d);
            Assert.That(GetPlayableNotes()[0].WasHit, Is.True);
            AssertEffect(receptor, System.Drawing.Color.Lime);
            Assert.That(slotColor.b, Is.EqualTo(1f).Within(0.001f),
                "A hit effect uses the note identity color while the fret retains its saved-slot color.");
            yield return _fixture.Dispose();
            _fixture = null;
        }

        [UnityTest]
        public IEnumerator ActionOnlyKickStrikesBothSplitReceptorsWithKickInputEffect()
        {
            var layout = EliteDrumsLayoutPreset.Full;
            layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            var ordinaryKick = Note(EliteDrumPad.Kick, false, 2d, 960);
            var doubleKick = Note(EliteDrumPad.Kick, true, 3d, 1440);
            var kickFlam = Note(EliteDrumPad.Kick, false, 4d, 1920);
            kickFlam.IsFlam = true;
            var profile = Profile(false);
            profile.AddSingleModifier(Modifier.Enable2xKicks);
            _fixture = new InitializedElitePlayerFixture();
            yield return _fixture.Initialize(
                PreparedElitePlayerFixture.CreateChart(ordinaryKick, doubleKick, kickFlam), profile, layout);
            var player = _fixture.Player;
            var playable = GetPlayableNotes();
            var colors = new ColorProfile("Targeted kick effects");
            colors.EliteDrums.KickInputEffect = System.Drawing.Color.Magenta;
            colors.EliteDrums.DoubleKickInputEffect = System.Drawing.Color.Cyan;
            colors.EliteDrums.KickFlamInputEffect = System.Drawing.Color.Yellow;
            _fixture.Session.GetType().GetProperty("ColorProfile").SetValue(_fixture.Session, colors);

            QueuePlayerInput(player, EliteDrumsAction.Kick, 1d);
            UpdatePlayerEngine(player, 1d);
            Assert.That(playable[0].WasHit, Is.False, "An action-only input before the chart must not score a future kick.");
            Assert.That(playable[1].WasHit, Is.False);
            AssertEffect(GetReceptor(player, (int) EliteDrumPad.Kick), System.Drawing.Color.Magenta);
            AssertEffect(GetReceptor(player, 100), System.Drawing.Color.Magenta);
            var engine = GetEngine(player);
            Assert.That(GetNotesHit(engine), Is.EqualTo(0));

            QueuePlayerInput(player, EliteDrumsAction.Kick, 2d);
            UpdatePlayerEngine(player, 2d);
            Assert.That(playable[0].WasHit, Is.True, "The ordinary kick must score on the primary receptor.");
            Assert.That(playable[1].WasHit, Is.False);
            AssertEffect(GetReceptor(player, (int) EliteDrumPad.Kick), System.Drawing.Color.Magenta);
            AssertEffect(GetReceptor(player, 100), System.Drawing.Color.Magenta);

            QueuePlayerInput(player, EliteDrumsAction.Kick, 3d);
            UpdatePlayerEngine(player, 3d);
            Assert.That(playable[1].WasHit, Is.True, "IsDoubleKick must score against the second split receptor identity.");
            AssertEffect(GetReceptor(player, (int) EliteDrumPad.Kick), System.Drawing.Color.Cyan);
            AssertEffect(GetReceptor(player, 100), System.Drawing.Color.Cyan);

            QueuePlayerInput(player, EliteDrumsAction.Kick, 4d);
            UpdatePlayerEngine(player, 4d);
            Assert.That(playable[2].WasHit, Is.True,
                "A split kick flam remains one scored kick on its authored identity.");
            AssertEffect(GetReceptor(player, (int) EliteDrumPad.Kick), System.Drawing.Color.Yellow);
            AssertEffect(GetReceptor(player, 100), System.Drawing.Color.Yellow);
            Assert.That(GetNotesHit(engine), Is.EqualTo(3));
            var array = player.GetComponentInChildren(Production("YARG.Gameplay.Visuals.FretArray"), true);
            Assert.That(array.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.KickFret"), true), Is.Empty);
            yield return _fixture.Dispose();
            _fixture = null;
        }

        [UnityTest]
        public IEnumerator CombinedKickFlamSelectsLiveAccentGroup()
        {
            var layout = EliteDrumsLayoutPreset.Full;
            layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
            var flam = Note(EliteDrumPad.Kick, false, 2d, 960);
            flam.IsFlam = true;
            _fixture = new InitializedElitePlayerFixture();
            yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(flam), Profile(false), layout);
            var player = _fixture.Player;
            var note = GetPlayableNotes()[0];
            player.GetType().GetMethod("SpawnNote", PreparedElitePlayerFixture.INSTANCE)
                .Invoke(player, new object[] { note });
            var elementType = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
            var element = player.GetComponentsInChildren(elementType, true)
                .Single(e => ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), note));
            var groups = (Array) elementType.GetField("NoteGroups", PreparedElitePlayerFixture.INSTANCE).GetValue(element);
            var selected = elementType.BaseType.GetField("NoteGroup", PreparedElitePlayerFixture.INSTANCE).GetValue(element);
            int accent = (int) elementType.GetField("ACCENT", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
            Assert.That(selected, Is.SameAs(groups.GetValue(accent)),
                "The live combined kick flam must select Accent rather than the compact ordinary kick group.");
            Assert.That(((Component) selected).gameObject.activeInHierarchy, Is.True);
            yield return _fixture.Dispose();
            _fixture = null;
        }

        [UnityTest]
        public IEnumerator SplitKickOverhitCallbackAnimatesBothKickReceptorsWithoutScoring()
        {
            var layout = EliteDrumsLayoutPreset.Full;
            layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            var pendingSnare = Note(EliteDrumPad.Snare, false, 4d, 1920);
            _fixture = new InitializedElitePlayerFixture();
            yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(
                Note(EliteDrumPad.Snare, false, 1d, 480), pendingSnare), Profile(false),
                layout, noFail: true);
            var player = _fixture.Player;
            var colors = new ColorProfile("Split kick overhit effects");
            colors.EliteDrums.KickInputEffect = System.Drawing.Color.Magenta;
            _fixture.Session.GetType().GetProperty("ColorProfile").SetValue(_fixture.Session, colors);
            var engine = GetEngine(player);
            var playable = GetPlayableNotes();
            AddPadHitDiagnostic(engine, this);

            QueuePlayerInput(player, EliteDrumsAction.EliteSnare, playable[0].Time);
            UpdatePlayerEngine(player, playable[0].Time);
            Assert.That(playable[0].WasHit, Is.True, "Consume a real note before testing the in-chart overhit guard.");
            Assert.That(GetNotesHit(engine), Is.EqualTo(1));
            int callbacksBefore = _padHitDiagnostics.Count;
            var scoreStats = engine.GetType().GetField("EngineStats", BindingFlags.Public | BindingFlags.Instance).GetValue(engine);
            var totalScore = scoreStats.GetType().GetProperty("TotalScore");
            var scoreBefore = totalScore.GetValue(scoreStats);
            QueuePlayerInput(player, EliteDrumsAction.Kick, 2d, _fixture.Manager, _inputSubmissionDiagnostics);
            UpdatePlayerEngine(player, 2d);
            Assert.That(_padHitDiagnostics.Count, Is.EqualTo(callbacksBefore + 1));
            Assert.That(playable[1].WasHit, Is.False);
            Assert.That(GetNotesHit(engine), Is.EqualTo(1), "The unmatched kick must not score another note.");
            Assert.That(totalScore.GetValue(scoreStats), Is.EqualTo(scoreBefore), "The overhit must leave score unchanged.");
            var stats = engine.GetType().GetField("EngineStats", BindingFlags.Public | BindingFlags.Instance).GetValue(engine);
            Assert.That(stats.GetType().GetField("Overhits", BindingFlags.Public | BindingFlags.Instance).GetValue(stats),
                Is.EqualTo(1), "An unmatched kick during the chart must reach the real overhit callback.");
            AssertEffect(GetReceptor(player, (int) EliteDrumPad.Kick), System.Drawing.Color.Magenta, miss: true);
            AssertEffect(GetReceptor(player, 100), System.Drawing.Color.Magenta, miss: true);
            yield return _fixture.Dispose();
            _fixture = null;
        }

        [UnityTest]
        public IEnumerator MatchedUnmatchedAndWildcardFeedbackResolveThroughActualEngineCallbacks()
        {
            var wildcard = Note(EliteDrumPad.Wildcard, false, 1d, 480);
            var snare = Note(EliteDrumPad.Snare, false, 4d, 3840);
            _fixture = new InitializedElitePlayerFixture();
            yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(wildcard, snare), Profile(false),
                EliteDrumsLayoutPreset.Compact, noFail: true);
            var player = _fixture.Player;
            var colors = new ColorProfile("Engine callback effects");
            colors.EliteDrums.RideInputEffect = System.Drawing.Color.Orange;
            colors.EliteDrums.Tom2InputEffect = System.Drawing.Color.Magenta;
            colors.EliteDrums.SnareInputEffect = System.Drawing.Color.Cyan;
            _fixture.Session.GetType().GetProperty("ColorProfile").SetValue(_fixture.Session, colors);
            var engine = GetEngine(player);
            var playable = GetPlayableNotes();
            AddPadHitDiagnostic(engine, this);

            QueuePlayerInput(player, EliteDrumsAction.EliteRide, 1d);
            UpdatePlayerEngine(player, 1d);
            Assert.That(playable[0].WasHit, Is.True, "A real compatible hand input must score the wildcard chart note.");
            Assert.That(GetNotesHit(engine), Is.EqualTo(1), "The engine must report the scored note, not just invoke a callback.");
            AssertEffect(GetReceptor(player, (int) EliteDrumPad.Ride), System.Drawing.Color.Orange);
            Assert.That(playable[1].WasHit, Is.False);

            QueuePlayerInput(player, EliteDrumsAction.EliteTom2, 2d);
            UpdatePlayerEngine(player, 2d);
            var stats = engine.GetType().GetField("EngineStats", BindingFlags.Public | BindingFlags.Instance).GetValue(engine);
            Assert.That(stats.GetType().GetField("Overhits", BindingFlags.Public | BindingFlags.Instance).GetValue(stats), Is.EqualTo(1),
                "An unmatched tom during the chart must be adjudicated as an overhit.");
            AssertEffect(GetReceptor(player, (int) EliteDrumPad.Tom2), System.Drawing.Color.Magenta, miss: true);
            Assert.That(playable[1].WasHit, Is.False);

            // The overhit intentionally latches the native strike-protection barrier for 750 ms.
            // Advance past it while the next chart note is still pending, then strike at its prepared time.
            UpdatePlayerEngine(player, 3d);
            QueuePlayerInput(player, EliteDrumsAction.EliteSnare, playable[1].Time,
                _fixture.Manager, _inputSubmissionDiagnostics);
            UpdatePlayerEngine(player, playable[1].Time);
            if (!playable[1].WasHit)
            {
                Assert.Fail("The matching snare input must score through the engine callback. " +
                    DescribeEngineInputState(player, engine, playable));
            }
            Assert.That(GetNotesHit(engine), Is.EqualTo(2));
            AssertEffect(GetReceptor(player, (int) EliteDrumPad.Snare), System.Drawing.Color.Cyan);
            yield return _fixture.Dispose();
            _fixture = null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                if (_fixture != null) yield return _fixture.Dispose();
            }
            finally
            {
                _fixture = null;

                try
                {
                    if (_hasCapturedOriginalSettings)
                    {
                        var settingsManager = Production("YARG.Settings.SettingsManager");
                        var settingContainer = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
                        settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null,
                            new[] { _originalSettings });
                        settingContainer.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static)
                            .SetValue(null, _originalSettingsInitialized);
                    }
                }
                finally
                {
                    _hasCapturedOriginalSettings = false;
                    if (_ownsAudioManager)
                    {
                        Production("YARG.Core.Audio.GlobalAudioHandler")
                            .GetMethod("Close", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                        _ownsAudioManager = false;
                    }
                }
            }
        }
    }
}
