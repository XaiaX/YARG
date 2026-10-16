using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Game;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YARG.Tests.PlayMode
{
    public sealed class ProfileListSelectionPlayModeTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PRIVATE_STATIC = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags INHERITED_STATIC = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        private const string MENU_PREFAB_PATH = "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab";
        private const string HEADER_PREFAB_PATH = "Assets/Prefabs/Menu/ProfileList/ListHeader.prefab";
        private const string MENU_COLORS_PATH = "Assets/Art/Menu/Data/MenuColors.asset";

        private GameObject _host;
        private GameObject _menuDataObject;
        private object _originalSettings;
        private bool _originalSettingsInitialized;
        private bool _capturedSettings;
        private bool _ownsAudioManager;
        private IList _profiles;
        private IList _players;
        private IDictionary _playersByProfile;
        private IDictionary _profilesById;
        private IList _unloadedProfiles;
        private object[] _originalProfiles;
        private object[] _originalPlayers;
        private DictionaryEntry[] _originalPlayersByProfile;
        private DictionaryEntry[] _originalProfilesById;
        private object[] _originalUnloadedProfiles;
        private readonly List<YargProfile> _addedProfiles = new();

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static FieldInfo PrivateField(Type type, string name, bool isStatic = false) =>
            type.GetField(name, isStatic ? PRIVATE_STATIC : PRIVATE_INSTANCE);

        private static object Call(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == args.Length);
            return method.Invoke(target, args);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            _capturedSettings = false;
            _ownsAudioManager = false;
            SetUpSettingsAndAudio();
            SetUpMenuData();
            SetUpProductionMenu();
            SetUpProfiles();
            yield return null;
#else
            Assert.Ignore("Requires production prefab assets from the Unity Editor asset database.");
            yield break;
#endif
        }

