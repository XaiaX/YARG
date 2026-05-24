using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Input;
using YARG.Core.Logging;
using YARG.Core.Replays;
using YARG.Gameplay.HUD;
using YARG.Helpers;
using YARG.Input;
using YARG.Player;
using YARG.Settings;


namespace YARG.Gameplay.Player
{
    public class VocalsPlayer : BasePlayer
    {
        public VocalsEngineParameters EngineParams { get; private set; }
        public VocalsEngine           Engine       { get; private set; }

        public override BaseEngine BaseEngine => Engine;

        [SerializeField]
        private GameObject _needleVisualContainer;
        [SerializeField]
        private MeshRenderer _needleRenderer;
        [SerializeField]
        private Transform _needleTransform;
        [SerializeField]
        private ParticleGroup _hittingParticleGroup;

        public override bool ShouldUpdateInputsOnResume => false;

        protected override float[] StarMultiplierThresholds { get; set; } =
        {
            0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f
        };

        private InstrumentDifficulty<VocalNote> NoteTrack { get; set; }
        private InstrumentDifficulty<VocalNote> OriginalNoteTrack { get; set; }

        private MicInputContext _inputContext;
        private List<MicInputContext> _inputContexts;

        // Total simulated "vocalists" for Party Vocals. For humans: matches the bound mic
        // count. For Party Vocals bots: matches the song's HARM part count (one bot
        // vocalist per HARM line). Used to size needles and the engine's per-mic buffers
        // before _inputContexts is populated.
        private int _partyVocalsMicCount = 1;

        // Multi-mic needles for Party Vocals
        private readonly List<(MeshRenderer renderer, Transform transform, Material material)> _micNeedles = new();

        // Per-mic particle groups for Party Vocals (one trail per needle)
        private readonly List<ParticleGroup> _micParticleGroups = new();

        // Per-mic last pitch cache — keeps each needle at its prior position when not
        // actively hitting, instead of all converging on the shared anchor pitch.
        private readonly List<float?> _micLastPitches = new();

        private VocalNote _lastTargetNote;
        private double?   _lastHitTime;
        private double?   _lastSingTime;
        private double    _previousStarPowerPercent;
        private bool      _hotStartChecked;
        private bool      _newHighScoreShown;

        private VocalsPlayerHUD _hud;
        private VocalPercussionTrack _percussionTrack;
        private bool _shouldHideNeedle;

        private int _phraseIndex = -1;

        private const int NEEDLES_COUNT = 7;

        // Per-mic pitch recording buffer for Party Vocals replays
        private List<float>[] _micPitchBuffers;

        // Replay playback state for Party Vocals
        private ReplayFrame _replayFrame;
        private int _replayMicIndex;

        // Mic disconnect detection
        private float _lastDisconnectCheckTime;
        private const float DISCONNECT_CHECK_INTERVAL = 1.0f; // Check every second

        private SongChart _chart;

        // Free vocals: needle material instance (mutable copy of Addressable)
        private Material _needleMaterialInstance;

