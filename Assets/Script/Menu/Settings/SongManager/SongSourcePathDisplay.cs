using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using YARG.Menu.Navigation;

namespace YARG.Menu.Settings
{
    /// <summary>Fits paths by retaining the first component and the most useful trailing components.</summary>
    public sealed class SongSourcePathDisplay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerMoveHandler, ISelectHandler, IDeselectHandler, IScrollHandler
    {
        private TextMeshProUGUI _label;
        private string _path;
        private float _lastWidth = -1;
        private GameObject _detail;
        private const float HOVER_DELAY = 0.2f;
        private NavigatableBehaviour[] _controls;
        private NavigatableBehaviour _focusedControl;
        private bool _hovered;
        private bool _selected;
        private Vector2 _pointerPosition;
        private Camera _pointerCamera;
        private float _showAt;
        private ScrollRect[] _scrollRects;
        private Vector2[] _contentPositions;
        private RectTransform _canvasRect;
        private RectTransform _detailRect;
        private TextMeshProUGUI _detailText;
        private Sprite _backgroundSprite;

        private bool HasInterest => _hovered || _selected || _focusedControl;

        public void BindNavigation(GameObject row)
        {
            UnbindNavigation();
            _controls = row.GetComponentsInChildren<NavigatableBehaviour>();
            foreach (var control in _controls) control.SelectionStateChanged += OnRowSelectionChanged;
            foreach (var button in row.GetComponentsInChildren<Button>())
            {
                if (button.targetGraphic is Image image && image.sprite && image.type == Image.Type.Sliced)
                {
                    _backgroundSprite = image.sprite;
                    break;
                }
            }
            CacheScrollRects();
        }

        private void OnRowSelectionChanged(NavigatableBehaviour control, bool selected, SelectionOrigin origin)
        {
            if (selected)
            {
                _focusedControl = origin == SelectionOrigin.Mouse ? null : control;
                RestartDelay();
            }
            else if (_focusedControl == control)
            {
                _focusedControl = null;
                RestartDelay();
            }
        }

        private void UnbindNavigation()
        {
            if (_controls == null) return;
            foreach (var control in _controls)
                if (control) control.SelectionStateChanged -= OnRowSelectionChanged;
            _controls = null;
            _focusedControl = null;
            RestartDelay();
        }

        private void OnDestroy()
        {
            UnbindNavigation();
            if (_detail) Destroy(_detail);
        }

        private void CacheScrollRects()
        {
            _scrollRects = GetComponentsInParent<ScrollRect>();
            _contentPositions = new Vector2[_scrollRects.Length];
            for (int i = 0; i < _scrollRects.Length; i++)
                if (_scrollRects[i].content) _contentPositions[i] = _scrollRects[i].content.anchoredPosition;
        }

        private void OnTransformParentChanged() => CacheScrollRects();

        private void OnEnable()
        {
            CacheScrollRects();
            RestartDelay();
        }

        public static void SetPath(TextMeshProUGUI label, string path)
        {
            var display = label.GetComponent<SongSourcePathDisplay>() ??
                label.gameObject.AddComponent<SongSourcePathDisplay>();
            display._label = label;
            display._path = path ?? string.Empty;
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
            label.raycastTarget = true;
            display._lastWidth = -1;
            display.CacheScrollRects();
            display.RestartDelay();
            display.Refresh();
        }

        private void LateUpdate()
        {
            if (_label && !Mathf.Approximately(_lastWidth, _label.rectTransform.rect.width))
            {
                Refresh();
                RestartDelay();
            }
            bool scrolling = false;
            if (_scrollRects != null)
            {
                for (int i = 0; i < _scrollRects.Length; i++)
                {
                    var scroll = _scrollRects[i];
                    if (!scroll || !scroll.isActiveAndEnabled || !scroll.content) continue;
                    Vector2 position = scroll.content.anchoredPosition;
                    scrolling |= (position - _contentPositions[i]).sqrMagnitude > 0.0001f ||
                        scroll.velocity.sqrMagnitude > 0.01f;
                    _contentPositions[i] = position;
                }
                // At a scroll boundary the content may not move, but wheel/trackpad input still dismisses hints.
                if (HasInterest && _scrollRects.Length > 0 && Mouse.current != null)
                    scrolling |= Mouse.current.scroll.ReadValue().sqrMagnitude > 0;
            }
            if (scrolling) RestartDelay();
            if (!HasInterest || !_label || _label.text == _path)
            {
                HideDetail();
                return;
            }
            if (!scrolling && Time.unscaledTime >= _showAt)
            {
                ShowDetail();
                PositionDetail();
            }
        }

        private void Refresh()
        {
            if (!_label) return;
            _lastWidth = _label.rectTransform.rect.width;
            float width = Mathf.Max(0, _lastWidth - _label.margin.x - _label.margin.z);
            _label.text = FitPath(_path, width, text => _label.GetPreferredValues(text).x);
        }

