using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Menu.Settings
{
    public sealed class SongSourceTextLayout : MonoBehaviour
    {
        private TextMeshProUGUI _label;
        private LayoutElement _layout;

        public void Initialize(TextMeshProUGUI label, LayoutElement layout)
        {
            _label = label;
            _layout = layout;
            _label.textWrappingMode = TextWrappingModes.Normal;
            _label.overflowMode = TextOverflowModes.Overflow;
            // Resize the prefab's fixed-height root as well as its layout preference so
            // a short status does not retain the original 250-pixel text block.
            UpdateHeight();
        }

        private void LateUpdate() => UpdateHeight();

        private void UpdateHeight()
        {
            if (_label == null || _layout == null) return;
            float width = _label.rectTransform.rect.width;
            float height = string.IsNullOrEmpty(_label.text) ? 0
                : Mathf.Max(32, _label.GetPreferredValues(_label.text, Mathf.Max(1, width), 0).y + 12);
            _layout.minHeight = height;
            _layout.preferredHeight = height;
            var wrapper = _label.rectTransform.parent as RectTransform;
            if (wrapper != null)
                wrapper.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
    }
}