        public void Initialize(int index, int vocalIndex, YargPlayer player, SongChart chart,
            VocalsPlayerHUD hud, VocalPercussionTrack percussionTrack, int? lastHighScore, float trackSpeed)
        {
            if (IsInitialized)
            {
                return;
            }

            base.Initialize(index, player, chart, lastHighScore);

            // Save the chart
            _chart = chart;

            // Resolve the vocal track first — we need parts.Count to compute the bot's
            // simulated mic count (one bot "vocalist" per HARM part).
            // For Free Vocals on songs that have a Harmony chart, source from Harmony so the
            // bot's pitch values are in the same register as the visualized HARM lines
            // (the global VocalTrack is initialized with Chart.Harmony in this case — see
            // GameManager.Loading.cs).
            // Free Vocals: prefer Harmony chart when present, fall back to Solo Vocals so
            // solo-only songs (e.g. older charts without HARM parts) still play — they
            // degenerate to single-HARM rendering. Mirrors GameManager.Loading's chart
            // pick so visualization and engine agree. Don't trust CurrentInstrument here
            // because it can be stale from a previous song's selection.
            VocalsTrack multiTrack;
            if (Player.Profile.IsFreeVocals)
            {
                // Harmony track may have placeholder parts with no phrases (e.g. older
                // solo-only songs like Creep load 3 empty HARM parts). Check for actual
                // content, not just Parts.Count, so we fall back to Solo Vocals.
                bool harmonyHasContent = chart.Harmony.Parts.Any(p => p.NotePhrases.Count > 0);
                multiTrack = harmonyHasContent ? chart.Harmony : chart.Vocals;
            }
            else
            {
                multiTrack = chart.GetVocalsTrack(Player.Profile.CurrentInstrument);
            }

            // Compute Party Vocals mic count up front so needle creation and engine
            // construction agree. Humans: real bound-mic count. Free Vocals bots: one
            // synthetic vocalist per HARM part (so a 3-HARM song gets 3 bot needles).
            if (Player.Profile.IsFreeVocals && player.Profile.IsBot)
            {
                // 0 = Auto (one bot mic per HARM part). 1-7 = explicit override for
                // testing mismatched mic-to-part ratios.
                int botMicOverride = Player.Profile.PartyVocalsMicCountOverride;
                _partyVocalsMicCount = botMicOverride > 0
                    ? Mathf.Clamp(botMicOverride, 1, 7)
                    : Mathf.Max(1, multiTrack.Parts.Count);
            }
            else if (player.Bindings.Microphones.Count > 0)
            {
                _partyVocalsMicCount = player.Bindings.Microphones.Count;
            }
            else
            {
                _partyVocalsMicCount = 1;
            }
            bool isPartyVocals = _partyVocalsMicCount > 1;

            // Display index for HUD ShowPlayerName: party-vocals uses the lowest mic-color
            // index (the leader needle); single-mic uses the player slot's needle index.
            int needleIndex = (vocalIndex % NEEDLES_COUNT) + 1;
            if (isPartyVocals)
            {
                // Hide the default single needle — we'll create per-mic needles instead.
                _needleVisualContainer.SetActive(false);

                // Create per-mic needles
                for (int i = 0; i < _partyVocalsMicCount; i++)
                {
                    var micNeedleIndex = (i % NEEDLES_COUNT) + 1;
                    var materialPath = $"VocalNeedle/{micNeedleIndex}";
                    var baseMaterial = Addressables.LoadAssetAsync<Material>(materialPath).WaitForCompletion();
                    var materialInstance = new Material(baseMaterial);

                    // Clone the visual container so the cloned subtree includes the renderer
                    // (which lives on a child object, not on _needleTransform itself).
                    var needleObj = Instantiate(_needleVisualContainer, _needleVisualContainer.transform.parent);
                    needleObj.SetActive(true);
                    var renderer = needleObj.GetComponentInChildren<MeshRenderer>();
                    renderer.material = materialInstance;

                    _micNeedles.Add((renderer, needleObj.transform, materialInstance));
                    _micLastPitches.Add(null);
                }
            }
            else
            {
                // Existing single-needle path
                var materialPath = $"VocalNeedle/{needleIndex}";
                var baseMaterial = Addressables.LoadAssetAsync<Material>(materialPath).WaitForCompletion();
                _needleMaterialInstance = new Material(baseMaterial);
                _needleRenderer.material = _needleMaterialInstance;
            }

            VocalsPart selectedPart;

            // For Free profiles, use part 0 as the base chart to satisfy VocalsEngine's contract.
            // On multi-HARM tracks, all parts will be rendered via BuildCountdownsFromAllParts.
            // On single-part tracks, Free degenerates to Solo rendering (AC4.2).
            if (Player.Profile.IsFreeVocals)
            {
                selectedPart = multiTrack.Parts[0];
            }
            else
            {
                selectedPart = multiTrack.Parts[Player.Profile.HarmonyIndex];
            }

            player.Profile.ApplyVocalModifiers(selectedPart);

            OriginalNoteTrack = selectedPart.CloneAsInstrumentDifficulty();
            NoteTrack = OriginalNoteTrack;

            _phraseIndex = -1;
            _previousStarPowerPercent = 0.0;

            // Update speed of particles
            var particles = _hittingParticleGroup.GetComponentsInChildren<ParticleSystem>();
            foreach (var system in particles)
            {
                // This interface is weird lol, `.main` is readonly but
                // doesn't need to be re-assigned, changes are forwarded automatically
                var main = system.main;

                var startSpeed = main.startSpeed;
                startSpeed.constant *= trackSpeed;
                main.startSpeed = startSpeed;
                // For Free vocals, use HARM1 color by default
                int colorIndex = Player.Profile.IsFreeVocals ? 0 : Player.Profile.HarmonyIndex;
                main.startColor = VocalTrack.Colors[colorIndex];
            }

            if (isPartyVocals)
            {
                // Clone the particle group per mic so each needle has its own trail.
                // Original is hidden — only the clones render.
                _hittingParticleGroup.gameObject.SetActive(false);
                for (int i = 0; i < _partyVocalsMicCount; i++)
                {
                    var pgObj = Instantiate(_hittingParticleGroup.gameObject,
                        _hittingParticleGroup.transform.parent);
                    pgObj.SetActive(true);
                    var pg = pgObj.GetComponent<ParticleGroup>();
                    pg.Colorize(VocalTrack.Colors[i % VocalTrack.Colors.Length]);
                    _micParticleGroups.Add(pg);
                }
            }

            // Initialize player specific vocal visuals

            hud.Initialize(player.EnginePreset);
            _hud = hud;

            // Free Vocals ignores percussion events — with up to 7 mics on a phrase,
            // synchronized percussion taps would be a UX cacophony. Pass an empty list
            // so nothing spawns and the fret never shows.
            percussionTrack.Initialize(Player.Profile.IsFreeVocals
                ? new List<VocalNote>()
                : NoteTrack.Notes);
            _percussionTrack = percussionTrack;

            _hud.ShowPlayerName(player, needleIndex);

            // Create and start input contexts for microphones. Skip entirely for bots —
            // bots have no real audio devices; the engine synthesizes their pitches
            // internally via UpdateBot / UpdateBotMultiMic.
            if (!Player.IsReplay && !player.Profile.IsBot && player.Bindings.Microphones.Count > 0)
            {
                int micCount = player.Bindings.Microphones.Count;

                _inputContexts = new List<MicInputContext>(micCount);
                for (int i = 0; i < micCount; i++)
                {
                    var mic = player.Bindings.Microphones[i];
                    var ctx = new MicInputContext(mic, GameManager);
                    ctx.Start();
                    _inputContexts.Add(ctx);
                }
                // Preserve _inputContext as the first-element accessor for legacy single-mic code paths.
                _inputContext = _inputContexts[0];

                // Initialize per-mic pitch recording buffer for Party Vocals replays
                if (_inputContexts.Count > 1)
                {
                    _micPitchBuffers = new List<float>[_inputContexts.Count];
                    for (int i = 0; i < _inputContexts.Count; i++)
                        _micPitchBuffers[i] = new List<float>();
                }
            }

            // Store replay frame for Party Vocals playback
            if (Player.IsReplay && GameManager.ReplayData != null)
            {
                _replayFrame = GameManager.ReplayData.Frames[player.ReplayIndex];
            }

            Engine = CreateEngine();

            Engine.OnComboIncrement += OnComboIncrement;
            Engine.OnComboReset += OnComboReset;

            if (vocalIndex == 0)
            {
                if (Player.Profile.IsFreeVocals)
                {
                    // Free Vocals ignores percussion entirely — exclude it from the
                    // countdown source so percussion-only stretches surface as gaps.
                    Engine.BuildCountdownsFromAllParts(multiTrack.Parts, excludePercussion: true);
                }
                else if (Player.Profile.CurrentInstrument == Instrument.Vocals)
                {
                    Engine.BuildCountdownsFromSelectedPart();
                }
                else if (Player.Profile.CurrentInstrument == Instrument.Harmony)
                {
                    Engine.BuildCountdownsFromAllParts(multiTrack.Parts);
                }

                Engine.OnCountdownChange += (countdownLength, endTime) =>
                {
                    GameManager.VocalTrack.UpdateCountdown(countdownLength, endTime);
                };
            }

            if (GameManager.IsPractice)
            {
                Engine.SetSpeed(GameManager.SongSpeed >= 1 ? GameManager.SongSpeed : 1);
            }
            else
            {
                Engine.SetSpeed(GameManager.SongSpeed);
            }

        }

