using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Gameplay.Vocals;
using YARG.Core.Game;
using YARG.Helpers.Extensions;
using YARG.Helpers.UI;
using YARG.Localization;
using YARG.Playback;
using YARG.Player;
using YARG.Settings;

namespace YARG.Gameplay.HUD
{
    public class VocalsPlayerHUD : GameplayBehaviour
    {
        [SerializeField]
        private Image _comboMeterFill;
        [SerializeField]
        private Image _starPowerFill;
        [SerializeField]
        private Image _starPowerPulse;
        [SerializeField] private Image _harm1Fill;
        [SerializeField] private Image _harm2Fill;
        [SerializeField] private Image _harm3Fill;
        [SerializeField] private Image _harm1Background;
        [SerializeField] private Image _harm2Background;
        [SerializeField] private Image _harm3Background;
        [SerializeField] private Image _harm1Rim;
        [SerializeField] private Image _harm2Rim;
        [SerializeField] private Image _harm3Rim;
        [SerializeField] private GameObject _harmFillContainer;
        private readonly Image[] _harmFills = new Image[3];
        private readonly Image[] _harmBackgrounds = new Image[3];
        private readonly Image[] _harmRims = new Image[3];
        private readonly float[] _harmFillTargets = new float[3];
        private readonly float[] _harmFillAlphaTargets = new float[3];
        private readonly Color[] _harmColorTargets = new Color[3];
        private readonly Color[] _harmBackgroundColorTargets = new Color[3];
        private readonly Color[] _harmBackgroundBaseColors = new Color[3];
        private readonly Color[] _harmRimColorTargets = new Color[3];
        private readonly float[] _harmRimAlphaTargets = new float[3];
        private readonly bool[] _harmPartPresent = new bool[3];
        private readonly bool[] _harmPartAvailable = new bool[3];
        private readonly bool[] _harmPartCountIn = new bool[3];
        private readonly long[] _harmCountInTargetTicks = { -1, -1, -1 };
        private bool _harmImagesCached;
        private bool _partyHarmonyVisuals;

        private static readonly Color PARTY_FC_RIM_COLOR =
            new(1f, 0.85490196f, 0.34901961f, 1f);
        private const float HARM_RIM_FADE_SPEED = 8f;
        private const float HARM_VISUAL_LERP = 12f;
        private const float HARM_COUNT_IN_RIM_FINAL_FRACTION = 0.25f;

        [Space]
        [SerializeField]
        private TextMeshProUGUI _multiplierText;
        [SerializeField]
        private GameObject _multiplierTextContainer; // Required as the different multipliers are not the same object.
        [SerializeField]
        private TextNotifications _textNotifications;
        [SerializeField]
        private PlayerNameDisplay _playerNameDisplay;
        [SerializeField]
        private Image _multiplierRim;
        [SerializeField]
        private VocalSunburstEffects _sunburstEffects;
        [SerializeField]
        private Image _fcRing;
        [Header("Combo Rim Images")]
        [SerializeField]
        private Image _grooveRim;
        [SerializeField]
        private Image _starPowerRim;

        private Sequence _multiplierIncreaseSequence;
        private bool     _isSp;
        private bool     _isFc = true;
        private int      _multiplier = 1;

        private float _comboMeterFillTarget;

        private Coroutine _hudCoroutine;

        private bool                             _shouldPulse;
        private bool                             _hudShowing = true;
        private TextMeshProUGUI[] _textCache;

