// pattern: Imperative Shell
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
        public const int SECOND_KICK_RECEPTOR = 100;
        public EliteDrumsResolvedLayout Layout { get; private set; }

        public int GetReceptorKey(EliteDrumNote note) =>
            IsFootPad(note.Pad) && note.IsDoubleKick && Layout.KickMode == EliteDrumsLayoutKickMode.Split
                ? SECOND_KICK_RECEPTOR : note.Pad;

        public bool UsesKickBars => Layout.KickMode == EliteDrumsLayoutKickMode.Bars;
        public bool UsesHistoricalPedalBars { get; private set; }

        internal static string GetLayoutSource(int pad) => (EliteDrumPad) pad switch
        {
            EliteDrumPad.Snare => "snare", EliteDrumPad.HiHat or EliteDrumPad.HatPedal => "hi-hat",
            EliteDrumPad.LeftCrash => "left-crash", EliteDrumPad.Tom1 => "tom1",
            EliteDrumPad.Tom2 => "tom2", EliteDrumPad.Ride => "ride",
            EliteDrumPad.Tom3 => "tom3", EliteDrumPad.RightCrash => "right-crash",
            _ => "kick"
        };

        public int GetVisualPosition(EliteDrumNote note) =>
            _ordering.TryGetValue(GetReceptorKey(note), out var info) ? info.Position : 0;
        private readonly Dictionary<int, HighwayOrderingInfo> _ordering = new();
        private EliteDrumsAction? _pendingOverhitAction;
        private bool _pendingWildcardHit;

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

        public static int GetColorIndex(int pad) => (int) ((EliteDrumPad) pad switch
        {
            EliteDrumPad.Kick => ColorProfile.EliteDrumsFret.Kick,
            EliteDrumPad.Snare => ColorProfile.EliteDrumsFret.Snare,
            EliteDrumPad.HiHat or EliteDrumPad.HatPedal => ColorProfile.EliteDrumsFret.Hat,
            EliteDrumPad.LeftCrash or EliteDrumPad.Tom1 => ColorProfile.EliteDrumsFret.LeftCrashTom1,
            EliteDrumPad.Ride or EliteDrumPad.Tom2 => ColorProfile.EliteDrumsFret.RideTom2,
            EliteDrumPad.RightCrash or EliteDrumPad.Tom3 => ColorProfile.EliteDrumsFret.RightCrashTom3,
            _ => throw new ArgumentOutOfRangeException(nameof(pad), pad, "Unknown Elite fret pad.")
        });

        internal static System.Drawing.Color GetLaneColor(ColorProfile.EliteDrumsColors colors,
            EliteDrumNote note) => colors.GetNoteColor(EliteDrumsColorRoles.GetHandRole(note));

        private GameObject _noteTemplate;

        protected override void SetupTheme()
        {
            // The stock five-lane visual is typed DrumNote/DrumsPlayer. Assemble the
            // typed model-free template at runtime with independent Elite theme models.
            var modelFreePrefab = new GameObject("Native Elite drum note template");
            modelFreePrefab.SetActive(false);
            // Keep the template outside the active highway; it is only a source for
            // ThemeManager's cloned themed prefab and is destroyed after pool prewarm.
            modelFreePrefab.AddComponent<EliteDrumsNoteElement>();
            var themed = ThemeManager.Instance.CreateNotePrefabFromTheme(Player.ThemePreset,
                VisualStyle.EliteDrums, modelFreePrefab, "NativeElite");
            // ThemeContainer's cached prefab can serve multiple players. Each
            // player's preparation owns an isolated source and current-song cache.
            _noteTemplate = Instantiate(themed, transform);
            _noteTemplate.SetActive(false);
            Destroy(modelFreePrefab);
        }

        protected override InstrumentDifficulty<EliteDrumNote> GetNotes(SongChart chart)
        {
            if (Player.ResolvedDrumPlayback != null) return Player.PlayableEliteDrumDifficulty;
            var difficulty = DrumDifficultySelector.SelectNativeEliteTrack(chart, Player.Profile).Clone()
                .GetDifficulty(Player.Profile.CurrentDifficulty);
            // Direct/native sessions also own a clone: normalize before rendering and
            // engine maxima consume it, without changing the cached authored chart.
            DrumPlaybackPreparer.NormalizeEliteFlams(difficulty);
            return difficulty;
        }

        protected override EliteDrumsEngine CreateEngine()
        {
            EngineParams = Player.IsReplay ? (DrumsEngineParameters) Player.EngineParameterOverride :
                Player.EnginePreset.Drums.Create(StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
                    DrumsEngineParameters.DrumMode.ProFourLane);
            if (EngineContainer != null)
            {
                if (Engine != null) Engine.OnFlamFollowUp -= OnFlamFollowUp;
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
            engine.OnFlamFollowUp += OnFlamFollowUp;
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
            Layout = EliteDrumsLayoutResolver.TryResolve(Player.EliteDrumsLayout, out var selectedLayout)
                ? selectedLayout : EliteDrumsLayoutResolver.Resolve(EliteDrumsLayoutPreset.Compact);
            var compact = EliteDrumsLayoutResolver.Resolve(EliteDrumsLayoutPreset.Compact);
            UsesHistoricalPedalBars = Layout.Lanes.Count == compact.Lanes.Count &&
                Layout.Lanes.Select(lane => string.Join(",", lane.EffectiveIds))
                    .SequenceEqual(compact.Lanes.Select(lane => string.Join(",", lane.EffectiveIds)));
            LaneCount = Layout.Lanes.Count;
            _ordering.Clear();
            for (int pad = 0; pad <= (int) EliteDrumPad.RightCrash; pad++)
            {
                string source = IsFootPad(pad) && Layout.KickMode == EliteDrumsLayoutKickMode.Split
                    ? "kick1" : GetLayoutSource(pad);
                if (Layout.SourceToLane.TryGetValue(source, out int lane))
                    _ordering.Add(pad, new HighwayOrderingInfo(ApplyLefty(lane),
                        ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(Layout.Lanes[lane].SavedColumn)));
            }
            if (Layout.KickMode == EliteDrumsLayoutKickMode.Split)
                _ordering.Add(SECOND_KICK_RECEPTOR, new HighwayOrderingInfo(
                    ApplyLefty(Layout.SourceToLane["kick2"]), ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(
                        Layout.Lanes[Layout.SourceToLane["kick2"]].SavedColumn)));
            _fretArray.UseKickFrets = UsesKickBars;
            var colors = Player.ColorProfile.EliteDrums;
            var kickPrefab = ThemeManager.Instance.CreateKickFretPrefabFromTheme(Player.ThemePreset,
                VisualStyle.FiveLaneDrums);
            var slotEffects = new Dictionary<int, EliteDrumsColorRole>();
            foreach (var pair in _ordering)
            {
                if (slotEffects.ContainsKey(pair.Value.ColorIndex)) continue;
                int pad = pair.Key == SECOND_KICK_RECEPTOR ? (int) EliteDrumPad.Kick : pair.Key;
                var note = new EliteDrumNote(pad, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent,
                    EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None, NoteFlags.None,
                    EliteDrumsChannelFlag.None, 0, 0, pair.Key == SECOND_KICK_RECEPTOR);
                slotEffects.Add(pair.Value.ColorIndex, EliteDrumsColorRoles.GetRole(note));
            }
            _fretArray.Initialize(_ordering, LaneCount, kickPrefab,
                new ColorProfile.EliteDrumsFretColorAdapter(colors, slotEffects), Player.ThemePreset,
                VisualStyle.FiveLaneDrums);
            _kickFretFlash.Initialize(colors.GetParticleColor((int) ColorProfile.EliteDrumsFret.Kick).ToUnityColor());
            NoteTrack.SetDrumActivationFlags(Player.Profile.StarPowerActivationType);
            Notes = NoteTrack.Notes;
            TrackEffect.ConfigureEliteDrumFills(Notes, _trackEffects,
                note => UsesKickBars && IsFootPad(note.Pad) ? -1f : GetVisualPosition(note), LaneCount);
            BRELanes = new LaneElement[LaneCount];
            _noteTemplate.GetComponent<EliteDrumsNoteElement>().PrepareSongGeometry(this, Notes);
            NotePool.SetPrefabAndReset(_noteTemplate);
            base.FinishInitialization();
        }

        private int ApplyLefty(int lane) => Player.Profile.LeftyFlip ? LaneCount - 1 - lane : lane;

        public int GetVisualPosition(int pad) =>
            _ordering.TryGetValue(pad, out var info) ? info.Position : LaneCount / 2;

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

            if (!IsNativeAuthoredLane(record)) return false;
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
            if (!IsNativeAuthoredLane(record) ||
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
            IsNativeAuthoredLane(record) && !IsFootPad((int) record.AuthoredPad);

        private static bool IsNativeAuthoredLane(EliteDrumNativeAuthoredLaneRecord record) =>
            record != null && record.MemberSources.Count >= 2 &&
            record.AuthoredPad != EliteDrumPad.HatPedal && record.MemberSources.All(source =>
                source != null && source.Pad == (int) record.AuthoredPad &&
                record.ContainsTick(source.StartTick));

        // Native Elite rolls render from their per-pad authored records below.
        // Playback also supplies generic lane flags for engine bookkeeping; feeding
        // those flags to TrackPlayer would draw a second, cross-pad-truncated strip.
        protected override bool ShouldSpawnNativeLane(EliteDrumNote note) => false;

        protected override void OnNoteSpawned(EliteDrumNote parentNote)
        {
            base.OnNoteSpawned(parentNote);
            foreach (var member in parentNote.AllNotes)
            {
                if (UsesHistoricalPedalBars || member.Pad != (int) EliteDrumPad.HatPedal || member.IsInvisibleTerminator ||
                    member.SourceDefinition == null || member.SourceDefinition.EndTime <= member.SourceDefinition.StartTime) continue;
                if (!LanePool.CanSpawnAmount(1)) BeforeNativeLaneAllocation(1);
                var pedalLane = TakeNativeLane();
                if (pedalLane == null) continue;
                int position = GetVisualPosition(member);
                pedalLane.SetTimeRange(member.Time, member.Time +
                    member.SourceDefinition.EndTime - member.SourceDefinition.StartTime);
                pedalLane.SetIndexRange(position, position);
                pedalLane.SetAppearance(Instrument.FiveLaneDrums, position, position, LaneCount,
                    GetLaneColor(Player.ColorProfile.EliteDrums, member).ToUnityColor(), GetLaneScale());
                pedalLane.EnableFromPool();
            }
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

                int pad = (int) record.AuthoredPad;
                foreach (int receptor in GetNativeAuthoredLaneReceptors(pad, Layout.KickMode))
                {
                    if (!LanePool.CanSpawnAmount(1)) BeforeNativeLaneAllocation(1);
                    var lane = TakeNativeLane();
                    if (lane == null) continue;

                    var position = _ordering.TryGetValue(receptor, out var info) ? info.Position : LaneCount / 2;
                    // Kick bars use the same full-width strip as classic drums. Dedicated
                    // and split kicks follow their configured receptor columns instead.
                    lane.SetTimeRange(timeRange.Start, timeRange.End);
                    lane.SetIndexRange(position, position);
                    lane.SetAppearance(Instrument.FiveLaneDrums, position, position, LaneCount,
                        GetLaneColor(Player.ColorProfile.EliteDrums, surviving[record.MemberSources[0]]).ToUnityColor(),
                        GetLaneScale());
                    lane.ToggleFullWidth(IsFootPad(pad) && UsesKickBars);
                    lane.EnableFromPool();
                }
            }
        }

        internal static int[] GetNativeAuthoredLaneReceptors(int pad, EliteDrumsLayoutKickMode kickMode) =>
            IsFootPad(pad) && kickMode == EliteDrumsLayoutKickMode.Split
                ? new[] { pad, SECOND_KICK_RECEPTOR }
                : new[] { pad };

        protected override void SpawnNote(EliteDrumNote note)
        {
            if (!note.IsInvisibleTerminator) base.SpawnNote(note);
        }

        protected override void InitializeSpawnedNote(IPoolable poolable, EliteDrumNote note) =>
            ((EliteDrumsNoteElement) poolable).NoteRef = note;

        protected override void InitializeSpawnedLane(LaneElement lane, EliteDrumNote note)
        {
            var info = _ordering.TryGetValue(GetReceptorKey(note), out var ordering) ? ordering :
                new HighwayOrderingInfo(LaneCount / 2, 0);
            lane.SetIndexRange(info.Position, info.Position);
            lane.SetAppearance(Instrument.FiveLaneDrums, info.Position, info.Position, LaneCount,
                GetLaneColor(Player.ColorProfile.EliteDrums, note).ToUnityColor(), GetLaneScale());
        }

        protected override void InitializeBRELane(LaneElement lane, int laneIndex)
        {
            lane.SetIndexRange(laneIndex, laneIndex);
            lane.SetAppearance(Instrument.FiveLaneDrums, laneIndex, laneIndex, LaneCount,
                Player.ColorProfile.EliteDrums.GetSlotFretColor(
                    Layout.Lanes[ApplyLefty(laneIndex)].SavedColumn).ToUnityColor(), GetLaneScale());
        }

        // Match Pro drums: its four-column baseline mesh scale is corrected for
        // the five-unit fret-array padding before adapting to the actual columns.
        private float GetLaneScale() => TrackPlayer.TRACK_WIDTH / 4f * (0.97f * 5f / LaneCount);

        protected override void RescaleLanesForBRE()
        {
        }

        private void OnPedalAssisted(EliteDrumNote note)
        {
            (NotePool.GetByKey(note) as EliteDrumsNoteElement)?.HitNote();
            var effect = Player.ColorProfile.EliteDrums.GetInputEffectColor(EliteDrumsColorRoles.GetRole(note));
            _fretArray.PlayHitAnimation(note.Pad, effect, false);
        }

        protected override void OnNoteHit(int index, EliteDrumNote note)
        {
            base.OnNoteHit(index, note);
            (NotePool.GetByKey(note) as EliteDrumsNoteElement)?.HitNote();
            if (note.Pad == (int) EliteDrumPad.Wildcard)
            {
                // The following pad callback supplies the physical strike, not the wildcard identity.
                _pendingWildcardHit = true;
                return;
            }
            var colors = Player?.ColorProfile?.EliteDrums ?? new ColorProfile.EliteDrumsColors();
            var effectRole = IsFootPad(note.Pad) && (note.IsFlam || note.IsFlatFlam)
                ? EliteDrumsColorRole.KickFlam
                : EliteDrumsColorRoles.GetHandRole(note);
            var effect = colors.GetInputEffectColor(effectRole);
            if (IsFootPad(note.Pad) && !UsesKickBars)
            {
                _fretArray.PlayHitAnimation(note.Pad, effect, false);
                if (Layout.KickMode == EliteDrumsLayoutKickMode.Split)
                    _fretArray.PlayHitAnimation(SECOND_KICK_RECEPTOR, effect, false);
                CameraPositioner.Bounce();
            }
            else if (IsFootPad(note.Pad))
            {
                _kickFretFlash.Initialize(effect.ToUnityColor());
                _kickFretFlash.PlayHitAnimation();
                _fretArray.PlayKickFretAnimation();
                CameraPositioner.Bounce();
            }
            else
            {
                _fretArray.PlayHitAnimation(note.Pad, effect,
                    IsCymbal(note.Pad) && Player.Profile.UseCymbalModels);
            }
        }

        private void OnFlamFollowUp(EliteDrumNote note, EliteDrumsAction action, float velocity,
            bool awardedExtraUnit)
        {
            // The source note already completed its lifecycle. Only the physical follow-up
            // gets feedback here; do not hit its pooled gem or repeat SP/solo callbacks.
            _pendingOverhitAction = null;
            var colors = Player?.ColorProfile?.EliteDrums ?? new ColorProfile.EliteDrumsColors();
            var effect = colors.GetInputEffectColor(EliteDrumsColorRoles.GetHandRole(note));
            _fretArray.PlayHitAnimation(note.Pad, effect,
                IsCymbal(note.Pad) && Player.Profile.UseCymbalModels);
        }

        protected override void GameplayDestroy()
        {
            if (Engine != null) Engine.OnFlamFollowUp -= OnFlamFollowUp;
            base.GameplayDestroy();
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

            _pendingOverhitAction = null;
            int pad = GetInputPad(action);
            var effect = (Player?.ColorProfile?.EliteDrums ?? new ColorProfile.EliteDrumsColors())
                .GetInputEffectColor(EliteDrumsColorRoles.GetInputRole(action));
            if (IsFootPad(pad) && !UsesKickBars)
            {
                _fretArray.PlayMissAnimation(pad, effect);
                if (Layout.KickMode == EliteDrumsLayoutKickMode.Split)
                    _fretArray.PlayMissAnimation(SECOND_KICK_RECEPTOR, effect);
            }
            else if (IsFootPad(pad))
            {
                _kickFretFlash.Initialize(effect.ToUnityColor());
                _fretArray.PlayKickFretAnimation();
            }
            else if (_ordering.ContainsKey(pad)) _fretArray.PlayMissAnimation(pad, effect);
        }

        private void OnPadHit(EliteDrumsAction action, bool noteWasHit, bool bonus,
            bool wasOverhitInLane, DrumNoteType type, float velocity)
        {
            if (Engine.IsCodaActive)
                CurrentCoda.HitLane(Engine.CurrentTime, (int) action);

            if (_pendingWildcardHit)
            {
                _pendingWildcardHit = false;
                AnimateUnmatchedAction(action);
            }
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
            int pad = GetInputPad(action);
            var role = EliteDrumsColorRoles.GetInputRole(action);
            var effect = (Player?.ColorProfile?.EliteDrums ?? new ColorProfile.EliteDrumsColors())
                .GetInputEffectColor(role);
            if (IsFootPad(pad) && !UsesKickBars)
            {
                _fretArray.PlayHitAnimation(pad, effect, false);
                if (Layout.KickMode == EliteDrumsLayoutKickMode.Split)
                    _fretArray.PlayHitAnimation(SECOND_KICK_RECEPTOR, effect, false);
                CameraPositioner.Bounce();
            }
            else if (IsFootPad(pad))
            {
                _kickFretFlash.Initialize(effect.ToUnityColor());
                _kickFretFlash.PlayHitAnimation();
                _fretArray.PlayKickFretAnimation();
                CameraPositioner.Bounce();
            }
            else if (_ordering.ContainsKey(pad))
                _fretArray.PlayHitAnimation(pad, effect, IsCymbal(pad) && Player.Profile.UseCymbalModels);
        }

        private static int GetInputPad(EliteDrumsAction action) => action switch
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
                EliteDrumsAction.FourLaneRedDrum or EliteDrumsAction.FiveLaneRedDrum => (int) EliteDrumPad.Snare,
                EliteDrumsAction.FourLaneYellowDrum or EliteDrumsAction.FourLaneYellowCymbal or
                    EliteDrumsAction.FiveLaneYellowCymbal => (int) EliteDrumPad.HiHat,
                EliteDrumsAction.FourLaneBlueDrum or EliteDrumsAction.FourLaneBlueCymbal or
                    EliteDrumsAction.FiveLaneBlueDrum => (int) EliteDrumPad.Ride,
                EliteDrumsAction.FourLaneGreenDrum or EliteDrumsAction.FourLaneGreenCymbal or
                    EliteDrumsAction.FiveLaneGreenDrum => (int) EliteDrumPad.RightCrash,
                EliteDrumsAction.FiveLaneOrangeCymbal => (int) EliteDrumPad.LeftCrash,
                EliteDrumsAction.WildcardPad => (int) EliteDrumPad.Tom1,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown Elite input.")
            };

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
            _pendingWildcardHit = false;
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
            {
                if (Player.Profile.CurrentDifficulty == Difficulty.Beginner &&
                    (action == EliteDrumsAction.WildcardPad ||
                     action is >= EliteDrumsAction.FourLaneRedDrum and <= EliteDrumsAction.FiveLaneOrangeCymbal))
                    return false;
                return action is not (EliteDrumsAction.Kick or EliteDrumsAction.EliteStomp or
                    EliteDrumsAction.EliteSplash or EliteDrumsAction.EliteSnare or
                    EliteDrumsAction.EliteClosedHiHat or EliteDrumsAction.EliteOpenHiHat or
                    EliteDrumsAction.EliteSizzleHiHat or EliteDrumsAction.EliteLeftCrash or
                    EliteDrumsAction.EliteTom1 or EliteDrumsAction.EliteTom2 or
                    EliteDrumsAction.EliteTom3 or EliteDrumsAction.EliteRide or
                    EliteDrumsAction.EliteRightCrash);
            }
            return false;
        }

        public override (ReplayFrame Frame, ReplayStats Stats) ConstructReplayData()
        {
            var frame = new ReplayFrame(Player.Profile, EngineParams, Engine.EngineStats, ReplayInputs.ToArray(),
                Player.ResolvedDrumPlayback);
            return (frame, Engine.EngineStats.ConstructReplayStats(Player.Profile.Name, Player.IsReplay));
        }
    }
}
