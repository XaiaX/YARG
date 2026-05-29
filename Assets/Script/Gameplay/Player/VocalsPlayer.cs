using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
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
        public VocalsEngineParameters EngineParams { get; protected set; }
        public VocalsEngine           Engine       { get; private set; }

        public override BaseEngine BaseEngine => Engine;

        [SerializeField]
        protected GameObject _needleVisualContainer;
        [SerializeField]
        protected MeshRenderer _needleRenderer;
        [SerializeField]
        protected Transform _needleTransform;
        [SerializeField]
        protected ParticleGroup _hittingParticleGroup;

        
        public override bool ShouldUpdateInputsOnResume => false;

        protected override float[] StarMultiplierThresholds { get; set; } =
        {
            0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f
        };

        protected InstrumentDifficulty<VocalNote> NoteTrack { get; set; }
        private InstrumentDifficulty<VocalNote> OriginalNoteTrack { get; set; }

        protected MicInputContext _inputContext;

        protected VocalNote _lastTargetNote;
        protected double?   _lastHitTime;
        protected double?   _lastSingTime;
        private double    _previousStarPowerPercent;
        private bool      _hotStartChecked;
        private bool      _newHighScoreShown;

        protected VocalsPlayerHUD _hud;
        protected VocalPercussionTrack _percussionTrack;
        protected bool _shouldHideNeedle;

        // Stored engine event handlers for clean unsubscription on practice reset
        private BaseEngine<VocalNote>.StarPowerPhraseHitEvent _onStarPowerPhraseHitHandler;
        private VocalsEngine.PhraseHitEvent _onPhraseHitHandler;
        private BaseEngine<VocalNote>.NoteHitEvent _onNoteHitHandler;
        private BaseEngine<VocalNote>.NoteMissedEvent _onNoteMissedHandler;
        private Action<bool> _onSingHandler;
        private Action<bool> _onHitHandler;
        private BaseEngine<VocalNote>.CountdownChangeEvent _onCountdownChangeHandler;

        private int _phraseIndex = -1;

        protected const int NEEDLES_COUNT = 7;

        
        protected SongChart _chart;

        // Free vocals: needle material instance (mutable copy of Addressable)
        private Material _needleMaterialInstance;
        private AsyncOperationHandle<Material> _needleMaterialHandle;

        public virtual void Initialize(int index, int vocalIndex, YargPlayer player, SongChart chart,
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
                multiTrack = VocalChartSelection.ResolveMultitrack(chart, Player.Profile);
            }
            else
            {
                multiTrack = chart.GetVocalsTrack(Player.Profile.CurrentInstrument);
            }

            // Get the effective microphone(s) for this player
            IReadOnlyList<MicDevice> effectiveMics = player.Bindings.Microphones;
            if (player.Profile.GameMode == GameMode.Vocals && effectiveMics.Count > 1)
            {
                effectiveMics = new[] { effectiveMics[0] };
            }

            // Get the needle index for this player
            int needleIndex = (vocalIndex % NEEDLES_COUNT) + 1;

            // Load material for the needle
            // AC27.2: one-shot sync load at Initialize; handle released in FinishDestruction.
            var materialPath = $"VocalNeedle/{needleIndex}";
            _needleMaterialHandle = Addressables.LoadAssetAsync<Material>(materialPath);
            var baseMaterial = _needleMaterialHandle.WaitForCompletion();
            _needleMaterialInstance = new Material(baseMaterial);
            _needleRenderer.material = _needleMaterialInstance;

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
                // Trail identifies the singer, not the lane being scored. For Free
                // vocals, color by the player's needle slot; otherwise by HARM index.
                int colorIndex = Player.Profile.IsFreeVocals
                    ? (needleIndex - 1) % VocalTrack.Colors.Length
                    : Player.Profile.HarmonyIndex;
                main.startColor = VocalTrack.Colors[colorIndex];
            }

            
            // Initialize player specific vocal visuals

            hud.Initialize(player.EnginePreset);
            _hud = hud;

            // Initialize percussion track
            percussionTrack.Initialize(NoteTrack.Notes);
            _percussionTrack = percussionTrack;

            _hud.ShowPlayerName(player, needleIndex);

            // Create and start input context for microphone
            if (!Player.IsReplay && !player.Profile.IsBot && effectiveMics.Count > 0)
            {
                _inputContext = new MicInputContext(effectiveMics[0], GameManager);
                _inputContext.Start();
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

                _onCountdownChangeHandler = (countdownLength, endTime) =>
                {
                    GameManager.VocalTrack.UpdateCountdown(countdownLength, endTime);
                };
                Engine.OnCountdownChange += _onCountdownChangeHandler;
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

        protected virtual void UnsubscribeEngineEvents()
        {
            if (Engine == null) return;

            Engine.OnStarPowerPhraseHit -= _onStarPowerPhraseHitHandler;
            Engine.OnStarPowerStatus -= OnStarPowerStatus;
            Engine.OnTargetNoteChanged -= OnTargetNoteChangedHandler;
            Engine.OnPhraseHit -= _onPhraseHitHandler;
            Engine.OnNoteHit -= _onNoteHitHandler;
            Engine.OnNoteMissed -= _onNoteMissedHandler;
            Engine.OnSing -= _onSingHandler;
            Engine.OnHit -= _onHitHandler;
            Engine.OnComboIncrement -= OnComboIncrement;
            Engine.OnComboReset -= OnComboReset;
            Engine.OnCountdownChange -= _onCountdownChangeHandler;
            Engine.OnPartyVocalsPhrase -= OnPartyVocalsPhrase;
        }

        protected override void FinishDestruction()
        {
            // Stop input context
            _inputContext?.Stop();

            UnsubscribeEngineEvents();

            // Release Addressable handle (AC20)
            if (_needleMaterialHandle.IsValid())
            {
                Addressables.Release(_needleMaterialHandle);
            }

            // Clean up material
            if (_needleMaterialInstance != null)
            {
                Destroy(_needleMaterialInstance);
            }
        }

        protected void OnTargetNoteChangedHandler(VocalNote note)
        {
            _lastTargetNote = note;

            // Free vocals single-mic: tint the trail to the HARM lane being scored, so
            // the trail "lights up" that lane. Needle keeps its singer-slot material.
            if (Player.Profile.IsFreeVocals
                && Engine is YargFreeVocalsEngine freeEngine)
            {
                int idx = freeEngine.CurrentTargetHarmonyIndex;
                if (idx >= 0 && idx < VocalTrack.Colors.Length)
                {
                    _hittingParticleGroup.Colorize(VocalTrack.Colors[idx]);
                }
            }
        }

        protected virtual VocalsEngine CreateEngine()
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
                var multiTrack = VocalChartSelection.ResolveMultitrack(_chart, Player.Profile);

                engine = new YargFreeVocalsEngine(NoteTrack, multiTrack.Parts, SyncTrack, EngineParams, Player.Profile.IsBot,
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

            _onStarPowerPhraseHitHandler = _ => OnStarPowerPhraseHit();
            engine.OnStarPowerPhraseHit += _onStarPowerPhraseHitHandler;
            engine.OnStarPowerStatus += OnStarPowerStatus;

            engine.OnTargetNoteChanged += OnTargetNoteChangedHandler;

            _onPhraseHitHandler = (percent, fullPoints, isLastPhrase) =>
            {
                if (!fullPoints)
                {
                    IsFc = false;
                }

                LastCombo = Combo;

                ShowTextNotifications(isLastPhrase);

                // Multi-mic free vocals shows its banner via OnPartyVocalsPhrase
                // (AWESOME / DOUBLE AWESOME / TRIPLE AWESOME) — suppress the legacy
                // percent-based text so we don't stack two phrase notifications.
                bool multiMicFree = Engine is PartyVocalsCoordinatorEngine;
                if (!multiMicFree)
                {
                    _hud.ShowPhraseHit(percent, Combo);
                }
            };
            engine.OnPhraseHit += _onPhraseHitHandler;

            _onNoteHitHandler = (_, note) =>
            {
                // Free Vocals doesn't spawn percussion visuals, so the pool is empty —
                // calling HitPercussionNote would NRE in Pool.Return.
                if (note.IsPercussion && !Player.Profile.IsFreeVocals)
                {
                    _percussionTrack.HitPercussionNote(note);
                }
            };
            engine.OnNoteHit += _onNoteHitHandler;

            _onNoteMissedHandler = (_, _) =>
            {
                if (LastCombo >= 2)
                {
                    GlobalAudioHandler.PlaySoundEffect(SfxSample.NoteMiss);
                }

                LastCombo = Combo;
            };
            engine.OnNoteMissed += _onNoteMissedHandler;

            _onSingHandler = (singing) =>
            {
                _lastSingTime = singing
                    ? GameManager.InputTime
                    : null;
            };
            engine.OnSing += _onSingHandler;

            _onHitHandler = (hitting) =>
            {
                // Only refresh _lastHitTime on hit; let IsInThreshold's window do
                // the decay on miss. Multi-mic engines fire OnHit(false) every
                // tick when no mic is on a note, which would otherwise snap the
                // trail off on every pitch-tracker dropout. Solo gets a side
                // benefit: missed-note trails decay over ~50ms instead of
                // cutting instantly.
                if (hitting) _lastHitTime = GameManager.InputTime;
            };
            engine.OnHit += _onHitHandler;

            return engine;
        }

        protected void OnPartyVocalsPhrase(PhraseGrade grade, IReadOnlyList<double> canonicalMeters, bool isLastPhrase)
        {
            if (grade == PhraseGrade.Miss)
            {
                // No awesome banner for a missed phrase — fall back to the legacy
                // percent-based "Messy / Okay / Good / Strong" text so the player still
                // gets phrase feedback.
                double bestMeter = 0;
                for (int i = 0; i < canonicalMeters.Count; i++)
                {
                    if (canonicalMeters[i] > bestMeter) bestMeter = canonicalMeters[i];
                }
                double threshold = EngineParams.PhraseHitPercent;
                double percent = threshold > 0 ? bestMeter / threshold : 0;
                _hud.ShowPhraseHit(percent, Combo);
                return;
            }
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
            _percussionTrack.Initialize(NoteTrack.Notes);

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

            if (_inputContext is null)
            {
                return;
            }

            // Get input from the microphone
            foreach (var input in _inputContext.GetInputsFromMic())
            {
                var copy = input;
                OnGameInput(ref copy);
            }
        }

        protected bool IsInThreshold(double currentTime, double? lastTime)
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

            // Update per-HARM fill for Party Vocals (multi-mic via coordinator)
            if (Engine is PartyVocalsCoordinatorEngine coordinator)
            {
                _hud.UpdateHarmFill(coordinator.CanonicalMeters, coordinator.AwesomeThreshold, coordinator.PartHasContent);
            }
            else
            {
                _hud.HideHarmFill();
            }
        }

        protected void ShowTextNotifications(bool isLastPhrase)
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

        protected float GetNeedleRotation(float pitchDist)
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

            // Get whether or not the player has sang within the time threshold.
            // We gotta use a threshold here because microphone inputs are passed every X seconds,
            // not in a constant stream.
            if (!IsInThreshold(singTime, _lastSingTime) || _shouldHideNeedle)
            {
                // Hide needle if there's no singing
                if (_needleVisualContainer.activeSelf)
                {
                    _needleVisualContainer.SetActive(false);
                }
                _hittingParticleGroup.Stop();
            }
            else
            {
                // Show needle if it's hidden
                if (!_needleVisualContainer.activeSelf)
                {
                    _needleVisualContainer.SetActive(true);
                }

                float lerpRate = 30f;
                const float NEEDLE_POS_SNAP_MULTIPLIER = 10f;

                // Lerp faster if we've just started showing the needle
                lerpRate *= NEEDLE_POS_SNAP_MULTIPLIER;

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

        private void UpdatePercussionPhrase(double time)
        {
            // Prevent the HUD from hiding too quickly
            if (time < 0)
            {
                return;
            }

            // Free Vocals ignores percussion — keep HUD/needle visible.
            if (Player.Profile.IsFreeVocals)
            {
                _hud.SetHUDShowing(true);
                _shouldHideNeedle = false;
                return;
            }

            while (ShouldAdvancePhraseIndex(time))
            {
                _phraseIndex++;

                // We've reached the end. No need to continue.
                if (_phraseIndex >= NoteTrack.Notes.Count)
                {
                    SetPercussionMode(false);
                    return;
                }

                var phrase = NoteTrack.Notes[_phraseIndex];
                SetPercussionMode(HasPercussion(phrase));
            }
        }

        private bool ShouldAdvancePhraseIndex(double time)
        {
            // Since phrases start at the note, and not sometime before it, use
            // the end times of phrases instead (where the phrase lines are). Problem
            // with this is that we still gotta account for the first phrase, so use
            // an index of -1 for that.
            bool beforeFirstPhrase = _phraseIndex == -1;
            if (beforeFirstPhrase)
            {
                // Track has no notes. Bail early.
                if (NoteTrack.Notes.Count <= 0)
                {
                    return false;
                }

                var firstPhrase = NoteTrack.Notes[0];
                var firstPhraseHasStarted = firstPhrase.Time <= time;
                return firstPhraseHasStarted || HasPercussion(firstPhrase);
            }

            bool atTheEndOfTrack = _phraseIndex >= NoteTrack.Notes.Count;
            if (atTheEndOfTrack)
            {
                return false;
            }

            var currentPhrase = NoteTrack.Notes[_phraseIndex];
            return currentPhrase.TimeEnd <= time;
        }

        private void SetPercussionMode(bool show)
        {
            // Free Vocals ignores percussion entirely — keep HUD on, needle visible, hide the fret.
            if (Player.Profile.IsFreeVocals)
            {
                _hud.SetHUDShowing(true);
                _percussionTrack.ShowPercussionFret(false);
                _shouldHideNeedle = false;
                return;
            }
            _hud.SetHUDShowing(!show);
            _percussionTrack.ShowPercussionFret(show);
            _shouldHideNeedle = show;
        }

        private static bool HasPercussion(VocalNote phrase)
        {
            foreach (var note in phrase.ChildNotes)
            {
                if (note.IsPercussion)
                {
                    return true;
                }
            }

            return false;
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

            UnsubscribeEngineEvents();
            Engine = CreateEngine();
            ResetPracticeSection();
        }

        public override void SetStemMuteState(bool muted)
        {
            // Vocals has no stem muting
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
        protected float AnchorPitchToOctave(float sungPitch, float referenceNotePitch)
        {
            if (referenceNotePitch < 0f)
            {
                // No reference note (callers pass the -1 sentinel) or a non-pitched/talky
                // note (Pitch < 0): add one octave to keep the needle in the middle of the
                // track. Uses the parameter rather than _lastTargetNote so per-mic Party
                // Vocals callers — which don't populate _lastTargetNote — anchor to their
                // own line instead of always falling through here and jumping an octave up.
                return sungPitch + 12f;
            }

            // Find the octave shift that makes sungPitch closest to the reference
            (_, int octaveShift) = GetPitchDistanceIgnoringOctave(referenceNotePitch, sungPitch);

            // Apply the octave shift
            int referenceOctave = (int) (referenceNotePitch / 12f);
            float normalized = sungPitch % 12f;
            return normalized + 12f * (referenceOctave + octaveShift);
        }

        protected static (float Distance, int OctaveShift) GetPitchDistanceIgnoringOctave(float target, float other)
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

            return (frame, Engine.EngineStats.ConstructReplayStats(Player.Profile.Name, Player.IsReplay));
        }
    }
}