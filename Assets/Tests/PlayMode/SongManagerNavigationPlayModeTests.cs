using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YARG.Tests.PlayMode
{
    public sealed partial class EliteDrumsLayoutMenuPlayModeTests
    {
        [UnityTest]
        public IEnumerator SongManagerNavigationFollowsVisibleControls() => CheckSongManagerNavigation(false);

        [UnityTest]
        public IEnumerator SongManagerNavigationAfterAddingFolderFollowsVisibleControls() => CheckSongManagerNavigation(true);

        private IEnumerator CheckSongManagerNavigation(bool addFolder)
        {
            using var state = new SongManagerInteractionState(this);
            if (addFolder) ClickSource(state.Container, "AddFolder");
            ((RectTransform)state.Container.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 1500);
            yield return null;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)state.Container.transform);

            Vector3 Center(Button button) => ((RectTransform)button.transform).TransformPoint(
                ((RectTransform)button.transform).rect.center);
            var runtimeType = Production("YARG.Menu.Navigation.RuntimeNavigatable");
            var rows = new List<Button[]>();
            var header = (Component)state.Container.GetComponentInChildren(Production("YARG.Menu.Settings.SongManagerHeader"));
            rows.Add(header.GetComponentsInChildren<Button>().OrderBy(button => Center(button).x).ToArray());
            Assert.That(rows[0], Does.Contain(SourceButton(state.Container, "Help")));
            Assert.That(rows[0], Does.Contain(SourceButton(state.Container, "ScanAll")));
            rows.Add(new[] { SourceButton(state.Container, "AddFolder"), SourceButton(state.Container, "ScanSongs") });
            var directories = state.Container.GetComponentsInChildren(Production("YARG.Menu.Settings.SettingsDirectory"));
            Button[] DirectoryButtons(Component directory) => new[] { "Browse", "Remove" }.Select(method =>
                directory.GetComponentsInChildren<Button>().Single(button => Enumerable.Range(0, button.onClick.GetPersistentEventCount())
                    .Any(index => button.onClick.GetPersistentMethodName(index) == method))).ToArray();
            Assert.That(directories.Length, Is.EqualTo(addFolder ? 3 : 2));
            if (addFolder) rows.Add(DirectoryButtons(directories[0]));
            rows.Add(new[] { SourceButton(state.Container, "ScanUpdates") });
            rows.Add(DirectoryButtons(directories[addFolder ? 1 : 0]));
            rows.Add(new[] { SourceButton(state.Container, "ScanUpgrades") });
            rows.Add(DirectoryButtons(directories[addFolder ? 2 : 1]));

            for (int row = 0; row < rows.Count; row++)
            {
                for (int column = 1; column < rows[row].Length; column++)
                    Assert.That(Center(rows[row][column]).x, Is.GreaterThan(Center(rows[row][column - 1]).x),
                        "Expected actions must run left-to-right, including Browse before Remove.");
                if (row > 0)
                    Assert.That(Center(rows[row][0]).y, Is.LessThan(Center(rows[row - 1][0]).y),
                        "Expected action rows must run top-to-bottom.");
            }
            var expected = rows.SelectMany(row => row).ToArray();
            var actual = state.Container.GetComponentsInChildren<Button>()
                .Where(button => button.GetComponent(runtimeType) != null).ToArray();
            CollectionAssert.AreEquivalent(expected, actual, "Cover every active header, section, browse and delete control.");
            var outlines = expected.ToDictionary(button => button, button =>
            {
                var matches = button.GetComponentsInChildren<Transform>(true)
                    .Where(child => child.name == "SelectedOutline").ToArray();
                Assert.That(matches.Length, Is.EqualTo(1), $"{button.name} must own exactly one outline, including clones.");
                var image = matches[0].GetComponent<Image>();
                Assert.That(image, Is.Not.Null);
                Assert.That(image.enabled && image.color.a > 0, Is.True);
                return matches[0].gameObject;
            });
            void AssertSelected(Button selected)
            {
                Assert.That(state.Navigation.GetType().GetProperty("SelectedBehaviour").GetValue(state.Navigation),
                    Is.SameAs(selected.GetComponent(runtimeType)), $"Wrong action selected: expected {selected.name}.");
                foreach (var pair in outlines)
                    Assert.That(pair.Value.activeInHierarchy, Is.EqualTo(pair.Key == selected),
                        $"Only the selected control's own outline may be visible: {pair.Key.name}.");
            }
            var selectFirst = state.Navigation.GetType().GetMethod("SelectFirst");
            selectFirst.Invoke(state.Navigation, new[] { System.Enum.Parse(selectFirst.GetParameters()[0].ParameterType, "Programmatically") });
            for (int i = 0; i < expected.Length; i++)
            {
                AssertSelected(expected[i]);
                if (i + 1 < expected.Length) Call(state.Navigation, "SelectNext", false);
            }
            for (int i = expected.Length - 2; i >= 0; i--)
            {
                Call(state.Navigation, "SelectPrevious", false);
                AssertSelected(expected[i]);
            }
            // Also check departure from the final source control into ordinary settings.
            for (int i = 1; i < expected.Length; i++) Call(state.Navigation, "SelectNext", false);
            Call(state.Navigation, "SelectNext", false);
            foreach (var outline in outlines.Values) Assert.That(outline.activeInHierarchy, Is.False);
        }
    }
}