        protected override void FinishDestruction()
        {
            // Stop all input contexts
            if (_inputContexts != null)
            {
                foreach (var ctx in _inputContexts)
                {
                    ctx.Stop();
                }
            }
            _inputContext?.Stop();

            // Unsubscribe from engine events and clean up material instance
            if (Engine != null)
            {
                Engine.OnTargetNoteChanged -= OnTargetNoteChangedHandler;

                // Unsubscribe from Party Vocals phrase events
                if (Engine is YargFreeVocalsEngine freeEngine)
                {
                    freeEngine.OnPartyVocalsPhrase -= OnPartyVocalsPhrase;
                }
            }

            // Clean up single-needle material
            if (_needleMaterialInstance != null)
            {
                Destroy(_needleMaterialInstance);
            }

            // Clean up mic needles
            foreach (var (_, transform, material) in _micNeedles)
            {
                if (transform.gameObject != null)
                {
                    Destroy(transform.gameObject);
                }
                if (material != null)
                {
                    Destroy(material);
                }
            }
            _micNeedles.Clear();
        }

        private void OnTargetNoteChangedHandler(VocalNote note)
        {
            _lastTargetNote = note;

            // For Free vocals, tint the needle to match the closest-match HARM line
            if (_needleMaterialInstance == null)
            {
                return;
            }

            if (Player.Profile.IsFreeVocals && Engine is YargFreeVocalsEngine freeEngine)
            {
                int targetHarmonyIndex = freeEngine.CurrentTargetHarmonyIndex;
                if (targetHarmonyIndex >= 0 && targetHarmonyIndex < VocalTrack.Colors.Length)
                {
                    _needleMaterialInstance.color = VocalTrack.Colors[targetHarmonyIndex];
                }
            }
        }

