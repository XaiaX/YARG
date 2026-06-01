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
using YARG.Core.Game;
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
    /// <summary>
    /// Party Vocals player: structurally the Solo single-needle path from
    /// VocalsPlayer.UpdateSingNeedle, replicated N times — one needle and one
    /// particle trail per bound mic. Per-mic singer pitch and on-note state
    /// come from the PartyVocalsCoordinatorEngine (GetMicPitch / GetMicHittingParts).
    /// Visibility is gated on per-mic input recency so blocking one mic doesn't
    /// make all needles disappear (or appear).
    /// </summary>
    public sealed class PartyVocalsPlayer : VocalsPlayer
    {
        // Per-mic input contexts (indices 0..N-2; mic 0 uses base._inputContext).
        // Stored so PartyVocalsPlayer can stop them in FinishDestruction.
        private readonly List<MicInputContext> _additionalMicContexts = new();

        // Number of mics for this player. Promoted from Initialize local so
        // CreateEngine (called later) can read it.
        private int _micCount;

        private struct Slot
        {
            public MeshRenderer Renderer;
            public Transform    Transform;
            public Material     Material;
            public ParticleGroup Particles;
            public double?      LastSingTime;
            public double?      LastOnNoteTime;
            public int          LastResolvedPart;
            public VocalNote    TargetNote;        // this mic's own current chart note (from its sub-engine)
            public AsyncOperationHandle<Material> MaterialHandle;
        }

        private readonly List<Slot> _slots = new();

        // Per-sub-engine OnTargetNoteChanged unsubscribers. Each sub-engine reports
        // the note ITS mic is on; we record it per-slot so each needle snaps to its
        // own line instead of a single shared _lastTargetNote (which all mics would
        // otherwise follow — it ends up as whichever sub-engine fired last).
        private readonly List<System.Action> _targetNoteUnsubscribers = new();


        // Stored coordinator event handlers for clean unsubscription on practice reset
        private BaseEngine<VocalNote, VocalsEngineParameters, VocalsStats>.StarPowerPhraseHitEvent _coordinatorStarPowerHandler;
        private VocalsEngine.PhraseHitEvent _coordinatorPhraseHitHandler;
        private BaseEngine<VocalNote, VocalsEngineParameters, VocalsStats>.NoteHitEvent _coordinatorNoteHitHandler;
        private BaseEngine<VocalNote, VocalsEngineParameters, VocalsStats>.NoteMissedEvent _coordinatorNoteMissedHandler;
        private System.Action<bool> _coordinatorSingHandler;
        private System.Action<bool> _coordinatorHitHandler;

        public override void Initialize(int index, int vocalIndex, YargPlayer player, SongChart chart,
            VocalsPlayerHUD hud, VocalPercussionTrack percussionTrack, int? lastHighScore, float trackSpeed)
        {
            // Compute mic count BEFORE base.Initialize: base.Initialize calls
            // CreateEngine() (virtual) which now dispatches to our override,
            // and the override needs _micCount to construct the coordinator.
            IReadOnlyList<MicDevice> effectiveMics = null;
            if (!player.IsReplay)
            {
                effectiveMics = player.Bindings.Microphones;
                if (player.Profile.GameMode == GameMode.Vocals && effectiveMics.Count > 1)
                {
                    effectiveMics = new[] { effectiveMics[0] };
                }
            }

            if (player.IsReplay)
            {
                // For replays, derive mic count from the packed inputs in the flat stream.
                var replayFrame = GameManager.ReplayData.Frames[player.ReplayIndex];
                _micCount = DetermineMicCountFromInputs(replayFrame.Inputs);

                Debug.Log($"[PartyVocals] replay: derived mic count={_micCount} from flat stream");
            }
            else if (player.Profile.IsFreeVocals && player.Profile.IsBot)
            {
                // One bot mic per charted HARM part. Use the same track CreateEngine
                // scores on (Harmony when it has content, else Vocals) so the slot
                // count matches the coordinator's part count. chart.Vocals always has
                // exactly one part, so reading chart.Vocals.Parts.Count here gave every
                // bot a single needle (no per-mic slots) — visible as missing needles
                // and trails even though scoring ran correctly off the Harmony parts.
                var botTrack = VocalChartSelection.ResolveMultitrack(chart, player.Profile);

                // PartyVocalsMicCountOverride: 0 = Auto (one synthetic vocalist per
                // charted HARM part). 1-7 forces that many regardless of part count —
                // a test/dev knob for mismatched mic-to-part ratios. The coordinator
                // handles mic counts that differ from part count (cap/wrap mapping).
                byte micOverride = player.Profile.PartyVocalsMicCountOverride;
                _micCount = micOverride == 0
                    ? Mathf.Max(1, botTrack.Parts.Count)
                    : micOverride;
            }
            else if (effectiveMics.Count > 0)
            {
                _micCount = effectiveMics.Count;
            }
            else
            {
                _micCount = 1;
            }

            base.Initialize(index, vocalIndex, player, chart, hud, percussionTrack, lastHighScore, trackSpeed);

            // Always use per-mic slots (even for _micCount == 1). The engine is a
            // PartyVocalsCoordinatorEngine, not a regular VocalsEngine — the base
            // single-needle path doesn't drive the coordinator's sub-engines, so
            // bots and real-mic visuals would break on solo-only charts where
            // _micCount == 1.
            if (_micCount < 1) return;

            // Hide the base single needle + trail — we own per-mic clones.
            _needleVisualContainer.SetActive(false);
            _hittingParticleGroup.gameObject.SetActive(false);

            for (int i = 0; i < _micCount; i++)
            {
                // Needle clone.
                // AC27.2: one-shot sync load per slot at Initialize; handle released in FinishDestruction.
                int micNeedleIndex = (i % NEEDLES_COUNT) + 1;
                var materialHandle = Addressables.LoadAssetAsync<Material>($"VocalNeedle/{micNeedleIndex}");
                var baseMaterial = materialHandle.WaitForCompletion();
                var materialInstance = new Material(baseMaterial);

                var needleObj = Instantiate(_needleVisualContainer, _needleVisualContainer.transform.parent);
                needleObj.SetActive(true);
                var renderer = needleObj.GetComponentInChildren<MeshRenderer>();
                renderer.material = materialInstance;

                // Particle clone (must stay SetActive(true) — Play() is a no-op on inactive).
                var pgObj = Instantiate(_hittingParticleGroup.gameObject, _hittingParticleGroup.transform.parent);
                pgObj.SetActive(true);
                var pg = pgObj.GetComponent<ParticleGroup>();

                _slots.Add(new Slot
                {
                    Renderer  = renderer,
                    Transform = needleObj.transform,
                    Material  = materialInstance,
                    Particles = pg,
                    LastSingTime = null,
                    LastOnNoteTime = null,
                    LastResolvedPart = -1,
                    MaterialHandle = materialHandle,
                });
            }

            // Create additional mic input contexts for mics 1..N.
            // Mic 0 is handled by base._inputContext (created in base.Initialize).
            if (!Player.IsReplay && !player.Profile.IsBot)
            {
                for (int i = 1; i < effectiveMics.Count && i < _micCount; i++)
                {
                    var ctx = new MicInputContext(effectiveMics[i], GameManager);
                    ctx.Start();
                    _additionalMicContexts.Add(ctx);
                }
            }
        }

        private void RouteMicInputs(int micIndex, MicInputContext context)
        {
            if (context == null) return;
            foreach (var input in context.GetInputsFromMic())
            {
                var action = input.GetAction<VocalsAction>();
                int packed = PartyVocalsInput.Pack(micIndex, action);

                // Preserve the union value: pitch carries Axis (float), hit/SP carry Button.
                GameInput tagged = action == VocalsAction.Pitch
                    ? new GameInput(input.Time, packed, input.Axis)
                    : new GameInput(input.Time, packed, input.Button);

                // OnGameInput applies relative-time + InputCalibration, queues to the
                // engine (coordinator demux routes by mic and sets the per-mic sang flag),
                // and records to _replayInputs.
                OnGameInput(ref tagged);
            }
        }

        private void StampMicSingTimes(PartyVocalsCoordinatorEngine coordinator)
        {
            if (coordinator == null) return;
            var singTime = GameManager.InputTime;
            for (int i = 0; i < _micCount && i < _slots.Count; i++)
            {
                bool micActive = coordinator.DidMicSingThisTick(i)
                                 || coordinator.GetMicHittingParts(i) != 0u;
                if (!micActive) continue;
                var s = _slots[i];
                s.LastSingTime = singTime;
                _slots[i] = s;
            }
        }

        protected override void UpdateInputs(double time)
        {
            // Handle replay playback via the base flat-input path.
            if (Player.IsReplay)
            {
                if (Engine is PartyVocalsCoordinatorEngine replayCoordinator)
                {
                    replayCoordinator.ResetMicSangFlags();
                    base.UpdateInputs(time);             // processes flat _replayInputs → demux
                    StampMicSingTimes(replayCoordinator);
                }
                else
                {
                    base.UpdateInputs(time);
                }
                return;
            }

            // For coordinator engines: route all input through OnGameInput with packed mic index.
            // The base.UpdateInputs path is bypassed (it uses single _inputContext and doesn't know
            // about mic indices). OnGameInput handles relative time conversion, calibration,
            // queuing to the coordinator, and replay recording.
            var liveCoordinator = Engine as PartyVocalsCoordinatorEngine;
            bool isCoordinator = liveCoordinator != null;

            if (!isCoordinator)
            {
                base.UpdateInputs(time);
                return;
            }

            // Reset per-mic sang flags before routing inputs. The coordinator demux sets
            // the per-mic sang flag while processing each pitch during OnGameInput.
            liveCoordinator.ResetMicSangFlags();

            // Route all mic input through OnGameInput with packed mic index.
            // OnGameInput handles time conversion, calibration, queuing, and replay recording.
            RouteMicInputs(0, _inputContext);
            for (int i = 0; i < _additionalMicContexts.Count; i++)
                RouteMicInputs(i + 1, _additionalMicContexts[i]);

            // Advance the engine for this frame.
            BaseEngine.Update(time + InputCalibration);

            // Stamp per-mic singing recency via coordinator flags.
            StampMicSingTimes(liveCoordinator);
        }

        protected override void ResetVisuals()
        {
            base.ResetVisuals();

            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                s.LastSingTime = null;
                s.LastOnNoteTime = null;
                s.TargetNote = null;
                s.LastResolvedPart = -1;
                s.Particles.Stop();
                if (s.Transform.gameObject.activeSelf)
                    s.Transform.gameObject.SetActive(false);
                _slots[i] = s;
            }
        }

        protected override void UpdateVisuals(double visualTime)
        {
            base.UpdateVisuals(visualTime);

            if (_slots.Count == 0) return;

            // Same root-zero reset the legacy multi-mic shadow does: the player
            // GameObject sits at (-5, 0.2, 0) in the prefab; without zeroing it
            // every clone projects offscreen.
            transform.localPosition = Vector3.zero;

            var singTime  = GameManager.InputTime;
            var songTime  = GameManager.SongTime;
            var coordinator = Engine as PartyVocalsCoordinatorEngine;

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];

                // This mic's own chart note (recorded per sub-engine) — each needle
                // snaps to its own line, not a single shared global target note.
                var   slotNote      = slot.TargetNote;
                float lastNotePitch = slotNote?.PitchAtSongTime(songTime) ?? -1f;
                bool  isNonPitched  = slotNote?.IsNonPitched ?? false;

                // Per-mic visibility: this mic specifically singing recently.
                bool singing = IsInThreshold(singTime, slot.LastSingTime) && !_shouldHideNeedle;
                if (!singing)
                {
                    if (slot.Transform.gameObject.activeSelf)
                        slot.Transform.gameObject.SetActive(false);
                    slot.Particles.Stop();
                    continue;
                }

                // Refresh per-mic LastOnNoteTime whenever this mic's pitch lands
                // on any chart note's tolerance window this tick.
                uint hitMask = coordinator?.GetMicHittingParts(i) ?? 0u;
                if (hitMask != 0u)
                {
                    slot.LastOnNoteTime = singTime;
                }

                // Per-mic on-note gate (drives trail + snap-to-chart-pitch).
                bool hitting = coordinator != null
                    && slotNote is not null
                    && IsInThreshold(singTime, slot.LastOnNoteTime)
                    && IsInThreshold(singTime, slot.LastSingTime);

                float micPitch = coordinator?.GetMicPitch(i) ?? 0f;
                float pitch;
                if (hitting && !isNonPitched)
                {
                    pitch = lastNotePitch;
                }
                else if (hitting && isNonPitched)
                {
                    pitch = micPitch + 12f;
                }
                else
                {
                    // Not hitting: anchor singer pitch to the chart line so the
                    // needle hovers near the active part instead of an arbitrary
                    // octave away.
                    pitch = AnchorPitchToOctave(micPitch, lastNotePitch);
                }

                // Show/snap if just becoming visible.
                if (!slot.Transform.gameObject.activeSelf)
                {
                    slot.Transform.gameObject.SetActive(true);
                }

                // Position + rotation lerp (mirrors single-needle Solo path).
                const float NEEDLE_POS_LERP = 30f;
                const float NEEDLE_ROT_LERP = 25f;
                float lerpRate = NEEDLE_POS_LERP;

                float targetRotation = 0f;
                if (hitting && !isNonPitched)
                {
                    (float pitchDist, _) = GetPitchDistanceIgnoringOctave(lastNotePitch, micPitch);
                    targetRotation = GetNeedleRotation(pitchDist);
                }

                float z = GameManager.VocalTrack.GetPosForPitch(pitch);
                float lerp = Mathf.Lerp(slot.Transform.localPosition.z, z, Time.deltaTime * lerpRate);
                slot.Transform.localPosition = new Vector3(0f, 0f, lerp);
                slot.Transform.rotation = Quaternion.Lerp(slot.Transform.rotation,
                    Quaternion.Euler(0f, targetRotation + 90f, 0f), Time.deltaTime * NEEDLE_ROT_LERP);

                // Trail: only when this mic is actually on a note. Color follows
                // the HARM lane(s) the mic actually scored on this tick.
                if (hitting && !GameManager.Rewinding)
                {
                    int  partCount = System.Math.Max(1, coordinator?.PartCount ?? 1);
                    int  assignedPart = i % partCount;

                    int trailPart;
                    if (hitMask == 0u)
                    {
                        // No information this tick — keep the last resolved color.
                        trailPart = slot.LastResolvedPart >= 0 ? slot.LastResolvedPart : i;
                    }
                    else if ((hitMask & (1u << assignedPart)) != 0)
                    {
                        // Ambiguous OR single-hit-on-assigned: mic's own lane wins.
                        trailPart = assignedPart;
                    }
                    else
                    {
                        // Either single hit on a non-assigned lane, or multi-hit
                        // with assigned not included — pick the lowest set bit.
                        trailPart = LowestSetBit(hitMask);
                    }

                    // Remember for the next silent tick(s).
                    slot.LastResolvedPart = trailPart;
                    _slots[i] = slot;

                    slot.Particles.Colorize(VocalTrack.Colors[trailPart % VocalTrack.Colors.Length]);
                    slot.Particles.transform.localPosition = new Vector3(0f, 0f, slot.Transform.localPosition.z);
                    slot.Particles.Play();
                }
                else
                {
                    slot.Particles.Stop();
                }
            }
        }

        private static int LowestSetBit(uint v)
        {
            // v is never 0 at the call site; guarded for safety.
            if (v == 0u) return 0;
            int n = 0;
            while ((v & 1u) == 0u) { v >>= 1; n++; }
            return n;
        }

        // Records the chart note a given mic's sub-engine is currently on, so that
        // mic's needle snaps to its own line. Fired from each sub-engine's
        // OnTargetNoteChanged (see CreateEngine).
        private void SetSlotTargetNote(int micIndex, VocalNote note)
        {
            if (micIndex < 0 || micIndex >= _slots.Count) return;
            var s = _slots[micIndex];
            s.TargetNote = note;
            _slots[micIndex] = s;
        }

        protected override VocalsEngine CreateEngine()
        {
            if (!Player.IsReplay)
            {
                var singToActivateStarPower =
                    SettingsManager.Settings.VoiceActivatedVocalStarPower.Value &&
                    !Player.Profile.IsModifierActive(Modifier.ManualVocalStarPower);

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

            // Must match the chart-selection logic in Initialize so the engine sees
            // the same parts (and pitch register) as the visualization.
            var multiTrack = VocalChartSelection.ResolveMultitrack(_chart, Player.Profile);

            var coordinator = new PartyVocalsCoordinatorEngine(NoteTrack, multiTrack.Parts, SyncTrack,
                EngineParams, Player.Profile.IsBot,
                micCount: _micCount,
                botPartIndex: Player.Profile.HarmonyIndex);

            // Register using the free vocals overload
            EngineContainer = GameManager.EngineManager.Register(coordinator, NoteTrack.Instrument, freeVocals: true, _chart, Player.RockMeterPreset);

            // Wire all engine events (mirrors VocalsPlayer.CreateEngine wiring)
            _coordinatorStarPowerHandler = _ => OnStarPowerPhraseHit();
            coordinator.OnStarPowerPhraseHit += _coordinatorStarPowerHandler;
            coordinator.OnStarPowerStatus += OnStarPowerStatus;

            // The coordinator itself does not fire OnTargetNoteChanged — sub-engines do.
            // Subscribe each sub-engine to record ITS mic's note into that mic's slot,
            // so every needle snaps to its own line. (Subscribing them all to the shared
            // OnTargetNoteChangedHandler made all needles follow whichever sub-engine
            // fired last — i.e. the highest HARM present.)
            for (int i = 0; i < coordinator.SubEngines.Count; i++)
            {
                int micIndex = i;
                var sub = coordinator.SubEngines[i];
                VocalsEngine.TargetNoteChangeEvent handler = note => SetSlotTargetNote(micIndex, note);
                sub.OnTargetNoteChanged += handler;
                _targetNoteUnsubscribers.Add(() => sub.OnTargetNoteChanged -= handler);
            }

            _coordinatorPhraseHitHandler = (percent, fullPoints, isLastPhrase) =>
            {
                if (!fullPoints)
                {
                    IsFc = false;
                }

                LastCombo = Combo;

                ShowTextNotifications(isLastPhrase);

                // Multi-mic shows its banner via OnPartyVocalsPhrase — suppress
                // the legacy percent-based text to avoid stacking two notifications.
            };
            coordinator.OnPhraseHit += _coordinatorPhraseHitHandler;

            _coordinatorNoteHitHandler = (_, note) =>
            {
                // Party Vocals shows percussion hit feedback (scored at the coordinator).
                if (note.IsPercussion)
                {
                    // [PV-PERC-DIAG] C: coordinator scored a percussion note and called
                    // back. If B logs but this doesn't, the engine rejected the tap
                    // (hit-window/timing). If this logs but no on-track feedback shows,
                    // the defect is in the visual layer (HitPercussionNote / track mode).
                    YargLogger.LogFormatInfo("[PV-PERC-DIAG] C NoteHit: percussion scored t={0:0.000}", note.Time);
                    _percussionTrack.HitPercussionNote(note);
                }
            };
            coordinator.OnNoteHit += _coordinatorNoteHitHandler;

            _coordinatorNoteMissedHandler = (_, _) =>
            {
                if (LastCombo >= 2)
                {
                    GlobalAudioHandler.PlaySoundEffect(SfxSample.NoteMiss);
                }

                LastCombo = Combo;
            };
            coordinator.OnNoteMissed += _coordinatorNoteMissedHandler;

            _coordinatorSingHandler = (singing) =>
            {
                _lastSingTime = singing
                    ? GameManager.InputTime
                    : null;
            };
            coordinator.OnSing += _coordinatorSingHandler;

            _coordinatorHitHandler = (hitting) =>
            {
                // Only refresh _lastHitTime on hit; let IsInThreshold's window do
                // the decay on miss.
                if (hitting) _lastHitTime = GameManager.InputTime;
            };
            coordinator.OnHit += _coordinatorHitHandler;

            coordinator.OnPartyVocalsPhrase += OnPartyVocalsPhrase;

            return coordinator;
        }

        protected override void UnsubscribeEngineEvents()
        {
            base.UnsubscribeEngineEvents();

            if (Engine is PartyVocalsCoordinatorEngine coordinator)
            {
                coordinator.OnStarPowerPhraseHit -= _coordinatorStarPowerHandler;
                coordinator.OnPhraseHit -= _coordinatorPhraseHitHandler;
                coordinator.OnNoteHit -= _coordinatorNoteHitHandler;
                coordinator.OnNoteMissed -= _coordinatorNoteMissedHandler;
                coordinator.OnSing -= _coordinatorSingHandler;
                coordinator.OnHit -= _coordinatorHitHandler;
            }

            foreach (var unsubscribe in _targetNoteUnsubscribers)
            {
                unsubscribe();
            }
            _targetNoteUnsubscribers.Clear();

            // Reset per-mic slot state so stale trails/singing timestamps
            // don't persist across practice resets.
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                s.LastSingTime = null;
                s.LastOnNoteTime = null;
                s.TargetNote = null;
                s.LastResolvedPart = -1;
                s.Particles.Stop();
                _slots[i] = s;
            }
        }

        protected override void FinishDestruction()
        {
            foreach (var slot in _slots)
            {
                if (slot.MaterialHandle.IsValid())
                {
                    Addressables.Release(slot.MaterialHandle);
                }
                if (slot.Transform != null && slot.Transform.gameObject != null)
                    Destroy(slot.Transform.gameObject);
                if (slot.Material != null) Destroy(slot.Material);
                if (slot.Particles != null && slot.Particles.gameObject != null)
                    Destroy(slot.Particles.gameObject);
            }
            _slots.Clear();

            // Stop additional mic input contexts
            foreach (var ctx in _additionalMicContexts)
            {
                ctx.Stop();
            }
            _additionalMicContexts.Clear();

            base.FinishDestruction();
        }

        public override (ReplayFrame Frame, ReplayStats Stats) ConstructReplayData()
        {
            var frame = new ReplayFrame(Player.Profile, EngineParams, Engine.EngineStats, ReplayInputs.ToArray());

            return (frame, Engine.EngineStats.ConstructReplayStats(Player.Profile.Name, Player.IsReplay));
        }


        /// <summary>
        /// Override Rewind to stop per-mic particle trails (mirrors base VocalsPlayer behavior).
        /// </summary>
        public override void Rewind(double visualTime)
        {
            base.Rewind(visualTime);

            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                s.Particles.Stop();
                _slots[i] = s;
            }
        }

        /// <summary>
        /// Determine the number of mics from the packed flat input stream.
        /// Each mic's inputs are packed with the mic index in the action field.
        /// </summary>
        private int DetermineMicCountFromInputs(GameInput[] inputs)
        {
            if (inputs == null || inputs.Length == 0)
                return 1;

            int maxMicIndex = 0;
            foreach (var input in inputs)
            {
                // PartyVocalsInput.Pack packs the mic index in the upper bits
                // and the action in the lower bits.
                int packedAction = input.Action;
                int micIndex = PartyVocalsInput.UnpackMic(packedAction);

                if (micIndex > maxMicIndex)
                    maxMicIndex = micIndex;
            }

            // micIndex is 0-based, so +1 gives the count
            return maxMicIndex + 1;
        }
    }
}