        public static string FitPath(string path, float width, Func<string, float> measure)
        {
            if (string.IsNullOrEmpty(path) || width <= 0 || measure(path) <= width) return path;
            int start = path.StartsWith("\\\\", StringComparison.Ordinal) ? 2
                : path[0] == '/' || path[0] == '\\' ? 1 : 0;
            int boundary = path.IndexOfAny(new[] { '/', '\\' }, start);
            string prefix = boundary >= 0 ? path.Substring(0, boundary + 1) : string.Empty;
            string ellipsis = "…";
            // Prefer the complete final component to a prefix that leaves only a few letters visible.
            int lastSeparator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            string filename = path.Substring(lastSeparator + 1);
            if (measure(prefix + ellipsis) > width ||
                (measure(ellipsis + filename) <= width && measure(prefix + ellipsis + filename) > width))
                prefix = string.Empty;
            int minimum = prefix.Length > 0 ? boundary + 1 : 0;
            if (measure(ellipsis) > width) return string.Empty;
            string best = prefix + ellipsis;
            for (int cut = minimum; cut < path.Length; cut++)
            {
                // Avoid splitting a UTF-16 surrogate pair.
                if (char.IsLowSurrogate(path[cut])) continue;
                string candidate = prefix + ellipsis + path.Substring(cut);
                if (measure(candidate) <= width)
                {
                    best = candidate;
                    // Prefer a directory boundary when that still leaves a visible suffix.
                    int next = path.IndexOfAny(new[] { '/', '\\' }, cut);
                    if (next >= cut && next < path.Length - 1)
                    {
                        string components = prefix + ellipsis + path.Substring(next);
                        if (measure(components) <= width) best = components;
                    }
                    break;
                }
            }
            return best;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            OnPointerMove(eventData);
            RestartDelay();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            _pointerPosition = eventData.position;
            _pointerCamera = eventData.enterEventCamera;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            RestartDelay();
        }

        public void OnSelect(BaseEventData eventData)
        {
            _selected = eventData is not PointerEventData;
            RestartDelay();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _selected = false;
            RestartDelay();
        }

        public void OnScroll(PointerEventData eventData)
        {
            RestartDelay();
            // Handling scroll on the label must not swallow the ScrollRect's event.
            if (_scrollRects != null && _scrollRects.Length > 0 && _scrollRects[0])
                _scrollRects[0].OnScroll(eventData);
        }

        private void OnDisable()
        {
            _hovered = false;
            _selected = false;
            _focusedControl = null;
            RestartDelay();
        }

        private void RestartDelay()
        {
            _showAt = Time.unscaledTime + HOVER_DELAY;
            HideDetail();
        }

        private void ShowDetail()
        {
            if (_detail && _detail.activeSelf) return;
            if (!_label || _label.text == _path) return;
            var canvas = GetComponentInParent<Canvas>();
            if (!canvas) return;
            canvas = canvas.rootCanvas;
            _canvasRect = (RectTransform) canvas.transform;
            if (!_detail)
            {
                _detail = new GameObject("Full path", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                _detail.transform.SetParent(_canvasRect, false);
                _detailRect = (RectTransform) _detail.transform;
                _detailRect.pivot = new Vector2(0, 1);
                var background = _detail.GetComponent<Image>();
                background.sprite = _backgroundSprite;
                background.type = Image.Type.Sliced;
                background.color = new Color(0.015f, 0.035f, 0.05f, 0.98f);
                background.raycastTarget = false;
                var outline = RuntimeNavigatable.CreateSelectionOutline(_detailRect, new Color(0.6f, 0.75f, 0.8f, 0.3f));
                // Keep the authored sliced outline close to the tooltip rather than its button-focus oversize.
                ((RectTransform) outline.transform).sizeDelta = new Vector2(2, 2);
                outline.SetActive(true);
                var textObject = new GameObject("Path", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(_detailRect, false);
                _detailText = textObject.GetComponent<TextMeshProUGUI>();
                _detailText.richText = false;
                _detailText.raycastTarget = false;
                _detailText.textWrappingMode = TextWrappingModes.Normal;
                _detailText.enableAutoSizing = true;
                var textRect = _detailText.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(12, 10);
                textRect.offsetMax = new Vector2(-12, -10);
            }
            _detailText.font = _label.font;
            _detailText.fontSize = _label.fontSize;
            _detailText.fontSizeMax = _label.fontSize;
            _detailText.fontSizeMin = Mathf.Min(10, _label.fontSize);
            _detailText.color = _label.color;
            _detailText.text = _path;
            Rect bounds = VisibleCanvasBounds();
            float width = Mathf.Max(1, Mathf.Min(640, bounds.width - 16));
            float height = Mathf.Max(1, Mathf.Min(_detailText.GetPreferredValues(_path, Mathf.Max(1, width - 24), 0).y + 20,
                bounds.height - 16));
            _detailRect.sizeDelta = new Vector2(width, height);
            _detail.transform.SetAsLastSibling();
            _detail.SetActive(true);
        }

        private Rect VisibleCanvasBounds()
        {
            var canvas = _canvasRect.GetComponent<Canvas>();
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, Vector2.zero, camera, out var min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, new Vector2(Screen.width, Screen.height), camera, out var max);
            var rect = _canvasRect.rect;
            return Rect.MinMaxRect(Mathf.Max(rect.xMin, min.x), Mathf.Max(rect.yMin, min.y),
                Mathf.Min(rect.xMax, max.x), Mathf.Min(rect.yMax, max.y));
        }

        private void PositionDetail()
        {
            if (!_detail || !_detail.activeSelf) return;
            Vector2 position;
            if (_hovered)
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, _pointerPosition, _pointerCamera, out position);
            else
                position = _canvasRect.InverseTransformPoint(_label.rectTransform.TransformPoint(
                    new Vector3(_label.rectTransform.rect.xMin, _label.rectTransform.rect.yMin, 0)));
            position += new Vector2(12, -16);
            Rect bounds = VisibleCanvasBounds();
            Vector2 size = _detailRect.sizeDelta;
            position.x = Mathf.Clamp(position.x, bounds.xMin + 8, Mathf.Max(bounds.xMin + 8, bounds.xMax - size.x - 8));
            position.y = Mathf.Clamp(position.y, Mathf.Min(bounds.yMax - 8, bounds.yMin + size.y + 8), bounds.yMax - 8);
            _detailRect.position = _canvasRect.TransformPoint(position);
        }

        private void HideDetail()
        {
            if (_detail) _detail.SetActive(false);
        }
    }
}
