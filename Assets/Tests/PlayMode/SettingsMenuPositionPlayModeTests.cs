using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YARG.Tests.PlayMode
{
    public sealed partial class EliteDrumsLayoutMenuPlayModeTests
    {
        private static object PositionMenu() => Production("YARG.Menu.Settings.SettingsMenu")
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);

        private static Component PositionSelection(SongManagerInteractionState state) =>
            (Component)state.Navigation.GetType().GetProperty("SelectedBehaviour").GetValue(state.Navigation);

        private static void SelectPositionControl(Component control) => control.GetType().GetMethod("SetSelected")
            .Invoke(control, new[] { (object)true, System.Enum.Parse(Production("YARG.Menu.Navigation.SelectionOrigin"), "Programmatically") });

        [UnityTest]
        public IEnumerator SourceFocusSurvivesFolderChangesAndMigrationDismissal()
        {
            using var state = new SongManagerInteractionState(this);
            var menu = PositionMenu();
            var folders = (System.Collections.Generic.List<string>)state.Settings.GetType().GetField("SongFolders").GetValue(state.Settings);
            folders.Add("first");
            folders.Add("second");
            state.Settings.GetType().GetField("ShowExplicitSongSourcesWarning").SetValue(state.Settings, true);
            menu.GetType().GetMethod("RefreshSettingsKeepPosition").Invoke(menu, null);
            yield return null;
            var browse = SourceButton(state.Container, "Select", 0);
            SelectPositionControl((Component)browse.GetComponentInParent(Production("YARG.Menu.Navigation.NavigatableBehaviour")));

            folders.Insert(0, "new");
            menu.GetType().GetMethod("RefreshSettingsKeepPosition").Invoke(menu, null);
            Assert.That(PositionSelection(state).GetComponentInChildren<Button>(), Is.SameAs(SourceButton(state.Container, "Select", 0)),
                "Folder insertion must retain Updates browse, not the previous numeric index.");
            yield return null;
            folders.RemoveAt(0);
            menu.GetType().GetMethod("RefreshSettingsKeepPosition").Invoke(menu, null);
            Assert.That(PositionSelection(state).GetComponentInChildren<Button>(), Is.SameAs(SourceButton(state.Container, "Select", 0)),
                "Deleting an earlier folder must retain Updates browse.");
            yield return null;
            state.Settings.GetType().GetField("ShowExplicitSongSourcesWarning").SetValue(state.Settings, false);
            menu.GetType().GetMethod("RefreshSettingsKeepPosition").Invoke(menu, null);
            Assert.That(PositionSelection(state).GetComponentInChildren<Button>(), Is.SameAs(SourceButton(state.Container, "Select", 0)),
                "Dismissing the migration row must retain Updates browse.");
        }

        [UnityTest]
        public IEnumerator FolderFocusTracksPathAndDuplicateOccurrence()
        {
            using var state = new SongManagerInteractionState(this);
            var menu = PositionMenu();
            var folders = (System.Collections.Generic.List<string>)state.Settings.GetType().GetField("SongFolders").GetValue(state.Settings);
            folders.AddRange(new[] { "earlier", "duplicate", "duplicate" });
            menu.GetType().GetMethod("RefreshSettingsKeepPosition").Invoke(menu, null);
            yield return null;
            var rowType = Production("YARG.Menu.Settings.SettingsDirectory");
            var rows = state.Container.GetComponentsInChildren(rowType);
            var remove = ((Component)rows[2]).GetComponentsInChildren<Button>();
            Button selectedButton = null;
            foreach (var button in remove)
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    if (button.onClick.GetPersistentMethodName(i) == "Remove") selectedButton = button;
            Assert.That(selectedButton, Is.Not.Null);
            SelectPositionControl((Component)selectedButton.GetComponentInParent(Production("YARG.Menu.Navigation.NavigatableBehaviour")));
            folders.RemoveAt(0);
            menu.GetType().GetMethod("RefreshSettingsKeepPosition").Invoke(menu, null);
            var selected = PositionSelection(state);
            var selectedRow = selected.GetComponentInParent(rowType);
            Assert.That(rowType.GetField("_index", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(selectedRow), Is.EqualTo(1),
                "Focus must remain on the second occurrence of the duplicate path after an earlier row is removed.");
            var selectedAction = selected.GetComponentInChildren<Button>();
            Assert.That(selectedAction, Is.Not.Null);
            Assert.That(selectedAction.onClick.GetPersistentMethodName(0), Is.EqualTo("Remove"));
        }

        [UnityTest]
        public IEnumerator AddFolderActionKeepsItsRowAndInnerChoice()
        {
            using var state = new SongManagerInteractionState(this);
            var menu = PositionMenu();
            menu.GetType().GetMethod("RefreshSettingsKeepPosition").Invoke(menu, null);
            yield return null;
            var add = SourceButton(state.Container, "AddFolder");
            var inner = (Component)add.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
            SelectPositionControl(inner);
            var group = inner.GetType().GetProperty("NavigationGroup").GetValue(inner);
            var before = group.GetType().GetProperty("SelectedIndex").GetValue(group);
            add.onClick.Invoke();
            yield return null;
            var rebuiltAdd = SourceButton(state.Container, "AddFolder");
            Assert.That(PositionSelection(state), Is.SameAs(rebuiltAdd.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"))),
                "The compact Add Folder action must retain focus after inserting a folder.");
            var rebuiltInner = (Component)rebuiltAdd.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
            var rebuiltGroup = rebuiltInner.GetType().GetProperty("NavigationGroup").GetValue(rebuiltInner);
            Assert.That(rebuiltGroup.GetType().GetProperty("SelectedIndex").GetValue(rebuiltGroup), Is.EqualTo(before));
            Assert.That(((System.Collections.Generic.List<string>)state.Settings.GetType().GetField("SongFolders").GetValue(state.Settings)).Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ClearingSourceKeepsScrollAndValidControllerSelection()
        {
            using var state = new SongManagerInteractionState(this);
            var menuType = Production("YARG.Menu.Settings.SettingsMenu");
            var menu = menuType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static |
                BindingFlags.FlattenHierarchy).GetValue(null);
            var viewport = new GameObject("Settings position viewport", typeof(RectTransform), typeof(ScrollRect));
            viewport.transform.SetParent(state.Container.transform.parent, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.sizeDelta = new Vector2(600f, 180f);
            state.Container.transform.SetParent(viewport.transform, false);
            var content = state.Container.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            var fitter = state.Container.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = content;
            scroll.horizontal = false;
            menuType.GetField("_scrollRect", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(menu, scroll);

            state.Settings.GetType().GetField("SongUpdatesDtaPath").SetValue(state.Settings, "community.dta");
            menuType.GetMethod("RefreshSettingsKeepPosition").Invoke(menu, null);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(content.rect.height, Is.GreaterThan(viewportRect.rect.height));
            scroll.verticalNormalizedPosition = 0.37f;
            state.Navigation.GetType().GetMethod("SelectLast").Invoke(state.Navigation,
                new[] { System.Enum.Parse(Production("YARG.Menu.Navigation.SelectionOrigin"), "Programmatically") });

            ClickSource(state.Container, "Clear");
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(0.37f).Within(0.01f));
            var selected = (int?)state.Navigation.GetType().GetProperty("SelectedIndex").GetValue(state.Navigation);
            var count = (int)state.Navigation.GetType().GetProperty("Count").GetValue(state.Navigation);
            Assert.That(selected.HasValue, Is.True);
            Assert.That(selected.Value, Is.InRange(0, count - 1));
            Assert.That(state.Navigation.GetType().GetProperty("SelectedBehaviour").GetValue(state.Navigation), Is.Not.Null);
        }
    }
}
