using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Input;
using YARG.Gameplay.HUD;
using YARG.Helpers;
using YARG.Input;
using YARG.Player;

namespace YARG.Gameplay.Player
{
    /// <summary>
    /// Coordinator for Party Vocals (GameMode.PartyVocals). Extends VocalsPlayer
    /// by spawning N visual sub-engines (one per bound mic), each running a
    /// single-mic YargFreeVocalsEngine to drive its own needle and trail.
    ///
    /// Scoring remains the responsibility of the band-slot engine (the base
    /// VocalsPlayer's engine). Sub-engines are visual-only — they are NOT
    /// registered with EngineManager and do NOT contribute to score, combo,
    /// or star power.
    /// </summary>
    public sealed class PartyVocalsPlayer : VocalsPlayer
    {
        protected override bool IsPartyVocals => true;

        private readonly List<PartyVocalsMicSlot> _slots = new();

        public override void Initialize(int index, int vocalIndex, YargPlayer player, SongChart chart,
            VocalsPlayerHUD hud, VocalPercussionTrack percussionTrack, int? lastHighScore, float trackSpeed)
        {
            base.Initialize(index, vocalIndex, player, chart, hud, percussionTrack, lastHighScore, trackSpeed);

            if (player.Profile.GameMode != GameMode.PartyVocals)
            {
                // Defensive: PartyVocalsPlayer instantiated for a non-PartyVocals profile.
                // Treat as base VocalsPlayer — no sub-engines.
                return;
            }

            // Determine which multi-track to use (mirrors base.Initialize logic).
            VocalsTrack multiTrack;
            if (player.Profile.IsFreeVocals)
            {
                bool harmonyHasContent = chart.Harmony.Parts.Any(p => p.NotePhrases.Count > 0);
                multiTrack = harmonyHasContent ? chart.Harmony : chart.Vocals;
            }
            else
            {
                multiTrack = chart.GetVocalsTrack(player.Profile.CurrentInstrument);
            }

            // Replays still short-circuit (deferred to Phase 6 replay format bump).
            if (player.IsReplay)
            {
                return;
            }

            // Slot count derivation:
            // - Bot Party Vocals: _partyVocalsMicCount was set by base.Initialize.
            // - Human Party Vocals: _inputContexts.Count.
            int slotCount = player.Profile.IsBot
                ? _partyVocalsMicCount
                : (_inputContexts?.Count ?? 0);
            if (slotCount <= 1) return; // single-mic falls through to base

            for (int i = 0; i < slotCount; i++)
            {
                // 1. Single-mic sub-engine (not registered with EngineManager).
                var subEngine = new YargFreeVocalsEngine(
                    NoteTrack,
                    multiTrack.Parts,
                    SyncTrack,
                    EngineParams,
                    isBot: player.Profile.IsBot,
                    micCount: 1,
                    botPartIndex: i);

                // 2. Clone the needle visual.
                var needleObj = Instantiate(_needleVisualContainer, _needleVisualContainer.transform.parent);
                needleObj.SetActive(false);
                var renderer = needleObj.GetComponentInChildren<MeshRenderer>();

                int micNeedleIndex = (i % NEEDLES_COUNT) + 1;
                var materialPath = $"VocalNeedle/{micNeedleIndex}";
                var baseMaterial = Addressables.LoadAssetAsync<Material>(materialPath).WaitForCompletion();
                var materialInstance = new Material(baseMaterial);
                renderer.material = materialInstance;

                // 3. Clone the particle group.
                var pgObj = Instantiate(_hittingParticleGroup.gameObject, _hittingParticleGroup.transform.parent);
                pgObj.SetActive(false);
                var pg = pgObj.GetComponent<ParticleGroup>();
                pg.Colorize(VocalTrack.Colors[i % VocalTrack.Colors.Length]);

                var inputContext = (i < (_inputContexts?.Count ?? 0)) ? _inputContexts[i] : null;
                var device = inputContext?.Device;

                var slot = new PartyVocalsMicSlot(i, device, inputContext, subEngine,
                    needleObj, needleObj.transform, renderer, materialInstance, pg);

                WireSubEngineEvents(slot);
                _slots.Add(slot);
            }

            // Hide the base class's default needle and particle group —
            // sub-engine slots own their own visuals.
            _needleVisualContainer.SetActive(false);
            _hittingParticleGroup.gameObject.SetActive(false);
        }