        public void Initialize(EnginePreset enginePreset)
        {
            GameManager.BeatEventHandler.Visual.Subscribe(_sunburstEffects.PulseSunburst, BeatEventType.StrongBeat);

            _multiplierIncreaseSequence = DOTween.Sequence(_multiplierTextContainer)
                .Append(_multiplierTextContainer.transform.DOScale(1.75f, 0.15f))
                .Join(_multiplierTextContainer.transform.DOLocalMoveX(-30f, 0.15f))
                .Append(_multiplierTextContainer.transform.DOScale(1f, 0.15f))
                .Join(_multiplierTextContainer.transform.DOLocalMoveX(0f, 0.15f))
                .SetAutoKill(false);
            _sunburstEffects.SetSunburstEffects(false, false, 1);
            _textCache = MultiplierTextHelper.CreateMultiplierTextCache(EnginePreset.DEFAULT_MAX_MULTIPLIER, _multiplierText, GameManager.Players.Count > 1);

            if (enginePreset == EnginePreset.Default)
            {
                // Don't change combo meter fill color if it's the default
            }
            else if (enginePreset == EnginePreset.Casual)
            {
                _comboMeterFill.color = new Color(0.9f, 0.3f, 0.9f);
            }
            else if (enginePreset == EnginePreset.Precision)
            {
                _comboMeterFill.color = new Color(1.0f, 0.9f, 0.0f);
            }
            else
            {
                // Otherwise, it must be a custom preset
                _comboMeterFill.color = new Color(1.0f, 0.25f, 0.25f);
            }

            _starPowerFill.fillAmount = 0f;

            CacheHarmonyImages();
        }

        private void Update()
        {
            // Update combo meter
            if (_comboMeterFillTarget == 0f)
            {
                // Go to zero instantly
                _comboMeterFill.fillAmount = 0f;
            }
            else
            {
                _comboMeterFill.fillAmount = Mathf.Lerp(_comboMeterFill.fillAmount,
                    _comboMeterFillTarget, Time.deltaTime * 12f);
            }

            // Update pulse
            if (_shouldPulse)
            {
                float pulse = 1 - (float) GameManager.BeatEventHandler.Visual.StrongBeat.CurrentPercentage;
                _starPowerPulse.color = Color.white.WithAlpha(pulse);
            }
            else
            {
                _starPowerPulse.color = Color.white.WithAlpha(0);
            }

            if (_partyHarmonyVisuals && _harmFillContainer != null && _harmFillContainer.activeSelf)
            {
                UpdateHarmonyMeter(0);
                UpdateHarmonyMeter(1);
                UpdateHarmonyMeter(2);
            }

            if (!_isFc)
            {
                var spRimAlpha = Mathf.Clamp01(_starPowerRim.color.a + (_isSp ? 1 : -1) * 3f * Time.deltaTime);
                var grooveRimAlpha = Mathf.Clamp01(_grooveRim.color.a + (!_isSp && _multiplier == 4 ? 1 : -1) * 3f * Time.deltaTime);

                _grooveRim.color = Color.white.WithAlpha(grooveRimAlpha);
                _starPowerRim.color = Color.white.WithAlpha(spRimAlpha);
            }
        }

        private void UpdateHarmonyMeter(int index)
        {
            var fill = _harmFills[index];
            var background = _harmBackgrounds[index];
            var rim = _harmRims[index];
            float lerp = Mathf.Clamp01(Time.deltaTime * HARM_VISUAL_LERP);

            if (fill != null)
            {
                float target = _harmFillTargets[index];
                if (_harmPartCountIn[index])
                    fill.fillAmount = target;
                else
                    fill.fillAmount = Mathf.Lerp(fill.fillAmount, target, lerp);

                // Count-in color is derived from the continuous denominator-pulse strength;
                // apply it directly so frame-rate smoothing cannot distort the scheduled phase.
                var fillColor = _harmPartCountIn[index]
                    ? _harmColorTargets[index]
                    : Color.Lerp(fill.color, _harmColorTargets[index], lerp);
                fillColor.a = Mathf.Lerp(fill.color.a, _harmFillAlphaTargets[index], lerp);
                fill.color = fillColor;
            }

            if (background != null)
            {
                // Apply black/current or authored gray/inactive without fading alpha.
                background.color = _harmBackgroundColorTargets[index];
            }

            if (rim != null)
            {
                var rimColor = _harmRimColorTargets[index];
                if (!_harmPartAvailable[index])
                {
                    // An absent part is hidden immediately, including after reset or
                    // a rewind to a chart with fewer parts.
                    rimColor.a = 0f;
                }
                else if (_harmPartPresent[index])
                {
                    // The active phrase rim is fully visible. Count-in uses the eased
                    // path below so entering the next phrase does not pop the rim.
                    rimColor.a = 1f;
                }
                else
                {
                    float rimLerp = Mathf.Clamp01(Time.deltaTime * HARM_RIM_FADE_SPEED);
                    rimColor.a = Mathf.Lerp(rim.color.a, _harmRimAlphaTargets[index], rimLerp);
                }
                rim.color = rimColor;
            }
        }