        protected VocalsEngine CreateEngine()
        {
            if (!Player.IsReplay)
            {
                var singToActivateStarPower = SettingsManager.Settings.VoiceActivatedVocalStarPower.Value;

                // Create the engine params from the engine preset
                EngineParams = Player.EnginePreset.Vocals.Create(StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
                    Player.Profile.CurrentDifficulty, MicDevice.UPDATES_PER_SECOND, singToActivateStarPower);
            }
            else
            {
                // Otherwise, get from the replay
                EngineParams = (VocalsEngineParameters) Player.EngineParameterOverride;
            }

            // The hit window can just be taken from the params
            HitWindow = EngineParams.HitWindow;

            VocalsEngine engine;
            if (Player.Profile.IsFreeVocals)
            {
                // Must match the chart-selection logic in Initialize above so the engine sees
                // the same parts (and pitch register) as the visualization.
                // Match the chart-selection in Initialize so engine and visuals agree.
                bool harmonyHasContent = _chart.Harmony.Parts.Any(p => p.NotePhrases.Count > 0);
                var multiTrack = harmonyHasContent ? _chart.Harmony : _chart.Vocals;
                engine = new YargFreeVocalsEngine(NoteTrack, multiTrack.Parts, SyncTrack, EngineParams, Player.Profile.IsBot,
                    micCount: _partyVocalsMicCount,
                    botPartIndex: Player.Profile.HarmonyIndex);
                // Register using the free vocals overload
                EngineContainer = GameManager.EngineManager.Register(engine, NoteTrack.Instrument, freeVocals: true, _chart, Player.RockMeterPreset);
            }
            else
            {
                // For Solo/Harmony, use single-part engine
                engine = new YargVocalsEngine(NoteTrack, SyncTrack, EngineParams, Player.Profile.IsBot);
                // Register using the indexed overload
                EngineContainer = GameManager.EngineManager.Register(engine, NoteTrack.Instrument, Player.Profile.HarmonyIndex, _chart, Player.RockMeterPreset);
            }

            // Subscribe to Party Vocals phrase events for any multi-mic profile (human or bot).
            if (engine is YargFreeVocalsEngine freeEngine && _partyVocalsMicCount > 1)
            {
                freeEngine.OnPartyVocalsPhrase += OnPartyVocalsPhrase;
            }

            engine.OnStarPowerPhraseHit += _ => OnStarPowerPhraseHit();
            engine.OnStarPowerStatus += OnStarPowerStatus;

            engine.OnTargetNoteChanged += OnTargetNoteChangedHandler;

            engine.OnPhraseHit += (percent, fullPoints, isLastPhrase) =>
            {
                if (!fullPoints)
                {
                    IsFc = false;
                }

                LastCombo = Combo;

                ShowTextNotifications(isLastPhrase);

                // Order is important here. ShowVocalPhraseResult() will skip showing AWESOME! if other, more important notifications are already showing.
                _hud.ShowPhraseHit(percent, Combo);
            };

            engine.OnNoteHit += (_, note) =>
            {
                // Free Vocals doesn't spawn percussion visuals, so the pool is empty —
                // calling HitPercussionNote would NRE in Pool.Return.
                if (note.IsPercussion && !Player.Profile.IsFreeVocals)
                {
                    _percussionTrack.HitPercussionNote(note);
                }
            };

            engine.OnNoteMissed += (_, _) =>
            {
                if (LastCombo >= 2)
                {
                    GlobalAudioHandler.PlaySoundEffect(SfxSample.NoteMiss);
                }

                LastCombo = Combo;
            };

            engine.OnSing += (singing) =>
            {
                _lastSingTime = singing
                    ? GameManager.InputTime
                    : null;
            };

            engine.OnHit += (hitting) =>
            {
                _lastHitTime = hitting
                    ? GameManager.InputTime
                    : null;
            };

            return engine;
        }

        private void OnPartyVocalsPhrase(PhraseGrade grade, IReadOnlyList<double> canonicalMeters, bool isLastPhrase)
        {
            _hud.ShowPartyVocalsGrade(grade);
        }

        protected override void ResetVisuals()
        {
            _lastTargetNote = null;
        }

        public override void ResetPracticeSection()
        {
            Engine.Reset(true);

            if (NoteTrack.Notes.Count > 0)
            {
                NoteTrack.Notes[0].OverridePreviousNote();
                NoteTrack.Notes[^1].OverrideNextNote();
            }

            _phraseIndex = -1;

            base.ResetPracticeSection();
        }

        public override void Rewind(double visualTime)
        {
            _hittingParticleGroup.Stop();
        }

        public override void PostRewind(double visualTime)
        {
            ResetVisuals();
            UpdateVisuals(visualTime);
        }

        protected override void UpdateInputs(double time)
        {
            base.UpdateInputs(time);

            if (_inputContexts is null)
            {
                // During replay playback for Party Vocals, feed per-mic pitches from the replay frame.
                if (Player.IsReplay && Engine is YargFreeVocalsEngine freeEngine
                    && _replayFrame != null && _replayFrame.MicCount > 0)
                {
                    for (int i = 0; i < _replayFrame.MicCount; i++)
                    {
                        if (_replayFrame.MicPitches != null && i < _replayFrame.MicPitches.Length
                            && _replayMicIndex < _replayFrame.MicPitches[i].Length)
                        {
                            freeEngine.SetMicPitch(i, _replayFrame.MicPitches[i][_replayMicIndex]);
                        }
                    }
                    _replayMicIndex++;
                }
                return;
            }

            bool isPartyVocals = Player.Profile.IsFreeVocals && _inputContexts.Count > 1
                                && Engine is YargFreeVocalsEngine partyVocalsEngine;

            for (int i = 0; i < _inputContexts.Count; i++)
            {
                var ctx = _inputContexts[i];
                foreach (var input in ctx.GetInputsFromMic())
                {
                    if (isPartyVocals && input.GetAction<VocalsAction>() == VocalsAction.Pitch)
                    {
                        ((YargFreeVocalsEngine)Engine).SetMicPitch(i, input.Axis);

                        // Record per-mic pitch for replay
                        if (_micPitchBuffers != null && i < _micPitchBuffers.Length)
                        {
                            _micPitchBuffers[i].Add(input.Axis);
                        }
                    }
                    else
                    {
                        if (isPartyVocals && input.GetAction<VocalsAction>() == VocalsAction.Hit)
                        {
                            continue;
                        }

                        var copy = input;
                        OnGameInput(ref copy);
                    }
                }
            }
        }

