using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Gameplay.HUD;
using YARG.Helpers;
using YARG.Player;

namespace YARG.Gameplay.Player
{
    /// <summary>
    /// Party Vocals player: structurally the Solo single-needle path from
    /// VocalsPlayer.UpdateSingNeedle, replicated N times — one needle and one
    /// particle trail per bound mic. Per-mic singer pitch and on-note state
    /// come from the band-slot YargFreeVocalsEngine (GetMicPitch / IsMicOnNote /
    /// GetEffectivePartForMic). Visibility is gated on per-mic input recency
    /// so blocking one mic doesn't make all needles disappear (or appear).
    /// </summary>
    public sealed class PartyVocalsPlayer : VocalsPlayer
    {
        protected override bool IsPartyVocals => true;

        private struct Slot
        {
            public MeshRenderer Renderer;
            public Transform    Transform;
            public Material     Material;
            public ParticleGroup Particles;
            public double?      LastSingTime;       // per-mic input recency
            public int          LastResolvedPart;   // sticky trail color source; -1 = none yet
        }

        private readonly List<Slot> _slots = new();

        public override void Initialize(int index, int vocalIndex, YargPlayer player, SongChart chart,
            VocalsPlayerHUD hud, VocalPercussionTrack percussionTrack, int? lastHighScore, float trackSpeed)
        {
            base.Initialize(index, vocalIndex, player, chart, hud, percussionTrack, lastHighScore, trackSpeed);

            // Falls through to base single-needle for 1-mic Party Vocals (rare but supported).
            if (_partyVocalsMicCount <= 1) return;

            // Hide the base single needle + trail — we own per-mic clones.
            _needleVisualContainer.SetActive(false);
            _hittingParticleGroup.gameObject.SetActive(false);

            for (int i = 0; i < _partyVocalsMicCount; i++)
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
                    LastResolvedPart = -1,
                });
            }
        }

        protected override void UpdateInputs(double time)
        {
            base.UpdateInputs(time);

            if (_slots.Count == 0) return;

            // Track per-mic input recency from this frame's drained mic inputs.
            // _lastFrameInputs is populated by base.UpdateInputs.
            foreach (var (i, _) in _lastFrameInputs)
            {
                if (i >= 0 && i < _slots.Count)
                {
                    var s = _slots[i];
                    s.LastSingTime = GameManager.InputTime;
                    _slots[i] = s;
                }
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
            var freeEngine = Engine as YargFreeVocalsEngine;
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

                // Per-mic on-note (drives trail + snap-to-chart-pitch). Mirrors
                // Solo's gate (_lastTargetNote + IsInThreshold(_lastHitTime)) and
                // ANDs with two per-mic conditions:
                // - IsMicOnNote(i): band-slot assignment says this mic is on a
                //   chart note this tick.
                // - LastSingTime recency: this mic is actually producing audio.
                //   Solo gets this implicitly because its OnHit only fires when
                //   pitch is detected; for us a silent mic can still be
                //   "assigned" by the rolling window, so we gate explicitly.
                bool hitting = freeEngine != null
                    && _lastTargetNote is not null
                    && IsInThreshold(singTime, _lastHitTime)
                    && freeEngine.IsMicOnNote(i)
                    && IsInThreshold(singTime, slot.LastSingTime);

                float micPitch = freeEngine?.GetMicPitch(i) ?? 0f;
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
                // the HARM lane(s) the mic actually scored on this tick — not the
                // slot's static assignment. Resolution:
                //   0 hits        → assigned-slot color (fallback)
                //   1 hit         → that part's color (regardless of slot)
                //   ≥2 hits, slot's assigned part is in the set → slot color
                //   ≥2 hits, slot's assigned part is NOT in the set → lowest hit part
                // Effect: when ambiguous (unison across harmonies, talky overlaps)
                // the mic "wins the tie" toward its own assigned color; otherwise
                // it picks the lowest active lane.
                if (hitting && !GameManager.Rewinding)
                {
                    uint hitMask = freeEngine.GetMicHittingParts(i);
                    int  partCount = System.Math.Max(1, freeEngine.PartCount);
                    int  assignedPart = i % partCount;

                    int trailPart;
                    if (hitMask == 0u)
                    {
                        // No information this tick — keep the last resolved color
                        // rather than flicking to the slot's assigned color, which
                        // would oscillate yellow/blue on every silent tick within
                        // a HARM1-only stretch where the slot's assigned lane
                        // isn't active. Falls back to slot color only on the very
                        // first frame (before anything resolved).
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

        protected override void FinishDestruction()
        {
            foreach (var slot in _slots)
            {
                if (slot.Transform != null && slot.Transform.gameObject != null)
                    Destroy(slot.Transform.gameObject);
                if (slot.Material != null) Destroy(slot.Material);
                if (slot.Particles != null && slot.Particles.gameObject != null)
                    Destroy(slot.Particles.gameObject);
            }
            _slots.Clear();

            base.FinishDestruction();
        }
    }
}