        private void WireSubEngineEvents(PartyVocalsMicSlot slot)
        {
            // OnSing drives per-mic needle visibility ("is THIS mic singing?").
            // Target note / hit detection are driven from the band-slot engine in
            // UpdateSlotNeedle, not the sub-engine (single-mic hit detection is too
            // strict on real-world pitch).
            slot.OnSingHandler = singing =>
                slot.LastSingTime = singing ? GameManager.InputTime : (double?) null;

            slot.Engine.OnSing += slot.OnSingHandler;
        }

        protected override void UpdateInputs(double time)
        {
            base.UpdateInputs(time);

            if (_slots.Count == 0) return;

            // Route each mic's collected inputs to its sub-engine. Mic inputs carry
            // raw wallclock timestamps from MicInputContext; BasePlayer.OnGameInput
            // normalizes them to game-relative time before queueing, but the base
            // class's PartyVocals fast-path uses SetMicPitch (time-less) for the
            // band-slot engine, so the conversion never happens for these inputs.
            // Apply the same normalization here, and track the latest adjusted time
            // so Engine.Update is called at-or-after every queued input.
            double subEngineUpdateTime = time;
            foreach (var (i, input) in _lastFrameInputs)
            {
                if (i >= 0 && i < _slots.Count)
                {
                    double adjustedTime = GameManager.GetRelativeInputTime(input.Time) + InputCalibration;
                    if (adjustedTime > subEngineUpdateTime) subEngineUpdateTime = adjustedTime;
                    var copy = new GameInput(adjustedTime, input.Action, input.Integer);
                    _slots[i].Engine.QueueInput(ref copy);
                }
            }

            // Drive each sub-engine's update.
            foreach (var slot in _slots)
            {
                slot.Engine.Update(subEngineUpdateTime);
            }
        }

        protected override void UpdateVisuals(double visualTime)
        {
            base.UpdateVisuals(visualTime);

            if (_slots.Count == 0) return;

            // Match base multi-needle reset (UpdateSingNeedle line 829-836): the
            // VocalsPlayer root transform sits at (-5, 0.2, 0) in the prefab. The
            // single-needle code positions the root each frame, but our per-slot
            // needles are positioned individually as children, so leaving the root
            // at that offset would push every clone off-screen.
            transform.localPosition = Vector3.zero;

            var singTime = GameManager.InputTime;
            foreach (var slot in _slots)
            {
                UpdateSlotNeedle(slot, singTime);
            }
        }