        private bool IsInThreshold(double currentTime, double? lastTime)
        {
            if (lastTime is null)
            {
                return false;
            }

            return currentTime - lastTime.Value <= 1f / EngineParams.ApproximateVocalFps + 0.05;
        }

        protected override void UpdateVisuals(double visualTime)
        {
            UpdatePercussionPhrase(visualTime);
            UpdateSingNeedle();

            // Check for mic disconnects (throttled to avoid per-frame cost)
            if (_inputContexts != null && _inputContexts.Count > 1)
            {
                if (Time.time - _lastDisconnectCheckTime >= DISCONNECT_CHECK_INTERVAL)
                {
                    CheckMicDisconnect();
                    _lastDisconnectCheckTime = Time.time;
                }
            }

            // Get combo meter fill
            float fill = 0f;
            if (Engine.PhraseTicksTotal != null && Engine.PhraseTicksTotal.Value != 0)
            {
                fill = (float) (Engine.PhraseTicksHit / Engine.PhraseTicksTotal.Value);
                fill /= (float) EngineParams.PhraseHitPercent;
            }

            // In multiplayer, don't double the score multiplier in the strikeline element
            // Otherwise, it looks like the band multiplier applies on top of the score multiplier
            var engineStats = Engine.EngineStats;
            int displayMultiplier = GameManager.TotalPlayers > 1 && engineStats.IsStarPowerActive
                ? engineStats.ScoreMultiplier / 2
                : engineStats.ScoreMultiplier;

            // Update HUD
            _hud.UpdateInfo(fill, displayMultiplier,
                (float) Engine.GetStarPowerBarAmount(), Engine.EngineStats.IsStarPowerActive);

            // Update per-HARM fill for Party Vocals (humans and bots).
            if (Engine is YargFreeVocalsEngine freeEngine && _partyVocalsMicCount > 1)
            {
                var meters = freeEngine.CanonicalMeters;
                if (meters != null)
                {
                    _hud.UpdateHarmFill(meters, freeEngine.AwesomeThreshold);
                }
            }
            else
            {
                _hud.HideHarmFill();
            }
        }

        private void ShowTextNotifications(bool isLastPhrase)
        {
            if (SettingsManager.Settings.DisableTextNotifications.Value)
            {
                return;
            }

            var isStarPowerActive = Engine.EngineStats.IsStarPowerActive;
            var currentStarPowerPercent = Engine.GetStarPowerBarAmount();
            if (!isStarPowerActive && _previousStarPowerPercent < 0.5 && currentStarPowerPercent >= 0.5)
            {
                _hud.ShowNotification(TextNotificationType.StarPowerReady);

            }
            _previousStarPowerPercent = Engine.GetStarPowerBarAmount();

            var isMaxMultiplier = Engine.EngineStats.ScoreMultiplier == (isStarPowerActive ? 8 : 4);

            if (!_hotStartChecked && isMaxMultiplier && IsFc)
            {
                _hud.ShowNotification(TextNotificationType.HotStart);
                _hotStartChecked = true;
            }

            if (LastHighScore != null && !_newHighScoreShown && Score > LastHighScore)
            {
                _hud.ShowNotification(TextNotificationType.NewHighScore);
                _newHighScoreShown = true;
            }

            if (!isLastPhrase)
            {
                return;
            }
            if (IsFc)
            {
                _hud.ShowNotification(TextNotificationType.FullCombo);
            }
            else if (isMaxMultiplier)
            {
                _hud.ShowNotification(TextNotificationType.StrongFinish);
            }
        }

        private float GetNeedleRotation(float pitchDist)
        {
            const float NEEDLE_ROT_MAX = 12f;

            // Reduce the provided distance by applying a dead zone. This will prevent oversteer if the player's current pitch is well within the "Perfect" window.
            var deadzoneInSemitones = EngineParams.PitchWindowPerfect / 2;
            var adjustedPitchDist = ApplyPitchDeadZone(pitchDist, deadzoneInSemitones);

            // Determine how off that is compared to the hit window
            float distPercent = Mathf.Clamp(adjustedPitchDist / (EngineParams.PitchWindow - deadzoneInSemitones), -1f, 1f);

            // Use that to get the target rotation
            return distPercent * NEEDLE_ROT_MAX;
        }

