// pattern: Imperative Shell
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YARG.Core.Game;
using YARG.Localization;
using YARG.Menu.Navigation;

namespace YARG.Menu.HighwayConfiguration
{
    public sealed partial class EliteDrumsLayoutMenu
    {
        private const float FRET_WEIGHT_STEP = .05f;
        [SerializeField] private RectTransform _sizingButtonPrefab;
        [SerializeField] private Slider _sizingSliderPrefab;
        private Button _sizingButton;
        private GameObject _sizingPopup;
        private NavigationGroup _sizingNavigation;
        private float _sizingPreviewAt = float.PositiveInfinity;
        private bool SizingOpen => _sizingPopup != null && _sizingPopup.activeSelf;

        private void InitializeSizingButton()
        {
            // The right side of the management row sits immediately above the highway preview.
            var anchor = ((RectTransform)_save.transform.parent).anchorMin;
            var rect = SizingRect("Fret Widths", transform, new Vector2(.69f, anchor.y), new Vector2(.96f, anchor.y));
            rect.sizeDelta = new Vector2(0f, 50f);
            _sizingButton = SizingButton(rect, Localize.Key("Menu.EliteLayout.FretWidths"));
            Register(_navigation, _sizingButton, OpenSizing);
        }

        private void OpenSizing()
        {
            if (!CanTransformLayout || !BackgroundInputAllowed) return;
            var backdrop = SizingRect("Fret widths popup", transform, Vector2.zero, Vector2.one);
            _sizingPopup = backdrop.gameObject;
            _sizingNavigation = _sizingPopup.AddComponent<NavigationGroup>();
            _sizingPopup.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            _backgroundWasInteractable = _backgroundInputGroup.interactable;
            _backgroundInputGroup.interactable = false;
            var shade = _sizingPopup.AddComponent<Image>();
            shade.color = _popupBackdropButton.image.color;
            var dismiss = _sizingPopup.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(() => CloseSizing());

            var panel = SizingRect("Fret widths panel", backdrop, new Vector2(.035f, .10f), new Vector2(.625f, .89f));
            panel.gameObject.AddComponent<Image>().color = Color.clear;
            // Block clicks on the panel from bubbling up to the backdrop.
            var blocker = panel.gameObject.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;
            _popupLayout.CopyFrameTo(panel, Localize.Key("Menu.EliteLayout.FretWidths"));
            var sliders = new Slider[EliteDrumsLayoutPreset.SLOT_COUNT];
            var values = new TMP_Text[sliders.Length];
            for (int column = 0; column < EliteDrumsLayoutPreset.SLOT_COUNT; column++)
            {
                int savedColumn = column;
                float y = .84f - .058f * column;
                SizingLabel(panel, string.Format(Localize.Key("Menu.EliteLayout.FretWeight"), column + 1),
                    .055f, y, .24f, y + .056f);
                var valueLabel = SizingLabel(panel, "", .84f, y, .98f, y + .056f);
                var slider = Instantiate(_sizingSliderPrefab, panel, false);
                sliders[column] = slider;
                values[column] = valueLabel;
                var sliderObject = slider.gameObject;
                sliderObject.name = $"Fret {column + 1} width";
                var rect = (RectTransform)sliderObject.transform;
                PositionRect(rect, new Vector2(.25f, y + .028f), new Vector2(.81f, y + .028f));
                rect.sizeDelta = new Vector2(0f, 10f);
                valueLabel.alignment = TextAlignmentOptions.MidlineRight;
                slider.minValue = .5f;
                slider.maxValue = 2f;
                float original = _draft.GetFretWeight(column);
                slider.SetValueWithoutNotify(original);
                valueLabel.text = $"{original:0.00}×";
                slider.onValueChanged.AddListener(value =>
                {
                    float weight = Mathf.Round(value / FRET_WEIGHT_STEP) * FRET_WEIGHT_STEP;
                    slider.SetValueWithoutNotify(weight);
                    _draft.FretWeights[savedColumn] = weight;
                    valueLabel.text = $"{weight:0.00}×";
                    SizingChanged();
                });
                _sizingNavigation.AddNavigatable(RuntimeNavigatable.AttachTextHighlight(sliderObject, null, valueLabel));
            }

            AddSizingSwitch(panel, "GrowHighway", .225f, () => _draft.GrowHighway, value => _draft.GrowHighway = value);
            AddSizingSwitch(panel, "FitGems", .148f, () => _draft.FitGemsToFret, value => _draft.FitGemsToFret = value);
            var resetRect = SizingRect("Reset Widths", panel, new Vector2(.055f, .035f), new Vector2(.31f, .115f));
            var reset = SizingButton(resetRect, Localize.Key("Menu.EliteLayout.ResetWidths"));
            void ResetWidths()
            {
                for (int i = 0; i < sliders.Length; i++)
                {
                    _draft.FretWeights[i] = 1f;
                    sliders[i].SetValueWithoutNotify(1f);
                    values[i].text = $"{1f:0.00}×";
                }
                SizingChanged();
            }
            reset.onClick.AddListener(ResetWidths);
            _sizingNavigation.AddNavigatable(RuntimeNavigatable.Attach(reset.gameObject, ResetWidths));
            var closeRect = SizingRect("Close", panel, new Vector2(.68f, .035f), new Vector2(.945f, .115f));
            var close = SizingButton(closeRect, Localize.Key("Menu.Common.Close"));
            close.onClick.AddListener(() => CloseSizing());
            _sizingNavigation.AddNavigatable(RuntimeNavigatable.Attach(close.gameObject, () => CloseSizing()));
            EventSystem.current?.SetSelectedGameObject(null);
            _sizingNavigation.PushNavGroupToStack();
            _sizingNavigation.SelectFirst();
        }

