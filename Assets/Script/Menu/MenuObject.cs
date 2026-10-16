using UnityEngine;
using UnityEngine.Assertions;

namespace YARG.Menu
{
    public class MenuObject : MonoBehaviour
    {
        [field: SerializeField]
        public MenuManager.Menu Menu { get; private set; }

        public void Initialize(MenuManager.Menu menu)
        {
            Menu = menu;
        }

        [field: SerializeField]
        public bool HideBelow { get; private set; } = true;

        private void Start()
        {
            Assert.AreNotEqual(Menu, MenuManager.Menu.None);
        }

        public void CloseMenu()
        {
            MenuManager.Instance.PopMenu();
        }
    }
}