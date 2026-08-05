// pattern: Imperative Shell

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Menu.Data;
using YARG.Menu.Navigation;

namespace YARG.Menu.Dialogs
{
    public abstract class Dialog : MonoBehaviour
    {
        [SerializeField]
        private Transform _dialogButtonContainer;
        [SerializeField]
        private NavigationGroup _navigationGroup;
        [SerializeField]
        private ColoredButton _dialogButtonPrefab;

        [field: Space]
        [field: SerializeField]
        public TextMeshProUGUI Title { get; private set; }

        private void OnEnable()
        {
            Navigator.Instance.PushScheme(GetNavigationScheme());
        }

        protected virtual NavigationScheme GetNavigationScheme()
        {
            return new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateSelect,
                NavigationScheme.Entry.NavigateUp,
                NavigationScheme.Entry.NavigateDown
            }, null);
        }

        private void OnDisable()
        {
            Navigator.Instance.PopScheme();
        }

        public virtual void Initialize()
        {

        }

        public ColoredButton AddDialogButton(string localizeKey, UnityAction action)
        {
            var button = Instantiate(_dialogButtonPrefab, _dialogButtonContainer);
            RegisterNavigatable(button);

            button.Text.text = Localize.Key(localizeKey);
            button.OnClick.AddListener(action);

            return button;
        }

        protected void RegisterNavigatable(Component root)
        {
            var navigatable = root.GetComponentInChildren<NavigatableBehaviour>();
            if (navigatable != null)
                _navigationGroup.AddNavigatable(navigatable);
        }

        public virtual ColoredButton AddDialogButton(string localizeKey, Color backgroundColor, UnityAction action)
        {
            var button = AddDialogButton(localizeKey, action);

            button.SetBackgroundAndTextColor(backgroundColor);

            return button;
        }

        public virtual void ClearDialog()
        {
            Title.text = null;
            Title.color = MenuData.Colors.BrightText;

            ClearButtons();
        }

        public void ClearButtons()
        {
            _dialogButtonContainer.DestroyChildren();
            _navigationGroup.ClearNavigatables();
        }

        public virtual void Submit()
        {
        }

        public void Close()
        {
            OnBeforeClose();
            gameObject.SetActive(false);
        }

        protected virtual void OnBeforeClose()
        {
        }

        public UniTask WaitUntilClosed()
        {
            return UniTask.WaitUntil(() => this == null || !gameObject.activeSelf);
        }
    }
}