        private void UpdateSingleNeedle(MeshRenderer renderer, Transform transform, Material material,
            float pitch, float lastNotePitch, bool isHitting, bool isNonPitched,
            float zOffset = 0f, int harmonyColorIndex = -1)
        {
            const float NEEDLE_POS_LERP = 30f;
            const float NEEDLE_POS_SNAP_MULTIPLIER = 10f;
            const float NEEDLE_ROT_LERP = 25f;

            float lerpRate = NEEDLE_POS_LERP;

            // Show needle
            if (transform.gameObject.activeSelf == false)
            {
                transform.gameObject.SetActive(true);

                // Lerp X times faster if we've just started showing the needle
                lerpRate *= NEEDLE_POS_SNAP_MULTIPLIER;
            }

            float targetRotation = 0f;

            if (isHitting && !isNonPitched)
            {
                // Get how off the player is
                (float pitchDist, _) = GetPitchDistanceIgnoringOctave(lastNotePitch, pitch);
                targetRotation = GetNeedleRotation(pitchDist);
            }

            // Transform!
            float z = GameManager.VocalTrack.GetPosForPitch(pitch) - zOffset;
            var lerp = Mathf.Lerp(transform.localPosition.z, z, Time.deltaTime * lerpRate);
            transform.localPosition = new Vector3(0f, 0f, lerp);

            // Set WORLD rotation, matching single-mic's _needleTransform.rotation =. The
            // VocalsVisual root is rotated 90° on Y at runtime, so setting localRotation
            // here would double-compose and point the needle straight down.
            var targetRot = Quaternion.Euler(0f, targetRotation + 90f, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                targetRot, Time.deltaTime * NEEDLE_ROT_LERP);

            // Handle material color for Free Vocals
            if (material != null && harmonyColorIndex >= 0 && harmonyColorIndex < VocalTrack.Colors.Length)
            {
                material.color = VocalTrack.Colors[harmonyColorIndex];
            }
        }

        private float ApplyPitchDeadZone(float pitchDist, float deadZoneInSemitones)
        {
            if (pitchDist >= 0.0f)
            {
                return Mathf.Max(0.0f, pitchDist - deadZoneInSemitones);
            }

            return Mathf.Min(0.0f, pitchDist + deadZoneInSemitones);
        }

        private void UpdateSingNeedle()
        {
            // Get the appropriate sing time
            var singTime = GameManager.InputTime;

            // Multi-mic: single-mic positions the root transform to (0, 0, pitchZ),
            // but multi-mic positions each needle individually. Reset root so needle
            // offsets are relative to the correct origin (the root starts at (-5, 0.2, 0)
            // in the prefab, which would push all cloned needles off-screen).
            if (_micNeedles.Count > 0)
            {
                transform.localPosition = Vector3.zero;
            }

            // Get whether or not the player has sang within the time threshold.
            // We gotta use a threshold here because microphone inputs are passed every X seconds,
            // not in a constant stream.
            if (!IsInThreshold(singTime, _lastSingTime) || _shouldHideNeedle)
            {
                // Hide needles if there's no singing
                if (_micNeedles.Count > 0)
                {
                    foreach (var (_, transform, _) in _micNeedles)
                    {
                        transform.gameObject.SetActive(false);
                    }
                    foreach (var pg in _micParticleGroups)
                    {
                        pg.Stop();
                    }
                }
                else if (_needleVisualContainer.activeSelf)
                {
                    _needleVisualContainer.SetActive(false);
                }
                _hittingParticleGroup.Stop();
            }
            else
            {
                if (_micNeedles.Count > 0)
                {
                    float lastNotePitch = _lastTargetNote?.PitchAtSongTime(GameManager.SongTime) ?? -1f;

                    // Multi-mic update path. Iterate by needle count alone — bot Party Vocals
                    // has no _inputContexts (engine synthesizes pitches), but still has needles.
                    for (int i = 0; i < _micNeedles.Count; i++)
                    {
                        var (renderer, transform, material) = _micNeedles[i];
                        float micPitch;
                        bool isHitting = false;

                        bool hasMicNote = Engine is YargFreeVocalsEngine freeEngine
                            && _lastTargetNote is not null
                            && IsInThreshold(singTime, _lastHitTime)
                            && freeEngine.IsMicOnNote(i);

                        if (hasMicNote)
                        {
                            micPitch = ((YargFreeVocalsEngine) Engine).GetMicPitch(i);
                            isHitting = true;
                            _micLastPitches[i] = micPitch;
                        }
                        else
                        {
                            // Hide this needle (and its trail) when its mic isn't on a note.
                            // Position/cache preserved so the needle reappears in a sensible
                            // place when its next note starts.
                            if (transform.gameObject.activeSelf)
                            {
                                transform.gameObject.SetActive(false);
                            }
                            if (i < _micParticleGroups.Count)
                            {
                                _micParticleGroups[i].Stop();
                            }
                            continue;
                        }

                        // DIAG (party-vocals needle wobble): log per-mic pitch inputs each
                        // frame so we can confirm whether wobble comes from lastNotePitch
                        // (HARM1 target's PitchAtSongTime jumping across child-note
                        // boundaries), from _micPitches[i] flicker, or from _lastTargetNote
                        // flipping HARMs. Remove once the root cause is fixed.
                        (float diagPitchDist, _) = GetPitchDistanceIgnoringOctave(lastNotePitch, micPitch);
                        YargLogger.LogFormatTrace(
                            "[needle-diag] t={0:F3} mic={1} lastNotePitch={2:F3} micPitch={3:F3} pitchDist={4:F3} targetNoteTick={5} targetNotePitch={6:F3}",
                            GameManager.SongTime, i, lastNotePitch, micPitch, diagPitchDist,
                            _lastTargetNote?.Tick ?? 0, _lastTargetNote?.Pitch ?? -1f);

                        UpdateSingleNeedle(renderer, transform, material, micPitch, lastNotePitch,
                            isHitting, _lastTargetNote?.IsNonPitched ?? false,
                            zOffset: 0f, harmonyColorIndex: i);

                        // Drive the per-mic particle group: follow the needle's Z, play/stop
                        // independently. Skips index check defensively — list sizes match.
                        if (i < _micParticleGroups.Count)
                        {
                            var pg = _micParticleGroups[i];
                            var pgPos = pg.transform.localPosition;
                            pg.transform.localPosition = new Vector3(pgPos.x, pgPos.y, transform.localPosition.z);
                            if (!GameManager.Rewinding)
                            {
                                pg.Play();
                            }
                        }
                    }
                }
                else
                {
                    // Single-mic update path (existing logic)
                    float lerpRate = 30f;
                    const float NEEDLE_POS_SNAP_MULTIPLIER = 10f;

                    // Show needle
                    if (!_needleVisualContainer.activeSelf)
                    {
                        _needleVisualContainer.SetActive(true);

                        // Lerp X times faster if we've just started showing the needle
                        lerpRate *= NEEDLE_POS_SNAP_MULTIPLIER;
                    }

                    var transformCache = transform;
                    float lastNotePitch = _lastTargetNote?.PitchAtSongTime(GameManager.SongTime) ?? -1f;

                    if (_lastTargetNote is not null && IsInThreshold(singTime, _lastHitTime))
                    {
                        // Show particles if hitting (as long as we aren't rewinding)
                        if (!GameManager.Rewinding)
                        {
                            _hittingParticleGroup.Play();
                        }

                        float pitch;
                        float targetRotation = 0f;

                        if (!_lastTargetNote.IsNonPitched)
                        {
                            // If the player is hitting, just set the needle position to the note
                            pitch = lastNotePitch;

                            // Rotate the needle a little bit depending on how off it is (unless it's non-pitched)
                            // Get how off the player is
                            (float pitchDist, _) = GetPitchDistanceIgnoringOctave(lastNotePitch, Engine.PitchSang);
                            targetRotation = GetNeedleRotation(pitchDist);
                        }
                        else
                        {
                            // If the note is non-pitched, just use the singing position
                            pitch = Engine.PitchSang + 12f;
                        }

                        // Transform!
                        float z = GameManager.VocalTrack.GetPosForPitch(pitch);
                        var lerp = Mathf.Lerp(transformCache.localPosition.z, z, Time.deltaTime * lerpRate);
                        transformCache.localPosition = new Vector3(0f, 0f, lerp);
                        _needleTransform.rotation = Quaternion.Lerp(_needleTransform.rotation,
                            Quaternion.Euler(0f, targetRotation + 90f, 0f), Time.deltaTime * 25f);
                    }
                    else
                    {
                        // Stop particles if not hitting
                        _hittingParticleGroup.Stop();

                        // Get the pitch anchored to the correct octave for smooth needle tracking
                        float pitch = AnchorPitchToOctave(Engine.PitchSang, lastNotePitch);

                        // Set the position of the needle
                        var z = GameManager.VocalTrack.GetPosForPitch(pitch);
                        var lerp = Mathf.Lerp(transformCache.localPosition.z, z, Time.deltaTime * lerpRate);
                        transformCache.localPosition = new Vector3(0f, 0f, lerp);

                        // Lerp the rotation to none
                        _needleTransform.rotation = Quaternion.Lerp(_needleTransform.rotation,
                            Quaternion.Euler(0f, 90f, 0f), Time.deltaTime * 25f);
                    }
                }
            }
        }

