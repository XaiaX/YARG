using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Localization;
using YARG.Core.Input;
using YARG.Menu.Navigation;
using YARG.Menu.Dialogs;
using YARG.Menu.Persistent;

namespace YARG.Menu.Settings
{
    // This layout belongs only to Song Manager help, not shared message/error dialogs.
    public sealed class SongSourceHelp : MonoBehaviour
    {
        private MessageDialog _dialog;
        private RectTransform _panel;
        private RectTransform _viewport;
        private RectTransform _footer;
        private RectTransform _close;

        public static void Show()
        {
            var dialog = DialogManager.Instance.ShowMessage(
                Localize.Key("Menu.Settings.Sources.Help"), Localize.Key("Menu.Settings.Sources.HelpBody"));
            dialog.gameObject.AddComponent<SongSourceHelp>().Initialize(dialog);
        }

        private void Initialize(MessageDialog dialog)
        {
            _dialog = dialog;
            _panel = (RectTransform)dialog.Title.transform.parent;
            var body = dialog.Message;
            _viewport = (RectTransform)body.transform.parent;
            _viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = _viewport;
            scroll.content = body.rectTransform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45;
            body.alignment = TextAlignmentOptions.TopLeft;
            body.fontSize = 24;
            body.enableAutoSizing = false;
            body.richText = true;
            body.raycastTarget = true;
            body.overflowMode = TextOverflowModes.Overflow;
            body.rectTransform.anchorMin = new Vector2(0, 1);
            body.rectTransform.anchorMax = Vector2.one;
            body.rectTransform.pivot = new Vector2(0.5f, 1);
            body.rectTransform.anchoredPosition = Vector2.zero;
            body.rectTransform.sizeDelta = Vector2.zero;
            var fitter = body.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            dialog.ClearButtons();
            var close = dialog.AddDialogButton("Menu.Common.Close", new Color(0.18039216f, 0.8509804f, 1f),
                DialogManager.Instance.ClearDialog);
            _close = (RectTransform)close.transform;
            _footer = (RectTransform)_close.parent;
            var layout = _footer.GetComponent<HorizontalLayoutGroup>();
            if (layout != null) layout.enabled = false;
            _close.anchorMin = _close.anchorMax = new Vector2(1, 0.5f);
            _close.pivot = new Vector2(1, 0.5f);
            _close.sizeDelta = new Vector2(170, 40);
            _close.anchoredPosition = Vector2.zero;
            Resize();
            dialog.SelectLast();
            Navigator.Instance.PushSchemeImmediate(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Up", () => Scroll(1)),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Down", () => Scroll(-1)),
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Close", DialogManager.Instance.ClearDialog),
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Close", DialogManager.Instance.ClearDialog),
            }, null));
        }

        private void Scroll(int direction)
        {
            var scroll = _viewport.GetComponent<ScrollRect>();
            float overflow = scroll.content.rect.height - _viewport.rect.height;
            if (overflow > 0)
                scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + direction * 80f / overflow);
        }

        private void OnDisable()
        {
            if (Navigator.Instance != null) Navigator.Instance.PopScheme();
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }

        private void Resize()
        {
            var available = ((RectTransform)_panel.parent).rect.size;
            var width = Mathf.Min(900, available.x * 0.94f);
            var height = Mathf.Min(760, available.y * 0.94f);
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.anchoredPosition = Vector2.zero;
            _panel.sizeDelta = new Vector2(width, height);
            var title = _dialog.Title.rectTransform;
            title.anchorMin = new Vector2(0, 1);
            title.anchorMax = Vector2.one;
            title.pivot = new Vector2(0.5f, 0.5f);
            title.anchoredPosition = new Vector2(0, -32);
            title.sizeDelta = new Vector2(-40, 44);
            _dialog.Title.enableAutoSizing = true;
            _dialog.Title.fontSizeMin = 16;
            _dialog.Title.fontSizeMax = 32;
            Stretch(_viewport, new Vector2(24, 68), new Vector2(-24, -66));
            _footer.anchorMin = Vector2.zero;
            _footer.anchorMax = new Vector2(1, 0);
            _footer.pivot = new Vector2(0.5f, 0.5f);
            _footer.anchoredPosition = new Vector2(0, 32);
            _footer.sizeDelta = new Vector2(-48, 40);
        }

        private void LateUpdate() => Resize();
    }
}
