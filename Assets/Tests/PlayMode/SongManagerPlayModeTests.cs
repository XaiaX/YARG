using System;
using System.Collections;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using YARG.Core.Song.Cache;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YARG.Tests.PlayMode
{
    public sealed partial class EliteDrumsLayoutMenuPlayModeTests
    {
        private static string SourceLabel(string key) => (string)Production("YARG.Localization.Localize")
            .GetMethod("Key", new[] { typeof(string) }).Invoke(null, new object[] { "Menu.Settings.Sources." + key });

        private static string[] SourceTexts(GameObject container) => container
            .GetComponentsInChildren(Production("TMPro.TextMeshProUGUI"))
            .Select(label => (string)label.GetType().GetProperty("text").GetValue(label)).ToArray();

        private static Button SourceButton(GameObject container, string key, int index = 0)
        {
            string expected = SourceLabel(key);
            if (key == "Select" || key == "Clear")
            {
                var rows = container.GetComponentsInChildren(Production("YARG.Menu.Settings.SettingsDirectory"));
                var sources = rows.Where(row => row.GetType().GetField("_sourceScope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(row) != null).ToArray();
                var rowButtons = sources[index].GetComponentsInChildren<Button>();
                string method = key == "Select" ? "Browse" : "Remove";
                return rowButtons.Single(button => Enumerable.Range(0, button.onClick.GetPersistentEventCount())
                    .Any(i => button.onClick.GetPersistentMethodName(i) == method));
            }
            if (key == "ScanAll")
            {
                var header = (Component)container.GetComponentInChildren(Production("YARG.Menu.Settings.SongManagerHeader"));
                return header.GetComponentsInChildren<Button>().Single(button => Enumerable.Range(0, button.onClick.GetPersistentEventCount())
                    .Any(i => button.onClick.GetPersistentMethodName(i) == "RefreshSongs"));
            }
            if (key == "Help" || key == "AddFolder" || key == "ScanSongs" || key == "ScanUpdates" || key == "ScanUpgrades")
            {
                if (key != "Help")
                {
                    string section = key == "AddFolder" ? "Songs" : key.Substring(4);
                    string action = key == "AddFolder" ? "0" : section == "Songs" ? "1" : "0";
                    return container.transform.Find("SongSources." + section)
                        .GetComponentsInChildren<Button>().Single(button => button.name == "SongSources." + section + "." + action + ".Button");
                }
                return container.GetComponentsInChildren(Production("YARG.Menu.ColoredButton"))
                    .Single(control => SourceTexts(((Component)control).gameObject).Contains(expected))
                    .GetComponentInChildren<Button>();
            }
            var navigatables = container.GetComponentsInChildren(Production("YARG.Menu.Navigation.NavigatableUnityButton"), true);
            var matches = navigatables.Select(nav =>
                {
                    // SettingsButton clones Button Template beneath Button Container. The TextMeshPro label
                    // and Unity Button are sibling descendants of that clone, not parent/child of each other.
                    var template = ((Component)nav).transform.parent;
                    var labels = template.GetComponentsInChildren(Production("TMPro.TextMeshProUGUI"), true);
                    var label = labels.FirstOrDefault(item =>
                        (string)item.GetType().GetProperty("text").GetValue(item) == expected);
                    if (label == null) return null;
                    var button = ((Component)nav).GetComponent<Button>();
                    return button != null ? button : template.GetComponentInChildren<Button>(true);
                })
                .Where(button => button != null && button.gameObject.activeInHierarchy).ToArray();
            Assert.That(matches.Length, Is.GreaterThan(index),
                $"Missing active source button '{expected}' at {index}. Navigation templates: " + string.Join("; ", navigatables.Select(nav =>
                {
                    var template = ((Component)nav).transform.parent;
                    return $"{template.name}: active={template.gameObject.activeInHierarchy}, labels=[" +
                        string.Join(" | ", template.GetComponentsInChildren(Production("TMPro.TextMeshProUGUI"), true)
                            .Select(label => (string)label.GetType().GetProperty("text").GetValue(label))) + "]";
                })));
            return matches[index];
        }

        private static void ClickSource(GameObject container, string key, int index = 0)
        {
            var button = SourceButton(container, key, index);
            Assert.That(button.interactable, Is.True, $"Source button {key} must be enabled before activation.");
            button.onClick.Invoke();
        }

        private sealed class SongManagerInteractionState : IDisposable
        {
            public readonly GameObject Container;
            public readonly Component Navigation;
            public readonly object Settings;
            public readonly Type Actions = Production("YARG.Menu.Settings.SongSourceActions");
            private readonly Dictionary<FieldInfo, object> _saved = new();
            private readonly GameObject _menu;
            private readonly object _feedback;
            private readonly Dictionary<string, string> _language;

            public SongManagerInteractionState(EliteDrumsLayoutMenuPlayModeTests fixture)
            {
                fixture.MountMenuServices();
                var localization = Production("YARG.Localization.LocalizationManager");
                var language = (Dictionary<string, string>)localization.GetField("_localizationMap", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                _language = new Dictionary<string, string>(language);
                Assert.That((bool)localization.GetMethod("TryParseAndLoadLanguage", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { "en-US" }), Is.True, "Load the shipped language before building production labels.");
                Assert.That(SourceLabel("Select"), Is.EqualTo("Select / Replace DTA"));
                Assert.That((bool)Actions.GetProperty("Busy").GetValue(null), Is.False, "A previous test must not leave a refresh active.");
                Settings = Production("YARG.Settings.SettingsManager").GetProperty("Settings").GetValue(null);
                foreach (string name in new[] { "SongFolders", "SongUpdatesDtaPath", "SongUpgradesDtaPath", "ShowExplicitSongSourcesWarning" })
                    Save(Settings.GetType().GetField(name), Settings);
                Settings.GetType().GetField("SongFolders").SetValue(Settings, new List<string>());
                Settings.GetType().GetField("SongUpdatesDtaPath").SetValue(Settings, string.Empty);
                Settings.GetType().GetField("SongUpgradesDtaPath").SetValue(Settings, string.Empty);
                Settings.GetType().GetField("ShowExplicitSongSourcesWarning").SetValue(Settings, false);
                var initialized = Settings.GetType().GetField("IsInitialized");
                Save(initialized, null);
                initialized.SetValue(null, false); // Do not write the operator's settings file.
                foreach (string name in new[] { "ChooseFile", "Refresh", "IsRefreshing" }) Save(Actions.GetField(name), null);
                _feedback = Actions.GetProperty("Feedback").GetValue(null);
                Actions.GetField("IsRefreshing").SetValue(null, new Func<bool>(() => false));
                fixture._menuCanvasHost = new GameObject("Song Manager interaction Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                fixture._menuCanvasHost.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                Container = new GameObject("Song Manager", typeof(RectTransform), typeof(VerticalLayoutGroup));
                Container.transform.SetParent(fixture._menuCanvasHost.transform, false);
                var settingsLayout = Container.GetComponent<VerticalLayoutGroup>();
                settingsLayout.childControlHeight = false;
                settingsLayout.childForceExpandHeight = true;
                Navigation = Container.AddComponent(Production("YARG.Menu.Navigation.NavigationGroup"));
                var tab = ((IEnumerable)Production("YARG.Settings.SettingsManager").GetField("DisplayedSettingsTabs").GetValue(null))
                    .Cast<object>().Single(item => item.GetType() == Production("YARG.Settings.Metadata.SongManagerTab"));
                _menu = new GameObject("Inactive Song Manager rebuild host");
                _menu.SetActive(false);
                var menu = _menu.AddComponent(Production("YARG.Menu.Settings.SettingsMenu"));
                var singleton = menu.GetType().GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                // The inactive host may not have received Awake yet; set only its singleton backing field.
                for (Type type = menu.GetType(); type != null; type = type.BaseType)
                {
                    var field = type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                        .FirstOrDefault(candidate => candidate.FieldType == menu.GetType());
                    if (field == null) continue;
                    Save(field, null);
                    field.SetValue(null, null);
                    break;
                }
                menu.GetType().BaseType.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
                Assert.That(singleton.GetValue(null), Is.SameAs(menu));
                Assert.That(_menu.activeSelf, Is.False, "SettingsMenu.SingletonAwake must leave its rebuild host inactive.");
                menu.GetType().GetField("_settingsNavGroup", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(menu, Navigation);
                menu.GetType().GetField("_settingsContainer", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(menu, Container.transform);
                menu.GetType().GetProperty("CurrentTab").GetSetMethod(true).Invoke(menu, new[] { tab });
                Call(tab, "BuildSettingTab", Container.transform, Navigation);
                Assert.That(Container.GetComponentsInChildren<Button>().Length, Is.GreaterThanOrEqualTo(10));
                Assert.That(SourceButton(Container, "Select").interactable, Is.True);
                Assert.That(SourceButton(Container, "ScanUpgrades").interactable, Is.True);
            }

            private void Save(FieldInfo field, object target) => _saved.Add(field, field.GetValue(target));
            public void Dispose()
            {
                Actions.GetProperty("Feedback").GetSetMethod(true).Invoke(null, new[] { _feedback });
                foreach (var pair in _saved)
                    pair.Key.SetValue(pair.Key.IsStatic ? null : Settings, pair.Value);
                UnityEngine.Object.DestroyImmediate(_menu);
                var language = (Dictionary<string, string>)Production("YARG.Localization.LocalizationManager")
                    .GetField("_localizationMap", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                language.Clear();
                foreach (var pair in _language) language.Add(pair.Key, pair.Value);
            }
        }

        [UnityTest]
        public IEnumerator SongManagerPickerClearAndScopeDispatch()
        {
            using var state = new SongManagerInteractionState(this);
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "arbitrary-community.dta");
            File.WriteAllText(path, "()");
            var scopes = new List<SongScanScope>();
            Action<string> selected = null;
            int pickerCalls = 0;
            state.Actions.GetField("ChooseFile").SetValue(null, new Action<string, string, Action<string>>((start, extension, callback) =>
            { Assert.That(extension, Is.EqualTo("dta")); pickerCalls++; selected = callback; }));
            state.Actions.GetField("Refresh").SetValue(null, new Func<SongScanScope, Task>(scope =>
            { scopes.Add(scope); return Task.CompletedTask; }));
            try
            {
                yield return null;
                ClickSource(state.Container, "Select");
                Assert.That(pickerCalls, Is.EqualTo(1));
                Assert.That(selected, Is.Not.Null);
                // Cancellation is the picker returning without invoking its selected callback.
                var cancelledLabels = SourceTexts(state.Container);
                selected = null;
                yield return null;
                Assert.That(SourceTexts(state.Container), Is.EqualTo(cancelledLabels));
                Assert.That(state.Settings.GetType().GetField("SongUpdatesDtaPath").GetValue(state.Settings), Is.Empty);
                ClickSource(state.Container, "Select");
                selected(path);
                yield return null;
                Assert.That(state.Settings.GetType().GetField("SongUpdatesDtaPath").GetValue(state.Settings), Is.EqualTo(path));
                var pathDisplays = state.Container.GetComponentsInChildren(Production("YARG.Menu.Settings.SongSourcePathDisplay"));
                Assert.That(pathDisplays.Length, Is.GreaterThanOrEqualTo(2), "Both production source rows must use measured path displays.");
                ClickSource(state.Container, "Select", 1);
                selected(path);
                yield return null;
                Assert.That(state.Settings.GetType().GetField("SongUpgradesDtaPath").GetValue(state.Settings), Is.EqualTo(path));
                foreach (string key in new[] { "ScanSongs", "ScanUpdates", "ScanUpgrades", "ScanAll" })
                { ClickSource(state.Container, key); yield return null; }
                Assert.That(scopes, Is.EqualTo(new[] { SongScanScope.Songs, SongScanScope.Updates, SongScanScope.Upgrades, SongScanScope.All }));
                SourceButton(state.Container, "Clear").onClick.Invoke();
                yield return null;
                Assert.That(state.Settings.GetType().GetField("SongUpdatesDtaPath").GetValue(state.Settings), Is.Empty);
                Assert.That(state.Settings.GetType().GetField("SongUpgradesDtaPath").GetValue(state.Settings), Is.EqualTo(path));
                SourceButton(state.Container, "ScanUpdates").onClick.Invoke();
                yield return null;
                Assert.That(scopes.Last(), Is.EqualTo(SongScanScope.Updates), "Clear must leave removal scanning available.");
                SourceButton(state.Container, "Clear", 1).onClick.Invoke();
                yield return null;
                Assert.That(state.Settings.GetType().GetField("SongUpgradesDtaPath").GetValue(state.Settings), Is.Empty);
                ClickSource(state.Container, "Select");
                state.Settings.GetType().GetField("SongUpdatesDtaPath").SetValue(state.Settings, "newer-selection.dta");
                selected(path);
                Assert.That(state.Settings.GetType().GetField("SongUpdatesDtaPath").GetValue(state.Settings), Is.EqualTo("newer-selection.dta"),
                    "A stale picker callback must not overwrite a newer selection.");
            }
            finally { Directory.Delete(directory, true); }
        }

        [UnityTest]
        public IEnumerator SongManagerShowsSourceDefinitionsRatherThanAffectedSongs()
        {
            using var state = new SongManagerInteractionState(this);
            var containerType = Production("YARG.Song.SongContainer");
            var appliedProperty = containerType.GetProperty("AppliedSources");
            var countProperty = containerType.GetProperty("UpdatesDefinitionCount");
            var resultProperty = containerType.GetProperty("LastScanResult");
            object oldApplied = appliedProperty.GetValue(null);
            object oldCount = countProperty.GetValue(null);
            object oldResult = resultProperty.GetValue(null);
            string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dta");
            File.WriteAllText(file, "()");
            try
            {
                state.Settings.GetType().GetField("SongUpdatesDtaPath").SetValue(state.Settings, file);
                appliedProperty.GetSetMethod(true).Invoke(null, new object[] { new SongSourceConfiguration(Array.Empty<string>(), file, "") });
                countProperty.GetSetMethod(true).Invoke(null, new object[] { 1842 });
                // The row reads the published source count independently of the last attempt's result.
                resultProperty.GetSetMethod(true).Invoke(null, new object[] { null });
                var menuType = Production("YARG.Menu.Settings.SettingsMenu");
                var menu = menuType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy).GetValue(null);
                menuType.GetMethod("RefreshSettingsKeepPosition").Invoke(menu, null);
                yield return null;
                var expected = string.Format(SourceLabel("EntryCount"), 1842);
                Assert.That(SourceTexts(state.Container), Does.Contain(expected));
                countProperty.GetSetMethod(true).Invoke(null, new object[] { 0 });
                menuType.GetMethod("RefreshSettingsKeepPosition").Invoke(menu, null);
                yield return null;
                var zero = string.Format(SourceLabel("EntryCount"), 0);
                Assert.That(SourceTexts(state.Container), Does.Contain(zero), "An applied empty DTA is scanned, not scan-needed.");
            }
            finally
            {
                appliedProperty.GetSetMethod(true).Invoke(null, new[] { oldApplied });
                countProperty.GetSetMethod(true).Invoke(null, new[] { oldCount });
                resultProperty.GetSetMethod(true).Invoke(null, new[] { oldResult });
                File.Delete(file);
            }
        }

        [UnityTest]
        public IEnumerator SongManagerAppliedEmptyFolderShowsZeroAfterFailedAttempt()
        {
            using var state = new SongManagerInteractionState(this);
            var containerType = Production("YARG.Song.SongContainer");
            var applied = containerType.GetProperty("AppliedSources");
            var result = containerType.GetProperty("LastScanResult");
            var cache = containerType.GetField("_songCache", BindingFlags.Static | BindingFlags.NonPublic);
            object oldApplied = applied.GetValue(null), oldResult = result.GetValue(null), oldCache = cache.GetValue(null);
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                state.Settings.GetType().GetField("SongFolders").SetValue(state.Settings,
                    new List<string> { folder, folder + "-not-scanned" });
                applied.GetSetMethod(true).Invoke(null, new object[] { new SongSourceConfiguration(new[] { folder }, "", "") });
                cache.SetValue(null, Activator.CreateInstance(cache.FieldType));
                var menuType = Production("YARG.Menu.Settings.SettingsMenu");
                var menu = menuType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy).GetValue(null);
                void AssertStatus()
                {
                    menuType.GetMethod("RefreshSettingsKeepPosition").Invoke(menu, null);
                    var rows = state.Container.GetComponentsInChildren(Production("YARG.Menu.Settings.SettingsDirectory"));
                    string Status(int index)
                    {
                        var label = (Component)Field(rows[index], "_songCountText");
                        return (string)label.GetType().GetProperty("text").GetValue(label);
                    }
                    var localize = Production("YARG.Localization.Localize").GetMethod("Key", new[] { typeof(string) });
                    var zero = (string)localize.Invoke(null, new object[] { "Menu.Settings.SongCount" });
                    Assert.That(Status(0), Is.EqualTo(string.Format(zero, 0)), "Successfully scanned empty folders contain zero songs.");
                    Assert.That(Status(1), Is.EqualTo(localize.Invoke(null, new object[] { "Menu.Settings.ScanNeeded" })), "Unscanned folders still need a scan.");
                }
                result.GetSetMethod(true).Invoke(null, new object[] { null });
                AssertStatus();
                // A non-success result must not be used as the applied-library snapshot.
                var failed = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(result.PropertyType);
                var statusProperty = result.PropertyType.GetProperty("Status");
                var failureStatus = Enum.GetValues(statusProperty.PropertyType).Cast<object>().First(value => value.ToString() != "Success");
                var statusField = result.PropertyType.GetField("<Status>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                statusField.SetValue(failed, failureStatus);
                Assert.That(statusProperty.GetValue(failed).ToString(), Is.Not.EqualTo("Success"));
                result.GetSetMethod(true).Invoke(null, new[] { failed });
                AssertStatus();
                yield return null;
            }
            finally
            {
                applied.GetSetMethod(true).Invoke(null, new[] { oldApplied });
                result.GetSetMethod(true).Invoke(null, new[] { oldResult });
                cache.SetValue(null, oldCache);
                Directory.Delete(folder);
            }
        }

        [UnityTest]
        public IEnumerator SongManagerBusyAndFailureFeedback()
        {
            using var state = new SongManagerInteractionState(this);
            var completion = new TaskCompletionSource<bool>();
            int calls = 0;
            state.Actions.GetField("Refresh").SetValue(null, new Func<SongScanScope, Task>(scope =>
            { Assert.That(scope, Is.EqualTo(SongScanScope.Upgrades)); calls++; return completion.Task; }));
            yield return null;
            ClickSource(state.Container, "ScanUpgrades");
            try
            {
                yield return null;
                Assert.That((bool)state.Actions.GetProperty("Busy").GetValue(null), Is.True);
                Assert.That(SourceTexts(state.Container), Does.Contain(SourceLabel("Scanning")));
                foreach (string key in new[] { "Select", "Clear", "ScanSongs", "ScanUpdates", "ScanUpgrades", "ScanAll" })
                    Assert.That(SourceButton(state.Container, key).interactable, Is.False, "Every source action must be disabled during refresh.");
                // Programmatic duplicate activations are gated too, not just visual button disabling.
                state.Actions.GetMethod("Scan").Invoke(null, new object[] { SongScanScope.Upgrades });
                Assert.That(calls, Is.EqualTo(1));
                completion.SetException(new InvalidOperationException("Selected upgrade MIDI is unreadable; Scan Upgrades."));
                yield return null;
                yield return null;
                Assert.That((bool)state.Actions.GetProperty("Busy").GetValue(null), Is.False);
                Assert.That(SourceTexts(state.Container), Does.Contain("Selected upgrade MIDI is unreadable; Scan Upgrades."),
                    "Visible failure text must come from this operation, not stale LastScanResult.");
                Assert.That(SourceTexts(state.Container), Does.Not.Contain(SourceLabel("Scanning")));
                Assert.That(SourceButton(state.Container, "ScanAll").interactable, Is.True);
            }
            finally { completion.TrySetResult(true); }
        }

        [UnityTest]
        public IEnumerator SongManagerHelpClickHasBoundedScrollableBodyAndCompactClose()
        {
            using var state = new SongManagerInteractionState(this);
            var dialogHost = new GameObject("Bounded help viewport", typeof(RectTransform));
            dialogHost.transform.SetParent(_menuCanvasHost.transform, false);
            var hostRect = (RectTransform)dialogHost.transform;
            hostRect.sizeDelta = new Vector2(640, 360);
            _dialogRoot.transform.SetParent(dialogHost.transform, false);
            // DialogManager accepts a Transform container; use the sized RectTransform directly.
            _dialogManager.GetType().GetField("_dialogContainer", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_dialogManager, hostRect);
            ClickSource(state.Container, "Help");
            yield return null;
            Canvas.ForceUpdateCanvases();
            var helper = dialogHost.GetComponentInChildren(Production("YARG.Menu.Settings.SongSourceHelp"));
            Assert.That(helper, Is.Not.Null, "Activate the real production Help listener.");
            var dialog = helper.GetComponent(Production("YARG.Menu.Dialogs.MessageDialog"));
            var panel = (RectTransform)Field(helper, "_panel");
            var title = (Component)dialog.GetType().GetProperty("Title").GetValue(dialog);
            var body = (Component)dialog.GetType().GetProperty("Message").GetValue(dialog);
            var scroll = helper.GetComponentInChildren<ScrollRect>();
            var footer = (RectTransform)Field(helper, "_footer");
            Assert.That(SourceTexts(title.gameObject), Does.Contain(SourceLabel("Help")));
            Assert.That(body.GetType().GetProperty("alignment").GetValue(body).ToString(), Is.EqualTo("TopLeft"));
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(title.transform.IsChildOf(scroll.viewport), Is.False);
            Assert.That(footer.IsChildOf(scroll.viewport), Is.False);
            void Inside(RectTransform outer, RectTransform inner)
            {
                var corners = new Vector3[4];
                inner.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = outer.InverseTransformPoint(corner);
                    Assert.That(point.x, Is.InRange(outer.rect.xMin - 1, outer.rect.xMax + 1));
                    Assert.That(point.y, Is.InRange(outer.rect.yMin - 1, outer.rect.yMax + 1));
                }
            }
            foreach (var size in new[] { new Vector2(640, 360), new Vector2(480, 270), new Vector2(1280, 720) })
            {
                hostRect.sizeDelta = size;
                yield return null;
                Canvas.ForceUpdateCanvases();
                Inside(hostRect, panel);
                Inside(panel, (RectTransform)title.transform);
                Inside(panel, scroll.viewport);
                Inside(panel, footer);
                var titleCorners = new Vector3[4];
                var viewportCorners = new Vector3[4];
                var footerCorners = new Vector3[4];
                ((RectTransform)title.transform).GetWorldCorners(titleCorners);
                scroll.viewport.GetWorldCorners(viewportCorners);
                footer.GetWorldCorners(footerCorners);
                Assert.That(panel.InverseTransformPoint(titleCorners[0]).y,
                    Is.GreaterThanOrEqualTo(panel.InverseTransformPoint(viewportCorners[1]).y + 8),
                    "The fixed title must not overlap the scrolling body.");
                Assert.That(panel.InverseTransformPoint(viewportCorners[0]).y,
                    Is.GreaterThanOrEqualTo(panel.InverseTransformPoint(footerCorners[1]).y + 8),
                    "The scrolling body must not overlap the fixed Close footer.");
            }
            var closeRect = (RectTransform)Field(helper, "_close");
            var close = closeRect.GetComponent(Production("YARG.Menu.ColoredButton"));
            Assert.That(SourceTexts(close.gameObject), Does.Contain("Close"));
            Assert.That((Color)close.GetType().GetProperty("BackgroundColor").GetValue(close),
                Is.EqualTo(new Color(0.18039216f, 0.8509804f, 1f)));
            Assert.That(closeRect.rect.width, Is.EqualTo(170).Within(1));
            Assert.That(closeRect.rect.height, Is.LessThanOrEqualTo(40));
            scroll.verticalNormalizedPosition = 1;
            helper.GetType().GetMethod("Scroll", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(helper, new object[] { -1 });
            Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(1), "Controller scrolling can reach clipped help text.");
            closeRect.GetComponentInChildren<Button>().onClick.Invoke();
            yield return null;
            Assert.That((bool)_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.False);
        }

        [UnityTest]
        public IEnumerator SongManagerThreeBlocksRenderAndNavigate()
        {
            using var state = new SongManagerInteractionState(this);
            var container = state.Container;
            var navigation = state.Navigation;
            ((RectTransform)container.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 1500);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var labels = container.GetComponentsInChildren(Production("TMPro.TextMeshProUGUI"))
                .Select(label => (string)label.GetType().GetProperty("text").GetValue(label)).ToArray();
            var localize = Production("YARG.Localization.Localize").GetMethods()
                .Single(method => method.Name == "Key" && method.GetParameters().Length == 1);
            string Label(string key) => (string)localize.Invoke(null, new object[] { "Menu.Settings.Sources." + key });
            int songs = Array.IndexOf(labels, Label("Songs"));
            int updates = Array.IndexOf(labels, Label("Updates"));
            int upgrades = Array.IndexOf(labels, Label("Upgrades"));
            Assert.That(songs, Is.GreaterThanOrEqualTo(0));
            Assert.That(updates, Is.GreaterThan(songs));
            Assert.That(upgrades, Is.GreaterThan(updates));
            Assert.That(labels, Does.Not.Contain(Label("TrackOrder")), "Detailed help belongs in the Help dialog, not a tall text block.");
            Assert.That(labels, Does.Not.Contain(Label("MetadataOrder")));
            Assert.That(labels, Does.Contain(Label("Help")));
            Assert.That(container.GetComponentInChildren(Production("YARG.Menu.Settings.SongManagerHeader")), Is.Not.Null);
            var directories = container.GetComponentsInChildren(Production("YARG.Menu.Settings.SettingsDirectory"));
            Assert.That(directories.Length, Is.EqualTo(2), "Each supplemental section must use exactly one production directory row.");
            foreach (Component directory in directories)
            {
                var rect = (RectTransform)directory.transform;
                Assert.That(rect.rect.height, Is.InRange(1f, 110f), "A source row must retain the compact production row geometry.");
                var path = (Component)Field(directory, "_pathText");
                var countLabel = (Component)Field(directory, "_songCountText");
                Assert.That(path.transform.position.x, Is.LessThan(countLabel.transform.position.x), "Path stays left of the count/status.");
                Assert.That(directory.GetComponentsInChildren<Button>().Length, Is.EqualTo(2), "Use the browse/trash controls, not separate action blocks.");
            }
            foreach (string section in new[] { "ScanningOptions", "MusicLibrary", "PlayAShow" })
            {
                string sectionLabel = (string)localize.Invoke(null, new object[] { "Settings.Header." + section });
                Assert.That(labels, Does.Contain(sectionLabel), $"Missing registered {section} settings section.");
            }
            foreach (Component status in container.GetComponentsInChildren(Production("YARG.Menu.Settings.SongSourceTextLayout")))
                Assert.That(((RectTransform)status.transform).rect.height, Is.LessThan(100f), "Empty or single-line feedback must not reserve the Text prefab's 250-pixel block.");
            var settingVisuals = container.GetComponentsInChildren(Production("YARG.Menu.Settings.Visuals.BaseSettingVisual"));
            Assert.That(settingVisuals.Length, Is.GreaterThanOrEqualTo(12), "Registered scanning, library and Play A Show settings must render.");
            Assert.That(labels, Does.Contain(Label("ScanAll")));
            Assert.That((int)navigation.GetType().GetProperty("Count").GetValue(navigation), Is.GreaterThanOrEqualTo(4));
            var selectFirst = navigation.GetType().GetMethod("SelectFirst");
            selectFirst.Invoke(navigation, new[] { Enum.Parse(selectFirst.GetParameters()[0].ParameterType, "Programmatically") });
            Assert.That(navigation.GetType().GetProperty("SelectedBehaviour").GetValue(navigation), Is.Not.Null);
            Assert.That(container.GetComponentsInChildren<Button>().Length, Is.GreaterThanOrEqualTo(10));
            // Use the same directional callbacks as NavigateDown/Up, not SelectAt registration indices.
            var reached = new HashSet<object>();
            int count = (int)navigation.GetType().GetProperty("Count").GetValue(navigation);
            for (int i = 0; i < count; i++)
            {
                reached.Add(navigation.GetType().GetProperty("SelectedBehaviour").GetValue(navigation));
                Call(navigation, "SelectNext", false);
            }
            foreach (string key in new[] { "Help", "AddFolder", "ScanSongs", "ScanUpdates", "ScanUpgrades", "ScanAll" })
            {
                var button = SourceButton(container, key);
                var rect = (RectTransform)button.transform;
                var reference = (RectTransform)SourceButton(container, "ScanAll").transform;
                Assert.That(rect.rect.height, Is.EqualTo(reference.rect.height).Within(1), "Header actions share the original compact pill height.");
                Assert.That(rect.rect.width, Is.InRange(80f, 190f));
                var runtime = button.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
                Assert.That(runtime, Is.Not.Null, "Compact production buttons must retain runtime navigation.");
                Assert.That(reached, Does.Contain(runtime), "Compact header actions must be reachable by directional navigation.");
                Transform top = button.transform;
                while (top.parent != container.transform) top = top.parent;
                Assert.That(((RectTransform)top).rect.height, Is.LessThan(100f), "No large standalone action rows.");
                Assert.That(top.GetComponentsInChildren(Production("TMPro.TextMeshProUGUI")).Length,
                    Is.GreaterThanOrEqualTo(key == "ScanAll" || key == "Help" ? 2 : 2), "Actions belong in a labelled header.");
            }
            var rows = container.GetComponentsInChildren(Production("YARG.Menu.Settings.SettingsButton"));
            foreach (var row in rows)
            {
                Assert.That(reached, Does.Contain(row), "Every section action row must be reachable by down navigation.");
                var inner = Field(row, "_navGroup");
                var first = inner.GetType().GetMethod("SelectFirst");
                first.Invoke(inner, new[] { Enum.Parse(first.GetParameters()[0].ParameterType, "Programmatically") });
                var controls = row.GetComponentsInChildren(Production("YARG.Menu.Navigation.NavigatableUnityButton"));
                var visited = new HashSet<object>();
                for (int i = 0; i < controls.Length - 1; i++)
                {
                    visited.Add(inner.GetType().GetProperty("SelectedBehaviour").GetValue(inner));
                    Call(inner, "SelectNext", false);
                }
                visited.Add(inner.GetType().GetProperty("SelectedBehaviour").GetValue(inner));
                foreach (var control in controls) Assert.That(visited, Does.Contain(control));
                var before = inner.GetType().GetProperty("SelectedBehaviour").GetValue(inner);
                Call(inner, "SelectPrevious", false);
                if (controls.Length > 1)
                    Assert.That(inner.GetType().GetProperty("SelectedBehaviour").GetValue(inner), Is.Not.SameAs(before));
                else
                    Assert.That(inner.GetType().GetProperty("SelectedBehaviour").GetValue(inner), Is.SameAs(before));
            }
            Call(navigation, "SelectPrevious", false);
            Assert.That(navigation.GetType().GetProperty("SelectedBehaviour").GetValue(navigation), Is.Not.Null);
        }
    }
}
