using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YARG.Menu
{
    public class MenuManager : MonoSingleton<MenuManager>
    {
        public enum Menu
        {
            None,
            MainMenu,
            MusicLibrary,
            DifficultySelect,
            Credits,
            ProfileList,
            ProfileInfo,
            History,
            Content,
            MaestroSetup,
            EliteDrumsLayout,
        }

        /// <summary>
        /// The values that <see cref="_lastOpenMenu"/> is allowed to be set to
        /// (not including <see cref="Menu.None"/>.
        /// </summary>
        private static readonly HashSet<Menu> _allowedLastOpenMenus = new()
        {
            Menu.MusicLibrary,
            Menu.History,
            Menu.Content
        };

        /// <summary>
        /// The menu that was last open when the menu scene gets disabled.
        /// </summary>
        private static Menu _lastOpenMenu = Menu.None;

        private Dictionary<Menu, MenuObject> _menus;

        private readonly Stack<Menu> _openMenus = new();
        private Coroutine _reactivateCoroutine;
        private Canvas _dynamicMenuCanvas;

        protected override void SingletonAwake()
        {
            // Convert to dictionary with "Menu" as key
            var children = GetComponentsInChildren<MenuObject>(true);
            _menus = children.ToDictionary(i => i.Menu, i => i);
        }

        private void Start()
        {
            // Always push the main menu
            PushMenu(Menu.MainMenu);

            if (_lastOpenMenu != Menu.None)
            {
                PushMenu(_lastOpenMenu);
            }
        }

        private void OnDisable()
        {
            _lastOpenMenu = Menu.None;

            // Set the last open menu to the first instance of the allowed menu
            // Loops from top to bottom
            foreach (var menu in _openMenus)
            {
                if (_allowedLastOpenMenus.Contains(menu))
                {
                    _lastOpenMenu = menu;
                    break;
                }
            }
        }

        internal static string GetResourceName(Menu menu) => menu switch
        {
            Menu.EliteDrumsLayout => "EliteDrumsLayoutMenu",
            _ => menu.ToString()
        };

        public MenuObject ShowMenu(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
                throw new ArgumentException("A menu resource name is required.", nameof(resourceName));

            if (!Enum.TryParse(resourceName, out Menu menu) || menu == Menu.None)
                throw new ArgumentException($"No menu is registered for resource '{resourceName}'.", nameof(resourceName));

            return ShowMenu(menu);
        }

        public MenuObject ShowMenu(string resourceName, Transform parent)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
                throw new ArgumentException("A menu resource name is required.", nameof(resourceName));

            if (!Enum.TryParse(resourceName, out Menu menu) || menu == Menu.None)
                throw new ArgumentException($"No menu is registered for resource '{resourceName}'.", nameof(resourceName));

            return ShowMenu(menu, parent);
        }

        public MenuObject ShowMenu(Menu menu)
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
                throw new InvalidOperationException("Cannot show a menu without an active canvas.");

            return ShowMenu(menu, canvas.transform);
        }

        public MenuObject ShowMenu(Menu menu, Transform parent)
        {
            if (menu == Menu.None)
                throw new ArgumentOutOfRangeException(nameof(menu));
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            var parentCanvas = parent.GetComponent<Canvas>();
            if (parentCanvas == null)
                parentCanvas = parent.GetComponentInParent<Canvas>();
            if (parentCanvas == null)
                throw new InvalidOperationException("Cannot show a menu without a parent canvas.");

            var rootCanvas = parentCanvas.rootCanvas;
            if (rootCanvas == null || !rootCanvas.isActiveAndEnabled)
                throw new InvalidOperationException("Cannot show a menu under an inactive root canvas.");

            parent = rootCanvas.transform;
            // A standalone menu can own its root Canvas. Its replacement must not
            // remain beneath that Canvas when PushMenu hides the previous menu.
            if (rootCanvas.GetComponentInParent<MenuObject>() != null)
            {
                if (_dynamicMenuCanvas == null)
                {
                    var host = new GameObject("Dynamic Menu Canvas", typeof(RectTransform),
                        typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler),
                        typeof(UnityEngine.UI.GraphicRaycaster));
                    host.transform.SetParent(transform, false);
                    _dynamicMenuCanvas = host.GetComponent<Canvas>();
                    _dynamicMenuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    _dynamicMenuCanvas.sortingOrder = rootCanvas.sortingOrder;
                    _dynamicMenuCanvas.additionalShaderChannels = rootCanvas.additionalShaderChannels;
                    var sourceScaler = rootCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                    if (sourceScaler != null)
                    {
                        var scaler = host.GetComponent<UnityEngine.UI.CanvasScaler>();
                        scaler.uiScaleMode = sourceScaler.uiScaleMode;
                        scaler.referenceResolution = sourceScaler.referenceResolution;
                        scaler.screenMatchMode = sourceScaler.screenMatchMode;
                        scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
                        scaler.scaleFactor = sourceScaler.scaleFactor;
                        scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
                    }
                }
                parent = _dynamicMenuCanvas.transform;
            }

            if (!_menus.TryGetValue(menu, out var menuObject))
            {
                string resourceName = GetResourceName(menu);
                var prefab = Resources.Load<GameObject>(resourceName);
                if (prefab == null)
                    throw new InvalidOperationException($"Failed to load menu resource '{resourceName}'.");

                var instance = Instantiate(prefab, parent, false);
                menuObject = instance.GetComponent<MenuObject>();
                if (menuObject == null)
                {
                    Destroy(instance);
                    throw new InvalidOperationException(
                        $"Menu resource '{resourceName}' must have a MenuObject component.");
                }
                menuObject.Initialize(menu);
                _menus.Add(menu, menuObject);
            }
            else
            {
                menuObject.transform.SetParent(parent, false);
            }

            return PushMenu(menu);
        }

        public MenuObject PushMenu(Menu menu, bool setActiveImmediate = true)
        {
            bool hideOther;

            // Get the new one
            if (_menus.TryGetValue(menu, out var newMenu))
            {
                hideOther = newMenu.HideBelow;
            }
            else
            {
                throw new InvalidOperationException($"Failed to open menu {menu}.");
            }

            // Close the currently open one
            if (hideOther && _openMenus.TryPeek(out var currentMenuEnum) &&
                _menus.TryGetValue(currentMenuEnum, out var currentMenu))
            {
                currentMenu.gameObject.SetActive(false);
            }

            // Show the new one
            if (setActiveImmediate)
            {
                newMenu.gameObject.SetActive(true);
            }

            // ... and push it onto the stack
            _openMenus.Push(menu);

            return newMenu;
        }

        public void PopMenu()
        {
            //Don't pop the only remaining menu
            if (_openMenus.Count == 1)
            {
                return;
            }

            // Close the currently open one
            if (_openMenus.TryPop(out var currentMenuEnum) &&
                _menus.TryGetValue(currentMenuEnum, out var currentMenu))
            {
                currentMenu.gameObject.SetActive(false);
            }

            if (_openMenus.TryPeek(out var newMenuEnum) &&
                _menus.TryGetValue(newMenuEnum, out var newMenu))
            {
                newMenu.gameObject.SetActive(true);
            }
            else
            {
                throw new InvalidOperationException($"Failed to open menu {newMenuEnum}.");
            }
        }

        public bool PopToMenu(Menu menu)
        {
            if (!_openMenus.Contains(menu)) return false;
            while (_openMenus.Count > 1 && _openMenus.Peek() != menu) PopMenu();
            return _openMenus.Peek() == menu;
        }

        // Disables the current menu without popping it from the stack
        public void DisableCurrentMenu()
        {
            if (_openMenus.TryPeek(out var currentMenuEnum) && _menus.TryGetValue(currentMenuEnum, out var currentMenu))
            {
                currentMenu.gameObject.SetActive(false);
            }
        }

        public void ReactivateCurrentMenu(bool forceRefreshIfActive = true)
        {
            // Show the under one
            if (_openMenus.TryPeek(out var menu) && _menus.TryGetValue(menu, out var newMenu))
            {
                if (!forceRefreshIfActive && newMenu.gameObject.activeSelf)
                {
                    return;
                }

                if (_reactivateCoroutine != null)
                {
                    StopCoroutine(_reactivateCoroutine);
                    _reactivateCoroutine = null;
                }

                _reactivateCoroutine = StartCoroutine(ReactivateMenuCoroutine(newMenu.gameObject));
            }
            else
            {
                throw new InvalidOperationException($"Failed to activate menu {menu}.");
            }
        }

        private System.Collections.IEnumerator ReactivateMenuCoroutine(GameObject menuObject)
        {
            if (menuObject == null)
            {
                _reactivateCoroutine = null;
                yield break;
            }

            // Defer activation toggles to avoid SetActive during another object's OnEnable/OnDisable.
            yield return null;

            if (menuObject.activeSelf)
            {
                menuObject.SetActive(false);
                yield return null;
            }

            menuObject.SetActive(true);
            _reactivateCoroutine = null;
        }
    }
}
