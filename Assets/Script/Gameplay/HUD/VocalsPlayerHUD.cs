using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Engine.Vocals;
using YARG.Core.Game;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;
using YARG.Helpers.UI;
using YARG.Localization;
using YARG.Player;

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

        [Space]
        [SerializeField]
        private TextMeshProUGUI _multiplierText;
        [SerializeField]
        private TextNotifications _textNotifications;

        [SerializeField]
        private PlayerNameDisplay _playerNameDisplay;

        [Header("Party Vocals")]
        [SerializeField] private Image _harm1Fill;
        [SerializeField] private Image _harm2Fill;
        [SerializeField] private Image _harm3Fill;
        [SerializeField] private GameObject _harmFillContainer;

        private float _comboMeterFillTarget;

        private readonly float[] _harmFillTargets = new float[3];

        private Coroutine _hudCoroutine;

        private bool                             _shouldPulse;
        private bool                             _hudShowing = true;
        private TextMeshProUGUI[] _textCache;

        public void Initialize(EnginePreset enginePreset)
        {
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

            var harmFills = new[] { _harm1Fill, _harm2Fill, _harm3Fill };
            for (int i = 0; i < harmFills.Length; i++)
            {
                if (harmFills[i] != null && i < VocalTrack.Colors.Length)
                    harmFills[i].color = VocalTrack.Colors[i];
            }
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

            // Update harmony fills
            var harmFills = new[] { _harm1Fill, _harm2Fill, _harm3Fill };
            for (int i = 0; i < harmFills.Length; i++)
            {
                if (harmFills[i] == null) continue;
                harmFills[i].fillAmount = Mathf.Lerp(harmFills[i].fillAmount,
                    _harmFillTargets[i], Time.deltaTime * 12f);
            }
        }

        public void UpdateInfo(float phrasePercent, int multiplier,
            float starPowerPercent, bool isStarPowerActive)
        {
            _comboMeterFillTarget = phrasePercent;

            _multiplierText.enabled = false;
            if (multiplier > 1)
            {
                _multiplierText = _textCache[multiplier - 2];
                _multiplierText.enabled = true;
            }

            _starPowerFill.fillAmount = starPowerPercent;
            _starPowerPulse.fillAmount = starPowerPercent;

            _shouldPulse = isStarPowerActive || starPowerPercent >= 0.5;
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
            if (!Settings.SettingsManager.Settings.DisableTextNotifications.Value)
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
            System.Func<int, bool> partHasContent = null)
        {
            if (_harmFillContainer == null) return;

            _harmFillContainer.SetActive(true);
            var fills = new[] { _harm1Fill, _harm2Fill, _harm3Fill };
            double scale = awesomeThreshold > 0 ? 1.0 / awesomeThreshold : 1.0;

            for (int i = 0; i < fills.Length; i++)
            {
                if (fills[i] == null) continue;
                bool show = i < meters.Count && (partHasContent == null || partHasContent(i));
                fills[i].gameObject.SetActive(show);
                _harmFillTargets[i] = show ? (float) System.Math.Min(1.0, meters[i] * scale) : 0f;
            }
        }

        public void HideHarmFill()
        {
            if (_harmFillContainer != null)
                _harmFillContainer.SetActive(false);
        }

        public void ShowPartyVocalsGrade(PhraseGrade grade)
        {
            string text = grade switch
            {
                PhraseGrade.Awesome => "AWESOME!",
                PhraseGrade.DoubleAwesome => "DOUBLE AWESOME!",
                PhraseGrade.TripleAwesome => "TRIPLE AWESOME!",
                _ => null,
            };

            if (text != null)
            {
                // Reuse the existing notification display
                _textNotifications.ShowVocalPhraseResult(text, 0);
            }
        }
    }
}