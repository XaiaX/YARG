using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Engine;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Replays;
using YARG.Gameplay.HUD;
using YARG.Gameplay.Visuals;
using YARG.Helpers;
using YARG.Helpers.Extensions;
using YARG.Player;
using YARG.Themes;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Gameplay.Player
{
    /// <summary>Plays native Elite notes without downcharting them; only the presentation is five-lane.</summary>
    public sealed class EliteDrumsPlayer : TrackPlayer<EliteDrumsEngine, EliteDrumNote>
    {
        private const int FIXED_LANE_COUNT = 5;
        private readonly Dictionary<int, HighwayOrderingInfo> _ordering = new();
        private EliteDrumsAction? _pendingOverhitAction;

        [SerializeField] private FretArray _fretArray;
        [SerializeField] private KickFretFlash _kickFretFlash;

        public DrumsEngineParameters EngineParams { get; private set; }
        public override bool ShouldUpdateInputsOnResume => false;

        public void SetTrackCameraForNativeElite(Camera camera) =>
            typeof(TrackPlayer).GetField("<TrackCamera>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(this, camera);

        public void SetDrumComponentsForNativeElite(FretArray fretArray, KickFretFlash kickFlash)
        {
            _fretArray = fretArray;
            _kickFretFlash = kickFlash;
        }

        private readonly Dictionary<SongStem, bool> _stemMuteStates = new()
        {
            { SongStem.Drums1, false }, { SongStem.Drums2, false },
            { SongStem.Drums3, false }, { SongStem.Drums4, false }
        };

        public override void SetStemMuteState(bool muted)
        {
            foreach (var stem in new[] { SongStem.Drums1, SongStem.Drums2, SongStem.Drums3, SongStem.Drums4 })
            {
                if (_stemMuteStates[stem] == muted) continue;
                GameManager.ChangeStemMuteState(stem, muted);
                _stemMuteStates[stem] = muted;
            }
            IsStemMuted = muted;
        }
        protected override float[] StarMultiplierThresholds { get; set; } =
            { 0.06f, 0.12f, 0.2f, 0.45f, 0.75f, 1.09f };

        public static bool IsFootPad(int pad) => pad == (int) EliteDrumPad.Kick;
        public static bool IsCymbal(int pad) => pad is (int) EliteDrumPad.HiHat or (int) EliteDrumPad.LeftCrash or
            (int) EliteDrumPad.Ride or (int) EliteDrumPad.RightCrash;

        // A deliberately fixed presentation: no profile-defined 4L/5L ordering may change these positions.
        public static int GetLane(int pad) => (EliteDrumPad) pad switch
        {
            EliteDrumPad.Snare => 0,
            EliteDrumPad.HiHat or EliteDrumPad.HatPedal => 1,
            EliteDrumPad.LeftCrash or EliteDrumPad.Tom1 => 2,
            EliteDrumPad.Ride or EliteDrumPad.Tom2 => 3,
            EliteDrumPad.RightCrash or EliteDrumPad.Tom3 => 4,
            _ => 2
        };

        public static int GetColorIndex(int pad) => IsFootPad(pad)
            ? (int) YARG.Core.Game.ColorProfile.FiveLaneDrumsFret.DoubleKick
            : GetLane(pad) + 1;

        protected override void SetupTheme()
        {
            // The stock five-lane visual is typed DrumNote/DrumsPlayer. Assemble the
            // typed model-free template at runtime and reuse the stock five-lane theme models.
            var modelFreePrefab = new GameObject("Native Elite drum note template");
            modelFreePrefab.SetActive(false);
            // Keep the template outside the active highway; it is only a source for
            // ThemeManager's cloned themed prefab and is destroyed after pool prewarm.
            modelFreePrefab.AddComponent<EliteDrumsNoteElement>();
            var themed = ThemeManager.Instance.CreateNotePrefabFromTheme(Player.ThemePreset,
                VisualStyle.FiveLaneDrums, modelFreePrefab, "NativeElite");
            NotePool.SetPrefabAndReset(themed);
            Destroy(modelFreePrefab);
        }

        protected override InstrumentDifficulty<EliteDrumNote> GetNotes(SongChart chart) =>
            DrumDifficultySelector.SelectNativeEliteTrack(chart, Player.Profile).Clone()
                .GetDifficulty(Player.Profile.CurrentDifficulty);

        protected override EliteDrumsEngine CreateEngine()
        {
            EngineParams = Player.IsReplay ? (DrumsEngineParameters) Player.EngineParameterOverride :
                Player.EnginePreset.Drums.Create(StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
                    DrumsEngineParameters.DrumMode.ProFourLane);
            if (EngineContainer != null)
            {
                GameManager.EngineManager.Unregister(EngineContainer);
                EngineContainer = null;
            }

            var engine = new EliteDrumsEngine(NoteTrack, SyncTrack, EngineParams,
                Player.Profile.IsBot, true, Player.Profile.EffectiveAutoHiHatPedal);
            EngineContainer = GameManager.EngineManager.Register(engine, NoteTrack, Chart, Player.RockMeterPreset);
            HitWindow = EngineParams.HitWindow;
            engine.OnNoteHit += OnNoteHit;
            engine.OnNoteMissed += OnNoteMissed;
            engine.OnOverhit += OnEliteOverhit;
            engine.OnPadHit += OnPadHit;
            engine.OnPedalAssisted += OnPedalAssisted;
            engine.OnSoloStart += OnSoloStart;
            engine.OnSoloEnd += OnSoloEnd;
            engine.OnCodaStart += OnCodaStart;
            engine.OnCodaEnd += OnCodaEnd;
            engine.OnStarPowerPhraseHit += OnStarPowerPhraseHit;
            engine.OnStarPowerPhraseMissed += OnStarPowerPhraseMissed;
            engine.OnStarPowerStatus += OnStarPowerStatus;
            engine.OnStarPowerReady += OnStarPowerReady;
            engine.OnCountdownChange += OnCountdownChange;
            EngineContainer.OnHappinessNearFail += OnHappinessNearFail;
            EngineContainer.OnHappinessOverFail += OnHappinessOverFail;
            return engine;
        }

        protected override void FinishInitialization()
        {
            LaneCount = FIXED_LANE_COUNT;
            _ordering.Clear();
            for (int pad = 0; pad <= (int) EliteDrumPad.RightCrash; pad++)
            {
                if (!IsFootPad(pad))
                    _ordering.Add(pad, new HighwayOrderingInfo(ApplyLefty(GetLane(pad)), GetColorIndex(pad)));
            }
            var colors = Player.ColorProfile.FiveLaneDrums;
            var kickPrefab = ThemeManager.Instance.CreateKickFretPrefabFromTheme(Player.ThemePreset,
                VisualStyle.FiveLaneDrums);
            _fretArray.Initialize(_ordering, LaneCount, kickPrefab, colors, Player.ThemePreset,
                VisualStyle.FiveLaneDrums);
            _kickFretFlash.Initialize(colors.GetParticleColor(0).ToUnityColor());
            NoteTrack.SetDrumActivationFlags(Player.Profile.StarPowerActivationType);
            Notes = NoteTrack.Notes;
            BRELanes = new LaneElement[LaneCount];
            base.FinishInitialization();
            LaneElement.DefineLaneScale(Instrument.FiveLaneDrums, LaneCount);
        }

        private int ApplyLefty(int lane) => Player.Profile.LeftyFlip ? FIXED_LANE_COUNT - 1 - lane : lane;

        public int GetVisualPosition(int pad) => ApplyLefty(GetLane(pad));

        protected override void ResetNoteCounters()
        {
            NoteIndex = 0;
            TotalNotes = 0;
            foreach (var parent in Notes)
            {
                if (parent.IsBigRockEnding) continue;
                foreach (var note in parent.AllNotes)
                {
                    if (!note.IsInvisibleTerminator &&
                        !(Player.Profile.EffectiveAutoHiHatPedal && note.Pad == (int) EliteDrumPad.HatPedal)) TotalNotes++;
                }
            }
        }

        internal static bool IsNativeAuthoredLaneStart(EliteDrumNativeAuthoredLaneRecord record,
            EliteDrumNote parentNote)
        {
            if (record == null || parentNote == null)
            {
                return false;
            }

            if (!IsNativeAuthoredHandLane(record)) return false;
            var firstSource = record.MemberSources[0];
            if (firstSource == null)
            {
                return false;
            }

            foreach (var note in parentNote.AllNotes)
            {
                if (ReferenceEquals(note.SourceDefinition, firstSource))
                {
                    return true;
                }
            }

            return false;
        }

        internal static (double Start, double End)? GetNativeAuthoredLaneTimeRange(
            EliteDrumNativeAuthoredLaneRecord record,
            IReadOnlyDictionary<EliteDrumSourceDefinition, EliteDrumNote> surviving)
        {
            if (!IsNativeAuthoredHandLane(record) ||
                !record.MemberSources.All(source => surviving.ContainsKey(source))) return null;

            double start = double.PositiveInfinity;
            double end = double.NegativeInfinity;
            foreach (var source in record.MemberSources)
            {
                double time = surviving[source].Time;
                start = Math.Min(start, time);
                end = Math.Max(end, time);
            }
            return end > start ? (start, end) : null;
        }

        internal static bool IsNativeAuthoredHandLane(EliteDrumNativeAuthoredLaneRecord record) =>
            record != null && record.MemberSources.Count >= 2 && !IsFootPad((int) record.AuthoredPad) &&
            record.AuthoredPad != EliteDrumPad.HatPedal && record.MemberSources.All(source =>
                source != null && source.Pad == (int) record.AuthoredPad &&
                source.StartTick >= record.StartTick && source.StartTick < record.EndTick);

        protected override void OnNoteSpawned(EliteDrumNote parentNote)
        {
            base.OnNoteSpawned(parentNote);
            if (!Engine.BaseParameters.EnableLanes)
            {
                return;
            }

            // Native authored roll phrases are independent of the generic note lane flags.
            // Only render records that resolve to playable members in this exact chart,
            // matching the typed engine's fail-closed source membership.
            var surviving = new Dictionary<EliteDrumSourceDefinition, EliteDrumNote>();
            foreach (var parent in NoteTrack.Notes)
            {
                foreach (var member in parent.AllNotes)
                {
                    if (!member.IsInvisibleTerminator && member.SourceDefinition != null)
                        surviving.TryAdd(member.SourceDefinition, member);
                }
            }
            foreach (var record in NoteTrack.EliteDrumNativeAuthoredLaneRecords)
            {
                if (!IsNativeAuthoredLaneStart(record, parentNote) ||
                    GetNativeAuthoredLaneTimeRange(record, surviving) is not { } timeRange)
                {
                    continue;
                }

                // Kick/pedal markers do not have a hand lane in the five-lane presentation.
                int pad = (int) record.AuthoredPad;
                if (IsFootPad(pad) || pad == (int) EliteDrumPad.HatPedal)
                {
                    continue;
                }

                if (!LanePool.CanSpawnAmount(1))
                {
                    BeforeNativeLaneAllocation(1);
                }

                var lane = TakeNativeLane();
                if (lane == null)
                {
                    continue;
                }

                var info = _ordering[pad];
                // Match classic drums: the strip spans playable gem onsets, not the
                // wider authored phrase marker (nor any removed practice members).
                lane.SetTimeRange(timeRange.Start, timeRange.End);
                lane.SetIndexRange(info.Position, info.Position);
                lane.SetAppearance(Instrument.FiveLaneDrums, info.Position, info.Position, LaneCount,
                    Player.ColorProfile.FiveLaneDrums.GetNoteColor(info.ColorIndex).ToUnityColor());
                lane.EnableFromPool();
            }
        }

        protected override void SpawnNote(EliteDrumNote note)
        {
            if (!note.IsInvisibleTerminator) base.SpawnNote(note);
        }

        protected override void InitializeSpawnedNote(IPoolable poolable, EliteDrumNote note) =>
            ((EliteDrumsNoteElement) poolable).NoteRef = note;

        protected override void InitializeSpawnedLane(LaneElement lane, EliteDrumNote note)
        {
            var info = _ordering.TryGetValue(note.Pad, out var ordering) ? ordering :
                new HighwayOrderingInfo(2, 0);
            lane.SetAppearance(Instrument.FiveLaneDrums, note.LaneNote, info.Position, LaneCount,
                Player.ColorProfile.FiveLaneDrums.GetNoteColor(info.ColorIndex).ToUnityColor());
        }

        protected override void InitializeBRELane(LaneElement lane, int laneIndex)
        {
            lane.SetAppearance(Instrument.FiveLaneDrums, laneIndex + 2, laneIndex, LaneCount,
                Player.ColorProfile.FiveLaneDrums.GetNoteColor(laneIndex + 1).ToUnityColor());
        }

        protected override void RescaleLanesForBRE() =>
            LaneElement.DefineLaneScale(Instrument.FiveLaneDrums, LaneCount, true);

        private void OnPedalAssisted(EliteDrumNote note)
        {
            (NotePool.GetByKey(note) as EliteDrumsNoteElement)?.HitNote();
        }

        protected override void OnNoteHit(int index, EliteDrumNote note)
        {
            base.OnNoteHit(index, note);
            (NotePool.GetByKey(note) as EliteDrumsNoteElement)?.HitNote();
            if (IsFootPad(note.Pad))
            {
                _kickFretFlash.PlayHitAnimation();
                _fretArray.PlayKickFretAnimation();
                CameraPositioner.Bounce();
            }
            else if (IsCymbal(note.Pad) && Player.Profile.UseCymbalModels)
            {
                _fretArray.PlayCymbalHitAnimation(note.Pad);
            }
            else
            {
                _fretArray.PlayHitAnimation(note.Pad);
            }
        }

        protected override void OnNoteMissed(int index, EliteDrumNote note)
        {
            base.OnNoteMissed(index, note);
            (NotePool.GetByKey(note) as EliteDrumsNoteElement)?.MissNote();
        }

        private void OnEliteOverhit()
        {
            base.OnOverhit();
            if (_pendingOverhitAction is not { } action) return;

            int pad = action switch
            {
                EliteDrumsAction.Kick => (int) EliteDrumPad.Kick,
                EliteDrumsAction.EliteStomp or EliteDrumsAction.EliteSplash => (int) EliteDrumPad.HatPedal,
                EliteDrumsAction.EliteSnare => (int) EliteDrumPad.Snare,
                EliteDrumsAction.EliteClosedHiHat or EliteDrumsAction.EliteOpenHiHat or
                    EliteDrumsAction.EliteSizzleHiHat => (int) EliteDrumPad.HiHat,
                EliteDrumsAction.EliteLeftCrash => (int) EliteDrumPad.LeftCrash,
                EliteDrumsAction.EliteTom1 => (int) EliteDrumPad.Tom1,
                EliteDrumsAction.EliteTom2 => (int) EliteDrumPad.Tom2,
                EliteDrumsAction.EliteTom3 => (int) EliteDrumPad.Tom3,
                EliteDrumsAction.EliteRide => (int) EliteDrumPad.Ride,
                EliteDrumsAction.EliteRightCrash => (int) EliteDrumPad.RightCrash,
                _ => -1
            };
            _pendingOverhitAction = null;
            if (IsFootPad(pad)) _fretArray.PlayKickFretAnimation();
            else if (_ordering.ContainsKey(pad)) _fretArray.PlayMissAnimation(pad);
        }

        private void OnPadHit(EliteDrumsAction action, bool noteWasHit, bool bonus,
            bool wasOverhitInLane, DrumNoteType type, float velocity)
        {
            if (Engine.IsCodaActive)
                CurrentCoda.HitLane(Engine.CurrentTime, (int) action);

            _pendingOverhitAction = noteWasHit ? null : action;
            if (noteWasHit || wasOverhitInLane || _pendingOverhitAction is null) return;

            // Native Elite does not penalize pedal motion, or inputs before/after
            // the chart and during countdown/coda. Give those inputs hit feedback
            // rather than waiting for an overhit that will never be raised.
            if (action is EliteDrumsAction.EliteStomp or EliteDrumsAction.EliteSplash ||
                Engine.NoteIndex == 0 || Engine.NoteIndex >= Notes.Count ||
                Engine.IsWaitCountdownActive || Engine.IsCodaActive)
            {
                AnimateUnmatchedAction(action);
                _pendingOverhitAction = null;
            }
        }

        private void AnimateUnmatchedAction(EliteDrumsAction action)
        {
            int pad = action switch
            {
                EliteDrumsAction.Kick => (int) EliteDrumPad.Kick,
                EliteDrumsAction.EliteStomp or EliteDrumsAction.EliteSplash => (int) EliteDrumPad.HatPedal,
                EliteDrumsAction.EliteSnare => (int) EliteDrumPad.Snare,
                EliteDrumsAction.EliteClosedHiHat or EliteDrumsAction.EliteOpenHiHat or
                    EliteDrumsAction.EliteSizzleHiHat => (int) EliteDrumPad.HiHat,
                EliteDrumsAction.EliteLeftCrash => (int) EliteDrumPad.LeftCrash,
                EliteDrumsAction.EliteTom1 => (int) EliteDrumPad.Tom1,
                EliteDrumsAction.EliteTom2 => (int) EliteDrumPad.Tom2,
                EliteDrumsAction.EliteTom3 => (int) EliteDrumPad.Tom3,
                EliteDrumsAction.EliteRide => (int) EliteDrumPad.Ride,
                EliteDrumsAction.EliteRightCrash => (int) EliteDrumPad.RightCrash,
                _ => -1
            };
            if (IsFootPad(pad))
            {
                _kickFretFlash.PlayHitAnimation();
                _fretArray.PlayKickFretAnimation();
                CameraPositioner.Bounce();
            }
            else if (IsCymbal(pad) && Player.Profile.UseCymbalModels) _fretArray.PlayCymbalHitAnimation(pad);
            else if (_ordering.ContainsKey(pad)) _fretArray.PlayHitAnimation(pad);
        }

        protected override void OnCodaStart(CodaSection coda)
        {
            base.OnCodaStart(coda);
            _fretArray.SetBreMode(true);
        }

        protected override void OnCodaEnd(CodaSection coda)
        {
            base.OnCodaEnd(coda);
            _fretArray.SetBreMode(false);
        }

        protected override void ResetVisuals()
        {
            base.ResetVisuals();
            _pendingOverhitAction = null;
            _fretArray.ResetAll();
        }

        public override void ResetPracticeSection()
        {
            base.ResetPracticeSection();
            _fretArray.ResetAll();
        }

        protected override bool InterceptInput(ref GameInput input)
        {
            // Elite bindings retain the legacy actions for generated targets, but only the
            // selected chart's action family may reach its typed engine or replay recording.
            var action = input.GetAction<EliteDrumsAction>();
            if (Player.Profile.EliteDrumsDownchartTarget is null)
                return action is not (EliteDrumsAction.Kick or EliteDrumsAction.EliteStomp or
                    EliteDrumsAction.EliteSplash or EliteDrumsAction.EliteSnare or
                    EliteDrumsAction.EliteClosedHiHat or EliteDrumsAction.EliteOpenHiHat or
                    EliteDrumsAction.EliteSizzleHiHat or EliteDrumsAction.EliteLeftCrash or
                    EliteDrumsAction.EliteTom1 or EliteDrumsAction.EliteTom2 or
                    EliteDrumsAction.EliteTom3 or EliteDrumsAction.EliteRide or
                    EliteDrumsAction.EliteRightCrash);
            return false;
        }

        public override (ReplayFrame Frame, ReplayStats Stats) ConstructReplayData()
        {
            var frame = new ReplayFrame(Player.Profile, EngineParams, Engine.EngineStats, ReplayInputs.ToArray());
            return (frame, Engine.EngineStats.ConstructReplayStats(Player.Profile.Name, Player.IsReplay));
        }
    }
}
