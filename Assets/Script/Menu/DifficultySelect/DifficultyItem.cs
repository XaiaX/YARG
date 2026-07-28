using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using YARG.Menu.MusicLibrary;
using YARG.Menu.Navigation;

namespace YARG.Menu.DifficultySelect
{
    public class DifficultyItem : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _header;
        [SerializeField]
        private TextMeshProUGUI _body;

        [field: SerializeField]
        public NavigatableButton Button { get; private set; }

        public void Initialize(string header, string body, UnityAction action)
        {
            _header.gameObject.SetActive(true);
            _header.text = header;

            _body.text = body;
            Button.SetOnClickEvent(action);
        }

        public void Initialize(string body, UnityAction action)
        {
            _header.gameObject.SetActive(false);

            _body.text = body;
            Button.SetOnClickEvent(action);
        }

        public void SetBody(string body)
        {
            _body.text = body;
        }

        public ValueSlider AttachValueSlider(ValueSlider prefab)
        {
            // Keep the existing body row in the item's VerticalLayoutGroup and
            // stretch the mixed slider/text control inside it. Adding the slider
            // directly to the item lets the layout group collapse its width.
            _body.text = "";
            _body.enabled = false;

            var layout = _body.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 75f;

            var slider = Instantiate(prefab, _body.rectTransform);
            var rect = (RectTransform) slider.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;

            return slider;
        }

        /// <summary>
        /// Tints the header/body text, switching to a darker accent while the
        /// item is selected (the same treatment DifficultyItemGreen gets from
        /// its NavigationTextColorizer, but applied at runtime). Each text's
        /// own alpha is preserved.
        /// </summary>
        public void SetAccentColors(Color textColor, Color selectedTextColor)
        {
            ApplyAccent(Button.Selected);

            Button.SelectionStateChanged += (_, selected, _) => ApplyAccent(selected);

            void ApplyAccent(bool selected)
            {
                var color = selected ? selectedTextColor : textColor;
                _header.color = WithAlphaOf(color, _header.color);
                _body.color = WithAlphaOf(color, _body.color);
            }

            static Color WithAlphaOf(Color color, Color alphaSource)
            {
                color.a = alphaSource.a;
                return color;
            }
        }

        /// <summary>
        /// Shows the item as a non-interactive menu title: header text only, no
        /// body, no action. Used so a sub-menu identifies itself.
        /// </summary>
        public void InitializeAsTitle(string header)
        {
            _header.gameObject.SetActive(true);
            _header.text = header;
            _body.gameObject.SetActive(false);
        }

        /// <summary>
        /// Clones the given ring into a centered horizontal group on a new layout
        /// row below the text. Used by the main menu's instrument item to show
        /// every available instrument's tier wheel at once.
        /// </summary>
        public DifficultyRing[] AttachRingRow(DifficultyRing template, int count, float size = 56f,
            float spacing = 10f)
        {
            // A direct child of the item root joins its VerticalLayoutGroup as a
            // real row (that group controls child heights, so the LayoutElement
            // height makes the whole item grow to fit).
            var row = new GameObject("RingRow", typeof(RectTransform), typeof(LayoutElement));
            row.layer = gameObject.layer;

            var rowRect = (RectTransform) row.transform;
            rowRect.SetParent(transform, false);
            rowRect.sizeDelta = new Vector2(_body.rectTransform.sizeDelta.x, size + 8f);

            var layout = row.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = size + 8f;

            var rings = new DifficultyRing[count];
            for (int i = 0; i < count; i++)
            {
                var ring = Instantiate(template, rowRect);
                var rt = (RectTransform) ring.transform;

                // Normalize to the ring prefab's native 65x65 rect and scale
                // uniformly (as in AttachRing), positioned so the group of ring
                // boxes is centered in the row.
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(65f, 65f);
                rt.localScale = Vector3.one * (size / 65f);
                rt.anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * (size + spacing), 0f);

                ring.gameObject.SetActive(true);
                rings[i] = ring;
            }

            return rings;
        }

        /// <summary>
        /// Shrinks the body text to the header's font size. Used by rows whose
        /// body lists several settings and would otherwise dominate the menu.
        /// </summary>
        public void UseSmallBodyText()
        {
            _body.fontSize = _header.fontSize;
        }

        /// <summary>
        /// Dims the item and disables interaction (used to show a fixed, non-editable
        /// choice). Adds a CanvasGroup at runtime so no prefab change is needed.
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            var group = GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = interactable ? 1f : 0.3f;
            group.interactable = interactable;
            group.blocksRaycasts = interactable;
        }
    }
}