        private void UpdateSlotNeedle(PartyVocalsMicSlot slot, double singTime)
        {
            var needleContainer = slot.NeedleVisualContainer;

            if (!IsInThreshold(singTime, slot.LastSingTime))
            {
                if (needleContainer.activeSelf) needleContainer.SetActive(false);
                slot.HittingParticleGroup.Stop();
                return;
            }

            if (!needleContainer.activeSelf) needleContainer.SetActive(true);

            // Use the BAND-SLOT engine's hit state for trails. Sub-engines run a strict
            // single-mic check that almost never fires OnHit on imperfect pitch, while
            // the band-slot engine's multi-mic assignment is what actually scores and
            // drives _lastTargetNote / _lastHitTime / IsMicOnNote(i). This mirrors the
            // base multi-needle path (VocalsPlayer.cs line 875-878).
            float lastNotePitch = _lastTargetNote?.PitchAtSongTime(GameManager.SongTime) ?? -1f;
            bool hasTarget = _lastTargetNote is not null;
            bool hitTimeFresh = IsInThreshold(singTime, _lastHitTime);
            bool isFreeEngine = Engine is YargFreeVocalsEngine;
            bool micOnNote = isFreeEngine && ((YargFreeVocalsEngine)Engine).IsMicOnNote(slot.Index);
            bool hitting = hasTarget && hitTimeFresh && micOnNote;
            if (slot.Index == 0 && Time.frameCount % 30 == 0)
            {
                YARG.Core.Logging.YargLogger.LogFormatDebug(
                    "PV-trail slot=0 hasTarget={0} hitTimeFresh={1} micOnNote={2} hitting={3} _lastHitTime={4} singTime={5}",
                    hasTarget, hitTimeFresh, micOnNote, hitting, _lastHitTime, singTime);
            }

            const float NEEDLE_POS_LERP = 30f;
            const float NEEDLE_POS_SNAP_MULTIPLIER = 10f;
            const float NEEDLE_ROT_LERP = 25f;

            float lerpRate = NEEDLE_POS_LERP;

            if (!needleContainer.activeSelf)
            {
                needleContainer.SetActive(true);
                lerpRate *= NEEDLE_POS_SNAP_MULTIPLIER;
            }

            if (hitting)
            {
                if (!GameManager.Rewinding) slot.HittingParticleGroup.Play();

                float pitch;
                float targetRotation = 0f;

                if (!_lastTargetNote.IsNonPitched)
                {
                    pitch = lastNotePitch;
                    (float pitchDist, _) = GetPitchDistanceIgnoringOctave(lastNotePitch, slot.Engine.PitchSang);
                    targetRotation = GetNeedleRotation(pitchDist);
                }
                else
                {
                    pitch = slot.Engine.PitchSang + 12f;
                }

                float z = GameManager.VocalTrack.GetPosForPitch(pitch);
                var lerp = Mathf.Lerp(slot.NeedleTransform.localPosition.z, z, Time.deltaTime * lerpRate);
                slot.NeedleTransform.localPosition = new Vector3(0f, 0f, lerp);
                slot.NeedleTransform.rotation = Quaternion.Lerp(slot.NeedleTransform.rotation,
                    Quaternion.Euler(0f, targetRotation + 90f, 0f), Time.deltaTime * NEEDLE_ROT_LERP);
            }
            else
            {
                slot.HittingParticleGroup.Stop();
                float pitch = AnchorPitchToOctave(slot.Engine.PitchSang, lastNotePitch);
                float z = GameManager.VocalTrack.GetPosForPitch(pitch);
                var lerp = Mathf.Lerp(slot.NeedleTransform.localPosition.z, z, Time.deltaTime * lerpRate);
                slot.NeedleTransform.localPosition = new Vector3(0f, 0f, lerp);
                slot.NeedleTransform.rotation = Quaternion.Lerp(slot.NeedleTransform.rotation,
                    Quaternion.Euler(0f, 90f, 0f), Time.deltaTime * NEEDLE_ROT_LERP);
            }

            // Drive the per-slot particle group position.
            var pgPos = slot.HittingParticleGroup.transform.localPosition;
            slot.HittingParticleGroup.transform.localPosition = new Vector3(
                pgPos.x, pgPos.y, slot.NeedleTransform.localPosition.z);
        }

        protected override void FinishDestruction()
        {
            foreach (var slot in _slots)
            {
                if (slot.OnSingHandler != null) slot.Engine.OnSing -= slot.OnSingHandler;

                if (slot.NeedleVisualContainer != null) Destroy(slot.NeedleVisualContainer);
                if (slot.NeedleMaterial != null) Destroy(slot.NeedleMaterial);
                if (slot.HittingParticleGroup != null && slot.HittingParticleGroup.gameObject != null)
                    Destroy(slot.HittingParticleGroup.gameObject);
            }
            _slots.Clear();

            base.FinishDestruction();
        }
    }
}
