using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Input;
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
        }

        private readonly List<Slot> _slots = new();

        public override void Initialize(int index, int vocalIndex, YargPlayer player, SongChart chart,
            VocalsPlayerHUD hud, VocalPercussionTrack percussionTrack, int? lastHighScore, float trackSpeed)
        {
            // Compute mic count BEFORE base.Initialize: base.Initialize calls
            // CreateEngine() (virtual) which now dispatches to our override,
            // and the override needs _micCount to construct the coordinator.
            IReadOnlyList<MicDevice> effectiveMics = player.Bindings.Microphones;
            if (player.Profile.GameMode == GameMode.Vocals && effectiveMics.Count > 1)
            {
                effectiveMics = new[] { effectiveMics[0] };
            }

            if (player.Profile.IsFreeVocals && player.Profile.IsBot)
            {
                // One bot mic per charted HARM part. Use the same track CreateEngine
                // scores on (Harmony when it has content, else Vocals) so the slot
                // count matches the coordinator's part count. chart.Vocals always has
                // exactly one part, so reading chart.Vocals.Parts.Count here gave every
                // bot a single needle (no per-mic slots) — visible as missing needles
                // and trails even though scoring ran correctly off the Harmony parts.
                bool harmonyHasContent = chart.Harmony.Parts.Any(p => p.NotePhrases.Count > 0);
                var botTrack = harmonyHasContent ? chart.Harmony : chart.Vocals;
                _micCount = Mathf.Max(1, botTrack.Parts.Count);
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

            // Falls through to base single-needle for 1-mic Party Vocals (rare but supported).
            if (_micCount <= 1) return;

            // Hide the base single needle + trail — we own per-mic clones.
            _needleVisualContainer.SetActive(false);
            _hittingParticleGroup.gameObject.SetActive(false);

            for (int i = 0; i < _micCount; i++)
            {
                // Needle clone.
                int micNeedleIndex = (i % NEEDLES_COUNT) + 1;
                var baseMaterial = Addressables.LoadAssetAsync<Material>($"VocalNeedle/{micNeedleIndex}").WaitForCompletion();
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

        protected override void UpdateInputs(double time)
        {
            // For multi-mic: bypass base.UpdateInputs to route each mic's pitch
            // directly to the coordinator's SetMicPitch instead of the base engine's
            // input queue (coordinator.MutateStateWithInput ignores VocalsAction.Pitch).
            var coordinator = Engine as PartyVocalsCoordinatorEngine;
            bool isMultiMic = coordinator != null && _micCount > 1;

            if (!isMultiMic)
            {
                base.UpdateInputs(time);
                return;
            }

            // Mic 0: read from base._inputContext
            if (_inputContext != null)
            {
                foreach (var input in _inputContext.GetInputsFromMic())
                {
                    if (input.GetAction<VocalsAction>() == VocalsAction.Pitch)
                    {
                        coordinator.SetMicPitch(0, input.Axis);
                    }
                    else
                    {
                        // Hit / StarPower go through the normal input path
                        var copy = input;
                        OnGameInput(ref copy);
                    }
                }
            }

            // Mics 1..N
            for (int i = 0; i < _additionalMicContexts.Count; i++)
            {
                int micIndex = i + 1;
                foreach (var input in _additionalMicContexts[i].GetInputsFromMic())
                {
                    if (input.GetAction<VocalsAction>() == VocalsAction.Pitch)
                    {
                        coordinator.SetMicPitch(micIndex, input.Axis);
                    }
                    else
                    {
                        var copy = input;
                        OnGameInput(ref copy);
                    }
                }
            }

            // Advance the engine for this frame. The single-mic path delegates to
            // base.UpdateInputs, which calls BaseEngine.Update; the multi-mic path
            // above bypasses base to split pitch routing (Pitch -> SetMicPitch,
            // Hit/StarPower -> OnGameInput), so it must drive the engine itself.
            // Without this the engine only advanced when real-mic input frames
            // arrived, so bots (which have no input stream) never progressed — no
            // scoring and needles stuck at the bottom of the highway.
            BaseEngine.Update(time + InputCalibration);

            // Track per-mic input recency for needle visibility
            var singTime = GameManager.InputTime;
            for (int i = 0; i < _micCount && i < _slots.Count; i++)
            {
                var s = _slots[i];
                s.LastSingTime = singTime;
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
            float lastNotePitch = _lastTargetNote?.PitchAtSongTime(songTime) ?? -1f;
            bool  isNonPitched  = _lastTargetNote?.IsNonPitched ?? false;

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];

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
                    && _lastTargetNote is not null
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

        protected override VocalsEngine CreateEngine()
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

            // Must match the chart-selection logic in Initialize so the engine sees
            // the same parts (and pitch register) as the visualization.
            bool harmonyHasContent = _chart.Harmony.Parts.Any(p => p.NotePhrases.Count > 0);
            var multiTrack = harmonyHasContent ? _chart.Harmony : _chart.Vocals;

            var coordinator = new PartyVocalsCoordinatorEngine(NoteTrack, multiTrack.Parts, SyncTrack,
                EngineParams, Player.Profile.IsBot,
                micCount: _micCount,
                botPartIndex: Player.Profile.HarmonyIndex);

            // Register using the free vocals overload
            EngineContainer = GameManager.EngineManager.Register(coordinator, NoteTrack.Instrument, freeVocals: true, _chart, Player.RockMeterPreset);

            // Wire all engine events (mirrors VocalsPlayer.CreateEngine wiring)
            coordinator.OnStarPowerPhraseHit += _ => OnStarPowerPhraseHit();
            coordinator.OnStarPowerStatus += OnStarPowerStatus;

            // The coordinator itself does not fire OnTargetNoteChanged — sub-engines do.
            // Subscribe all sub-engines so _lastTargetNote stays current from any mic.
            foreach (var subEngine in coordinator.SubEngines)
            {
                subEngine.OnTargetNoteChanged += OnTargetNoteChangedHandler;
            }

            coordinator.OnPhraseHit += (percent, fullPoints, isLastPhrase) =>
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

            coordinator.OnNoteHit += (_, note) =>
            {
                // Free Vocals doesn't spawn percussion visuals
                if (note.IsPercussion && !Player.Profile.IsFreeVocals)
                {
                    _percussionTrack.HitPercussionNote(note);
                }
            };

            coordinator.OnNoteMissed += (_, _) =>
            {
                if (LastCombo >= 2)
                {
                    GlobalAudioHandler.PlaySoundEffect(SfxSample.NoteMiss);
                }

                LastCombo = Combo;
            };

            coordinator.OnSing += (singing) =>
            {
                _lastSingTime = singing
                    ? GameManager.InputTime
                    : null;
            };

            coordinator.OnHit += (hitting) =>
            {
                // Only refresh _lastHitTime on hit; let IsInThreshold's window do
                // the decay on miss.
                if (hitting) _lastHitTime = GameManager.InputTime;
            };

            coordinator.OnPartyVocalsPhrase += OnPartyVocalsPhrase;

            return coordinator;
        }

        protected override void FinishDestruction()
        {
            // Unsubscribe sub-engine target-note handlers (base.FinishDestruction
            // only unsubscribes from Engine itself, but the coordinator doesn't
            // fire OnTargetNoteChanged — its sub-engines do).
            if (Engine is PartyVocalsCoordinatorEngine coordinator)
            {
                foreach (var subEngine in coordinator.SubEngines)
                {
                    subEngine.OnTargetNoteChanged -= OnTargetNoteChangedHandler;
                }
            }

            foreach (var slot in _slots)
            {
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
    }
}
