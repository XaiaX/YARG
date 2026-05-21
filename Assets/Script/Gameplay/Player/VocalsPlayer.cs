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

            // Needle materials have names starting from 1.
            var needleIndex = (vocalIndex % NEEDLES_COUNT) + 1;
            var materialPath = $"VocalNeedle/{needleIndex}";
            var baseMaterial = Addressables.LoadAssetAsync<Material>(materialPath).WaitForCompletion();
            _needleMaterialInstance = new Material(baseMaterial);
            _needleRenderer.material = _needleMaterialInstance;

            // Get the notes from the specific harmony or solo part.
            // For Free Vocals on songs that have a Harmony chart, source from Harmony so the
            // bot's pitch values are in the same register as the visualized HARM lines
            // (the global VocalTrack is initialized with Chart.Harmony in this case — see
            // GameManager.Loading.cs). Otherwise the bot's needle is octave-offset from
            // the rendered lines.
            var multiTrack = (Player.Profile.IsFreeVocals && chart.Harmony.Parts.Count > 1)
                ? chart.Harmony
                : chart.GetVocalsTrack(Player.Profile.CurrentInstrument);

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

            // Initialize player specific vocal visuals

            hud.Initialize(player.EnginePreset);
            _hud = hud;

            percussionTrack.Initialize(NoteTrack.Notes);
            _percussionTrack = percussionTrack;

            _hud.ShowPlayerName(player, needleIndex);

            // Create and start an input context for the mic
            if (!Player.IsReplay && player.Bindings.Microphone != null)
            {
                _inputContext = new MicInputContext(player.Bindings.Microphone, GameManager);
                _inputContext.Start();
            }

            Engine = CreateEngine();

            Engine.OnComboIncrement += OnComboIncrement;
            Engine.OnComboReset += OnComboReset;

            if (vocalIndex == 0)
            {
                if (Player.Profile.CurrentInstrument == Instrument.Vocals)
                {
                    Engine.BuildCountdownsFromSelectedPart();
                }
                else if (Player.Profile.IsFreeVocals || Player.Profile.CurrentInstrument == Instrument.Harmony)
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
            _inputContext?.Stop();

            // Unsubscribe from engine events and clean up material instance
            if (Engine != null)
            {
                Engine.OnTargetNoteChanged -= OnTargetNoteChangedHandler;
            }

            if (_needleMaterialInstance != null)
            {
                Destroy(_needleMaterialInstance);
            }
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
            YargLogger.LogFormatInfo(
                "[FreeVocalsDiag] Player='{0}' IsBot={1} CurrentInstrument={2} FreeHarmony={3} IsFreeVocals={4} HarmonyIndex={5}",
                Player.Profile.Name, Player.Profile.IsBot, Player.Profile.CurrentInstrument,
                Player.Profile.FreeHarmony, Player.Profile.IsFreeVocals, Player.Profile.HarmonyIndex);
            if (Player.Profile.IsFreeVocals)
            {
                // Must match the chart-selection logic in Initialize above so the engine sees
                // the same parts (and pitch register) as the visualization.
                var multiTrack = (_chart.Harmony.Parts.Count > 1)
                    ? _chart.Harmony
                    : _chart.GetVocalsTrack(Player.Profile.CurrentInstrument);
                YargLogger.LogFormatInfo(
                    "[FreeVocalsDiag]  -> creating YargFreeVocalsEngine: parts.Count={0} botPartIndex={1} chartSource={2}",
                    multiTrack.Parts.Count, Player.Profile.HarmonyIndex,
                    multiTrack.Parts.Count > 1 ? "Harmony" : "Vocals");
                engine = new YargFreeVocalsEngine(NoteTrack, multiTrack.Parts, SyncTrack, EngineParams, Player.Profile.IsBot,
                    botPartIndex: Player.Profile.HarmonyIndex);
                // Register using the free vocals overload
                EngineContainer = GameManager.EngineManager.Register(engine, NoteTrack.Instrument, freeVocals: true, _chart, Player.RockMeterPreset);
            }
            else
            {
                YargLogger.LogFormatInfo("[FreeVocalsDiag]  -> creating YargVocalsEngine (NOT free)");
                // For Solo/Harmony, use single-part engine
                engine = new YargVocalsEngine(NoteTrack, SyncTrack, EngineParams, Player.Profile.IsBot);
                // Register using the indexed overload
                EngineContainer = GameManager.EngineManager.Register(engine, NoteTrack.Instrument, Player.Profile.HarmonyIndex, _chart, Player.RockMeterPreset);
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
                if (note.IsPercussion)
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
            // Push all inputs from mic
            if (!Player.IsReplay && _inputContext != null)
            {
                foreach (var input in _inputContext.GetInputsFromMic())
                {
                    var i = input;
                    OnGameInput(ref i);
                }
            }

            base.UpdateInputs(time);
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
            const float NEEDLE_POS_LERP = 30f;
            const float NEEDLE_POS_SNAP_MULTIPLIER = 10f;

            const float NEEDLE_ROT_LERP = 25f;

            // Get the appropriate sing time
            var singTime = GameManager.InputTime;

            // Get whether or not the player has sang within the time threshold.
            // We gotta use a threshold here because microphone inputs are passed every X seconds,
            // not in a constant stream.
            if (!IsInThreshold(singTime, _lastSingTime) || _shouldHideNeedle)
            {
                // Hide the needle if there's no singing
                if (_needleVisualContainer.activeSelf)
                {
                    _needleVisualContainer.SetActive(false);
                    _hittingParticleGroup.Stop();
                }
            }
            else
            {
                float lerpRate = NEEDLE_POS_LERP;

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
                        Quaternion.Euler(0f, targetRotation + 90f, 0f), Time.deltaTime * NEEDLE_ROT_LERP);
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
                        Quaternion.Euler(0f, 90f, 0f), Time.deltaTime * NEEDLE_ROT_LERP);
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

                _hud.SetHUDShowing(!hasPercussion);
                _percussionTrack.ShowPercussionFret(hasPercussion);
                _shouldHideNeedle = hasPercussion;
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
            return (frame, Engine.EngineStats.ConstructReplayStats(Player.Profile.Name, Player.IsReplay));
        }
    }
}