        public void UpdateInfo(float phrasePercent, int multiplier,
            float starPowerPercent, bool isStarPowerActive)
        {
            _comboMeterFillTarget = phrasePercent;

            _starPowerFill.fillAmount = starPowerPercent;
            _starPowerPulse.fillAmount = starPowerPercent;

            _shouldPulse = isStarPowerActive || starPowerPercent >= 0.5;


            _sunburstEffects.SetSunburstEffects(multiplier == 4 && !isStarPowerActive, isStarPowerActive, multiplier);

            if (multiplier == _multiplier && isStarPowerActive == _isSp)
            {
                return;
            }
            _multiplierText.enabled = false;

            if (multiplier > 1)
            {
                _multiplierText = _textCache[multiplier - 2];
                _multiplierText.enabled = true;
                if (isStarPowerActive == _isSp && multiplier > _multiplier)
                {
                    _multiplierIncreaseSequence.Restart();
                }
            }
            _multiplier = multiplier;
            _isSp = isStarPowerActive;
        }

        public static string GetVocalPerformanceText(double hitPercent)
        {
            string performanceKey = hitPercent switch
            {
                >= 1f => "Awesome",
                >= 0.8f => "Strong",
                >= 0.7f => "Good",
                >= 0.6f => "Okay",
                >= 0.1f => "Messy",
                _ => "Awful"
            };

            return Localize.Key("Gameplay.Vocals.Performance", performanceKey);
        }

        public void SetHUDShowing(bool show)
        {
            if (_hudShowing == show)
            {
                return;
            }

            _hudShowing = show;

            if (_hudCoroutine != null)
            {
                StopCoroutine(_hudCoroutine);
            }

            _hudCoroutine = StartCoroutine(ShowHUD(_hudShowing));
        }

        private IEnumerator ShowHUD(bool show)
        {
            if (show)
            {
                yield return transform
                    .DORotate(new Vector3(0f, 0f, 0f), 0.25f)
                    .WaitForCompletion();
            }
            else
            {
                yield return transform
                    .DORotate(new Vector3(90f, 0f, 0f), 0.25f)
                    .WaitForCompletion();
            }

            _hudCoroutine = null;
        }

        public void ShowPlayerName(YargPlayer player, int needleId)
        {
            _playerNameDisplay.ShowPlayer(player, needleId);
        }

        public void ShowPhraseHit(double hitPercent, int combo)
        {
            if (!SettingsManager.Settings.DisableTextNotifications.Value)
            {
                _textNotifications.UpdateNoteStreak(combo);
            }
            var resultText = GetVocalPerformanceText(hitPercent);
            _textNotifications.ShowVocalPhraseResult(resultText, combo);
        }

        public void ShowNotification(TextNotificationType notificationType)
        {
            _textNotifications.ShowNotification(notificationType);
        }

