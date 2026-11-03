// pattern: Imperative Shell
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
        [SerializeField] private RectTransform _context;
        [SerializeField] private TMP_Text _description;
        [SerializeField] private TMP_Text _markerLegend;
        private const float MIN_CHOICE_WIDTH = 500f;
        private const float CONTEXT_WIDTH = 420f;
        private const float COLUMN_GAP = 24f;
        [SerializeField] private float _headerHeight = 80f;
        [SerializeField] private float _footerHeight = 15f;
        private Vector2 _lastCanvasSize;
        private bool _layingOut;

        public void Configure(RectTransform panel, RectTransform canvas, TMP_Text title,
            TMP_Text[] labels, ScrollRect scroll, RectTransform body, RectTransform headerBacking,
            RectTransform context, TMP_Text description, TMP_Text markerLegend)
        {
            _panel = panel;
            _canvas = canvas;
            _title = title;
            _labels = labels;
            _scroll = scroll;
            _body = body;
            _headerBacking = headerBacking;
            _context = context;
            _description = description;
            _markerLegend = markerLegend;
        }

        public void SetTitle(string title) => _title.text = title;

        public void SetHelp(string description, string markerLegend)
        {
            if (_description != null)
            {
                _description.text = description;
                _description.enableAutoSizing = true;
                _description.fontSizeMin = 22f;
                _description.fontSizeMax = 26f;
            }
            if (_markerLegend != null) _markerLegend.text = markerLegend;
        }

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
                float choicesWidth = MIN_CHOICE_WIDTH;
                foreach (var label in _labels)
                {
                    if (!label.transform.parent.gameObject.activeSelf) continue;
                    label.enableAutoSizing = false;
                    choicesWidth = Mathf.Max(choicesWidth, label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity).x + 96f);
                }
                float width = Mathf.Max(_title.GetPreferredValues(_title.text).x + 96f,
                    choicesWidth + COLUMN_GAP + CONTEXT_WIDTH);
                width = Mathf.Min(width, Mathf.Max(1f, _lastCanvasSize.x - 48f));
                // The standard canvas has ample room. Keep both columns usable when resizing it.
                float contextWidth = Mathf.Min(CONTEXT_WIDTH, Mathf.Max(1f, width * 0.45f));
                choicesWidth = Mathf.Max(1f, width - contextWidth - COLUMN_GAP);
                float rowsHeight = 0f;
                foreach (var label in _labels)
                {
                    if (!label.transform.parent.gameObject.activeSelf) continue;
                    float height = Mathf.Max(64f, label.GetPreferredValues(label.text,
                        Mathf.Max(1f, choicesWidth - 96f), Mathf.Infinity).y + 16f);
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
                _scroll.viewport.offsetMax = new Vector2(-contextWidth - COLUMN_GAP, -_headerHeight);
                _body.offsetMin = _scroll.viewport.offsetMin;
                _body.offsetMax = new Vector2(0f, -_headerHeight);
                if (_context != null)
                {
                    _context.offsetMin = new Vector2(choicesWidth + COLUMN_GAP, _footerHeight);
                    _context.offsetMax = new Vector2(0f, -_headerHeight);
                }
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
