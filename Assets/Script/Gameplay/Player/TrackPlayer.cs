using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
using YARG.Assets.Script.Helpers;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Logging;
using YARG.Gameplay.HUD;
using YARG.Gameplay.Visuals;
using YARG.Helpers;
using YARG.Playback;
using YARG.Player;
using YARG.Settings;
using YARG.Themes;

namespace YARG.Gameplay.Player
{
    public abstract class TrackPlayer : BasePlayer
    {
        public const float STRIKE_LINE_POS       = -2f;
        public const float DEFAULT_ZERO_FADE_POS = 3f;
        public const float NOTE_SPAWN_OFFSET     = 5f;

        public const float TRACK_WIDTH  = 2f;

        public static int HighwayCount = 1;

        public double SpawnTimeOffset => (ZeroFadePosition + _spawnAheadDelay + -STRIKE_LINE_POS) / NoteSpeed;

        protected TrackView TrackView { get; private set; }

        [field: Header("Visuals")]
        [field: SerializeField]
        public Camera TrackCamera { get; private set; }

        [SerializeField]
        protected CameraPositioner CameraPositioner;
        [SerializeField]
        protected HighwayCameraRendering HighwayCameraRendering;
        [SerializeField]
        protected TrackMaterial TrackMaterial;
        [SerializeField]
        protected StrikelineAnimator StrikelineAnimator;
        [SerializeField]
        protected ComboMeter ComboMeter;
        [SerializeField]
        protected StarpowerBar StarpowerBar;
        [SerializeField]
        protected SunburstEffects SunburstEffects;
        [SerializeField]
        protected IndicatorStripes IndicatorStripes;
        [SerializeField]
        protected HitWindowDisplay HitWindowDisplay;

        [SerializeField]
        private Transform _hudLocation;

        [Header("Pools")]
        [SerializeField]
        protected KeyedPool NotePool;
        [SerializeField]
        protected Pool LanePool;
        [SerializeField]
        protected Pool BeatlinePool;
        [SerializeField]
        protected Pool EffectPool;

        public float ZeroFadePosition { get; private set; }
        public float FadeSize         { get; private set; }

        [field: Header("Star Power Trim Effect")]
        [SerializeField]
        protected StarPowerEffectElement StarPowerEffect;

        protected List<Beatline> Beatlines;

        protected int BeatlineIndex;

        protected bool IsBass { get; private set; }

        public int LaneCount { get; protected set; }

        private float _spawnAheadDelay;

        protected float SongLength;

        protected LaneElement[] BRELanes;

        /// <summary>
        /// Clears every BRE lane slot reference. Used when a StartBRE attempt fails to acquire a
        /// complete set of lanes and when ResetVisuals returns all pooled objects, so coda
        /// emission code can never dereference a stale, returned, or never-acquired lane slot.
        /// Lanes referenced at the time of the call are NOT returned here; callers that acquired
        /// lanes in the failed attempt must return them to the pool themselves.
        /// </summary>
        protected void ResetBRELanes()
        {
            if (BRELanes == null)
            {
                return;
            }

            Array.Clear(BRELanes, 0, BRELanes.Length);
        }

        /// <summary>
        /// Returns every lane held by a previous successful StartBRE attempt to the pool and
        /// clears the slots. Called at the start of a new StartBRE attempt so reentry never
        /// abandons (leaks) the previous lanes: after a failed attempt every slot is null and
        /// every previously held lane is accounted for in the pool, whether the new attempt
        /// succeeds or fails.
        /// </summary>
        protected void ReleasePriorBRELanes()
        {
            if (BRELanes == null)
            {
                return;
            }

            for (int i = 0; i < BRELanes.Length; i++)
            {
                if (BRELanes[i] != null)
                {
                    LanePool.Return(BRELanes[i]);
                }
            }

            Array.Clear(BRELanes, 0, BRELanes.Length);
        }

        public virtual void Initialize(int index, YargPlayer player, SongChart chart, TrackView trackView,
            StemMixer mixer, int? lastHighScore)
        {
            if (IsInitialized)
            {
                return;
            }

            Initialize(index, player, chart, lastHighScore);

            TrackView = trackView;

            Beatlines = SyncTrack.Beatlines;
            BeatlineIndex = 0;

            var preset = player.EnginePreset;
            IndicatorStripes.Initialize(preset);

            // Set fade information and highway length
            ZeroFadePosition = DEFAULT_ZERO_FADE_POS * Player.Profile.HighwayLength;
            FadeSize = Player.CameraPreset.FadeLength;

            _spawnAheadDelay = GameManager.IsPractice ? SettingsManager.Settings.PracticeRestartDelay.Value : 2;
            if (player.Profile.HighwayLength > 1)
            {
                FadeSize *= player.Profile.HighwayLength;
            }

            // Move the HUD location based on the highway length
            var change = ZeroFadePosition - DEFAULT_ZERO_FADE_POS;
            _hudLocation.position = _hudLocation.position.AddZ(change);

            // Must be done after the HUD location is set
            StarPowerEffect.Initialize();
            StarPowerEffect.gameObject.SetActive(false);

            // Determine if a track is bass or not for the BASS GROOVE text notification
            IsBass = Player.Profile.CurrentInstrument
                is Instrument.FiveFretBass
                or Instrument.SixFretBass
                or Instrument.ProBass_17Fret
                or Instrument.ProBass_22Fret;

            TrackView.ShowPlayerName(player);
        }

