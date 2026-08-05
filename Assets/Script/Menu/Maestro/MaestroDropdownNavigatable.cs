// pattern: Imperative Shell

using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Helpers.UI;
using YARG.Menu.Navigation;

namespace YARG.Menu.Maestro
{
    public sealed class MaestroDropdownNavigatable : NavigatableBehaviour
    {
        private static readonly Color SELECTED_TEXT_COLOR =
            new(1f, 0.83137256f, 0.22745098f, 1f);

        [SerializeField] private Image _focusBorder;

        private TMP_Dropdown _dropdown;
        private Color _defaultCaptionColor;
        private bool _captionColorCaptured;
        private NavigationScheme _dropdownScheme;

        public static MaestroDropdownNavigatable Attach(TMP_Dropdown dropdown)
        {
            if (dropdown == null)
                return null;

            var parent = dropdown.transform.parent.gameObject;
            var navigatable = parent.GetComponent<MaestroDropdownNavigatable>();
            if (navigatable == null)
                navigatable = parent.AddComponent<MaestroDropdownNavigatable>();

            navigatable.Initialize(dropdown);
            return navigatable;
        }

        // The prefab does not have the base class's serialized selected visual.
        protected override void Awake()
        {
        }

        private void Initialize(TMP_Dropdown dropdown)
        {
            _dropdown = dropdown;
            if (!_captionColorCaptured && _dropdown.captionText != null)
            {
                _defaultCaptionColor = _dropdown.captionText.color;
                _captionColorCaptured = true;
            }

            var forwarder = _dropdown.GetComponent<MaestroDropdownClickForwarder>();
            if (forwarder == null)
                forwarder = _dropdown.gameObject.AddComponent<MaestroDropdownClickForwarder>();
            forwarder.Target = this;

            if (_focusBorder == null)
            {
                var borderGo = new GameObject("FocusBorder");
                borderGo.transform.SetParent(_dropdown.transform.parent, false);
                var rt = borderGo.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(-4, 12);
                rt.offsetMax = new Vector2(4, -12);
                var img = borderGo.AddComponent<Image>();
                img.sprite = SpriteHelper.GetRoundedRect(12, 2);
                img.type = Image.Type.Sliced;
                img.color = new Color(1f, 0.83137256f, 0.22745098f, 1f);
                img.raycastTarget = false;
                _focusBorder = img;
                borderGo.SetActive(false);
            }
        }

        protected override void OnSelectionChanged(bool selected)
        {
            if (_dropdown == null || _dropdown.captionText == null)
                return;

            if (selected)
            {
                var color = SELECTED_TEXT_COLOR;
                color.a = _defaultCaptionColor.a;
                _dropdown.captionText.color = color;
            }
            else
            {
                _dropdown.captionText.color = _defaultCaptionColor;
            }

            if (_focusBorder != null)
            {
                bool dropdownOpen = _dropdown != null &&
                    _dropdown.transform.Find("Dropdown List") != null;
                _focusBorder.gameObject.SetActive(selected && !dropdownOpen);
            }
        }

        public override void Confirm()
        {
            OpenDropdownList();
        }

        public void CloseDropdown()
        {
            if (_dropdown != null && _dropdown.transform.Find("Dropdown List") != null)
                _dropdown.Hide();
            RemoveDropdownScheme();
        }

        protected override void OnDestroy()
        {
            CloseDropdown();
            base.OnDestroy();
        }

        private void OpenDropdownList()
        {
            var dropdown = _dropdown;
            if (dropdown == null || dropdown.options.Count == 0)
                return;

            if (_dropdownScheme != null)
                return;

            if (_focusBorder != null)
                _focusBorder.gameObject.SetActive(false);

            if (dropdown.transform.Find("Dropdown List") == null)
                dropdown.Show();

            var list = dropdown.transform.Find("Dropdown List");
            if (list == null)
                return;

            var toggles = list.GetComponentsInChildren<Toggle>();
            if (toggles.Length == 0)
                return;

            var scrollRect = list.GetComponent<ScrollRect>();
            int index = Mathf.Clamp(dropdown.value, 0, toggles.Length - 1);

            void Highlight()
            {
                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(toggles[index].gameObject);

                if (scrollRect != null && toggles.Length > 1)
                {
                    scrollRect.verticalNormalizedPosition =
                        1f - (float) index / (toggles.Length - 1);
                }
            }

            Highlight();

            var watcher = list.gameObject.AddComponent<MaestroDropdownListCloseWatcher>();
            watcher.Closed = RemoveDropdownScheme;

            if (Navigator.Instance == null)
                return;

            _dropdownScheme = new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Previous", () =>
                {
                    if (dropdown == null) { RemoveDropdownScheme(); return; }
                    index = (index - 1 + toggles.Length) % toggles.Length;
                    Highlight();
                }),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Next", () =>
                {
                    if (dropdown == null) { RemoveDropdownScheme(); return; }
                    index = (index + 1) % toggles.Length;
                    Highlight();
                }),
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Confirm", () =>
                {
                    if (dropdown == null) { RemoveDropdownScheme(); return; }
                    if (index == dropdown.value)
                        dropdown.Hide();
                    else
                        toggles[index].isOn = true;
                }),
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Cancel", () =>
                {
                    if (dropdown == null) { RemoveDropdownScheme(); return; }
                    dropdown.Hide();
                }),
            }, null);
            Navigator.Instance.PushScheme(_dropdownScheme);
        }

        private void RemoveDropdownScheme()
        {
            if (_dropdownScheme == null)
                return;

            var scheme = _dropdownScheme;
            _dropdownScheme = null;
            if (Navigator.Instance != null)
                Navigator.Instance.RemoveScheme(scheme);

            if (_focusBorder != null && Selected)
                _focusBorder.gameObject.SetActive(true);
        }
    }

    public sealed class MaestroDropdownListCloseWatcher : MonoBehaviour
    {
        public Action Closed;

        private void OnDestroy()
        {
            Closed?.Invoke();
        }
    }

    public sealed class MaestroDropdownClickForwarder : MonoBehaviour,
        IPointerDownHandler, IPointerClickHandler
    {
        public MaestroDropdownNavigatable Target;

        public void OnPointerDown(PointerEventData eventData)
        {
            Target?.SetSelected(true, SelectionOrigin.Mouse);
            // Open the dropdown on pointer-down rather than waiting for the
            // click event. IPointerClickHandler can fail to fire when the
            // navigation state changes between press and release.
            Target?.Confirm();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Handled in OnPointerDown to avoid timing issues.
        }
    }
}
