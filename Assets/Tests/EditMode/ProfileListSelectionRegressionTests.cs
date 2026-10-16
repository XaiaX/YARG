using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class ProfileListSelectionRegressionTests
    {
        private const string MENU_PREFAB_PATH = "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab";
        private const string PROFILE_VIEW_PREFAB_PATH = "Assets/Prefabs/Menu/ProfileList/ProfileView.prefab";
        private const string MENU_COLORS_ASSET_PATH = "Assets/Art/Menu/Data/MenuColors.asset";
        private const BindingFlags INSTANCE_PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags STATIC_PRIVATE = BindingFlags.Static | BindingFlags.NonPublic;

        private GameObject _menuInstance;
        private GameObject _menuDataGameObject;
        private GameObject _testMenuManagerObject;
        private GameObject _testCanvasObject;
        private GameObject _testEditorObject;
        private Component _menu;
        private Component _sidebar;
        private Component _navigationGroup;
        private IList _profiles;
        private IList _players;
        private IDictionary _playersByProfile;
        private IDictionary _profilesById;
        private YargProfile[] _testProfiles;
        private YargProfile _inactiveProfile;
        private Guid _defaultThemePreset;

        private static Type Production(string name) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(name, false))
                .FirstOrDefault(type => type != null)
            ?? throw new TypeLoadException($"Could not find production type '{name}' in loaded assemblies.");

        private static FieldInfo Field(Type type, string name, BindingFlags flags = INSTANCE_PRIVATE) =>
            type.GetField(name, flags);

        private static object Invoke(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(target, args);

        private static object GetProperty(object target, string name) =>
            target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(target);

        private static void SetProperty(object target, string name, object value) =>
            target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(target, value);

        [SetUp]
        public void SetUp()
        {
            var prefabContents = PrefabUtility.LoadPrefabContents(MENU_PREFAB_PATH);
            try
            {
                // Keep normal menu OnEnable callbacks out of this test: they rely on app singletons.
                prefabContents.SetActive(false);
                _menuInstance = UnityEngine.Object.Instantiate(prefabContents);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            var menuType = Production("YARG.Menu.ProfileList.ProfileListMenu");
            var sidebarType = Production("YARG.Menu.ProfileList.ProfileSidebar");
            _menu = _menuInstance.GetComponent(menuType);
            _sidebar = _menuInstance.GetComponentInChildren(sidebarType, true);
            _navigationGroup = (Component)Field(menuType, "_navigationGroup").GetValue(_menu);

            var menuDataType = Production("YARG.Menu.Data.MenuData");
            var menuDataInstance = menuDataType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .GetValue(null);
            if (menuDataInstance == null)
            {
                var menuColors = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MENU_COLORS_ASSET_PATH);
                Assert.That(menuColors, Is.Not.Null, "The production menu color asset must be available.");
                _menuDataGameObject = new GameObject("Profile selection test menu data");
                menuDataInstance = _menuDataGameObject.AddComponent(menuDataType);
                Field(menuDataType, "_colors").SetValue(menuDataInstance, menuColors);
                if (menuDataType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null)
                    != menuDataInstance)
                {
                    InvokeAwake((Component)menuDataInstance);
                }
            }

            Assert.That(_menu, Is.Not.Null);
            Assert.That(_sidebar, Is.Not.Null);
            Assert.That(_navigationGroup, Is.Not.Null);

            // EditMode prefab mounting does not dispatch Awake on inactive objects.
            InvokeAwake(_sidebar);

            var playerContainer = Production("YARG.Player.PlayerContainer");
            _profiles = (IList)Field(playerContainer, "_profiles", STATIC_PRIVATE).GetValue(null);
            _players = (IList)Field(playerContainer, "_players", STATIC_PRIVATE).GetValue(null);
            _playersByProfile = (IDictionary)Field(playerContainer, "_playersByProfile", STATIC_PRIVATE)
                .GetValue(null);
            _profilesById = (IDictionary)Field(playerContainer, "_profilesById", STATIC_PRIVATE).GetValue(null);

            var themePresetType = Production("YARG.Themes.ThemePreset");
            var defaultThemePreset = themePresetType.GetField("Default", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            _defaultThemePreset = (Guid)themePresetType.GetField("Id", BindingFlags.Public | BindingFlags.Instance)
                .GetValue(defaultThemePreset);
            _testProfiles = new[]
            {
                new YargProfile
                {
                    Name = "Selection Test One",
                    GameMode = GameMode.FiveFretGuitar,
                    NoteSpeed = 4,
                    EnginePreset = EnginePreset.Default.Id,
                    ThemePreset = _defaultThemePreset
                },
                new YargProfile
                {
                    Name = "Selection Test Two",
                    GameMode = GameMode.FiveFretGuitar,
                    NoteSpeed = 7,
                    EnginePreset = EnginePreset.Default.Id,
                    ThemePreset = _defaultThemePreset
                }
            };
            _inactiveProfile = new YargProfile
            {
                Name = "Selection Test Inactive",
                GameMode = GameMode.FiveLaneDrums,
                EnginePreset = EnginePreset.Default.Id,
                ThemePreset = _defaultThemePreset
            };

            var bindingsType = Production("YARG.Input.ProfileBindings");
            var playerType = Production("YARG.Player.YargPlayer");
            var playerConstructor = playerType.GetConstructor(new[] { typeof(YargProfile), bindingsType });
            Assert.That(playerConstructor, Is.Not.Null);

            foreach (var profile in _testProfiles)
            {
                var player = playerConstructor.Invoke(new object[] { profile, null });
                _profiles.Add(profile);
                _profilesById.Add(profile.Id, profile);
                _players.Add(player);
                _playersByProfile.Add(profile, player);
            }

            _profiles.Add(_inactiveProfile);
            _profilesById.Add(_inactiveProfile.Id, _inactiveProfile);
        }

        [TearDown]
        public void TearDown()
        {
            if (_testProfiles != null)
            {
                foreach (var profile in _testProfiles)
                {
                    var player = _playersByProfile?[profile];
                    if (player != null)
                    {
                        _players.Remove(player);
                        _playersByProfile.Remove(profile);
                    }
                    _profiles.Remove(profile);
                    _profilesById.Remove(profile.Id);
                }
            }

            if (_inactiveProfile != null)
            {
                _profiles?.Remove(_inactiveProfile);
                _profilesById?.Remove(_inactiveProfile.Id);
            }

            if (_menuInstance != null)
                UnityEngine.Object.DestroyImmediate(_menuInstance);

            if (_menuDataGameObject != null)
                UnityEngine.Object.DestroyImmediate(_menuDataGameObject);
            if (_testMenuManagerObject != null)
                UnityEngine.Object.DestroyImmediate(_testMenuManagerObject);
            if (_testCanvasObject != null)
                UnityEngine.Object.DestroyImmediate(_testCanvasObject);
            if (_testEditorObject != null)
                UnityEngine.Object.DestroyImmediate(_testEditorObject);
        }

        [Test]
        public void ProductionPrefabWiresEliteLayoutDropdownToOwningProfileMenu()
        {
            var sidebarType = _sidebar.GetType();
            var menuReference = Field(sidebarType, "_profileListMenu").GetValue(_sidebar);
            var layoutDropdown = (Component)Field(sidebarType, "_eliteLayoutDropdown").GetValue(_sidebar);
            var colorDropdown = (Component)Field(sidebarType, "_colorProfileDropdown").GetValue(_sidebar);
            var sidebarContent = (GameObject)Field(sidebarType, "_sidebarContent").GetValue(_sidebar);

            Assert.That(menuReference, Is.SameAs(_menu),
                "The sidebar must call the ProfileListMenu on its own production prefab.");
            Assert.That(layoutDropdown, Is.Not.Null,
                "The production sidebar must retain the Elite Drums layout selector.");
            Assert.That(colorDropdown, Is.Not.Null,
                "The production sidebar must retain its Color Profile selector.");
            Assert.That(layoutDropdown, Is.Not.SameAs(colorDropdown),
                "The Elite layout and Color Profile selectors must be different controls.");
            var colorEvent = GetProperty(colorDropdown, "onValueChanged");
            var layoutEvent = GetProperty(layoutDropdown, "onValueChanged");
            Assert.That((int)Invoke(colorEvent, "GetPersistentEventCount"), Is.EqualTo(1),
                "The existing Color Profile control must retain its persistent profile-color callback.");
            Assert.That(Invoke(colorEvent, "GetPersistentTarget", 0), Is.SameAs(_sidebar));
            Assert.That(Invoke(colorEvent, "GetPersistentMethodName", 0), Is.EqualTo("ChangeColorProfile"));
            Assert.That((int)Invoke(layoutEvent, "GetPersistentEventCount"), Is.Zero,
                "The cloned layout control must not retain the Color Profile callback.");

            Transform DirectRow(Component control)
            {
                var row = control.transform;
                while (row.parent != sidebarContent.transform)
                {
                    Assert.That(row.parent, Is.Not.Null,
                        "Profile controls must be descendants of the profile settings list.");
                    row = row.parent;
                }
                return row;
            }

            var colorRow = DirectRow(colorDropdown);
            var layoutRow = DirectRow(layoutDropdown);
            var configureButton = (Component)Field(sidebarType, "_eliteLayoutConfigureButton").GetValue(_sidebar);
            Assert.That(configureButton, Is.Not.Null,
                "The profile sidebar must retain the Elite layout Configure action.");
            var configureRow = DirectRow(configureButton);
            Assert.That(configureRow.name, Is.EqualTo("Configure Elite Drums Layout"));
            Assert.That(configureRow.Find("Option Name"), Is.Not.Null,
                "The Elite Configure row must retain its visible setting label.");
            var localizationManager = Production("YARG.Localization.LocalizationManager");
            var localizationMap = (IDictionary)localizationManager
                .GetField("_localizationMap", STATIC_PRIVATE).GetValue(null);
            var priorLocalization = new Hashtable(localizationMap);
            var priorCulture = (string)localizationManager.GetProperty("CultureCode").GetValue(null);
            try
            {
                Assert.That((bool)localizationManager.GetMethod("TryParseAndLoadLanguage", STATIC_PRIVATE)
                    .Invoke(null, new object[] { "en-US" }), Is.True,
                    "Load the production English dictionary before resolving the Profile label.");
                var localize = Production("YARG.Localization.Localize");
                const string configureKey = "Menu.ProfileList.ConfigureEliteDrumsLayout";
                var resolvedLabel = (string)localize.GetMethod("Key", new[] { typeof(string) })
                    .Invoke(null, new object[] { configureKey });
                Assert.That(resolvedLabel, Is.EqualTo("ELITE DRUMS HIGHWAY"));
                var optionName = configureRow.Find("Option Name");
                var localizedText = optionName.GetComponent(Production("YARG.Localization.LocalizeText"));
                Assert.That(localizedText, Is.Not.Null,
                    "The actual Configure row label must use production localization.");
                Invoke(localizedText, "UpdateText");
                var visibleLabel = optionName.GetComponent(Production("TMPro.TextMeshProUGUI"));
                Assert.That(visibleLabel, Is.Not.Null);
                Assert.That(GetProperty(visibleLabel, "text"), Is.EqualTo("ELITE DRUMS HIGHWAY"));
                var builder = Production("YARG.Editor.EliteDrumsLayoutMenuPrefabBuilder");
                Assert.That((string)builder.GetField("PROFILE_CONFIGURE_LABEL_KEY", BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic).GetRawConstantValue(), Is.EqualTo(configureKey),
                    "The production Profile row builder must bind the visible label to the localized key.");
            }
            finally
            {
                localizationMap.Clear();
                foreach (DictionaryEntry entry in priorLocalization) localizationMap.Add(entry.Key, entry.Value);
                localizationManager.GetProperty("CultureCode").GetSetMethod(true)
                    .Invoke(null, new object[] { priorCulture });
            }
            Assert.That(configureRow.GetSiblingIndex(), Is.EqualTo(
                sidebarContent.transform.Find("Configure 5-Lane Drums Highway").GetSiblingIndex() + 1),
                "Elite Configure must follow the existing drum highway Configure rows.");
            Assert.That(layoutRow, Is.Not.SameAs(colorRow),
                "Elite Drums Layout must have its own profile-settings row.");
            Assert.That(layoutRow.parent, Is.SameAs(colorRow.parent),
                "Color Profile and Elite Drums Layout must be peer rows.");
            Assert.That(layoutRow.GetSiblingIndex(), Is.Not.EqualTo(colorRow.GetSiblingIndex()),
                "The two selector rows must occupy distinct positions in the layout.");
        }

        [Test]
        public void ProfileSelectorAndConfigureVisibilityMatchesEveryGameMode()
        {
            var sidebarContent = (GameObject)Field(_sidebar.GetType(), "_sidebarContent").GetValue(_sidebar);
            var layoutDropdown = (Component)Field(_sidebar.GetType(), "_eliteLayoutDropdown").GetValue(_sidebar);
            var configureButton = (Component)Field(_sidebar.GetType(), "_eliteLayoutConfigureButton").GetValue(_sidebar);
            var configureFour = sidebarContent.transform.Find("Configure 4-Lane Drums Highway").gameObject;
            var configurePro = sidebarContent.transform.Find("Configure Pro Drums Highway").gameObject;
            var configureFive = sidebarContent.transform.Find("Configure 5-Lane Drums Highway").gameObject;
            var enableSettings = _sidebar.GetType().GetMethod("EnableSettingsForGameMode", INSTANCE_PRIVATE);
            Assert.That(enableSettings, Is.Not.Null);

            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
            {
                Field(_sidebar.GetType(), "_profile").SetValue(_sidebar, new YargProfile { GameMode = mode });
                enableSettings.Invoke(_sidebar, null);
                bool elite = mode == GameMode.EliteDrums;
                Assert.That(DirectRow(sidebarContent.transform, layoutDropdown).gameObject.activeSelf, Is.EqualTo(elite),
                    $"Layout selector visibility should match Elite Drums for {mode}.");
                Assert.That(DirectRow(sidebarContent.transform, configureButton).gameObject.activeSelf, Is.EqualTo(elite),
                    $"Elite Configure visibility should match Elite Drums for {mode}.");
                Assert.That(configureFour.activeSelf, Is.EqualTo(mode is GameMode.FourLaneDrums or GameMode.EliteDrums),
                    $"4-Lane Configure must remain visible only in its existing modes for {mode}.");
                Assert.That(configurePro.activeSelf, Is.EqualTo(mode is GameMode.FourLaneDrums or GameMode.EliteDrums),
                    $"Pro Drums Configure must remain visible only in its existing modes for {mode}.");
                Assert.That(configureFive.activeSelf, Is.EqualTo(mode is GameMode.FiveLaneDrums or GameMode.EliteDrums),
                    $"5-Lane Configure must remain visible only in its existing modes for {mode}.");
            }
        }

        [Test]
        public void ProfileSelectorRowsHaveDistinctProductionBoundsAndNoOverlap()
        {
            var sidebarType = _sidebar.GetType();
            var contentObject = (GameObject)Field(sidebarType, "_sidebarContent").GetValue(_sidebar);
            var content = (RectTransform)contentObject.transform;
            var color = (Component)Field(sidebarType, "_colorProfileDropdown").GetValue(_sidebar);
            var layout = (Component)Field(sidebarType, "_eliteLayoutDropdown").GetValue(_sidebar);
            var configure = (Component)Field(sidebarType, "_eliteLayoutConfigureButton").GetValue(_sidebar);
            var rows = new[] { DirectRow(content, color), DirectRow(content, layout), DirectRow(content, configure) };
            Assert.That(rows.Distinct().Count(), Is.EqualTo(rows.Length));

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Canvas.ForceUpdateCanvases();
            var corners = rows.Select(row =>
            {
                var rect = (RectTransform)row;
                var world = new Vector3[4];
                rect.GetWorldCorners(world);
                return (Rect: rect, MinY: world.Min(point => point.y), MaxY: world.Max(point => point.y));
            }).ToArray();

            Assert.That(corners.All(bounds => bounds.Rect.rect.height > 0f), Is.True,
                "Production profile rows must have non-empty bounds.");
            for (int i = 0; i < corners.Length; i++)
            for (int j = i + 1; j < corners.Length; j++)
            {
                bool overlaps = corners[i].MaxY > corners[j].MinY && corners[j].MaxY > corners[i].MinY;
                Assert.That(overlaps, Is.False,
                    $"Production profile rows {corners[i].Rect.name} and {corners[j].Rect.name} overlap vertically.");
            }
        }

        private static Transform DirectRow(Transform content, Component control)
        {
            var row = control.transform;
            while (row.parent != content)
            {
                Assert.That(row.parent, Is.Not.Null, "Profile control must be under the profile settings list.");
                row = row.parent;
            }
            return row;
        }

        [Test]
        public void ChangingEliteLayoutDoesNotChangeSelectedColorProfile()
        {
            var sidebarType = _sidebar.GetType();
            var profile = new YargProfile
            {
                Name = "Independent Profile Controls",
                GameMode = GameMode.EliteDrums,
                ColorProfile = YARG.Core.Game.ColorProfile.Default.Id,
                EliteDrumsLayout = YARG.Core.Game.EliteDrumsLayoutPreset.CompactId
            };
            Field(sidebarType, "_profile").SetValue(_sidebar, profile);
            var layout = (Component)Field(sidebarType, "_eliteLayoutDropdown").GetValue(_sidebar);
            var color = (Component)Field(sidebarType, "_colorProfileDropdown").GetValue(_sidebar);
            var colorIndexBefore = (int)GetProperty(color, "value");
            var colorIdBefore = profile.ColorProfile;

            Invoke(_menu, "RefreshEliteLayoutDropdown", layout, profile);
            var options = (IList)GetProperty(layout, "options");
            Assert.That(options.Count, Is.GreaterThan(1), "The production layout selector should expose layouts.");
            int currentIndex = (int)GetProperty(layout, "value");
            int changedIndex = currentIndex == 0 ? 1 : 0;
            SetProperty(layout, "value", changedIndex);

            Assert.That(profile.EliteDrumsLayout, Is.EqualTo(YARG.Core.Game.EliteDrumsLayoutPreset.FullId),
                "The layout dropdown's own callback must store the selected layout GUID.");
            Assert.That(profile.ColorProfile, Is.EqualTo(colorIdBefore));
            Assert.That((int)GetProperty(color, "value"), Is.EqualTo(colorIndexBefore));
        }

        [Test]
        public void ApplyingRecommendedThemeColorUpdatesColorGuidAndDropdownWithoutChangingLayout()
        {
            var sidebarType = _sidebar.GetType();
            var profile = new YargProfile
            {
                Name = "Recommended Color Profile",
                GameMode = GameMode.EliteDrums,
                ColorProfile = YARG.Core.Game.ColorProfile.Default.Id,
                EliteDrumsLayout = YARG.Core.Game.EliteDrumsLayoutPreset.FullId
            };
            Field(sidebarType, "_profile").SetValue(_sidebar, profile);
            Invoke(_sidebar, "PopulateDropdownOptions");
            var colors = (IList)Field(sidebarType, "_colorProfilesByIndex").GetValue(_sidebar);
            var recommendedColor = (Guid)colors[1];
            var layoutId = profile.EliteDrumsLayout;

            Invoke(_sidebar, "ApplyRecommendedPresets", null, (Guid?)recommendedColor);

            var dropdown = (Component)Field(sidebarType, "_colorProfileDropdown").GetValue(_sidebar);
            Assert.That(profile.ColorProfile, Is.EqualTo(recommendedColor));
            Assert.That((int)GetProperty(dropdown, "value"), Is.EqualTo(colors.IndexOf(recommendedColor)));
            Assert.That(profile.EliteDrumsLayout, Is.EqualTo(layoutId));
        }

        [Test]
        public void ProfileConfigureLoadsFullscreenResourceAndInitializesSelectedLayoutAndColor()
        {
            var resource = Resources.Load<GameObject>("EliteDrumsLayoutMenu");
            Assert.That(resource, Is.Not.Null, "The Elite layout menu resource must exist.");
            var managerType = Production("YARG.Menu.MenuManager");
            var menuEnum = managerType.GetNestedType("Menu");
            var eliteMenu = Enum.Parse(menuEnum, "EliteDrumsLayout");
            Assert.That(managerType.GetMethod("GetResourceName", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new[] { eliteMenu }), Is.EqualTo("EliteDrumsLayoutMenu"));
            var menuObjectType = Production("YARG.Menu.MenuObject");
            var resourceMenu = resource.GetComponent(menuObjectType);
            Assert.That(resourceMenu, Is.Not.Null);
            Assert.That(resourceMenu.GetType().GetProperty("Menu").GetValue(resourceMenu).ToString(),
                Is.EqualTo("EliteDrumsLayout"));
            Assert.That((bool)resourceMenu.GetType().GetProperty("HideBelow").GetValue(resourceMenu), Is.True,
                "The layout editor is a fullscreen menu that hides the profile screen beneath it.");
            Assert.That(resource.name, Is.EqualTo("EliteDrumsLayoutMenu"),
                "The enum maps to the existing prefab filename without adding a duplicate resource.");

            _testCanvasObject = new GameObject("Profile route test canvas", typeof(Canvas));
            _testMenuManagerObject = new GameObject("Profile route test menu manager");
            var manager = (Component)_testMenuManagerObject.AddComponent(managerType);
            var singletonInstance = managerType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            if (singletonInstance == null)
            {
                InvokeLifecycle(manager, "Awake");
                singletonInstance = managerType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            }
            Assert.That(singletonInstance, Is.SameAs(manager), "MenuManager must initialize as the active singleton.");

            var layoutId = YARG.Core.Game.EliteDrumsLayoutPreset.FullId;
            var colorId = YARG.Core.Game.ColorProfile.CircularDefault.Id;
            // Use a profile already registered in PlayerContainer so the sidebar follows its
            // normal selected-profile setup instead of injecting its private _profile field.
            var profile = _testProfiles[0];
            profile.Name = "Elite route profile";
            profile.GameMode = GameMode.EliteDrums;
            profile.EliteDrumsLayout = layoutId;
            profile.ColorProfile = colorId;
            Invoke(_sidebar, "UpdateSidebar", profile, null);

            try
            {
                var configureButton = (Button)Field(_sidebar.GetType(), "_eliteLayoutConfigureButton").GetValue(_sidebar);
                configureButton.onClick.Invoke();
                var editorType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
                var editorInstance = _testCanvasObject.GetComponentInChildren(editorType, true);
                Assert.That(editorInstance, Is.Not.Null,
                    "The standard menu route should instantiate the production fullscreen resource under the canvas.");
                _testEditorObject = editorInstance.gameObject;
                var draft = editorType.GetProperty("Draft").GetValue(editorInstance);
                Assert.That(draft, Is.Not.Null, "Configure must initialize the selected layout draft.");
                Assert.That(((EliteDrumsLayoutPreset)draft).Id, Is.EqualTo(layoutId));
                var previewColor = Field(editorType, "_previewColorProfile").GetValue(editorInstance);
                Assert.That(((ColorProfile)previewColor).Id, Is.EqualTo(colorId));
                Assert.That(editorInstance.gameObject.activeSelf, Is.True);
                var menuStack = Field(managerType, "_openMenus").GetValue(manager);
                var currentMenu = menuStack.GetType().GetMethod("Peek").Invoke(menuStack, null);
                Assert.That(currentMenu.ToString(), Is.EqualTo("EliteDrumsLayout"),
                    "ShowMenu must place the fullscreen editor on the normal menu stack.");
            }
            finally
            {
                if (manager != null)
                {
                    var singletonType = typeof(MonoBehaviour).Assembly.GetType("UnityEngine.MonoSingleton`1");
                    // Destroying the manager resets the generic singleton through its lifecycle.
                    UnityEngine.Object.DestroyImmediate(_testMenuManagerObject);
                    _testMenuManagerObject = null;
                }
            }
        }

        [Test]
        public void SelectingTwoDifferentProductionProfileRowsUpdatesSidebarAndClearsOldSelection()
        {
            var profileViewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PROFILE_VIEW_PREFAB_PATH);
            Assert.That(profileViewPrefab, Is.Not.Null, "The production profile-row prefab must be available.");

            var profileViewType = Production("YARG.Menu.ProfileList.ProfileView");
            var selectionOriginType = Production("YARG.Menu.Navigation.SelectionOrigin");
            object programmaticOrigin = Enum.Parse(selectionOriginType, "Programmatically");
            var layoutDropdown = (Component)Field(_sidebar.GetType(), "_eliteLayoutDropdown").GetValue(_sidebar);
            Assert.That(layoutDropdown.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable")), Is.Null,
                "The Elite layout dropdown must not take focus away from the selected profile row.");
            var profileList = (Transform)Field(_menu.GetType(), "_profileList").GetValue(_menu);
            Assert.That(profileList, Is.Not.Null, "The production menu must retain its profile-list transform.");
            var rows = _testProfiles.Select(profile => CreateProfileRow(profileViewPrefab, profile)).ToArray();
            Component inactiveRow = null;

            try
            {
                Assert.That(rows.All(row => row.transform.parent == profileList), Is.True,
                    "Production rows must be instantiated under the menu's serialized profile list.");
                var sidebarContents = (GameObject)Field(_sidebar.GetType(), "_contents").GetValue(_sidebar);

                Invoke(rows[0], "SetSelected", true, programmaticOrigin);
                Assert.That((bool)rows[0].GetType().GetProperty("Selected").GetValue(rows[0]), Is.True);
                Assert.That(_navigationGroup.GetType().GetProperty("SelectedBehaviour").GetValue(_navigationGroup),
                    Is.SameAs(rows[0]), "The production navigation group must track the selected profile row.");
                Assert.That(sidebarContents.activeSelf, Is.True,
                    "Selecting an active profile must populate and show the sidebar.");
                Assert.That(GetSidebarProfileName(), Is.EqualTo(_testProfiles[0].Name));
                Assert.That(GetSidebarNoteSpeed(), Is.EqualTo(4f.ToString("0.0###", CultureInfo.CurrentCulture)));

                Invoke(rows[1], "SetSelected", true, programmaticOrigin);

                Assert.That((bool)rows[0].GetType().GetProperty("Selected").GetValue(rows[0]), Is.False,
                    "Selecting another profile must clear the first row's highlight.");
                Assert.That((bool)rows[1].GetType().GetProperty("Selected").GetValue(rows[1]), Is.True);
                Assert.That(_navigationGroup.GetType().GetProperty("SelectedBehaviour").GetValue(_navigationGroup),
                    Is.SameAs(rows[1]), "The production navigation group must move selection to the second row.");
                Assert.That(sidebarContents.activeSelf, Is.True);
                Assert.That(GetSidebarProfileName(), Is.EqualTo(_testProfiles[1].Name),
                    "The visible settings must switch to the selected profile.");
                Assert.That(GetSidebarNoteSpeed(), Is.EqualTo(7f.ToString("0.0###", CultureInfo.CurrentCulture)));

                inactiveRow = CreateProfileRow(profileViewPrefab, _inactiveProfile);
                Assert.That(inactiveRow.transform.parent, Is.SameAs(profileList),
                    "Disconnected profile rows must also be added under the production profile list.");
                Invoke(inactiveRow, "SetSelected", true, programmaticOrigin);
                Assert.That((bool)inactiveRow.GetType().GetProperty("Selected").GetValue(inactiveRow), Is.True,
                    "Selecting a disconnected profile must not abort row selection.");
                Assert.That(_navigationGroup.GetType().GetProperty("SelectedBehaviour").GetValue(_navigationGroup),
                    Is.SameAs(inactiveRow), "The production navigation group must track the disconnected row too.");
                Assert.That((bool)rows[1].GetType().GetProperty("Selected").GetValue(rows[1]), Is.False);
                Assert.That(sidebarContents.activeSelf, Is.False,
                    "Disconnected profiles do not show settings, but their selection must still complete.");
            }
            finally
            {
                if (inactiveRow != null)
                    UnityEngine.Object.DestroyImmediate(inactiveRow.gameObject);

                foreach (var row in rows)
                {
                    if (row != null)
                        UnityEngine.Object.DestroyImmediate(row.gameObject);
                }
            }
        }

        private Component CreateProfileRow(GameObject prefab, YargProfile profile)
        {
            var profileList = (Transform)Field(_menu.GetType(), "_profileList").GetValue(_menu);
            var rowObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, profileList);
            var row = rowObject.GetComponent(Production("YARG.Menu.ProfileList.ProfileView"));
            InvokeAwake(row);
            var coloredButtonType = Production("YARG.Menu.ColoredButton");
            foreach (var coloredButton in rowObject.GetComponentsInChildren(coloredButtonType, true))
            {
                InvokeLifecycle(coloredButton, "OnEnable");
            }
            Invoke(row, "Init", _menu, profile, _sidebar);
            AddNavigatable(row);
            return row;
        }

        private string GetSidebarNoteSpeed()
        {
            var field = Field(_sidebar.GetType(), "_noteSpeedField");
            var input = field.GetValue(_sidebar);
            return (string)input.GetType().GetProperty("text").GetValue(input);
        }

        private string GetSidebarProfileName()
        {
            var field = Field(_sidebar.GetType(), "_profileName");
            var text = field.GetValue(_sidebar);
            return (string)text.GetType().GetProperty("text").GetValue(text);
        }

        private void AddNavigatable(Component row)
        {
            var method = _navigationGroup.GetType().GetMethods()
                .Single(candidate => candidate.Name == "AddNavigatable" && candidate.GetParameters().Length == 1 &&
                                     candidate.GetParameters()[0].ParameterType.IsInstanceOfType(row));
            method.Invoke(_navigationGroup, new object[] { row });
        }

        private static void InvokeAwake(Component component) => InvokeLifecycle(component, "Awake");

        private static void InvokeLifecycle(Component component, string methodName)
        {
            for (var type = component.GetType(); type != null; type = type.BaseType)
            {
                var method = type.GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method == null)
                    continue;

                method.Invoke(component, null);
                return;
            }
        }
    }
}