        protected override void ResetVisuals()
        {
            // "Muting a stem" isn't technically a visual,
            // but it's a form of feedback so we'll put it here.
            SetStemMuteState(false);

            ComboMeter.SetFullCombo(IsFc);
            TrackView.ForceReset();
            GameManager.ResetCoda();

            NotePool.ReturnAllObjects();
            LanePool.ReturnAllObjects();
            BeatlinePool.ReturnAllObjects();

            // Every pooled lane was just returned; drop BRE lane references so coda emissions
            // can never touch a lane that has gone back to the pool (or was reused since).
            ResetBRELanes();

            HitWindowDisplay.SetHitWindowSize();
        }
    }

    public abstract class TrackPlayer<TEngine, TNote> : TrackPlayer
        where TEngine : BaseEngine
        where TNote : Note<TNote>
    {
        public TEngine Engine { get; private set; }

        public override BaseEngine BaseEngine => Engine;

        protected List<TNote> Notes { get; set; }

        protected int NoteIndex { get; private set; }

        public InstrumentDifficulty<TNote> NoteTrack { get; private set; }

        protected InstrumentDifficulty<TNote> OriginalNoteTrack { get; private set; }

        private int _currentMultiplier;
        private int _previousMultiplier;

        private bool _isHotStartChecked;
        private bool _previousBassGrooveState;
        private bool _newHighScoreShown;

        /// <summary>
        /// Ensures a foreign-poolable lane pool misconfiguration is only logged once per
        /// player instead of on every failed native lane take.
        /// </summary>
        private bool _loggedForeignLanePoolable;

        private double _previousStarPowerAmount;

        private bool _wasStarPowerActive;
        private bool _didLowerTrack;

        private Queue<TrackEffect> _upcomingEffects = new();
        private List<TrackEffectElement> _currentEffects = new();
        protected List<TrackEffect> _trackEffects = new();

        private List<Phrase> _brePhrases = new();
        private int _breIndex;

        private List<EngineManager.UnisonPhrase> _unisonPhrases = new();
        private int                              _unisonStartIndex;
        private int                              _unisonEndIndex;

        protected SongChart Chart;

        private AutoCalibrator _autoCalibrator;

        protected CodaSection CurrentCoda;

        public override void Initialize(int index, YargPlayer player, SongChart chart, TrackView trackView,
            StemMixer mixer, int? currentHighScore)
        {
            if (IsInitialized)
            {
                return;
            }

            // Get player count
            if (index == 0)
            {
                // Reset
                HighwayCount = 1;
            }
            else if (index + 1 > HighwayCount)
            {
                HighwayCount = index + 1;
            }

            // Consolidate tracks into a parent object for animation purposes
            transform.SetParent(GameObject.Find("Visuals").transform);

            base.Initialize(index, player, chart, trackView, mixer, currentHighScore);

            SetupTheme();

            Chart = chart;

            OriginalNoteTrack = GetNotes(chart);
            player.Profile.ApplyModifiers(OriginalNoteTrack, chart.SyncTrack);

            NoteTrack = OriginalNoteTrack;
            Notes = NoteTrack.Notes;

            var events = NoteTrack.TextEvents;

            Engine = CreateEngine();
            base.ComboMeter.Initialize(player.EnginePreset, Engine.BaseParameters.MaxMultiplier, GameManager.Players.Count > 1);

            Engine.OnComboIncrement += OnComboIncrement;
            Engine.OnComboReset += OnComboReset;
            if (GameManager.IsPractice)
            {
                Engine.SetSpeed(GameManager.SongSpeed >= 1 ? GameManager.SongSpeed : 1);
            }
            else if (Player.IsReplay)
            {
                // If it's a replay, the "SongSpeed" parameter should be set properly
                // when it gets deserialized. Transfer this over to the engine.
                Engine.SetSpeed(Player.EngineParameterOverride.SongSpeed);
            }
            else
            {
                Engine.SetSpeed(GameManager.SongSpeed);
            }

            GameManager.BeatEventHandler.Visual.Subscribe(SunburstEffects.PulseSunburst, BeatEventType.StrongBeat);
            InitializeTrackEffects();
            InitializeCodaEvents();
            InitializeUnisonEvents();

            ResetNoteCounters();

            FinishInitialization();

            SongLength = (float) chart.GetEndTime();

            _autoCalibrator = new AutoCalibrator(GameManager);
        }

        protected override void FinishDestruction()
        {
            GameManager.BeatEventHandler.Visual.Unsubscribe(SunburstEffects.PulseSunburst);

            _autoCalibrator?.Dispose();

            base.FinishDestruction();
        }

        private void InitializeCodaEvents()
        {
            foreach (var phrase in NoteTrack.Phrases)
            {
                if (phrase.Type == PhraseType.BigRockEnding)
                {
                    _brePhrases.Add(phrase);
                }
            }
        }

        private void InitializeUnisonEvents()
        {
            _unisonStartIndex = 0;
            _unisonEndIndex = 0;
            _unisonPhrases = EngineContainer.UnisonPhrases;
        }

        private void InitializeTrackEffects()
        {

            // If the user doesn't want track effects, generate no effects
            if (!SettingsManager.Settings.EnableTrackEffects.Value)
            {
                return;
            }

            var phrases = new List<Phrase>();

            foreach (var phrase in NoteTrack.Phrases)
            {
                // We only want solo and drum fill here. Unisons are added later
                // and there are no track effects for the other phrase types
                if (phrase.Type is PhraseType.Solo or PhraseType.DrumFill)
                {
                    // It turns out that some charts have drum fill phrases that aren't SP activation
                    // (they have no notes), so we need to ignore those
                    if (phrase.Type is PhraseType.DrumFill)
                    {
                        foreach (var note in Notes)
                        {
                            if (note.Time >= phrase.Time && note.Time <= phrase.TimeEnd)
                            {
                                phrases.Add(phrase);
                                break;
                            }
                        }
                    }
                    else
                    {
                        phrases.Add(phrase);
                    }
                }
            }

            phrases.AddRange(EngineContainer.UnisonPhrases);

            var effects = TrackEffect.PhrasesToEffects(Notes, phrases);
            _trackEffects.AddRange(effects);
        }

        private void FinalizeTrackEffects()
        {
            foreach (var effect in TrackEffect.SliceEffects(NoteSpeed, _trackEffects))
            {
                _upcomingEffects.Enqueue(effect);
            }
        }

        private void SetupTheme()
        {
            var (gameMode, instrument) = (Player.Profile.GameMode, Player.Profile.CurrentInstrument);

            var style = VisualStyleHelpers.GetVisualStyle(gameMode, instrument);

            var themePrefab = ThemeManager.Instance.CreateNotePrefabFromTheme(
                Player.ThemePreset, style, NotePool.Prefab);
            NotePool.SetPrefabAndReset(themePrefab);
        }

        protected abstract InstrumentDifficulty<TNote> GetNotes(SongChart chart);
        protected abstract TEngine CreateEngine();

        protected virtual void FinishInitialization()
        {
            TrackMaterial.Initialize(Player.HighwayPreset);
            CameraPositioner.Initialize(Player.CameraPreset);
            FinalizeTrackEffects();

            GameManager.EngineManager.OnPlayerFailed += OnPlayerFailed;
            GameManager.EngineManager.OnPlayerRevived += OnPlayerRevived;
        }

        protected void ResetNoteCounters()
        {
            NoteIndex = 0;
            TotalNotes = Notes.Where(n => !n.IsBigRockEnding).Sum(i => Engine.GetNumberOfNotes(i));
        }

        public override void ResetPracticeSection()
        {
            Engine.Reset(true);

            if (NoteTrack.Notes.Count > 0)
            {
                NoteTrack.Notes[0].OverridePreviousNote();
                NoteTrack.Notes[^1].OverrideNextNote();
            }

            BeatlineIndex = 0;
            ResetNoteCounters();

            ResetTrackEffectOverlay(0);

            CurrentCoda = null;
            _breIndex = 0;
            _unisonStartIndex = 0;
            _unisonEndIndex = 0;

            ResetLastHitTimes();

            base.ResetPracticeSection();
        }

        protected virtual void ResetLastHitTimes()
        {

        }

        public override void Rewind(double visualTime)
        {
            for (int index = NotePool.AllSpawned.Count - 1; index >= 0; index--)
            {
                var poolable = NotePool.AllSpawned[index];
                if (poolable is INoteElement note)
                {
                    note.OnRewind();
                }
            }
        }

        public override void PostRewind(double visualTime)
        {

        }

        protected override void UpdateVisuals(double visualTime)
        {
            // Allow the HUD to track the highway with animations
            TrackView.UpdateHUDPosition(HighwayIndex, HighwayCount);

            UpdateNotes(visualTime);
            UpdateBeatlines(visualTime);
            UpdateTrackEffects(visualTime);
            UpdateCodaEvents(visualTime);
            UpdateUnisonEvents(visualTime);

            var stats = Engine.BaseStats;

            int maxMultiplier = Engine.BaseParameters.MaxMultiplier;
            if (stats.IsStarPowerActive)
            {
                maxMultiplier *= 2;
            }

            double currentStarPowerAmount = Engine.GetStarPowerBarAmount();

            bool groove = stats.ScoreMultiplier == maxMultiplier;

            _currentMultiplier = stats.ScoreMultiplier;

            TrackMaterial.SetTrackScroll(visualTime, NoteSpeed);
            TrackMaterial.GrooveMode = groove;
            TrackMaterial.StarpowerMode = stats.IsStarPowerActive;

            // In multiplayer, don't double the score multiplier in the strikeline element
            // Otherwise, it looks like the band multiplier applies on top of the score multiplier
            int displayMultiplier = GameManager.TotalPlayers > 1 && stats.IsStarPowerActive
                ? stats.ScoreMultiplier / 2
                : stats.ScoreMultiplier;

            ComboMeter.SetCombo(stats.ScoreMultiplier, displayMultiplier, maxMultiplier, stats.Combo, Engine.CodaHasStarted);
            StarpowerBar.SetStarpower(currentStarPowerAmount, stats.IsStarPowerActive, Engine.CodaHasStarted);
            StarpowerBar.UpdateFlash(GameManager.BeatEventHandler.Visual.StrongBeat.CurrentPercentage);
            SunburstEffects.SetSunburstEffects(groove, stats.IsStarPowerActive, _currentMultiplier);

            TrackView.UpdateNoteStreak(stats.Combo);


            // Could be if (!_isHotStartChecked && groove), but that would make it so hot start doesn't show
            // for bass until 6x.
            if (!_isHotStartChecked && stats.ScoreMultiplier == (!stats.IsStarPowerActive ? 4 : 8))
            {
                _isHotStartChecked = true;

                if (IsFc)
                {
                    TrackView.ShowHotStart();
                }
            }

            bool currentBassGrooveState = IsBass && groove;

            if (!_previousBassGrooveState && currentBassGrooveState)
            {
                TrackView.ShowBassGroove();
            }

            _previousBassGrooveState = currentBassGrooveState;

            if (stats.IsStarPowerActive && !_wasStarPowerActive && !_didLowerTrack)
            {
                CameraPositioner.Scoop();
            }

            _previousStarPowerAmount = currentStarPowerAmount;
            _wasStarPowerActive = stats.IsStarPowerActive;

            foreach (var haptics in SantrollerHaptics)
            {
                haptics.SetStarPowerFill((float) currentStarPowerAmount);
            }

            bool isSongEnd = visualTime > SongLength;
            bool shouldLowerTrack = isSongEnd || GameManager.PlayerHasFailed;
            if (!_didLowerTrack && shouldLowerTrack)
            {
                _didLowerTrack = true;
                CameraPositioner.Lower(isSongEnd);
            }
            else if (_didLowerTrack && !shouldLowerTrack)
            {
                _didLowerTrack = false;
                CameraPositioner.Raise(false);
            }
        }

        private void UpdateNotes(double visualTime)
        {
            while (NoteIndex < Notes.Count && Notes[NoteIndex].Time <= visualTime + SpawnTimeOffset)
            {
                var note = Notes[NoteIndex];

                // Skip this frame if the pool is full or note is part of a BRE
                if (!NotePool.CanSpawnAmount(note.ChildNotes.Count + 1))
                {
                    break;
                }

                NoteIndex++;

                // Don't spawn the note if it is under a BRE
                if (note.IsBigRockEnding)
                {
                    continue;
                }

                OnNoteSpawned(note);

                // Don't spawn hit or missed notes
                if (note.WasHit || note.WasMissed)
                {
                    continue;
                }

                // Spawn all of the notes and child notes
                foreach (var child in note.AllNotes)
                {
                    SpawnNote(child);
                }
            }
        }

        private void UpdateBeatlines(double time)
        {
            while (BeatlineIndex < Beatlines.Count && Beatlines[BeatlineIndex].Time <= time + SpawnTimeOffset)
            {
                if (BeatlineIndex + 1 < Beatlines.Count && Beatlines[BeatlineIndex + 1].Time <= time + SpawnTimeOffset)
                {
                    BeatlineIndex++;
                    continue;
                }

                var beatline = Beatlines[BeatlineIndex];

                if (Notes.Count > 0 && beatline.Time > Notes[^1].TimeEnd)
                {
                    return;
                }

                // Skip this frame if the pool is full
                if (!BeatlinePool.CanSpawnAmount(1))
                {
                    break;
                }

                var poolable = BeatlinePool.TakeWithoutEnabling();
                if (poolable == null)
                {
                    YargLogger.LogWarning("Attempted to spawn beatline, but it's at its cap!");
                    break;
                }

                ((BeatlineElement) poolable).BeatlineRef = beatline;
                poolable.EnableFromPool();

                BeatlineIndex++;
            }
        }

        private void UpdateCodaEvents(double time)
        {
            while (_breIndex < _brePhrases.Count && _brePhrases[_breIndex].Time <= time + SpawnTimeOffset)
            {

                var phrase = _brePhrases[_breIndex];
                _breIndex++;

                StartBRE(phrase.Time, phrase.TimeEnd);
            }
        }

        private void UpdateUnisonEvents(double time)
        {
            if (_unisonStartIndex < _unisonPhrases.Count && _unisonPhrases[_unisonStartIndex].Time <= time)
            {
                OnUnisonStart();
                _unisonStartIndex++;
            }

            if (_unisonEndIndex < _unisonPhrases.Count && _unisonPhrases[_unisonEndIndex].TimeEnd <= time)
            {
                OnUnisonEnd();
                _unisonEndIndex++;
            }
        }

        private void UpdateTrackEffects(double time)
        {
            if (_upcomingEffects.TryPeek(out var nextEffect) && nextEffect.Time <= time + SpawnTimeOffset)
            {
                SpawnEffect(nextEffect, false);
            }

            // If any of the current effects are drum fill, we need to react
            // when starpower goes from unavailable to available

            // Remove past effects from current list
            // This may actually fail if an effect is reused from the pool
            // too quickly, but as long as it is only being used for setting
            // drum fill visibility, it shouldn't break.
            for (var i = 0; i < _currentEffects.Count; i++)
            {
                var trackEffectElement = _currentEffects[i];
                if (!trackEffectElement.Active)
                {
                    _currentEffects.RemoveAt(i);
                }
                else
                {
                    // See if it's an invisible drum fill and if starpower has become available
                    // Since we never change visibility on anything but drum fills, there's no need to check
                    // the effect type.
                    if ((trackEffectElement.Visibility < 1.0f && Engine.CanStarPowerActivate) && !Engine.BaseStats.IsStarPowerActive)
                    {
                        trackEffectElement.MakeVisible();
                        // If start transition is disabled, previous should be disabled
                        if (!trackEffectElement.EffectRef.StartTransitionEnable && i > 0)
                        {
                            _currentEffects[i - 1].SetEndTransitionVisible(false);
                        }

                        // If end transition is disabled, next should be disabled if it is spawned
                        if (_currentEffects.Count > i + 1 && !trackEffectElement.EffectRef.EndTransitionEnable)
                        {
                            _currentEffects[i + 1].SetStartTransitionVisible(false);
                        }
                    }
                    // We also need to make already spawned drum fills disappear if the player activated SP
                    // And we do need to check effect type here
                    if (trackEffectElement.EffectRef.EffectType == TrackEffectType.DrumFill &&
                        (trackEffectElement.Visibility == 1.0f && Engine.BaseStats.IsStarPowerActive))
                    {
                        if (trackEffectElement.EffectRef.OriginalEffectType == TrackEffectType.DrumFillAndUnison)
                        {
                            // Turn this into a unison
                            trackEffectElement.EffectRef.EffectType = TrackEffectType.Unison;
                            SwapEffect(trackEffectElement);
                            return;
                        }

                        if (trackEffectElement.EffectRef.OriginalEffectType == TrackEffectType.SoloAndDrumFill)
                        {
                            // Turn this into a solo
                            trackEffectElement.EffectRef.EffectType = TrackEffectType.Solo;
                            SwapEffect(trackEffectElement);
                            return;
                        }

                        trackEffectElement.MakeVisible(false);

                        if (!trackEffectElement.EffectRef.StartTransitionEnable && i > 0)
                        {
                            // Previous maybe needs end transition enabled since we're disappearing
                            // (if the effect type doesn't have an end transition set, it won't
                            //  be active regardless of what we do here, so a hard enable is ok)
                            _currentEffects[i - 1].SetEndTransitionVisible(true);
                        }

                        if (!trackEffectElement.EffectRef.EndTransitionEnable)
                        {
                            // next needs start transition enabled, if it is spawned
                            // if it isn't yet spawned, it should already be set correctly
                            if (_currentEffects.Count > i + 1)
                            {
                                _currentEffects[i + 1].SetStartTransitionVisible(true);
                            }
                        }
                    }
                }
            }
        }

        private static async void SwapEffect(TrackEffectElement trackEffectElement)
        {
            await trackEffectElement.MakeVisibleAsync(false);
            trackEffectElement.Reinitialize();
            // ReSharper disable once MethodHasAsyncOverload
            trackEffectElement.MakeVisible(true);
        }

        private void SpawnEffect(TrackEffect nextEffect, bool seeking)
        {
            var poolable = EffectPool.TakeWithoutEnabling();
            if (poolable == null)
            {
                YargLogger.LogWarning("Attempted to spawn track effect, but it's at its cap!");
                return;
            }

            // The seeking code handles this for us if we're seeking
            if (!seeking)
            {
                _upcomingEffects.Dequeue();
            }

            // Do some magic to vanish drum fills if the player doesn't have enough SP to activate
            // or if SP is already active.

            if (Engine.BaseStats.IsStarPowerActive || !Engine.CanStarPowerActivate)
            {
                if (nextEffect.EffectType is TrackEffectType.DrumFill)
                {
                    nextEffect.Visibility = 0.0f;
                    if (!nextEffect.StartTransitionEnable)
                    {
                        if (_currentEffects.Count > 0)
                        {
                            _currentEffects[^1].SetEndTransitionVisible(true);
                            _currentEffects[^1].SetTransitionState();
                        }
                    }
                    if (!nextEffect.EndTransitionEnable)
                    {
                        // Get next next and turn on its start transition
                        // Since we are only spawning now, it shouldn't be possible
                        // for next next to be spawned yet.
                        if (_upcomingEffects.TryPeek(out var nextNextEffect))
                        {
                            nextNextEffect.StartTransitionEnable = true;
                        }
                    }

                    if (!nextEffect.StartTransitionEnable)
                    {
                        // Turn on end transition for previous effect

                        // Previous effect is by definition already spawned,
                        // but we'll check that _currentEffects isn't length zero
                        if (_currentEffects.Count > 0)
                        {
                            _currentEffects[^1].SetEndTransitionVisible(true);
                        }
                    }
                }

                if (nextEffect.EffectType is TrackEffectType.DrumFillAndUnison)
                {
                    nextEffect.EffectType = TrackEffectType.Unison;
                }

                if (nextEffect.EffectType is TrackEffectType.SoloAndDrumFill)
                {
                    nextEffect.EffectType = TrackEffectType.Solo;
                }
            }

            ((TrackEffectElement) poolable).EffectRef = nextEffect;
            _currentEffects.Add((TrackEffectElement) poolable);
            poolable.EnableFromPool();
        }

        // ReSharper disable once InconsistentNaming
        protected virtual void StartBRE(double timeStart, double timeEnd)
        {
            RescaleLanesForBRE();

            // Reentry (a new BRE phrase while a previous attempt's lanes are still held) must
            // not orphan those lanes: return them to the pool and clear the slots BEFORE the
            // new allocation attempt, so the capacity check and the transactional rollback
            // below always operate on a fully-owned lane set.
            ReleasePriorBRELanes();

            if (!LanePool.CanSpawnAmount(BRELanes.Length))
            {
                BeforeNativeLaneAllocation(BRELanes.Length);
                if (!LanePool.CanSpawnAmount(BRELanes.Length))
                {
                    return;
                }
            }

            // BRE lane acquisition is transactional: lanes are staged locally and only
            // committed to BRELanes once every required lane has been acquired. If any lane
            // is unavailable, every lane taken in this attempt is returned to the pool and
            // BRELanes is reset, so coda emissions can never dereference a stale or
            // unfilled slot. (Lanes held by a previous successful attempt were already
            // released above, so the reset cannot orphan them.) A complete acquisition
            // behaves exactly as before.
            var acquiredLanes = new List<LaneElement>(BRELanes.Length);
            for (int i = 0; i < BRELanes.Length; i++)
            {
                if (!LanePool.CanSpawnAmount(1))
                {
                    BeforeNativeLaneAllocation(1);
                }

                var newLane = TakeNativeLane();

                if (newLane == null)
                {
                    YargLogger.LogWarning("Attempted to spawn BRE lane, but it's at its cap!");

                    // Roll back: give back every lane this attempt acquired. All slots are
                    // then cleared so nothing later reads a half-built or stale BRE lane set.
                    foreach (var acquiredLane in acquiredLanes)
                    {
                        LanePool.Return(acquiredLane);
                    }

                    ResetBRELanes();
                    return;
                }

                newLane.SetTimeRange(timeStart, timeEnd);
                InitializeBRELane(newLane, i);
                newLane.EnableFromPool();

                newLane.SetEmissionColor(0);

                acquiredLanes.Add(newLane);
            }

            for (int i = 0; i < acquiredLanes.Count; i++)
            {
                BRELanes[i] = acquiredLanes[i];
            }
        }

        protected virtual void OnNoteSpawned(TNote parentNote)
        {
            SpawnLanesFromNote(parentNote);
        }

        protected virtual bool ShouldSpawnNativeLane(TNote note)
        {
            return true;
        }

        /// <summary>
        /// Whether an existing spawned lane may be extended by the native adjacency combiner.
        /// Lanes owned by the Elite visual adapter carry descriptor state and are never
        /// native-combined: each descriptor spans exactly its own first..last event.
        /// </summary>
        protected virtual bool ShouldExtendExistingLane(LaneElement lane)
        {
            return true;
        }

        protected virtual void BeforeNativeLaneAllocation(int count)
        {
        }

        protected virtual void AfterNativeLaneAllocation(LaneElement lane)
        {
        }

        /// <summary>
        /// Takes a native lane from the shared lane pool, or returns null when no native lane
        /// is available. The shared pool may also hold non-LaneElement poolables; a foreign
        /// item is parked out of the free stack unchanged (matching the Elite visual
        /// adapter), so it can never circulate back and poison later takes. The allocation
        /// hook only ever observes a validated, non-null lane element.
        /// </summary>
        protected LaneElement TakeNativeLane()
        {
            var poolable = LanePool.TakeWithoutEnabling();
            if (poolable == null)
            {
                return null;
            }

            if (poolable is not LaneElement lane)
            {
                // Foreign IPoolable in the shared lane pool (pool misconfiguration): fail
                // safely. The take already moved it into Pool.AllSpawned, which parks it out
                // of the free stack. Do NOT Return() it: that would run DisableIntoPool on an
                // item we do not own and push it back onto the free stack, where it would be
                // handed out again and poison every later take (native and adapter alike).
                // The parked item is recovered unchanged by ReturnAllObjects on the next full
                // reset. Treat this as pool exhaustion.
                LogForeignLanePoolableOnce(poolable);
                return null;
            }

            AfterNativeLaneAllocation(lane);
            return lane;
        }

        /// <summary>
        /// Warns once per player about a foreign poolable in the shared lane pool; a pool
        /// misconfiguration surfaces on every native take, so repeated logging would spam.
        /// </summary>
        private void LogForeignLanePoolableOnce(IPoolable poolable)
        {
            if (_loggedForeignLanePoolable)
            {
                return;
            }

            _loggedForeignLanePoolable = true;
            YargLogger.LogWarning($"Lane pool handed out a non-LaneElement poolable " +
                $"({poolable.GetType().Name}); it was parked out of the free stack unchanged. " +
                "Check the LanePool prefab configuration.");
        }

        protected virtual void SpawnLanesFromNote(TNote parentNote)
        {
            if (!Engine.BaseParameters.EnableLanes)
            {
                return;
            }

            bool containsLaneStart = false;
            foreach (var childNote in parentNote.AllNotes)
            {
                if (childNote.IsLaneStart && ShouldSpawnNativeLane(childNote))
                {
                    containsLaneStart = true;
                    break;
                }
            }

            if (containsLaneStart)
            {
                if (!LanePool.CanSpawnAmount(1))
                {
                    BeforeNativeLaneAllocation(1);
                    if (!LanePool.CanSpawnAmount(1))
                    {
                        return;
                    }
                }

                var laneStartNotes = new Dictionary<int, TNote>();
                var laneEndTimes = new Dictionary<int, double>();

                // Iterate forward to find the length of all lanes in this phrase
                var noteRef = parentNote;
                var thisLaneFlag = parentNote.IsTrill ? NoteFlags.Trill : NoteFlags.Tremolo;

                while (noteRef != null)
                {
                    // Create one lane for single notes, create multiple lanes for non-drum chords
                    bool containsLaneEnd = false;
                    foreach (var childNote in noteRef.AllNotes)
                    {
                        if (childNote.IsLaneEnd && ShouldSpawnNativeLane(childNote))
                        {
                            containsLaneEnd = true;
                        }

                        if (childNote.IsLane && ShouldSpawnNativeLane(childNote))
                        {
                            if (!laneStartNotes.ContainsKey(childNote.LaneNote))
                            {
                                laneStartNotes[childNote.LaneNote] = childNote;
                            }

                            laneEndTimes[childNote.LaneNote] = noteRef.Time;
                        }
                    }

                    if (containsLaneEnd)
                    {
                        break;
                    }

                    noteRef = noteRef.NextNote;
                }

                foreach (var (laneIndex, note) in laneStartNotes)
                {
                    if (!laneEndTimes.ContainsKey(laneIndex))
                    {
                        // Ending note was not found, do not create lane
                        continue;
                    }

                    var firstLaneNote = laneStartNotes[laneIndex];
                    double startTime = firstLaneNote.Time;
                    double endTime = laneEndTimes[laneIndex];

                    // Extend a previous lane if possible instead of creating two adjoining lanes at the same index
                    bool extendExisting = false;
                    foreach (var poolable in LanePool.AllSpawned)
                    {
                        // The shared pool can hold foreign IPoolables parked out of the free
                        // stack by a failed native take (see TakeNativeLane). Iterate by the
                        // poolable interface and skip them (and nulls) instead of casting:
                        // a direct LaneElement cast throws InvalidCastException on the next
                        // native lane start after a foreign item is parked.
                        if (poolable is not LaneElement existingLane)
                        {
                            continue;
                        }

                        if (!ShouldExtendExistingLane(existingLane))
                        {
                            // Descriptor-owned adapter lanes are invisible to the native combiner:
                            // never matched, never extended, and never allowed to consume the
                            // first-match slot a native lane at the same index relies on.
                            continue;
                        }

                        if (existingLane.ContainsIndex(laneIndex))
                        {
                            if (startTime - existingLane.EndTime <= LaneElement.COMBINE_LANE_THRESHOLD)
                            {
                                // New lane will overlap with existing one
                                // Determine if the previous notes in this chart should prevent combining
                                int notesToSearch = firstLaneNote.IsTrill ? 2 : 1;
                                noteRef = firstLaneNote.PreviousNote;
                                for (int n = 0; n < notesToSearch; n++)
                                {
                                    if (noteRef == null)
                                    {
                                        break;
                                    }

                                    if (existingLane.ContainsIndex(noteRef.LaneNote) && (noteRef.Flags & thisLaneFlag) != 0)
                                    {
                                        extendExisting = true;
                                        break;
                                    }

                                    noteRef = noteRef.PreviousNote;
                                }
                            }

                            if (extendExisting)
                            {
                                existingLane.SetTimeRange(existingLane.ElementTime, Math.Max(endTime, existingLane.EndTime));
                            }

                            break;
                        }
                    }

                    if (extendExisting)
                    {
                        continue;
                    }

                    // Create a new lane element at this index
                    if (!LanePool.CanSpawnAmount(1))
                    {
                        BeforeNativeLaneAllocation(1);
                    }

                    var newLane = TakeNativeLane();
                    if (newLane == null)
                    {
                        continue;
                    }
                    newLane.SetTimeRange(startTime, endTime);
                    InitializeSpawnedLane(newLane, note);
                    ModifyLaneFromNote(newLane, firstLaneNote);

                    newLane.EnableFromPool();
                }
            }
        }

        protected virtual InstrumentDifficulty<TNote> CreatePracticeTrack(uint start, uint end)
        {
            var practiceNotes = OriginalNoteTrack.Notes.Where(n => n.Tick >= start && n.Tick < end).ToList();

            YargLogger.LogFormatDebug("Practice notes: {0}", practiceNotes.Count);

            var practiceTrack = new InstrumentDifficulty<TNote>(OriginalNoteTrack.Instrument, OriginalNoteTrack.Difficulty,
                practiceNotes, OriginalNoteTrack.Phrases, OriginalNoteTrack.TextEvents,
                OriginalNoteTrack.RangeShiftEvents);
            // Practice reuses the original physical notes, including their conversion origins.
            // Preserve the authored phrase records too: without them the Elite V1 engine
            // mistakes overlapping hand lanes for one flattened, auto-hitting native lane.
            practiceTrack.SetEliteDrumAuthoredLanePhraseRecords(OriginalNoteTrack.EliteDrumAuthoredLanePhraseRecords);
            practiceTrack.SetEliteDrumVisualDescriptors(OriginalNoteTrack.EliteDrumVisualDescriptors);
            return practiceTrack;
        }

        public override void SetPracticeSection(uint start, uint end)
        {
            NoteTrack = CreatePracticeTrack(start, end);
            Notes = NoteTrack.Notes;

            ResetNoteCounters();

            BeatlineIndex = 0;

            // Removed by EngineManager
            EngineContainer = null;

            Engine = CreateEngine();

            if (GameManager.IsPractice)
            {
                Engine.SetSpeed(GameManager.SongSpeed >= 1 ? GameManager.SongSpeed : 1);
            }
            else
            {
                Engine.SetSpeed(GameManager.SongSpeed);
            }

            ResetPracticeSection();
        }

        public override void SetReplayTime(double time)
        {
            BeatlineIndex = 0;
            ResetNoteCounters();

            // Reset the track effect overlay
            ResetTrackEffectOverlay(time);

            base.SetReplayTime(time);
        }

        private void ResetTrackEffectOverlay(double time)
        {
            // despawn any existing track effects, rebuild track effect structures, spawn any that are now in current
            _upcomingEffects.Clear();
            for(var i = 0; i < EffectPool.AllSpawned.Count; i++)
            {
                var poolable = EffectPool.AllSpawned[i];
                poolable.ParentPool.Return(poolable);
            }

            foreach (var effect in TrackEffect.SliceEffects(NoteSpeed, _trackEffects))
            {
                if (effect.Time >= time)
                {
                    _upcomingEffects.Enqueue(effect);
                } else if (effect.Time < time && time < effect.TimeEnd)
                {
                    // current effect, spawn it
                    SpawnEffect(effect, true);
                }
            }
        }

        protected void SpawnNote(TNote note)
        {
            var poolable = NotePool.KeyedTakeWithoutEnabling(note);
            if (poolable == null)
            {
                YargLogger.LogWarning("Attempted to spawn note, but it's at its cap!");
                return;
            }

            InitializeSpawnedNote(poolable, note);
            poolable.EnableFromPool();
        }

        protected abstract void InitializeSpawnedNote(IPoolable poolable, TNote note);
        protected abstract void InitializeSpawnedLane(LaneElement lane, TNote note);
        protected abstract void InitializeBRELane(LaneElement lane, int laneIndex);
        protected virtual void ModifyLaneFromNote(LaneElement lane, TNote note) {}

        protected abstract void RescaleLanesForBRE();

        protected virtual void OnNoteHit(int index, TNote note)
        {
            if (!Player.Profile.IsBot)
            {
                _autoCalibrator.RecordAccuracy(Engine.CurrentTime, note.Time);
            }

            if (!GameManager.IsSeekingReplay)
            {
                UpdateMuteState(note, false);
                if (_currentMultiplier != _previousMultiplier)
                {
                    _previousMultiplier = _currentMultiplier;

                    foreach (var haptics in SantrollerHaptics)
                    {
                        haptics.SetMultiplier((byte) Math.Clamp(_currentMultiplier, 1, byte.MaxValue));
                    }
                }

                if (index >= Notes.Count - 1 && note.ParentOrSelf.WasFullyHit())
                {
                    if (IsFc)
                    {
                        TrackView.ShowFullCombo();
                    }
                    else if (Combo >= 30) // 30 to coincide with 4x multiplier (including on bass)
                    {
                        TrackView.ShowStrongFinish();
                    }
                }
            }

            LastCombo = Combo;
        }

        protected virtual void OnNoteMissed(int index, TNote note)
        {
            if (IsFc)
            {
                ComboMeter.SetFullCombo(false);
                IsFc = false;
            }

            if (!GameManager.IsSeekingReplay)
            {
                UpdateMuteState(note, true);

                if (LastCombo >= 10)
                {
                    GlobalAudioHandler.PlaySoundEffect(SfxSample.NoteMiss);
                    CameraPositioner.Punch();
                }

                foreach (var haptics in SantrollerHaptics)
                {
                    haptics.SetMultiplier(0);
                }
            }

            LastCombo = Combo;
        }

        protected virtual void OnOverhit()
        {
            if (IsFc)
            {
                ComboMeter.SetFullCombo(false);
                IsFc = false;
            }

            if (LastCombo >= 10)
            {
                CameraPositioner.Punch();
            }

            LastCombo = Combo;
        }

        protected virtual void UpdateMuteState(TNote note, bool isMuted)
        {
            SetStemMuteState(isMuted);
        }

        protected virtual void OnSoloStart(SoloSection solo)
        {
            TrackView.StartSolo(solo);

            foreach (var haptic in SantrollerHaptics)
            {
                haptic.SetSoloActive(true);
            }
        }

        protected virtual void OnSoloEnd(SoloSection solo)
        {
            TrackView.EndSolo(solo.SoloBonus);

            foreach (var haptic in SantrollerHaptics)
            {
                haptic.SetSoloActive(false);
            }
        }

        protected virtual void OnCodaStart(CodaSection coda)
        {
            CurrentCoda = coda;
            SetStemMuteState(false);
            TrackView.StartCoda();
        }

        protected virtual void OnCodaEnd(CodaSection coda)
        {
            TrackView.EndCoda();
        }

        private void OnUnisonStart()
        {
            TrackView.StartUnison();
        }

        private void OnUnisonEnd()
        {
            TrackView.EndUnison();
        }

        protected virtual void OnCountdownChange(double countdownLength, double endTime)
        {
            TrackView.UpdateCountdown(countdownLength, endTime);
        }

        protected virtual void OnStarPowerPhraseMissed(TNote note)
        {
            OnStarPowerPhraseMissed();
        }

        protected virtual void OnStarPowerPhraseHit(TNote note)
        {
            if (SettingsManager.Settings.EnableTrackEffects.Value)
            {
                StarPowerEffect.gameObject.SetActive(true);
                StarPowerEffect.PlayAnimation();
            }

            OnStarPowerPhraseHit();
        }

        protected override void OnStarPowerReady()
        {
            base.OnStarPowerReady();
            TrackView.ShowStarPowerReady();
        }

        protected void OnHappinessOverFail()
        {
            TrackMaterial.FailState = 0f;
        }

        protected void OnHappinessNearFail()
        {
            if (SettingsManager.Settings.NoFail.Value == NoFailMode.Off && !GameManager.IsPractice)
            {
                TrackMaterial.FailState = 1f;
            }
        }

        protected void OnPlayerFailed(int engineId)
        {
            if (SettingsManager.Settings.NoFail.Value != NoFailMode.Off
                || engineId != EngineContainer.EngineId
                || GameManager.IsPractice)
            {
                // Not for us
                return;
            }

            // Mark as failed and lower highway
            PlayerHasFailed = true;
            CameraPositioner.Lower(false);
        }

        protected void OnPlayerRevived()
        {
            if (!PlayerHasFailed)
            {
                return;
            }

            // Unfail and raise highway
            PlayerHasFailed = false;
            CameraPositioner.Raise(false);
        }

        public override void GameplayUpdate()
        {
            base.GameplayUpdate();

            if (LastHighScore != null && !_newHighScoreShown && Score > LastHighScore)
            {
                _newHighScoreShown = true;
                TrackView.ShowNewHighScore();
            }
        }

        protected override void GameplayDestroy()
        {
            base.GameplayDestroy();

            GameManager.EngineManager.OnPlayerFailed -= OnPlayerFailed;
            GameManager.EngineManager.OnPlayerRevived -= OnPlayerRevived;
        }
    }
}
