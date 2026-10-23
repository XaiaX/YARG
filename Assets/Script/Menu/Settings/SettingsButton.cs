using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Settings;

namespace YARG.Menu.Settings
{
    public class SettingsButton : NavigatableBehaviour
    {
        [SerializeField]
        private GameObject _buttonTemplate;

        [Space]
        [SerializeField]
        private NavigationGroup _navGroup;
        [SerializeField]
        private Transform _container;

        private bool _focused;
        private readonly List<string> _actionKeys = new();

        internal string RefreshIdentity => string.Join("\n", _actionKeys);
        internal int? SelectedActionIndex => _navGroup.SelectedIndex;

        internal void RestoreActionIndex(int? index)
        {
            if (index.HasValue && _navGroup.Count > 0)
                _navGroup.SelectAt(Mathf.Clamp(index.Value, 0, _navGroup.Count - 1));
        }

        public readonly struct CustomButton
        {
            public readonly string Label;
            public readonly Action Action;

            public CustomButton(string label, Action action)
            {
                Label = label;
                Action = action;
            }
        }

        public void SetCustomButtons(IEnumerable<CustomButton> buttons, bool localize = false, string localizationKey = "Settings.Button")
        {
            foreach (var buttonInfo in buttons)
            {
                var button = Instantiate(_buttonTemplate, _container);

                var labelText = localize
                    ? Localize.Key(localizationKey, buttonInfo.Label)
                    : buttonInfo.Label;

                button.GetComponentInChildren<TextMeshProUGUI>().text = labelText;

                _actionKeys.Add(buttonInfo.Label);
                var capture = buttonInfo.Action;
                button.GetComponentInChildren<Button>()
                    .onClick.AddListener(() => InvokeFocusedAction(capture));

                _navGroup.AddNavigatable(button.GetComponentInChildren<NavigatableUnityButton>());
            }

            Destroy(_buttonTemplate);
        }

        public void SetInfo(IEnumerable<string> buttons)
        {
            // Spawn button(s)
            foreach (var buttonName in buttons)
            {
                var button = Instantiate(_buttonTemplate, _container);

                // Set button text
                button.GetComponentInChildren<TextMeshProUGUI>().text =
                    Localize.Key("Settings.Button", buttonName);

                // Set button action
                _actionKeys.Add(buttonName);
                var capture = buttonName;
                button.GetComponentInChildren<Button>()
                    .onClick.AddListener(() => InvokeFocusedAction(() => SettingsManager.InvokeButton(capture)));

                // Add to nav group
                _navGroup.AddNavigatable(button.GetComponentInChildren<NavigatableUnityButton>());
            }

            // Remove the template
            Destroy(_buttonTemplate);
        }

        public void SetCustomCallback(Action action, string localizationKey)
        {
            _actionKeys.Add(localizationKey);
            _buttonTemplate.GetComponentInChildren<TextMeshProUGUI>().text = Localize.Key(localizationKey);

            _buttonTemplate.GetComponentInChildren<Button>().onClick.AddListener(() => InvokeFocusedAction(action));
        }

        public override void Confirm()
        {
            var scheme = new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateSelect,
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", () =>
                {
                    CloseFocusedNavigation();
                }),
                NavigationScheme.Entry.NavigateUp,
                NavigationScheme.Entry.NavigateDown
            }, true);

            scheme.PopCallback = () =>
            {
                _focused = false;
                _navGroup.SelectLastNavGroup();
            };

            _ = Navigator.Instance.PushScheme(scheme);

            _focused = true;
            _navGroup.PushNavGroupToStack();
            if (!_navGroup.SelectedIndex.HasValue)
                _navGroup.SelectFirst();
        }

        private void InvokeFocusedAction(Action action)
        {
            CloseFocusedNavigation();
            action?.Invoke();
        }

        private void CloseFocusedNavigation()
        {
            if (_focused)
            {
                Navigator.Instance.PopScheme();
            }
        }

        protected override void OnSelectionChanged(bool selected)
        {
            base.OnSelectionChanged(selected);
            OnDisable();
        }

        private void OnDisable()
        {
            // If the visual's nav scheme is still in the stack, make sure to pop it.
            if (_focused)
            {
                CloseFocusedNavigation();
            }
        }
    }
}