        public void UpdateHarmFill(IReadOnlyList<double> meters, double awesomeThreshold,
            System.Func<int, bool> partInCurrentPhrase = null,
            System.Func<int, bool> partInNextPhrase = null, double phraseProgress = 0.0,
            double phraseDurationSeconds = 0.0)
        {
            if (_harmFillContainer == null) return;
            _harmFillContainer.SetActive(true);
            _partyHarmonyVisuals = false;
            double scale = awesomeThreshold > 0 ? 1.0 / awesomeThreshold : 1.0;
            for (int i = 0; i < 3; i++)
            {
                bool present = partInCurrentPhrase != null && partInCurrentPhrase(i);
                float meter = present && i < meters.Count
                    ? (float) System.Math.Min(1.0, meters[i] * scale)
                    : 0f;
                SetHarmonyMeterTarget(i, meter, present, present, false, false, -1, false);
            }
        }

        /// <summary>
        /// Updates the Party Vocals per-part meters from the coordinator's live state.
        /// This is intentionally polled by the owning player during its visual update:
        /// coordinator meters are speculative and can change several times per frame,
        /// so a phrase event would be too coarse for the HUD.
        /// </summary>
        public void UpdatePartyVocalsMeters(PartyVocalsCoordinatorEngine coordinator, bool globalFc)
        {
            if (_harmFillContainer == null || coordinator == null)
                return;

            CacheHarmonyImages();
            _partyHarmonyVisuals = true;

            // Lead-only Party Vocals: the combo meter already represents the sole
            // lead-vocal performance, so hide the entire harmony container, HARM1
            // included. This keys on the coordinator's resolved per-part availability
            // (never on Party Vocals selection alone): duet/trio availability keeps the
            // container visible with its exact current placement. PartHasContent is
            // bounds-safe, so a resolved track with fewer than three parts — and empty
            // or malformed harmony upgrade lanes — evaluates as no harmony content.
            bool leadOnly = coordinator.PartHasContent(0)
                && !coordinator.PartHasContent(1)
                && !coordinator.PartHasContent(2);
            if (leadOnly)
            {
                if (_harmFillContainer.activeSelf)
                    _harmFillContainer.SetActive(false);
                return;
            }

            _harmFillContainer.SetActive(true);
            double scale = coordinator.AwesomeThreshold > 0
                ? 1.0 / coordinator.AwesomeThreshold
                : 1.0;
            for (int i = 0; i < 3; i++)
            {
                // PartHasContent is based on every phrase in the selected effective track,
                // not just the coordinator's current/next phrase. This keeps a charted part
                // visible during gaps while allowing genuinely absent HARM parts to hide.
                bool songPresent = coordinator.PartHasContent(i);
                var countInState = coordinator.GetCountInState(i);
                // The canonical phrase supplies active-meter presentation. Count-in is the one
                // permitted exception: the Core schedule exposes it only before the first actual
                // onset after song start or a lane-inactive phrase. Once onset is reached, that
                // schedule ends and silent child-run gaps cannot reactivate it.
                bool canonicalCurrent = coordinator.PartInCurrentMasterPhraseWithContent(i);
                bool pendingCountIn = countInState.IsPending;
                bool suppressCountIn = SettingsManager.Settings.DisablePartyVocalsCountIns.Value;
                bool countIn = pendingCountIn && !suppressCountIn;
                // A different HARM lane can advance the canonical phrase before this lane's
                // first note. The raw pending state, not its optional visual treatment, keeps
                // the lane inactive until the Core schedule ends exactly at the actual onset.
                bool current = canonicalCurrent && !pendingCountIn;
                float meter = countIn ? (float) countInState.FillAmount
                    : current && i < coordinator.CanonicalMeters.Count
                        ? Mathf.Clamp01((float) (coordinator.CanonicalMeters[i] * scale))
                        : 0f;
                SetHarmonyMeterTarget(i, meter, current, songPresent, countIn, pendingCountIn && suppressCountIn,
                    countInState.TargetTick, globalFc, countInState.PulseStrength, countInState.WindowBeats,
                    countInState.FillAmount);
            }
        }

