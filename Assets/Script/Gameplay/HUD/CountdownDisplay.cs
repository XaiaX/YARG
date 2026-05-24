using System.Collections;
using TMPro;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using System;

namespace YARG.Gameplay.HUD
{
    public enum CountdownDisplayMode
    {
        Disabled,
        Measures,
        Seconds
    }

    public class CountdownDisplay : GameplayBehaviour
    {
        private const float FADE_ANIM_LENGTH = 0.5f;
        private const double HIDE_DELAY = 1;

        // RB4-style behavior: switch from the numeric countdown to "GET READY" when
        // the displayed value drops to GET_READY_THRESHOLD or below — measured in
        // whichever unit the current DisplayStyle uses (measures or seconds), so
        // the swap point lines up with what the user sees. Set to <= 0 to keep
        // YARG's classic count-all-the-way-down behavior.
        private const int GET_READY_THRESHOLD = 2;
        private const int HIDE_AT_VALUE = 1;
        private const string GET_READY_TEXT = "GET READY";

        public static CountdownDisplayMode DisplayStyle;

        [SerializeField]
        private Image _backgroundCircle;
        [SerializeField]
        private TextMeshProUGUI _countdownText;
        [SerializeField]
        private Image _progressBar;

        [Space]
        [SerializeField]
        private CanvasGroup _canvasGroup;

        private Coroutine _currentCoroutine;

        private bool _displayActive;
        private string _displayedCountdownText;

        public void UpdateCountdown(double countdownLength, double endTime)
        {
            if (DisplayStyle == CountdownDisplayMode.Disabled)
            {
                return;
            }

            double currentTime = GameManager.SongTime;
            double timeRemaining = endTime - currentTime;
            if (timeRemaining < 0)
            {
                return;
            }

            int displayValue = 0;
            switch (DisplayStyle)
            {
                case CountdownDisplayMode.Seconds:
                    displayValue = (int) Math.Ceiling(timeRemaining);
                    break;
                case CountdownDisplayMode.Measures:
                    var syncTrack = GameManager.Chart.SyncTrack;
                    // This is floored to snap the end time to the start of the measure
                    double endMeasure = Math.Floor(syncTrack.GetMeasurePosition(endTime));
                    double currentMeasure = syncTrack.GetMeasurePosition(currentTime);
                    displayValue = (int) Math.Ceiling(endMeasure - currentMeasure);
                    break;
            }

            // Hide when the displayed value would drop to HIDE_AT_VALUE so the wheel
            // is fully faded out by the time the next note line crosses the highway —
            // matches RB4 (gone by the "1" mark). Wall-clock floor still prevents the
            // wheel from popping in for a sub-fade-length blink on tiny gaps.
            bool shouldDisplay = displayValue > HIDE_AT_VALUE
                && timeRemaining > HIDE_DELAY + FADE_ANIM_LENGTH;

            if (GameManager.IsPractice)
            {
                double sectionStartTime = GameManager.PracticeManager.TimeStart;
                if (currentTime <= sectionStartTime)
                {
                    // Do not show a countdown before the start of a practice section
                    // where all of the notes before that section are removed for practice stats
                    shouldDisplay = false;
                }
            }

            ToggleDisplay(shouldDisplay);

            if (!gameObject.activeSelf)
            {
                return;
            }

            if (GET_READY_THRESHOLD > 0 && displayValue <= GET_READY_THRESHOLD)
            {
                SetCountdownText(GET_READY_TEXT);
            }
            else
            {
                SetCountdownText(displayValue.ToString());
            }

            _progressBar.fillAmount = (float) (timeRemaining / countdownLength);
        }

        public void ForceReset()
        {
            StopCurrentCoroutine();

            _canvasGroup.alpha = 0f;
            gameObject.SetActive(true);
            _displayActive = false;
            _displayedCountdownText = null;
        }

        private void SetCountdownText(string text)
        {
            if (_displayedCountdownText == text)
            {
                return;
            }

            _displayedCountdownText = text;
            _countdownText.SetText(text);
        }

        private void ToggleDisplay(bool isActive)
        {
            if (isActive == _displayActive)
            {
                return;
            }

            _displayActive = isActive;

            StopCurrentCoroutine();

            if (isActive)
            {
                _canvasGroup.alpha = 0f;
                gameObject.SetActive(true);

                _currentCoroutine = StartCoroutine(ShowCoroutine());
            }
            else
            {
                if (_canvasGroup.alpha == 0f)
                {
                    // Do not animate a fade out if this is already invisible
                    gameObject.SetActive(false);
                    return;
                }

                _currentCoroutine = StartCoroutine(HideCoroutine());
            }
        }

        private IEnumerator ShowCoroutine()
        {
            // Fade in
            yield return _canvasGroup
                .DOFade(1f, FADE_ANIM_LENGTH)
                .WaitForCompletion();
        }

        private IEnumerator HideCoroutine()
        {
            // Fade out
            yield return _canvasGroup
                .DOFade(0f, FADE_ANIM_LENGTH)
                .WaitForCompletion();

            gameObject.SetActive(false);
            _currentCoroutine = null;
        }

        private void StopCurrentCoroutine()
        {
            if (_currentCoroutine != null)
            {
                StopCoroutine(_currentCoroutine);
                _currentCoroutine = null;
            }
        }
    }
}