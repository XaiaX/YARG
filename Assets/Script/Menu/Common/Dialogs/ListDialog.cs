// pattern: Imperative Shell

using UnityEngine;
using UnityEngine.Events;
using YARG.Core.Input;
using YARG.Helpers.Extensions;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;

namespace YARG.Menu.Dialogs
{
    public class ListDialog : Dialog
    {
        [Space]
        [SerializeField]
        private Transform _listContainer;
        [SerializeField]
        private ColoredButton _listButtonPrefab;

        public ColoredButton AddListButton(string text, UnityAction handler, bool closeOnClick = true)
        {
            var button = AddListEntry(_listButtonPrefab);
            RegisterNavigatable(button);

            button.Text.text = text;
            if (closeOnClick)
            {
                button.OnClick.AddListener(() =>
                {
                    handler();
                    DialogManager.Instance.ClearDialog();
                });
            }
            else
            {
                button.OnClick.AddListener(handler);
            }

            return button;
        }

        public T AddListEntry<T>(T prefab, bool registerNavigatable = false)
            where T : Object
        {
            var entry = Instantiate(prefab, _listContainer);
            if (registerNavigatable)
                RegisterNavigatable(entry as Component);
            return entry;
        }

        public void ClearList()
        {
            _listContainer.DestroyChildren();
        }

        public override void ClearDialog()
        {
            base.ClearDialog();

            ClearList();
        }

        protected override NavigationScheme GetNavigationScheme()
        {
            return new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateSelect,
                NavigationScheme.Entry.NavigateUp,
                NavigationScheme.Entry.NavigateDown,
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Cancel",
                    () => DialogManager.Instance.ClearDialog()),
            }, null);
        }
    }
}
