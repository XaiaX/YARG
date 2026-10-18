using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Menu.HighwayConfiguration
{
    /// <summary>Fits the assignment card to its visible, localized choices in canvas units.</summary>
    public sealed class AssignmentPopupLayout : MonoBehaviour
    {
        [SerializeField] private RectTransform _panel;
        [SerializeField] private RectTransform _canvas;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text[] _labels;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private RectTransform _body;
        [SerializeField] private RectTransform _headerBacking;
        [SerializeField] private float _headerHeight = 80f;
        [SerializeField] private float _footerHeight = 15f;
        private Vector2 _lastCanvasSize;
        private bool _layingOut;

        public void Configure(RectTransform panel, RectTransform canvas, TMP_Text title,
            TMP_Text[] labels, ScrollRect scroll, RectTransform body, RectTransform headerBacking)
        {
            _panel = panel;
            _canvas = canvas;
            _title = title;
            _labels = labels;
            _scroll = scroll;
            _body = body;
            _headerBacking = headerBacking;
        }

        public void SetTitle(string title) => _title.text = title;

        private void LateUpdate()
        {
            if (_canvas != null && _canvas.rect.size != _lastCanvasSize) Refresh(false);
        }

        public void Refresh(bool resetScroll = true)
        {
            if (_layingOut || _panel == null || _canvas == null) return;
            _layingOut = true;
            try
            {
                _lastCanvasSize = _canvas.rect.size;
                // Rows already have 24-unit authored insets; add 24 more on each side.
                float width = _title.GetPreferredValues(_title.text).x + 96f;
                foreach (var label in _labels)
                {
                    if (!label.transform.parent.gameObject.activeSelf) continue;
                    label.enableAutoSizing = false;
                    width = Mathf.Max(width, label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity).x + 96f);
                }
                width = Mathf.Min(width, Mathf.Max(1f, _lastCanvasSize.x - 48f));
                float rowsHeight = 0f;
                foreach (var label in _labels)
                {
                    if (!label.transform.parent.gameObject.activeSelf) continue;
                    float height = Mathf.Max(64f, label.GetPreferredValues(label.text,
                        Mathf.Max(1f, width - 96f), Mathf.Infinity).y + 16f);
                    var row = label.transform.parent.GetComponent<LayoutElement>();
                    row.minHeight = row.preferredHeight = height;
                    label.rectTransform.offsetMin = new Vector2(48f, 8f);
                    label.rectTransform.offsetMax = new Vector2(-48f, -8f);
                    rowsHeight += height;
                }
                float titleHeight = Mathf.Max(80f, _title.GetPreferredValues(_title.text,
                    Mathf.Max(1f, width - 96f), Mathf.Infinity).y + 24f);
                _headerHeight = titleHeight;
                var header = (RectTransform) _title.transform.parent;
                header.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, titleHeight);
                _headerBacking.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, titleHeight);
                float heightLimit = Mathf.Max(1f, _lastCanvasSize.y - 48f);
                float heightWanted = rowsHeight + _headerHeight + _footerHeight;
                _panel.sizeDelta = new Vector2(width, Mathf.Min(heightWanted, heightLimit));
                _scroll.viewport.offsetMin = new Vector2(0f, _footerHeight);
                _scroll.viewport.offsetMax = new Vector2(0f, -_headerHeight);
                _body.offsetMin = _scroll.viewport.offsetMin;
                _body.offsetMax = _scroll.viewport.offsetMax;
                LayoutRebuilder.ForceRebuildLayoutImmediate(_scroll.content);
                _scroll.vertical = heightWanted > heightLimit + 0.5f;
                if (resetScroll || !_scroll.vertical)
                {
                    _scroll.StopMovement();
                    _scroll.verticalNormalizedPosition = 1f;
                    _scroll.content.anchoredPosition = Vector2.zero;
                }
            }
            finally { _layingOut = false; }
            if (!resetScroll && _scroll.vertical)
            {
                foreach (var row in GetComponentsInChildren<AssignmentPopupRow>())
                    if (row.Selected) row.RevealRow();
            }
        }
    }
}