#if UNITY_EDITOR
        private void SetUpSettingsAndAudio()
        {
            var settingsManager = Production("YARG.Settings.SettingsManager");
            var settingContainer = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
            var initialized = settingContainer.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static);
            _originalSettings = settingsManager.GetProperty("Settings").GetValue(null);
            _originalSettingsInitialized = (bool)initialized.GetValue(null);
            _capturedSettings = true;

            var audioHandler = Production("YARG.Core.Audio.GlobalAudioHandler");
            var audioInstance = audioHandler.GetField("_instance", PRIVATE_STATIC);
            if (audioInstance.GetValue(null) == null)
            {
                Production("YARG.Helpers.PathHelper").GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                var bassAudioManager = Production("YARG.Audio.BASS.BassAudioManager");
                audioHandler.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)
                    .MakeGenericMethod(bassAudioManager).Invoke(null, null);
                _ownsAudioManager = true;
            }

            if (_originalSettings == null)
            {
                var temporarySettings = Activator.CreateInstance(settingContainer);
                settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null,
                    new[] { temporarySettings });
                initialized.SetValue(null, true);
            }
        }

        private void SetUpMenuData()
        {
            var menuDataType = Production("YARG.Menu.Data.MenuData");
            if (menuDataType.GetProperty("Instance", INHERITED_STATIC).GetValue(null) != null)
                return;

            var colors = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MENU_COLORS_PATH);
            Assert.That(colors, Is.Not.Null, "The production menu colors asset must be available.");
            _menuDataObject = new GameObject("Profile selection PlayMode menu data");
            _menuDataObject.SetActive(false);
            var menuData = _menuDataObject.AddComponent(menuDataType);
            PrivateField(menuDataType, "_colors").SetValue(menuData, colors);
            _menuDataObject.SetActive(true);
        }

        private void SetUpProductionMenu()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MENU_PREFAB_PATH);
            Assert.That(prefab, Is.Not.Null, "The production profile-list menu prefab must be available.");
            _host = new GameObject("Profile selection PlayMode host");
            _host.SetActive(false);
            var menuObject = UnityEngine.Object.Instantiate(prefab, _host.transform, false);
            var menuType = Production("YARG.Menu.ProfileList.ProfileListMenu");
            var menu = menuObject.GetComponent(menuType);
            Assert.That(menu, Is.Not.Null);

            // Suppress only the app-level menu callbacks (navigation schemes and profile persistence).
            // The real child sidebar and NavigationGroup still receive Unity lifecycle callbacks.
            ((Behaviour)menu).enabled = false;
            _host.SetActive(true);
        }

        private void SetUpProfiles()
        {
            var playerContainer = Production("YARG.Player.PlayerContainer");
            _profiles = (IList)PrivateField(playerContainer, "_profiles", true).GetValue(null);
            _players = (IList)PrivateField(playerContainer, "_players", true).GetValue(null);
            _playersByProfile = (IDictionary)PrivateField(playerContainer, "_playersByProfile", true).GetValue(null);
            _profilesById = (IDictionary)PrivateField(playerContainer, "_profilesById", true).GetValue(null);
            _unloadedProfiles = (IList)PrivateField(playerContainer, "_unloadedProfiles", true).GetValue(null);
            _originalProfiles = _profiles.Cast<object>().ToArray();
            _originalPlayers = _players.Cast<object>().ToArray();
            _originalPlayersByProfile = _playersByProfile.Cast<DictionaryEntry>().ToArray();
            _originalProfilesById = _profilesById.Cast<DictionaryEntry>().ToArray();
            _originalUnloadedProfiles = _unloadedProfiles.Cast<object>().ToArray();
            _profiles.Clear();
            _players.Clear();
            _playersByProfile.Clear();
            _profilesById.Clear();
            _unloadedProfiles.Clear();

            var themePresetType = Production("YARG.Themes.ThemePreset");
            var defaultTheme = themePresetType.GetField("Default", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            var themeId = (Guid)themePresetType.GetField("Id", BindingFlags.Public | BindingFlags.Instance)
                .GetValue(defaultTheme);
            var profiles = new[]
            {
                NewProfile("PlayMode Guitar", GameMode.FiveFretGuitar, 4, themeId),
                NewProfile("PlayMode Elite Drums", GameMode.EliteDrums, 7, themeId),
                NewProfile("PlayMode Inactive Drums", GameMode.FiveLaneDrums, 9, themeId)
            };
            profiles[1].EliteDrumsLayout = EliteDrumsLayoutPreset.FullId;
            profiles[2].EliteDrumsLayout = Guid.NewGuid();

            var bindingsType = Production("YARG.Input.ProfileBindings");
            var playerType = Production("YARG.Player.YargPlayer");
            var constructor = playerType.GetConstructor(new[] { typeof(YargProfile), bindingsType });
            Assert.That(constructor, Is.Not.Null);
            for (int i = 0; i < profiles.Length; i++)
            {
                var profile = profiles[i];
                _profiles.Add(profile);
                _profilesById.Add(profile.Id, profile);
                _addedProfiles.Add(profile);
                if (i < 2)
                {
                    var player = constructor.Invoke(new object[] { profile, null });
                    _players.Add(player);
                    _playersByProfile.Add(profile, player);
                }
            }
        }

        private static YargProfile NewProfile(string name, GameMode mode, float noteSpeed, Guid themeId) => new()
        {
            Name = name,
            GameMode = mode,
            NoteSpeed = noteSpeed,
            EnginePreset = EnginePreset.Default.Id,
            ThemePreset = themeId
        };
#endif

        [UnityTest]
        public IEnumerator ProductionRowsKeepSelectionOnProfileWhileEliteDropdownRefreshes()
        {
#if UNITY_EDITOR
            var menuType = Production("YARG.Menu.ProfileList.ProfileListMenu");
            var menu = _host.GetComponentInChildren(menuType, true);
            var sidebarType = Production("YARG.Menu.ProfileList.ProfileSidebar");
            var sidebar = _host.GetComponentInChildren(sidebarType, true);
            var navigationGroup = (Component)PrivateField(menuType, "_navigationGroup").GetValue(menu);
            var profileList = (Transform)PrivateField(menuType, "_profileList").GetValue(menu);
            var headerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HEADER_PREFAB_PATH);
            Assert.That(headerPrefab, Is.Not.Null, "The production profile-list header prefab must be available.");
            PrivateField(menuType, "_profileListHeaderPrefab").SetValue(menu, headerPrefab);
            Call(menu, "RefreshList", (object)null);

            var profiles = _addedProfiles.ToArray();
            Assert.That(profiles.Select(profile => profile.GameMode), Is.EquivalentTo(new[]
            {
                GameMode.FiveFretGuitar, GameMode.EliteDrums, GameMode.FiveLaneDrums
            }));
            Assert.That(_players.Count, Is.EqualTo(2), "The fixture must contain exactly two active profiles.");
            Assert.That(_profiles.Count, Is.EqualTo(3), "The fixture must contain exactly three profiles.");
            var profileViewType = Production("YARG.Menu.ProfileList.ProfileView");
            var rows = profiles.Select(profile => profileList.GetComponentsInChildren(profileViewType, true)
                    .Cast<Component>().Single(row => ReferenceEquals(row.GetType().GetProperty("Profile").GetValue(row), profile)))
                .ToArray();
            Assert.That(rows, Has.Length.EqualTo(3),
                "Production RefreshList must create all three mixed profile rows.");

            var layoutDropdown = (Component)PrivateField(sidebar.GetType(), "_eliteLayoutDropdown").GetValue(sidebar);
            var runtimeNavigatableType = Production("YARG.Menu.Navigation.RuntimeNavigatable");
            var selectionEventCount = 0;
            var eventCounter = new Action(() => selectionEventCount++);
            var selectionDelegateType = navigationGroup.GetType().GetNestedType("SelectionAction");
            var selectionParameters = selectionDelegateType.GetMethod("Invoke").GetParameters()
                .Select(parameter => System.Linq.Expressions.Expression.Parameter(parameter.ParameterType,
                    parameter.Name)).ToArray();
            var selectionHandler = System.Linq.Expressions.Expression.Lambda(selectionDelegateType,
                System.Linq.Expressions.Expression.Invoke(System.Linq.Expressions.Expression.Constant(eventCounter)),
                selectionParameters).Compile();
            var selectionEvent = navigationGroup.GetType().GetEvent("SelectionChanged");
            selectionEvent.AddEventHandler(navigationGroup, selectionHandler);
            Assert.That(layoutDropdown.GetComponent(runtimeNavigatableType), Is.Null,
                "The production Elite Drums selector must not take navigation focus from the profile row.");

            Select(rows[0]);
            Assert.That(IsSelected(rows[0]), Is.True);
            Assert.That(IsSelected(rows[1]), Is.False);
            Assert.That(IsSelected(rows[2]), Is.False);
            Assert.That(Selected(navigationGroup), Is.SameAs(rows[0]));
            Assert.That(SidebarContents(sidebar).activeSelf, Is.True);
            Assert.That(SidebarName(sidebar), Is.EqualTo(profiles[0].Name));
            Assert.That(SidebarNoteSpeed(sidebar), Is.EqualTo(4f.ToString("0.0###", CultureInfo.CurrentCulture)));

            Assert.That(layoutDropdown.GetComponent(runtimeNavigatableType), Is.Null,
                "Selecting a profile must not attach a separate navigatable to its layout dropdown.");
            Assert.That(layoutDropdown.transform.Find("SelectedOutline"), Is.Null,
                "The layout dropdown must not acquire a persistent selected outline.");
            InvokeValueChanged(layoutDropdown, (int)layoutDropdown.GetType().GetProperty("value").GetValue(layoutDropdown) == 0 ? 1 : 0);
            Assert.That(Selected(navigationGroup), Is.SameAs(rows[0]),
                "Changing the layout must leave the profile row selected.");
            Assert.That(IsSelected(rows[0]), Is.True,
                "Changing the layout must preserve the profile row highlight.");

            Select(rows[1]);
            Assert.That(IsSelected(rows[0]), Is.False);
            Assert.That(IsSelected(rows[1]), Is.True);
            Assert.That(IsSelected(rows[2]), Is.False);
            Assert.That(Selected(navigationGroup), Is.SameAs(rows[1]));
            Assert.That(SidebarContents(sidebar).activeSelf, Is.True);
            Assert.That(SidebarName(sidebar), Is.EqualTo(profiles[1].Name));
            Assert.That(SidebarNoteSpeed(sidebar), Is.EqualTo(7f.ToString("0.0###", CultureInfo.CurrentCulture)));
            Assert.That((int)layoutDropdown.GetType().GetProperty("value").GetValue(layoutDropdown), Is.EqualTo(1),
                "The dropdown refresh must display the selected profile's saved Full layout.");

            Guid otherProfileLayout = profiles[2].EliteDrumsLayout;
            Call(menu, "RefreshEliteLayoutDropdown", layoutDropdown, profiles[1]);
            Assert.That(profiles[1].EliteDrumsLayout, Is.EqualTo(EliteDrumsLayoutPreset.FullId),
                "Refreshing the selector must preserve the selected profile's saved layout.");
            Assert.That(profiles[2].EliteDrumsLayout, Is.EqualTo(otherProfileLayout),
                "Refreshing one profile must not change another profile's saved layout GUID.");

            Select(rows[2]);
            Assert.That(IsSelected(rows[0]), Is.False);
            Assert.That(IsSelected(rows[1]), Is.False);
            Assert.That(IsSelected(rows[2]), Is.True);
            Assert.That(Selected(navigationGroup), Is.SameAs(rows[2]));
            Assert.That(SidebarContents(sidebar).activeSelf, Is.False,
                "Selecting a disconnected profile should hide settings without leaving stale active rows selected.");
            Assert.That(profiles[2].EliteDrumsLayout, Is.EqualTo(otherProfileLayout));
            yield return null;
#else
            Assert.Ignore("Requires production prefab assets from the Unity Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator RefreshListKeepsEliteDropdownUnattachedAndRebindsOneListener()
        {
#if UNITY_EDITOR
            var menuType = Production("YARG.Menu.ProfileList.ProfileListMenu");
            var menu = _host.GetComponentInChildren(menuType, true);
            var sidebarType = Production("YARG.Menu.ProfileList.ProfileSidebar");
            var sidebar = _host.GetComponentInChildren(sidebarType, true);
            var navigationGroup = (Component)PrivateField(menuType, "_navigationGroup").GetValue(menu);
            var runtimeNavigatableType = Production("YARG.Menu.Navigation.RuntimeNavigatable");
            var headerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HEADER_PREFAB_PATH);
            Assert.That(headerPrefab, Is.Not.Null, "The production profile-list header prefab must be available.");
            PrivateField(menuType, "_profileListHeaderPrefab").SetValue(menu, headerPrefab);
            var eliteProfile = _addedProfiles.Single(profile => profile.GameMode == GameMode.EliteDrums);
            var guitarProfile = _addedProfiles.Single(profile => profile.GameMode == GameMode.FiveFretGuitar);

            // Initial selection leaves the profile row as the only selected navigation item.
            Call(menu, "RefreshList", (object)null);
            yield return null;
            var dropdown = (Component)PrivateField(sidebar.GetType(), "_eliteLayoutDropdown").GetValue(sidebar);
            Assert.That(dropdown, Is.Not.Null, "The production sidebar prefab must contain the Elite Drums selector.");
            Select(ProfileRow(menu, eliteProfile));

            Assert.That(dropdown.GetComponent(runtimeNavigatableType), Is.Null,
                "Profile selection must not add a RuntimeNavigatable to the Elite Drums selector.");
            Assert.That(dropdown.transform.Find("SelectedOutline"), Is.Null,
                "Profile selection must not create a unique persistent dropdown outline.");

            // Simulate a stale component left by an earlier refresh and verify cleanup follows
            // the component's normal destruction path, including its generated outline.
            var staleNavigatable = (Component)dropdown.gameObject.AddComponent(runtimeNavigatableType);
            var forwarderType = Production("YARG.Menu.Navigation.DropdownClickForwarder");
            var staleForwarder = (Behaviour)dropdown.gameObject.GetComponent(forwarderType);
            Assert.That(staleForwarder, Is.Not.Null,
                "RuntimeNavigatable must own a dropdown click forwarder on its child TMP dropdown.");
            Assert.That(forwarderType.GetField("Target").GetValue(staleForwarder), Is.SameAs(staleNavigatable));
            var staleOutline = new GameObject("SelectedOutline", typeof(RectTransform));
            staleOutline.transform.SetParent(dropdown.transform, false);
            Call(menu, "RefreshEliteLayoutDropdown", dropdown, eliteProfile);
            Assert.That((bool)staleForwarder.GetType().GetProperty("enabled").GetValue(staleForwarder), Is.False,
                "Refresh must disable the stale click forwarder immediately, before deferred Destroy runs.");
            Assert.That(forwarderType.GetField("Target").GetValue(staleForwarder), Is.Null,
                "A stale forwarder must no longer retain or select its old navigatable.");
            Assert.That((bool)dropdown.GetType().GetProperty("enabled").GetValue(dropdown), Is.True,
                "Cleaning stale navigation must leave the normal TMP dropdown enabled for pointer input.");
            yield return null;
            Assert.That(dropdown.GetComponent(forwarderType), Is.Null,
                "Refreshing the dropdown must destroy the stale owned click forwarder.");
            Assert.That(dropdown.GetComponent(runtimeNavigatableType), Is.Null,
                "Refreshing the dropdown must remove any stale RuntimeNavigatable.");
            Assert.That(dropdown.transform.Find("SelectedOutline"), Is.Null,
                "Refreshing the dropdown must remove any stale selected outline.");

            // RefreshList rebuilds the rows and clears the group. The profile row should be
            // reselected normally, with no lingering dropdown membership to clean up.
            Call(menu, "RefreshList", (object)null);
            yield return null;

            Assert.That(dropdown.GetComponent(runtimeNavigatableType), Is.Null,
                "Repeated RefreshList calls must not attach a RuntimeNavigatable.");
            Assert.That(Selected(navigationGroup), Is.Null,
                "RefreshList must clear the group's selection along with its membership.");
            Assert.That(SidebarContents(sidebar).activeSelf, Is.False,
                "RefreshList must hide the sidebar contents when rebuilding the list.");

            // Reselecting refreshes the dropdown callback, but must not give the dropdown
            // navigation membership or move focus away from the selected profile row.
            var eliteRow = ProfileRow(menu, eliteProfile);
            Select(eliteRow);

            Assert.That(Selected(navigationGroup), Is.SameAs(eliteRow),
                "Reselection must keep navigation focus on the profile row.");
            Assert.That(dropdown.GetComponent(runtimeNavigatableType), Is.Null,
                "Reselection must not register the dropdown as a navigatable.");
            Assert.That(RuntimeListenerCount(dropdown), Is.EqualTo(1),
                "Reselecting after refresh must leave exactly one onValueChanged listener.");

            // Another selection round-trip exercises the already-registered path: no duplicate
            // membership and no duplicated listener.
            Select(ProfileRow(menu, guitarProfile));
            Select(ProfileRow(menu, eliteProfile));

            Assert.That(Selected(navigationGroup), Is.SameAs(ProfileRow(menu, eliteProfile)),
                "Switching profiles and back must leave the Elite profile row selected.");
            Assert.That(RuntimeListenerCount(dropdown), Is.EqualTo(1),
                "Repeated profile switches must not duplicate the onValueChanged listener.");

            // The dropdown must still work: it shows the Elite profile's saved layout, and a
            // changed selection writes through the rebound listener onto that profile.
            var layouts = UsableEliteLayouts();
            Assert.That(layouts.Count, Is.GreaterThanOrEqualTo(2),
                "The fixture needs at least two usable Elite Drums layouts to prove the rebind.");
            int savedIndex = LayoutIndex(layouts, eliteProfile);
            Assert.That((int)dropdown.GetType().GetProperty("value").GetValue(dropdown), Is.EqualTo(savedIndex),
                "The refreshed dropdown must display the Elite profile's saved layout.");
            int otherIndex = savedIndex == 0 ? 1 : 0;
            InvokeValueChanged(dropdown, otherIndex);
            Assert.That(eliteProfile.EliteDrumsLayout, Is.EqualTo(LayoutId(layouts[otherIndex])),
                "The dropdown listener must write a changed layout onto the selected Elite profile.");
            yield return null;
#else
            Assert.Ignore("Requires production prefab assets from the Unity Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator HiddenProfileMenuRestoresSelectionAndSavedLayoutUnlessProfileWasDeleted()
        {
#if UNITY_EDITOR
            var menuType = Production("YARG.Menu.ProfileList.ProfileListMenu");
            var menu = _host.GetComponentInChildren(menuType, true);
            var sidebarType = Production("YARG.Menu.ProfileList.ProfileSidebar");
            var sidebar = _host.GetComponentInChildren(sidebarType, true);
            var headerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HEADER_PREFAB_PATH);
            Assert.That(headerPrefab, Is.Not.Null);
            PrivateField(menuType, "_profileListHeaderPrefab").SetValue(menu, headerPrefab);
            var eliteProfile = _addedProfiles.Single(profile => profile.GameMode == GameMode.EliteDrums);
            var layoutDropdown = (Component)PrivateField(sidebarType, "_eliteLayoutDropdown").GetValue(sidebar);

            Call(menu, "RefreshList", (object)null);
            yield return null;
            Select(ProfileRow(menu, eliteProfile));
            int savedLayoutIndex = LayoutIndex(UsableEliteLayouts(), eliteProfile);
            Assert.That((int)layoutDropdown.GetType().GetProperty("value").GetValue(layoutDropdown),
                Is.EqualTo(savedLayoutIndex));

            // The fullscreen menu deactivates the profile screen and its rows before the
            // profile list's selection is captured by the menu's disable callback.
            var profileListTransform = (Transform)PrivateField(menuType, "_profileList").GetValue(menu);
            profileListTransform.gameObject.SetActive(false);
            Call(menu, "CaptureSelectedProfile");
            profileListTransform.gameObject.SetActive(true);
            Call(menu, "RefreshListAfterReactivation");
            yield return null;

            var restoredRow = ProfileRow(menu, eliteProfile);
            Assert.That(IsSelected(restoredRow), Is.True,
                "Returning from fullscreen Configure must reselect the previously active profile.");
            Assert.That(SidebarContents(sidebar).activeSelf, Is.True,
                "Restoring selection must also restore the profile settings sidebar.");
            Assert.That(SidebarName(sidebar), Is.EqualTo(eliteProfile.Name));
            Assert.That((int)layoutDropdown.GetType().GetProperty("value").GetValue(layoutDropdown),
                Is.EqualTo(savedLayoutIndex),
                "The restored sidebar must show the profile's saved Elite layout.");

            // A selection may become stale while the fullscreen menu is open (for example, if
            // another action deletes the profile). It must not be resurrected on reactivation.
            Call(menu, "CaptureSelectedProfile");
            var player = _playersByProfile[eliteProfile];
            _players.Remove(player);
            _playersByProfile.Remove(eliteProfile);
            _profiles.Remove(eliteProfile);
            _profilesById.Remove(eliteProfile.Id);
            try
            {
                Call(menu, "RefreshListAfterReactivation");
                yield return null;

                var profileViewType = Production("YARG.Menu.ProfileList.ProfileView");
                var profileList = (Transform)PrivateField(menuType, "_profileList").GetValue(menu);
                Assert.That(profileList.GetComponentsInChildren(profileViewType, true).Cast<Component>()
                    .Where(IsSelected), Is.Empty,
                    "A deleted profile must not remain selected after the list is rebuilt.");
                Assert.That(SidebarContents(sidebar).activeSelf, Is.False,
                    "A deleted profile must not leave its old settings visible.");
            }
            finally
            {
                _players.Add(player);
                _playersByProfile.Add(eliteProfile, player);
                _profiles.Add(eliteProfile);
                _profilesById.Add(eliteProfile.Id, eliteProfile);
            }

#else
            Assert.Ignore("Requires production prefab assets from the Unity Editor asset database.");
            yield break;
#endif
        }

#if UNITY_EDITOR
        private static void Select(Component row) => Call(row, "SetSelected", true,
            Enum.Parse(Production("YARG.Menu.Navigation.SelectionOrigin"), "Programmatically"));

        private static bool IsSelected(Component row) => (bool)row.GetType().GetProperty("Selected").GetValue(row);

        private static object Selected(Component group) =>
            group.GetType().GetProperty("SelectedBehaviour").GetValue(group);

        private static GameObject SidebarContents(Component sidebar) =>
            (GameObject)PrivateField(sidebar.GetType(), "_contents").GetValue(sidebar);

        private static string SidebarName(Component sidebar)
        {
            var text = PrivateField(sidebar.GetType(), "_profileName").GetValue(sidebar);
            return (string)text.GetType().GetProperty("text").GetValue(text);
        }

        private static string SidebarNoteSpeed(Component sidebar)
        {
            var input = PrivateField(sidebar.GetType(), "_noteSpeedField").GetValue(sidebar);
            return (string)input.GetType().GetProperty("text").GetValue(input);
        }

        private static Component ProfileRow(Component menu, YargProfile profile)
        {
            var profileList = (Transform)PrivateField(menu.GetType(), "_profileList").GetValue(menu);
            var profileViewType = Production("YARG.Menu.ProfileList.ProfileView");
            return profileList.GetComponentsInChildren(profileViewType, true)
                .Cast<Component>().Single(row =>
                    ReferenceEquals(row.GetType().GetProperty("Profile").GetValue(row), profile));
        }

        private static int RuntimeListenerCount(Component dropdown)
        {
            var changed = dropdown.GetType().GetProperty("onValueChanged").GetValue(dropdown);
            Assert.That(changed, Is.Not.Null, "The selector must expose an onValueChanged UnityEvent.");
            var calls = InstanceFieldValue(changed, "m_Calls");
            var runtimeCalls = calls.GetType().GetField("m_RuntimeCalls", PRIVATE_INSTANCE)
                ?.GetValue(calls) as ICollection;
            Assert.That(runtimeCalls, Is.Not.Null,
                "Unexpected UnityEvent internals: the runtime listener list field is missing.");
            return runtimeCalls.Count;
        }

        private static object InstanceFieldValue(object target, string name)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, PRIVATE_INSTANCE);
                if (field != null)
                    return field.GetValue(target);
            }

            Assert.Fail($"Field {name} was not found on {target.GetType()} or its base types.");
            return null;
        }

        private static IReadOnlyList<object> UsableEliteLayouts()
        {
            var container = Production("YARG.Settings.Customization.CustomContentManager")
                .GetField("EliteDrumsLayouts", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            Assert.That(container, Is.Not.Null, "The Elite Drums layout container must be initialized.");
            return (IReadOnlyList<object>)container.GetType().GetProperty("UsablePresets").GetValue(container);
        }

        private static Guid LayoutId(object layout) => (Guid)layout.GetType()
            .GetField("Id", BindingFlags.Public | BindingFlags.Instance).GetValue(layout);

        private static int LayoutIndex(IReadOnlyList<object> layouts, YargProfile profile)
        {
            for (int i = 0; i < layouts.Count; i++)
            {
                if (LayoutId(layouts[i]) == profile.EliteDrumsLayout)
                    return i;
            }

            Assert.Fail($"The usable Elite Drums layouts must contain the profile's saved layout {profile.EliteDrumsLayout}.");
            return -1;
        }

        private static void InvokeValueChanged(Component dropdown, int index)
        {
            var changed = dropdown.GetType().GetProperty("onValueChanged").GetValue(dropdown);
            changed.GetType().GetMethod("Invoke").Invoke(changed, new object[] { index });
        }
#endif

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_host != null)
                UnityEngine.Object.Destroy(_host);
            if (_menuDataObject != null)
                UnityEngine.Object.Destroy(_menuDataObject);
            yield return null;

            if (_originalProfiles != null)
            {
                _profiles.Clear();
                _players.Clear();
                _playersByProfile.Clear();
                _profilesById.Clear();
                _unloadedProfiles.Clear();
                foreach (var profile in _originalProfiles) _profiles.Add(profile);
                foreach (var player in _originalPlayers) _players.Add(player);
                foreach (var entry in _originalPlayersByProfile) _playersByProfile.Add(entry.Key, entry.Value);
                foreach (var entry in _originalProfilesById) _profilesById.Add(entry.Key, entry.Value);
                foreach (var record in _originalUnloadedProfiles) _unloadedProfiles.Add(record);
            }
            _addedProfiles.Clear();

            try
            {
                if (_capturedSettings)
                {
                    var settingsManager = Production("YARG.Settings.SettingsManager");
                    var settingContainer = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
                    settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null,
                        new[] { _originalSettings });
                    settingContainer.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static)
                        .SetValue(null, _originalSettingsInitialized);
                }
            }
            finally
            {
                _capturedSettings = false;
                if (_ownsAudioManager)
                {
                    Production("YARG.Core.Audio.GlobalAudioHandler")
                        .GetMethod("Close", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                    _ownsAudioManager = false;
                }
            }
        }
    }
}