        private void UpdatePercussionPhrase(double time)
        {
            // Prevent the HUD from hiding too quickly
            if (time < 0)
            {
                return;
            }

            // Check if this is a Party Vocals profile (humans or bots).
            bool isPartyVocals = Player.Profile.IsFreeVocals && _partyVocalsMicCount > 1
                                && Engine is YargFreeVocalsEngine;

            // For Party Vocals, don't hide HUD/needle during percussion phrases since percussion is ignored
            if (isPartyVocals)
            {
                _hud.SetHUDShowing(true);
                _shouldHideNeedle = false;
                return;
            }

            // Since phrases start at the note, and not sometime before it, use
            // the end times of phrases instead (where the phrase lines are). Problem
            // with this is that we still gotta account for the first phrase, so use
            // an index of -1 for that.
            while (_phraseIndex == -1 ||
                (_phraseIndex < NoteTrack.Notes.Count && NoteTrack.Notes[_phraseIndex].TimeEnd <= time))
            {
                _phraseIndex++;

                // End if that's the last note
                if (_phraseIndex >= NoteTrack.Notes.Count)
                {
                    break;
                }

                var phrase = NoteTrack.Notes[_phraseIndex];

                bool hasPercussion = false;
                uint totalTime = 0;
                foreach (var note in phrase.ChildNotes)
                {
                    if (note.IsPercussion)
                    {
                        hasPercussion = true;
                        continue;
                    }

                    totalTime += note.TotalTickLength;
                }

                // Free Vocals ignores percussion entirely — don't show the fret, don't
                // hide the HUD, don't hide the needle for what would otherwise be a
                // percussion phrase.
                if (Player.Profile.IsFreeVocals)
                {
                    _hud.SetHUDShowing(true);
                    _percussionTrack.ShowPercussionFret(false);
                    _shouldHideNeedle = false;
                }
                else
                {
                    _hud.SetHUDShowing(!hasPercussion);
                    _percussionTrack.ShowPercussionFret(hasPercussion);
                    _shouldHideNeedle = hasPercussion;
                }
            }
        }

