// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YARG.Core.Diagnostics;
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
using YARG.Helpers.Authoring;
using YARG.Helpers.Extensions;
using YARG.Player;
using YARG.Settings;
using YARG.Themes;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Gameplay.Player
{
    /// <summary>Plays native Elite notes without downcharting them; only the presentation is five-lane.</summary>
    public sealed class EliteDrumsPlayer : TrackPlayer<EliteDrumsEngine, EliteDrumNote>
    {
        private const float LANE_WIDTH_FRACTION = 0.97f;
        private const int FIXED_LANE_COUNT = 5;
        public const int SECOND_KICK_RECEPTOR = 100;
        public EliteDrumsResolvedLayout Layout { get; private set; }

        public int GetReceptorKey(EliteDrumNote note) =>
            IsFootPad(note.Pad) && note.IsDoubleKick && Layout.KickMode == EliteDrumsLayoutKickMode.Split
                ? SECOND_KICK_RECEPTOR : note.Pad;

        private OpenHatPulsePlan _openHatPulsePlan = OpenHatPulsePlan.Preview;
        private EliteDoubleKickWarningPlan _doubleKickWarning = EliteDoubleKickWarningPlan.Empty;
        private EliteDoubleKickWarningVisual _doubleKickVisual;
        private readonly List<(double Start, double End)> _visibleKickWindows = new();
        private readonly List<(double Time, bool IsKick)> _visibleKickMarkers = new();
        private uint _openHatVisualTick;
        internal float OpenHatPulsesPerQuarter =>
            _openHatPulsePlan.PulsesPerQuarter(Player.Profile.OpenHatPulseCadence, _openHatVisualTick);

        private float _highwayWidth = TRACK_WIDTH;
        public override float HighwayWidth => _highwayWidth;
        public bool AppliesHighwayGrowth { get; private set; }

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
        private double[] _breLaneLastHit = Array.Empty<double>();
        private EliteDrumsAction? _pendingOverhitAction;
        private bool _pendingWildcardHit;
        private HiHatSustainVisual _hiHatSustainVisual;
        private bool _hiHatFretSustained;

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

        private static readonly SongStem[] _drumStems =
        {
            SongStem.Drums1, SongStem.Drums2, SongStem.Drums3, SongStem.Drums4
        };

        public override void SetStemMuteState(bool muted)
        {
            if (IsStemMuted == muted) return;
            foreach (var stem in _drumStems)
                GameManager.ChangeStemMuteState(stem, muted);
            IsStemMuted = muted;
        }

        internal IEnumerable<EffectLight> GetHiHatLights() =>
            _fretArray.GetHitLights((int) EliteDrumPad.HiHat, includeSustain: true);
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
        private EliteDrumsLayoutPreset _cymbalLayout;
        private IReadOnlyList<EliteDrumsPlayableLaneSegment> _playableLaneSegments =
            Array.Empty<EliteDrumsPlayableLaneSegment>();
        private EliteDrumsRollLayout _rollLayout;
        private EliteFretBoundaryDotVisual _fretBoundaryDots;
        private FretBoundaryDotSubdivision _fretBoundaryDotSpacing;
        private bool _alwaysShowFretBoundaryDots;
        private bool _colorHeldFretCenters;
        private bool _colorAllFretCenters;
        private int? _mergedAction;
        private int? _mergedKeeperAction;

        public static byte ResolveCymbalMergePolicy(bool isReplay, BaseEngineParameters recorded) =>
            isReplay ? (recorded as DrumsEngineParameters)?.EliteCymbalMergePolicy
                ?? EliteDrumsCymbalProjection.LEGACY_POLICY : EliteDrumsCymbalProjection.CURRENT_POLICY;

        protected override bool HasPhysicalInputAliases => _mergedAction.HasValue;

        protected override int? GetPhysicalInputAliasGroup(int action) =>
            action == _mergedAction || action == _mergedKeeperAction ? _mergedKeeperAction : null;

        protected override void SetupTheme()
        {
            // The stock five-lane visual is typed DrumNote/DrumsPlayer. Assemble the
            // typed model-free template at runtime with independent Elite theme models.
            var modelFreePrefab = new GameObject("Native Elite drum note template");
            modelFreePrefab.SetActive(false);
            // Keep the template outside the active highway; it is only a source for
            // ThemeManager's cloned themed prefab and is destroyed after pool prewarm.
            modelFreePrefab.AddComponent<EliteDrumsNoteElement>();
            var themed = ThemeManager.Instance.CreateEliteNotePrefab(Player.ThemePreset, modelFreePrefab,
                Player.Profile.UseCymbalModels && Player.Profile.UseVolumeCymbals);
            // ThemeContainer's cached prefab can serve multiple players. Each
            // player's preparation owns an isolated source and current-song cache.
            _noteTemplate = Instantiate(themed, transform);
            _noteTemplate.SetActive(false);
            Destroy(modelFreePrefab);
        }

        protected override InstrumentDifficulty<EliteDrumNote> GetNotes(SongChart chart)
        {
            if (Player.ResolvedDrumPlayback != null) return Player.PlayableEliteDrumDifficulty;
            var difficulty = DrumDifficultySelector.SelectNativeEliteTrack(chart, Player.Profile,
                DrumsPlayer.ResolveClassicArticulationRuleset(Player.IsReplay, Player.EngineParameterOverride)).Clone()
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
                    DrumsEngineParameters.DrumMode.ProFourLane,
                    SettingsManager.Settings?.EliteHiHatClosureForgiveness.Value ?? false,
                    lenientHiHatArticulation: SettingsManager.Settings?.LenientHiHatArticulation.Value ?? false,
                    alternateGhostScoring: SettingsManager.Settings?.AlternateGhostScoring.Value ?? false);
            if (EngineContainer != null)
            {
                if (Engine != null)
                {
                    Engine.OnFlamFollowUp -= OnFlamFollowUp;
                    Engine.OnHiHatSustainEnded -= OnHiHatSustainEnded;
                    Engine.OnHiHatSustainStarted -= OnHiHatSustainStarted;
                }
                GameManager.EngineManager.Unregister(EngineContainer);
                EngineContainer = null;
            }

            _cymbalLayout ??= Player.EliteDrumsLayout?.Copy();
            _mergedAction = _mergedKeeperAction = null;
            foreach (var action in new[] { EliteDrumsAction.EliteLeftCrash, EliteDrumsAction.EliteRide,
                EliteDrumsAction.EliteRightCrash })
            {
                var canonical = EliteDrumsCymbalProjection.NormalizeAction(action, _cymbalLayout,
                    EngineParams.EliteCymbalMergePolicy);
                if (canonical == action) continue;
                _mergedAction = (int)action;
                _mergedKeeperAction = (int)canonical;
            }
            // The engine's idempotent projection runs after profile modifiers, including
            // direct/practice sessions, and before scores and visual geometry are built.
            var engine = new EliteDrumsEngine(NoteTrack, SyncTrack, EngineParams,
                Player.Profile.IsBot, true, Player.Profile.EffectiveAutoHiHatPedal, cymbalLayout: _cymbalLayout);
            _doubleKickWarning = PrepareDoubleKickWarning();
            _playableLaneSegments = EngineParams.EliteCymbalMergePolicy >= EliteDrumsCymbalProjection.PHYSICAL_UNION_POLICY &&
                EliteDrumsCymbalPairs.TryGet(_cymbalLayout, out _)
                ? EliteDrumsPlayableLaneSegments.ResolvePhysical(NoteTrack)
                : EliteDrumsPlayableLaneSegments.Resolve(NoteTrack);
            // Practice slices recreate the engine after the fret layout is already resolved.
            if (Layout != null) PrepareRollLayout();
            EngineContainer = GameManager.EngineManager.Register(engine, NoteTrack, Chart, Player.RockMeterPreset);
            HitWindow = EngineParams.HitWindow;
            engine.OnNoteHit += OnNoteHit;
            engine.OnNoteMissed += OnNoteMissed;
            engine.OnOverhit += OnEliteOverhit;
            engine.OnPadHit += OnPadHit;
            engine.OnPedalAssisted += OnPedalAssisted;
            engine.OnFlamFollowUp += OnFlamFollowUp;
            engine.OnHiHatSustainEnded += OnHiHatSustainEnded;
            engine.OnHiHatSustainStarted += OnHiHatSustainStarted;
            if (_hiHatSustainVisual != null) ConfigureHiHatSustainVisual(engine);
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
            Layout = EliteDrumsLayoutResolver.TryResolve(_cymbalLayout, out var selectedLayout)
                ? selectedLayout : EliteDrumsLayoutResolver.Resolve(EliteDrumsLayoutPreset.Compact);
            int activePlayers = GameManager.YargPlayers?.Count(p => !p.SittingOut) ?? GameManager.TotalPlayers;
            AppliesHighwayGrowth = Layout.GrowHighway && activePlayers <= 1;
            _highwayWidth = AppliesHighwayGrowth ? TRACK_WIDTH * Layout.TotalWeight / 5f : TRACK_WIDTH;
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
            PrepareRollLayout();
            _fretArray.TrackWidth = HighwayWidth;
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
            _fretArray.SetColumnEdges(Layout.GetRestingEdges(Player.Profile.LeftyFlip));
            if (Player.Profile.AutomaticFretColors)
                _fretArray.ApplyAutomaticColors(Layout, colors, Player.Profile.LeftyFlip, _cymbalLayout, Player.Profile.FretGradient);
            _kickFretFlash.Initialize(colors.GetParticleColor((int) ColorProfile.EliteDrumsFret.Kick).ToUnityColor());
            NoteTrack.SetDrumActivationFlags(Player.Profile.StarPowerActivationType);
            Notes = NoteTrack.Notes;
            _openHatVisualTick = 0;
            TrackEffect.ConfigureEliteDrumFills(Notes, _trackEffects,
                note => UsesKickBars && IsFootPad(note.Pad) ? -1f : GetVisualPosition(note), LaneCount);
            BRELanes = new LaneElement[LaneCount];
            _breLaneLastHit = new double[LaneCount];
            _noteTemplate.GetComponent<EliteDrumsNoteElement>().PrepareSongGeometry(this, Notes);
            NotePool.SetPrefabAndReset(_noteTemplate);
            base.FinishInitialization();
            if (_ordering.ContainsKey((int)EliteDrumPad.HiHat))
            {
                _fretArray.UseBaseSustainAnimation((int)EliteDrumPad.HiHat);
                if (_hiHatSustainVisual == null)
                {
                    var root = new GameObject("Hi-hat sustains");
                    root.transform.SetParent(transform, false);
                    _hiHatSustainVisual = root.AddComponent<HiHatSustainVisual>();
                }
                ConfigureHiHatSustainVisual(Engine);
            }
        }

        private void ConfigureHiHatSustainVisual(EliteDrumsEngine engine)
        {
            int position = GetVisualPosition((int)EliteDrumPad.HiHat);
            float column = RestingWidth(position);
            float headCenter = RestingCenter(position);
            var (first, end) = EliteDrumsRuntimeGeometry.GetPedalColumnRange(position, LaneCount);
            float left = RestingCenter(first) - RestingWidth(first) * .5f;
            float right = RestingCenter(end - 1) + RestingWidth(end - 1) * .5f;
            float center = (left + right) * .5f;
            float width = right - left;
            if (Player.Profile.OneFretHiHatSustains)
            {
                center = headCenter;
                width = column;
            }
            var color = Player.ColorProfile.EliteDrums.GetNoteColor(EliteDrumsColorRole.HatOpen).ToUnityColor();
            _hiHatSustainVisual.Configure(engine, center, width, color);
            // Keep the actual hi-hat axis separate from the shifted pedal footprint;
            // the full fret width defines the narrow, waiting sustain.
            float coreWidth = column * (Player.Profile.OneFretHiHatSustains ? 1 : .25f);
            _hiHatSustainVisual.ConfigureEnvelopes(SyncTrack, headCenter, coreWidth, column);
            _hiHatSustainVisual.ConfigureRollHeads(this);
            _hiHatSustainVisual.SetColors(color, Player.ColorProfile.EliteDrums
                .GetNoteStarPowerColor(EliteDrumsColorRole.HatOpen).ToUnityColor());
            SetHiHatSustained(false);
        }

        protected override void UpdateVisuals(double visualTime)
        {
            UpdateDoubleKickWarning(visualTime);
            UpdateRollFrets(visualTime);
            UpdateOpenHatCadence(visualTime);
            base.UpdateVisuals(visualTime);
            UpdateHiHatSustains(visualTime);
            if (Engine.IsCodaActive)
            {
                for (int lane = 0; lane < BRELanes.Length; lane++)
                {
                    BRELanes[lane]?.SetEmissionColor(
                        CodaSection.GetNormalizedTimeSinceLastHit(visualTime, _breLaneLastHit[lane]));
                }
            }
        }

        private EliteDoubleKickWarningPlan PrepareDoubleKickWarning()
        {
            var playback = Player.ResolvedDrumPlayback;
            bool includesExtraKicks = playback != null ? playback.ExtraKickPolicy == DrumExtraKickPolicy.Include
                : Player.Profile.CurrentDifficulty == Difficulty.ExpertPlus;
            if (!includesExtraKicks) return EliteDoubleKickWarningPlan.Empty;

            AuthoredDrumSourceTier source = null;
            if (playback != null)
                Chart.AuthoredDrumSources.TryGetTier(playback.SourceFormat, playback.SourceDifficulty, out source);
            else if (Chart.EliteDrums.TryGetDifficulty(Difficulty.ExpertPlus, out var native) &&
                native.Notes.Exists(note => !note.IsInvisibleTerminator))
                Chart.AuthoredDrumSources.TryGetTier(DrumSourceFormat.Elite, Difficulty.Expert, out source);
            return EliteDoubleKickWarningPlan.FromChart(NoteTrack, SyncTrack, source?.PairedKickTicks);
        }

        private void UpdateDoubleKickWarning(double visualTime)
        {
            var settings = SettingsManager.Settings;
            var style = settings?.EliteDoubleKickWarningStyle.Value ?? DoubleKickWarningStyle.EdgeRails;
            bool through = settings?.EliteDoubleKickWarningThroughMeasures.Value ?? true;
            bool combined = style == DoubleKickWarningStyle.EmberTrailsAndFlames;
            _visibleKickWindows.Clear();
            if (style != DoubleKickWarningStyle.Off && style != DoubleKickWarningStyle.BeaterFlames && NoteSpeed > 0)
            {
                double horizon = visualTime + (ZeroFadePosition - STRIKE_LINE_POS) / NoteSpeed;
                _doubleKickWarning.GetVisibleWindows(visualTime, horizon, through, _visibleKickWindows);
            }

            bool flames = (style == DoubleKickWarningStyle.BeaterFlames ||
                (combined && (!(settings?.EliteDoubleKickFlamesRequireFullMultiplier.Value ?? true) ||
                    Engine.BaseStats.ScoreMultiplier == CurrentMaxMultiplier))) &&
                _doubleKickWarning.IsActive(visualTime, through);
            if (!flames && _visibleKickWindows.Count == 0)
            {
                _doubleKickVisual?.Hide();
                return;
            }

            if (_doubleKickVisual == null)
            {
                var root = new GameObject("Elite double kick warning") { layer = gameObject.layer };
                root.transform.SetParent(transform, false);
                _doubleKickVisual = root.AddComponent<EliteDoubleKickWarningVisual>();
            }
            _visibleKickMarkers.Clear();
            if (combined || style == DoubleKickWarningStyle.EmberTrails)
                _doubleKickWarning.GetEmberMarkers(_visibleKickWindows, _visibleKickMarkers);
            float pulse = flames ? _doubleKickWarning.BeaterKickPulse(visualTime, through) : 0;
            _doubleKickVisual.Show(visualTime, NoteSpeed, ZeroFadePosition, _visibleKickWindows,
                style == DoubleKickWarningStyle.EdgeRails, _visibleKickMarkers,
                settings?.StaggerEliteKickSparkAnimation.Value ?? true, flames, pulse);
        }

        private void UpdateOpenHatCadence(double visualTime)
        {
            if (!_openHatPulsePlan.HasTempoSections) return;
            float previous = OpenHatPulsesPerQuarter;
            _openHatVisualTick = SyncTrack.TimeToTick(Math.Max(0, visualTime));
            if (OpenHatPulsesPerQuarter == previous) return;
            foreach (var element in NotePool.AllSpawned)
                if (element is EliteDrumsNoteElement gem) gem.RefreshOpenHatGlow();
        }

        private void UpdateHiHatSustains(double visualTime)
        {
            if (Player.Profile.HideHiHatSustains)
            {
                _hiHatSustainVisual?.Clear(resetActivationTimes: false);
                SetHiHatSustained(false);
                return;
            }
            _hiHatSustainVisual?.SetBeatPhase((float)GameManager.BeatEventHandler.Visual.QuarterNote.CurrentPercentage);
            _hiHatSustainVisual?.Render(visualTime, NoteSpeed, ZeroFadePosition + NOTE_SPAWN_OFFSET);
            SetHiHatSustained(Engine.ActiveHiHatSustain != null);
        }

        private void SetHiHatSustained(bool active)
        {
            if (Player == null) return;
            active &= !Player.Profile.HideHiHatSustains;
            if (_hiHatFretSustained == active || !_ordering.ContainsKey((int)EliteDrumPad.HiHat)) return;
            _hiHatFretSustained = active;
            _fretArray.SetSustained((int)EliteDrumPad.HiHat, active);
        }

        private void OnHiHatSustainStarted(EliteHiHatSustain sustain, double time) =>
            _hiHatSustainVisual?.NotifyActivation(sustain, time);

        private void OnHiHatSustainEnded(EliteHiHatSustain sustain, bool broken)
        {
            if (broken) _hiHatSustainVisual?.ResetInheritedWidth();
            SetHiHatSustained(false);
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

        private void PrepareRollLayout()
        {
            var cadence = Player.Profile.OpenHatPulseCadence;
            _openHatPulsePlan = cadence == DrumOpenHatPulseCadence.Auto && SyncTrack != null
                ? OpenHatPulsePlan.FromChart(NoteTrack.Notes, SyncTrack) : OpenHatPulsePlan.Preview;
            _rollLayout = new EliteDrumsRollLayout(GetRollStrips(), NoteTrack.Notes, LaneCount, Player.Profile.LeftyFlip,
                GetReceptorKey, note => EliteDrumsRuntimeGeometry.GetCollisionPartner(this, note), GetVisualPosition,
                SyncTrack, GetCenterDotFootprint, Player.Profile.DynamicFretWidths, Player.Profile.ExpandFretsForFlams, UsesKickBars,
                Layout.GetRestingEdges(Player.Profile.LeftyFlip), HighwayWidth, Layout.FitGemsToFret);
            if (Player.Profile.AutomaticFretColors && (Player.Profile.ColoredHeldFretDots == true || Player.Profile.ColoredFretDots == true))
                _rollLayout.PrepareDotCymbals(GetVisualPosition);
            _fretBoundaryDotSpacing = 0;
            UpdateFretBoundaryDotRows();
        }

        private IEnumerable<EliteDrumsRollLayout.Strip> GetRollStrips()
        {
            if (!EngineParams.EnableLanes) yield break;
            foreach (var segment in _playableLaneSegments)
            {
                int pad = segment.Pad;
                // Pedal-down duration describes hat state, not a rendered roll lane.
                if (pad == (int) EliteDrumPad.HatPedal || segment.EndTime <= segment.StartTime) continue;
                foreach (int receptor in GetNativeAuthoredLaneReceptors(pad, Layout.KickMode))
                    yield return new EliteDrumsRollLayout.Strip(segment, receptor, GetVisualPosition(receptor),
                        IsFootPad(pad) && UsesKickBars);
            }
        }

        private void UpdateRollFrets(double visualTime)
        {
            _fretArray.SetColumnEdges(_rollLayout?.Sample(visualTime));
            _fretArray.UpdateColorSplit(_rollLayout, visualTime);
            UpdateFretBoundaryDotRows();
            _fretBoundaryDots?.Render(visualTime, NoteSpeed, ZeroFadePosition + NOTE_SPAWN_OFFSET);
        }

        private void UpdateFretBoundaryDotRows()
        {
            if (_rollLayout == null) return;
            bool alwaysShow = Player.Profile.AlwaysShowFretDots == true;
            bool colorHeldCenters = Player.Profile.ColoredHeldFretDots == true;
            bool colorAllCenters = Player.Profile.ColoredFretDots == true;
            if (_fretBoundaryDots == null)
            {
                if (!_rollLayout.HasExpansion && !alwaysShow) return;
                var root = new GameObject("Elite fret boundary dots") { layer = gameObject.layer };
                root.transform.SetParent(transform, false);
                _fretBoundaryDots = root.AddComponent<EliteFretBoundaryDotVisual>();
                _fretBoundaryDots.Diamond = Player.Profile.DiamondFretDots;
            }
            _fretBoundaryDots.SizeScale = Mathf.Clamp(Player.Profile.FretDotSizePercent, 25, 200) / 100f;
            _fretBoundaryDots.MarkCollisionFrets = Player.Profile.MarkCollisionFrets;
            _fretBoundaryDots.SmallScale = Player.Profile.SmallFretDotPercent == 0 ? 0f :
                Player.Profile.DynamicFretWidths || Player.Profile.MarkCollisionFrets
                    ? Mathf.Clamp(Player.Profile.SmallFretDotPercent, 6.25f, 200f) / 100f : _fretBoundaryDots.SizeScale;
            var spacing = (FretBoundaryDotSubdivision)(Player.Profile.FretDotSubdivision ?? 32);
            spacing = spacing is FretBoundaryDotSubdivision.Eighth or FretBoundaryDotSubdivision.Sixteenth
                or FretBoundaryDotSubdivision.ThirtySecond or FretBoundaryDotSubdivision.SixtyFourth
                or FretBoundaryDotSubdivision.HundredTwentyEighth
                ? spacing : SettingsManager.DEFAULT_FRET_BOUNDARY_DOT_SPACING;
            if (spacing == _fretBoundaryDotSpacing && alwaysShow == _alwaysShowFretBoundaryDots &&
                colorHeldCenters == _colorHeldFretCenters && colorAllCenters == _colorAllFretCenters) return;
            _fretBoundaryDotSpacing = spacing;
            _alwaysShowFretBoundaryDots = alwaysShow;
            _colorHeldFretCenters = colorHeldCenters;
            _colorAllFretCenters = colorAllCenters;
            Color32[] fretTints = null;
            if (colorHeldCenters || colorAllCenters)
            {
                fretTints = new Color32[LaneCount];
                for (int column = 0; column < LaneCount; column++)
                {
                    var color = GetFretSlotColor(column);
                    fretTints[column] = new Color32(color.R, color.G, color.B, 255);
                }
            }
            if (fretTints != null && Player.Profile.AutomaticFretColors)
            {
                var pairs = new (Color32 Left, Color32 Right, bool Paired)[LaneCount];
                for (int column = 0; column < LaneCount; column++)
                {
                    var tints = FretArray.GetLaneTints(Layout.Lanes[ApplyLefty(column)], Player.ColorProfile.EliteDrums, _cymbalLayout);
                    pairs[column] = Player.Profile.LeftyFlip ? (tints.Cymbal, tints.Drum, tints.Paired)
                        : (tints.Drum, tints.Cymbal, tints.Paired);
                    fretTints[column] = Color.Lerp(tints.Drum, tints.Cymbal, .5f);
                }
                _fretBoundaryDots.SplitTints = pairs;
                _fretBoundaryDots.CymbalOnLeft = Player.Profile.LeftyFlip;
                _fretBoundaryDots.CymbalCueTint = note => Player.ColorProfile.EliteDrums.GetNoteColor(
                    EliteDrumsColorRoles.GetHandRole(note, Player.Profile.KeepOriginalCymbalColors)).ToUnityColor();
            }
            else _fretBoundaryDots.SplitTints = null;
            _fretBoundaryDots.SetRows(_rollLayout, (int)spacing, alwaysShow, colorHeldCenters, colorAllCenters, fretTints);
        }

        private int GetCenterDotFootprint(EliteDrumNote note)
        {
            if (note.Pad == (int)EliteDrumPad.Wildcard || (IsFootPad(note.Pad) && UsesKickBars))
                return (1 << LaneCount) - 1;
            if (!_ordering.TryGetValue(GetReceptorKey(note), out var receptor)) return 0;
            if (note.Pad != (int)EliteDrumPad.HatPedal) return 1 << receptor.Position;

            var (first, end) = EliteDrumsRuntimeGeometry.GetPedalColumnRange(receptor.Position, LaneCount);
            return ((1 << (end - first)) - 1) << first;
        }

        internal bool TryGetArrivalColumn(EliteDrumNote note, out float center, out float width)
        {
            var edges = _rollLayout?.Sample(note.Time);
            if (edges == null || !_ordering.TryGetValue(GetReceptorKey(note), out var column))
            {
                center = width = 0;
                return false;
            }
            int i = column.Position;
            center = ((edges[i] + edges[i + 1]) * .5f - .5f) * HighwayWidth;
            width = (edges[i + 1] - edges[i]) * HighwayWidth;
            return true;
        }

        // Borrowed until the next layout sample. Both kick variants copy the seam
        // positions they need into their meshes during ConfigureKickLanes.
        internal float[] TryGetKickBoundaries(EliteDrumNote note) => _rollLayout?.Sample(note.Time);

        internal bool TryGetRollSlot(EliteDrumNote note, out float center, out float width)
        {
            if (_rollLayout != null && _rollLayout.TryGetSlot(note, out var slot))
            {
                center = slot.Center;
                width = slot.Width;
                return true;
            }

            // A full-size head on a donor fret can cross the seam into a simultaneous
            // hand note next door. Fit it to its arrival fret only for that chord.
            var chord = note.ParentOrSelf;
            if (chord.ChildNotes.Count > 0 && EliteDrumsNoteElement.IsHandFlamPad(note.Pad) &&
                TryGetArrivalColumn(note, out center, out width) &&
                width < RestingWidth(GetVisualPosition(note)) - .00001f)
            {
                int column = GetVisualPosition(note);
                foreach (var other in chord.AllNotes)
                {
                    if (!other.IsInvisibleTerminator && EliteDrumsNoteElement.IsHandFlamPad(other.Pad) &&
                        Math.Abs(GetVisualPosition(other) - column) == 1)
                        return true;
                }
            }
            center = width = 0f;
            return false;
        }

        protected override void OnNoteSpawned(EliteDrumNote parentNote)
        {
            if (_rollLayout == null || !_rollLayout.TryGetStarts(parentNote, out var starts)) return;
            foreach (var strip in starts)
            {
                if (!LanePool.CanSpawnAmount(1)) BeforeNativeLaneAllocation(1);
                var lane = TakeNativeLane();
                if (lane == null) continue;
                var segment = strip.Segment;
                lane.SetTimeRange(segment.StartTime, segment.EndTime);
                lane.SetIndexRange(strip.Column, strip.Column);
                lane.SetAppearance(Instrument.FiveLaneDrums, strip.Column, strip.Center,
                    GetLaneColor(Player.ColorProfile.EliteDrums, segment.Members[0]).ToUnityColor(),
                    GetLaneScale(strip.Width));
                lane.ToggleFullWidth(strip.FullWidth);
                lane.SetRollPath(strip.Path);
                lane.EnableFromPool();
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
            lane.SetAppearance(Instrument.FiveLaneDrums, info.Position, RestingCenter(info.Position),
                GetLaneColor(Player.ColorProfile.EliteDrums, note).ToUnityColor(), GetLaneScale(RestingWidth(info.Position)));
        }

        protected override void InitializeBRELane(LaneElement lane, int laneIndex)
        {
            lane.SetIndexRange(laneIndex, laneIndex);
            lane.SetAppearance(Instrument.FiveLaneDrums, laneIndex, RestingCenter(laneIndex),
                GetFretSlotColor(laneIndex).ToUnityColor(), GetLaneScale(RestingWidth(laneIndex)));
        }

        private System.Drawing.Color GetFretSlotColor(int column) =>
            Player.ColorProfile.EliteDrums.GetSlotFretColor(Layout.Lanes[ApplyLefty(column)].SavedColumn);

        // The lane mesh is one unit wide. Fit its outer borders inside the real
        // compacted column width, leaving a small gap between neighboring strips.
        private static float GetLaneScale(float width) => width * LANE_WIDTH_FRACTION;

        private float RestingWidth(int column) => Layout.GetLaneWidth(ApplyLefty(column), HighwayWidth);
        private float RestingCenter(int column) => Layout.GetLaneX(ApplyLefty(column), HighwayWidth, Player.Profile.LeftyFlip);

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
            using var hit = TargetedPerformance.MeasureWithContext(TargetedOperation.EliteDrumHit, unchecked((int) note.Tick));
            if (Engine.IsJudgingMisarticulatedHiHat) InvalidateFullCombo();
            if (Engine.IsJudgingClosedAsOpen) _hiHatSustainVisual?.ResetInheritedWidth();
            using (TargetedPerformance.Measure(TargetedOperation.DrumHitBookkeeping))
                base.OnNoteHit(index, note);
            using (TargetedPerformance.Measure(TargetedOperation.DrumHitGemReturn))
                (NotePool.GetByKey(note) as EliteDrumsNoteElement)?.HitNote();
            if (note.Pad == (int) EliteDrumPad.Wildcard)
            {
                // The following pad callback supplies the physical strike, not the wildcard identity.
                _pendingWildcardHit = true;
                return;
            }
            AnimateHitFeedback(note);
            SetHiHatSustained(Engine.ActiveHiHatSustain != null);
        }

        private void AnimateHitFeedback(EliteDrumNote note)
        {
            using var timing = TargetedPerformance.MeasureWithContext(TargetedOperation.EliteHitFeedback, note.Pad);
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
            if (Engine.IsJudgingMisarticulatedHiHat) InvalidateFullCombo();
            AnimateHitFeedback(note);
        }

        protected override void GameplayDestroy()
        {
            SetHiHatSustained(false);
            if (Engine != null)
            {
                Engine.OnFlamFollowUp -= OnFlamFollowUp;
                Engine.OnHiHatSustainEnded -= OnHiHatSustainEnded;
                Engine.OnHiHatSustainStarted -= OnHiHatSustainStarted;
            }
            base.GameplayDestroy();
        }

        protected override void OnNoteMissed(int index, EliteDrumNote note)
        {
            _hiHatSustainVisual?.ResetInheritedWidth();
            ShowNoteMiss(note, preserveCombo: EngineParams.AlternateGhostScoring && note.IsGhost);
            (NotePool.GetByKey(note) as EliteDrumsNoteElement)?.MissNote();
        }

        private void OnEliteOverhit()
        {
            _hiHatSustainVisual?.ResetInheritedWidth();
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
            {
                CurrentCoda?.HitLane(Engine.CurrentTime, (int) action);
                AnimateBreHit(action, GameManager.VisualTime);
                _pendingOverhitAction = null;
                return;
            }

            if (_pendingWildcardHit)
            {
                _pendingWildcardHit = false;
                AnimateUnmatchedAction(action);
            }
            if (Engine.IsHiHatSustainRelease)
            {
                _hiHatSustainVisual?.ResetInheritedWidth();
                AnimateUnmatchedAction(action);
                _pendingOverhitAction = null;
                return;
            }
            _pendingOverhitAction = noteWasHit ? null : action;
            if (noteWasHit || wasOverhitInLane || _pendingOverhitAction is null) return;

            // Ordinary pedal motion is harmless; the experimental paired open-hat
            // fault explicitly follows the overhit path. Boundary/countdown/coda
            // exemptions still receive hit feedback.
            if (!Engine.IsHiHatSustainOverhit && (((action is EliteDrumsAction.EliteStomp or EliteDrumsAction.EliteSplash) &&
                 !Engine.IsHiHatClosurePedalOverhit) ||
                Engine.NoteIndex == 0 || Engine.NoteIndex >= Notes.Count ||
                Engine.IsWaitCountdownActive || Engine.IsCodaActive))
            {
                AnimateUnmatchedAction(action);
                _pendingOverhitAction = null;
            }
        }

        private void AnimateBreHit(EliteDrumsAction action, double visualTime)
        {
            int pad = GetInputPad(action);
            if (IsFootPad(pad) && UsesKickBars)
            {
                AnimateUnmatchedAction(action);
                return;
            }

            LightBreReceptor(pad, visualTime);
            if (IsFootPad(pad))
            {
                if (Layout.KickMode == EliteDrumsLayoutKickMode.Split)
                    LightBreReceptor(SECOND_KICK_RECEPTOR, visualTime);
                CameraPositioner.Bounce();
            }
        }

        private void LightBreReceptor(int receptor, double visualTime)
        {
            if (!_ordering.TryGetValue(receptor, out var info)) return;
            _breLaneLastHit[info.Position] = visualTime;
            _fretArray.PlayCodaHitAnimation(receptor);
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
            Array.Fill(_breLaneLastHit, double.NegativeInfinity);
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
            _doubleKickVisual?.Hide();
            Array.Fill(_breLaneLastHit, double.NegativeInfinity);
            _pendingOverhitAction = null;
            _pendingWildcardHit = false;
            _fretBoundaryDots?.Clear();
            // Replay seeking rebuilds hit history before resetting the visuals.
            _hiHatSustainVisual?.Clear(resetActivationTimes: false);
            SetHiHatSustained(false);
            _fretArray.ResetAll();
        }

        public override void SetReplayTime(double time)
        {
            // Clear old activation timestamps before the base replays input events.
            _hiHatSustainVisual?.Clear();
            base.SetReplayTime(time);
        }

        public override void ResetPracticeSection()
        {
            base.ResetPracticeSection();
            _hiHatSustainVisual?.Clear();
            SetHiHatSustained(false);
            _fretArray.ResetAll();
        }

        protected override bool InterceptInput(ref GameInput input)
        {
            // Elite bindings retain the legacy actions for generated targets, but only the
            // selected chart's action family may reach its typed engine or replay recording.
            var action = input.GetAction<EliteDrumsAction>();
            if (action == EliteDrumsAction.EliteHiHatPosition)
                return !EngineParams.HiHatClosureSuppression || input.Integer != 1;
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