        private void SizingChanged()
        {
            IsDirty = _newCopy || !Equivalent(_original, _draft);
            // Reassembling the highway for every pointer sample stalls a slider drag.
            _sizingPreviewAt = Time.unscaledTime + .15f;
        }

        private void AddSizingSwitch(RectTransform panel, string key, float y, Func<bool> read, Action<bool> write)
        {
            SizingLabel(panel, Localize.Key("Menu.EliteLayout." + key), .055f, y, .69f, y + .064f);
            var toggle = Instantiate(_leftyFlipToggle, panel, false);
            toggle.name = key;
            toggle.interactable = true;
            var rect = (RectTransform)toggle.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.945f, y + .032f);
            rect.anchoredPosition = Vector2.zero;
            toggle.SetIsOnWithoutNotify(read());
            toggle.onValueChanged.AddListener(value =>
            {
                write(value);
                SizingChanged();
            });
            var nav = toggle.GetComponent<RuntimeNavigatable>();
            var focus = toggle.transform.Find("Lefty Flip Focus").gameObject;
            focus.SetActive(false);
            nav.SelectionVisual = focus.SetActive;
            nav.ConfirmCallback = () => toggle.isOn = !toggle.isOn;
            _sizingNavigation.AddNavigatable(nav);
        }

        private void CloseSizing(bool refresh = true)
        {
            if (!SizingOpen) return;
            NavigationGroup.RemoveFromNavigationStack(_sizingNavigation);
            _sizingPopup.SetActive(false);
            Destroy(_sizingPopup);
            _sizingPopup = null;
            _sizingNavigation = null;
            _backgroundInputGroup.interactable = _backgroundWasInteractable;
            _popupDismissedFrame = Time.frameCount;
            bool pendingPreview = !float.IsPositiveInfinity(_sizingPreviewAt);
            _sizingPreviewAt = float.PositiveInfinity;
            if (!refresh) return;
            if (pendingPreview) Render();
            _sizingButton.GetComponent<RuntimeNavigatable>().SetSelected(true, SelectionOrigin.Programmatically);
            EventSystem.current?.SetSelectedGameObject(_sizingButton.gameObject);
        }

        private Button SizingButton(RectTransform rect, string caption)
        {
            var control = Instantiate(_sizingButtonPrefab, rect, false);
            PositionRect(control, Vector2.zero, Vector2.one);
            var button = control.GetComponentInChildren<Button>();
            SetButtonLabel(button, caption);
            return button;
        }

        private TMP_Text SizingLabel(RectTransform parent, string text, float x0, float y0, float x1, float y1)
        {
            var rect = SizingRect("Label", parent, new Vector2(x0, y0), new Vector2(x1, y1));
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = _status.font;
            label.fontSize = 26f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        private static RectTransform SizingRect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            PositionRect(rect, min, max);
            return rect;
        }

        private static void PositionRect(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