        public override void SetPracticeSection(uint start, uint end)
        {
            var practiceNotes = OriginalNoteTrack.Notes.Where(n => n.Tick >= start && n.Tick < end).ToList();

            NoteTrack = new InstrumentDifficulty<VocalNote>(
                OriginalNoteTrack.Instrument,
                OriginalNoteTrack.Difficulty,
                practiceNotes,
                OriginalNoteTrack.Phrases,
                OriginalNoteTrack.TextEvents);

            _phraseIndex = -1;

            Engine = CreateEngine();
            ResetPracticeSection();
        }

        public override void SetStemMuteState(bool muted)
        {
            // Vocals has no stem muting
        }

        private void CheckMicDisconnect()
        {
            if (_inputContexts == null || _inputContexts.Count <= 1) return;

            for (int i = _inputContexts.Count - 1; i >= 0; i--)
            {
                var ctx = _inputContexts[i];
                if (ctx.Device == null || !IsDeviceConnected(ctx.Device))
                {
                    // Mic disconnected.
                    ctx.Stop();
                    _inputContexts.RemoveAt(i);

                    // Clear the mic's pitch in the engine so it stops accumulating hits
                    if (Engine is YargFreeVocalsEngine freeEngine)
                    {
                        freeEngine.SetMicPitch(i, -1f);
                    }

                    // Remove the corresponding needle.
                    if (i < _micNeedles.Count)
                    {
                        var (_, transform, material) = _micNeedles[i];
                        if (transform.gameObject != null)
                        {
                            Destroy(transform.gameObject);
                        }
                        if (material != null)
                        {
                            Destroy(material);
                        }
                        _micNeedles.RemoveAt(i);
                    }
                }
            }

            // All mics gone?
            if (_inputContexts.Count == 0)
            {
                YargLogger.LogWarning("All microphones disconnected during gameplay");
            }
        }

        private bool IsDeviceConnected(MicDevice device)
        {
            if (device is YARG.Audio.BASS.BassMicDevice bassMicDevice)
            {
                return bassMicDevice.IsDeviceStillValid();
            }

            return true;
        }

        protected override bool InterceptInput(ref GameInput input)
        {
            return false;
        }

        /// <returns>
        /// The first value in the pair (<c>Distance</c>) is the distance between <paramref name="target"/> and '
        /// <paramref name="other"/> ignoring the octave.<br/>
        /// The second value in the pair (<c>OctaveShift</c>) is how much the <paramref name="target"/> octave
        /// had to be shifted in order for the closest distance to be found.
        /// </returns>
        /// <param name="target">The target note (as MIDI pitch).</param>
        /// <param name="other">The other note (as MIDI pitch).</param>
        /// <summary>
        /// Anchors the sung pitch to the correct octave relative to a reference note.
        /// Prefer the octave closest to the reference note for smoother needle tracking.
        /// Rejected alternative: midpoint of all parts (less stable during quick pitch changes).
        /// </summary>
        private float AnchorPitchToOctave(float sungPitch, float referenceNotePitch)
        {
            if (_lastTargetNote is null || _lastTargetNote.IsNonPitched)
            {
                // No reference note: add one octave to keep needle in middle of track
                return sungPitch + 12f;
            }

            // Find the octave shift that makes sungPitch closest to the reference
            (_, int octaveShift) = GetPitchDistanceIgnoringOctave(referenceNotePitch, sungPitch);

            // Apply the octave shift
            int referenceOctave = (int) (referenceNotePitch / 12f);
            float normalized = sungPitch % 12f;
            return normalized + 12f * (referenceOctave + octaveShift);
        }

        private static (float Distance, int OctaveShift) GetPitchDistanceIgnoringOctave(float target, float other)
        {
            // Normalize the parameters
            target %= 12f;
            other %= 12f;

            // Start off with the current octave
            float closest = other - target;
            int octaveShift = 0;

            // Upper octave
            float upperDist = (other + 12f) - target;
            if (Mathf.Abs(upperDist) < Mathf.Abs(closest))
            {
                closest = upperDist;
                octaveShift = 1;
            }

            // Lower octave
            float lowerDist = (other - 12f) - target;
            if (Mathf.Abs(lowerDist) < Mathf.Abs(closest))
            {
                closest = lowerDist;
                octaveShift = -1;
            }

            return (closest, octaveShift);
        }

        public override (ReplayFrame Frame, ReplayStats Stats) ConstructReplayData()
        {
            var frame = new ReplayFrame(Player.Profile, EngineParams, Engine.EngineStats, ReplayInputs.ToArray());

            if (_micPitchBuffers != null)
            {
                frame.MicCount = _micPitchBuffers.Length;
                frame.MicPitches = new float[_micPitchBuffers.Length][];
                for (int i = 0; i < _micPitchBuffers.Length; i++)
                {
                    frame.MicPitches[i] = _micPitchBuffers[i].ToArray();
                }
            }

            return (frame, Engine.EngineStats.ConstructReplayStats(Player.Profile.Name, Player.IsReplay));
        }
    }
}