        private void SetHarmonyMeterTarget(int index, float target, bool current,
            bool songPresent, bool countIn, bool suppressedPendingCountIn, long targetTick, bool globalFc,
            double pulseStrength = 0.0, int windowBeats = 32, double countInFillAmount = 0.0)
        {
            CacheHarmonyImages();
            var laneColor = YARG.Gameplay.Player.VocalTrack.Colors[index];
            bool enteredCountIn = countIn && (_harmCountInTargetTicks[index] != targetTick);

            _harmFillTargets[index] = Mathf.Clamp01(target);
            _harmFillAlphaTargets[index] = songPresent && (current || countIn) ? 1f : 0f;
            _harmColorTargets[index] = !songPresent
                ? laneColor.WithAlpha(0f)
                : countIn && !current ? GetCountInColor(laneColor, (float)pulseStrength) : laneColor;

            // Only an active playable meter uses black. Count-in retains the prefab-authored
            // medium gray so its draining fill reads as an upcoming-part indicator instead.
            // Only genuinely empty parts are transparent.
            _harmBackgroundColorTargets[index] = current
                ? Color.black
                : _harmBackgroundBaseColors[index];
            _harmBackgroundColorTargets[index].a = songPresent ? 1f : 0f;

            // Harmony rims are a player-level FC indicator, never a lane indicator. Keep the
            // rim hidden through most of a count-in, then fade it in over the final quarter so
            // the pending indicator transitions toward the active-meter presentation at onset.
            _harmRimColorTargets[index] = globalFc ? PARTY_FC_RIM_COLOR : Color.white;
            float countInRimAlpha = countIn
                ? Mathf.InverseLerp(HARM_COUNT_IN_RIM_FINAL_FRACTION, 0f, (float) countInFillAmount)
                : 0f;
            _harmRimAlphaTargets[index] = songPresent
                ? current ? 1f : countInRimAlpha
                : 0f;
            _harmPartPresent[index] = current;
            _harmPartAvailable[index] = songPresent;
            bool exitedCountIn = _harmPartCountIn[index] && !countIn;
            _harmPartCountIn[index] = countIn;
            _harmCountInTargetTicks[index] = countIn ? targetTick : -1;
            if ((exitedCountIn || suppressedPendingCountIn) && _harmFills[index] != null)
            {
                _harmFills[index].fillAmount = 0f;
                _harmFills[index].color = laneColor.WithAlpha(0f);
            }
            if (suppressedPendingCountIn && _harmRims[index] != null)
                _harmRims[index].color = _harmRimColorTargets[index].WithAlpha(0f);

            // Fill resets only when the actual child-note re-entry target changes; master
            // phrase boundaries and empty phrases cannot restart an in-progress countdown.
            if (enteredCountIn && _harmFills[index] != null)
                _harmFills[index].fillAmount = 1f;

            // An absent part must be hidden as a whole immediately. In particular, do not
            // let the previous phrase's fill/background/rim interpolation flash during a
            // reset, rewind, or transition to a chart with fewer HARM parts.
            if (!songPresent)
            {
                if (_harmFills[index] != null)
                {
                    _harmFills[index].fillAmount = 0f;
                    _harmFills[index].color = laneColor.WithAlpha(0f);
                }
                if (_harmBackgrounds[index] != null)
                    _harmBackgrounds[index].color = _harmBackgroundColorTargets[index];
                if (_harmRims[index] != null)
                    _harmRims[index].color = Color.white.WithAlpha(0f);
            }
        }

        private Color GetCountInColor(Color laneColor, float pulseStrength)
        {
            Color.RGBToHSV(laneColor, out float hue, out float saturation, out float value);
            // Each denominator pulse is bright/desaturated, then smoothly fades back to
            // the dimmer authored-saturation resting color over its cell.
            pulseStrength = Mathf.Clamp01(pulseStrength);
            float pulseSaturation = Mathf.Lerp(saturation, saturation * 0.22f, pulseStrength);
            float pulseValue = Mathf.Lerp(Mathf.Max(0.15f, value - 0.18f), Mathf.Min(1f, value + 0.18f), pulseStrength);
            return Color.HSVToRGB(hue, pulseSaturation, pulseValue);
        }

        private void CacheHarmonyImages()
        {
            if (_harmImagesCached) return;
            _harmFills[0] = _harm1Fill;
            _harmFills[1] = _harm2Fill;
            _harmFills[2] = _harm3Fill;
            _harmBackgrounds[0] = _harm1Background;
            _harmBackgrounds[1] = _harm2Background;
            _harmBackgrounds[2] = _harm3Background;
            _harmRims[0] = _harm1Rim;
            _harmRims[1] = _harm2Rim;
            _harmRims[2] = _harm3Rim;

            for (int i = 0; i < _harmBackgrounds.Length; i++)
            {
                _harmBackgroundBaseColors[i] = _harmBackgrounds[i] != null
                    ? _harmBackgrounds[i].color
                    : Color.white;
            }
            _harmImagesCached = true;
        }

        public void HideHarmFill()
        {
            _partyHarmonyVisuals = false;
            ResetHarmonyVisuals();
            // The HUD hierarchy can be destroyed before its owning player.
            // Unity's null comparison also detects destroyed native objects.
            if (_harmFillContainer != null)
                _harmFillContainer.SetActive(false);
        }

        private void ResetHarmonyVisuals()
        {
            CacheHarmonyImages();
            for (int i = 0; i < 3; i++)
            {
                _harmFillTargets[i] = 0f;
                _harmFillAlphaTargets[i] = 0f;
                _harmColorTargets[i] = YARG.Gameplay.Player.VocalTrack.Colors[i].WithAlpha(0f);
                _harmBackgroundColorTargets[i] = _harmBackgroundBaseColors[i].WithAlpha(0f);
                _harmRimColorTargets[i] = Color.white;
                _harmRimAlphaTargets[i] = 0f;
                _harmPartPresent[i] = false;
                _harmPartAvailable[i] = false;
                _harmPartCountIn[i] = false;
                _harmCountInTargetTicks[i] = -1;
                if (_harmFills[i] != null)
                {
                    _harmFills[i].fillAmount = 0f;
                    _harmFills[i].color = _harmColorTargets[i];
                }
                if (_harmBackgrounds[i] != null)
                    _harmBackgrounds[i].color = _harmBackgroundColorTargets[i];
                if (_harmRims[i] != null)
                {
                    var color = _harmRimColorTargets[i];
                    color.a = 0f;
                    _harmRims[i].color = color;
                }
            }
        }

        public void ShowPartyVocalsGrade(PhraseGrade grade)
        {
            string text = grade switch { PhraseGrade.Awesome => "AWESOME!", PhraseGrade.DoubleAwesome => "DOUBLE AWESOME!", PhraseGrade.TripleAwesome => "TRIPLE AWESOME!", _ => null };
            if (text != null) _textNotifications.ShowVocalPhraseResult(text, 0);
        }

        public void SetFullCombo(bool isFullCombo)
        {
            _isFc = isFullCombo;
            if (isFullCombo)
            {
                _fcRing.gameObject.SetActive(true);
            }
            else
            {
                // Instantly show the SP rim if in star power
                if (_isSp)
                {
                    _starPowerRim.color = Color.white.WithAlpha(1f);
                }
                _fcRing.gameObject.SetActive(false);
            }

            if (!_partyHarmonyVisuals)
            {
                return;
            }

            for (int i = 0; i < 3; i++)
            {
                if (_harmRims[i] == null)
                {
                    continue;
                }

                // Rim color is global player state, never the HARM lane color. Preserve
                // the currently eased alpha; SetHarmonyMeterTarget controls whether this
                // part should be visible for the current/next phrase.
                var color = isFullCombo ? PARTY_FC_RIM_COLOR : Color.white;
                color.a = _harmRims[i].color.a;
                _harmRims[i].color = color;
                _harmRimColorTargets[i] = color;
            }
        }
    }
}