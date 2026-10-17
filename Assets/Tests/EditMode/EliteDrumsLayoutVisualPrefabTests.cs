using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Tests.EditMode
{
    public sealed class EliteDrumsLayoutVisualPrefabTests
    {
        private const string PREFAB_PATH = "Assets/Resources/EliteDrumsLayoutMenu.prefab";
        private GameObject _prefab;
        private GameObject _canvas;
        private Type _tmpTextType;

        [SetUp]
        public void LoadProductionPrefab()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Assert.That(source, Is.Not.Null, "Generate the production prefab before visual geometry tests.");
            _canvas = new GameObject("Visual prefab test canvas", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_canvas.transform).sizeDelta = new Vector2(1920f, 1080f);
            _prefab = UnityEngine.Object.Instantiate(source, _canvas.transform, false);
            Canvas.ForceUpdateCanvases();
            _tmpTextType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("TMPro.TMP_Text"))
                .First(type => type != null);
        }

        [TearDown]
        public void Cleanup()
        {
            if (_canvas != null) UnityEngine.Object.DestroyImmediate(_canvas);
        }

        private static RectTransform Rect(Transform parent, string childName) =>
            parent.Find(childName)?.GetComponent<RectTransform>();

        private static Vector2 WorldSize(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return new Vector2(Vector3.Distance(corners[0], corners[3]),
                Vector3.Distance(corners[0], corners[1]));
        }

        [Test]
        public void EnglishProfileEntryAndConfigurationTitleUseApprovedLabels()
        {
            var json = File.ReadAllText("Assets/StreamingAssets/lang/en-US.json");
            var table = JsonUtility.FromJson<LocaleLabels>(json);
            Assert.That(table.Menu.ProfileList.ConfigureEliteDrumsLayout, Is.EqualTo("ELITE DRUMS HIGHWAY"));
            Assert.That(table.Menu.EliteLayout.Title, Is.EqualTo("Elite Drums Highway"));
        }

        [Serializable]
        private sealed class LocaleLabels
        {
            public LocaleMenu Menu;
        }

        [Serializable]
        private sealed class LocaleMenu
        {
            public LocaleProfileList ProfileList;
            public LocaleEliteLayout EliteLayout;
        }

        [Serializable]
        private sealed class LocaleProfileList
        {
            public string ConfigureEliteDrumsLayout;
        }

        [Serializable]
        private sealed class LocaleEliteLayout
        {
            public string Title;
        }

        [Test]
        public void ManagementButtonsUseActualLargePresetSizedButtonTargets()
        {
            var presetActions = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Menu/Settings/Presets/PresetActions.prefab");
            Assert.That(presetActions, Is.Not.Null, "The reference preset actions prefab must exist.");
            var presetRow = presetActions.GetComponentsInChildren<RectTransform>(true)
                .FirstOrDefault(rect => rect.name == "Button Container");
            Assert.That(presetRow, Is.Not.Null,
                "The reference prefab must contain its preset button row, at the root or below it.");
            var referenceHeight = presetRow.rect.height;
            Assert.That(referenceHeight, Is.EqualTo(56f).Within(0.1f));

            var referenceButton = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Menu/Common/Buttons/RoundButton.prefab");
            var referenceSprite = referenceButton.GetComponentInChildren<Button>(true).image.sprite;
            foreach (var label in new[] { "Save", "Rename", "Copy", "Delete", "Import", "Export" })
            {
                var wrapper = Rect(_prefab.transform, label);
                var button = wrapper.GetComponentInChildren<Button>(true);
                var buttonRect = button.GetComponent<RectTransform>();
                Assert.That(button.image.sprite, Is.EqualTo(referenceSprite), label + " uses the preset action button art");
                Assert.That(button.transform.Find("Selection Outline"), Is.Not.Null,
                    label + " uses the RoundButton selection outline");
                var wrapperSize = WorldSize(wrapper);
                var buttonSize = WorldSize(buttonRect);
                float expectedWidth = ((0.056f + 9f * 0.061f) * 1920f - 6f * 10f - 64f) / 6f;
                Assert.That(wrapper.rect.width, Is.EqualTo(expectedWidth).Within(0.01f),
                    label + " shares the grid width equally after preset spacing and group spacer");
                Assert.That(wrapper.rect.height, Is.EqualTo(referenceHeight).Within(0.01f));
                Assert.That(buttonRect.rect.height, Is.EqualTo(referenceButton.GetComponentInChildren<Button>(true)
                    .GetComponent<RectTransform>().rect.height).Within(0.01f));
                Assert.That(buttonSize.y, Is.GreaterThanOrEqualTo(referenceHeight - 10f), label + " actual button height");
                Assert.That(buttonSize.x, Is.GreaterThan(wrapperSize.x * 0.9f), label + " actual button width");
                Assert.That(buttonSize.y, Is.GreaterThan(wrapperSize.y * 0.8f), label + " actual button fills wrapper");
                var text = wrapper.GetComponentInChildren(_tmpTextType, true);
                Assert.That(text, Is.Not.Null, label + " wrapper contains its label");
                var referenceText = referenceButton.GetComponentInChildren(_tmpTextType, true);
                Assert.That(text.GetType().GetProperty("fontSize").GetValue(text),
                    Is.EqualTo(referenceText.GetType().GetProperty("fontSize").GetValue(referenceText)));
                Assert.That(text.GetType().GetProperty("font").GetValue(text),
                    Is.EqualTo(referenceText.GetType().GetProperty("font").GetValue(referenceText)));
                var textRect = ((Component)text).GetComponent<RectTransform>();
                Assert.That(WorldSize(textRect).x, Is.LessThan(buttonSize.x), label + " text has inner padding");
            }
        }

        [Test]
        public void LayoutTransformActionsUseImportStyleWithinUnchangedNoticeBand()
        {
            var notice = Rect(_prefab.transform, "Built In Notice");
            Assert.That(notice.anchorMin, Is.EqualTo(new Vector2(0.04f, 0.3088889f)));
            Assert.That(notice.anchorMax, Is.EqualTo(new Vector2(0.63f, 0.3788889f)));
            var import = Rect(_prefab.transform, "Import");
            var importButton = import.GetComponentInChildren<Button>(true);
            var importText = import.GetComponentInChildren(_tmpTextType, true);
            foreach (var label in new[] { "Shift Left", "Shift Right", "Reverse" })
            {
                var wrapper = Rect(_prefab.transform, label);
                Assert.That(wrapper, Is.Not.Null);
                Assert.That(wrapper.gameObject.activeSelf, Is.False, "Runtime chooses actions or built-in notice.");
                var button = wrapper.GetComponentInChildren<Button>(true);
                Assert.That(button.image.sprite, Is.EqualTo(importButton.image.sprite));
                Assert.That(button.image.color, Is.EqualTo(importButton.image.color));
                Assert.That(wrapper.rect.height, Is.EqualTo(import.rect.height));
                var text = wrapper.GetComponentInChildren(_tmpTextType, true);
                foreach (var property in new[] { "font", "fontSize", "fontStyle" })
                    Assert.That(text.GetType().GetProperty(property).GetValue(text),
                        Is.EqualTo(importText.GetType().GetProperty(property).GetValue(importText)), label + " " + property);
                var corners = new Vector3[4];
                wrapper.GetWorldCorners(corners);
                foreach (var corner in corners)
                    Assert.That(notice.rect.Contains(notice.InverseTransformPoint(corner)), Is.True,
                        label + " must occupy only the built-in notice region.");
            }
        }

        [Test]
        public void FretMarkersUseSmallerBlackNumberInsets()
        {
            var cellType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutCell"))
                .First(type => type != null);
            var cells = _prefab.GetComponentsInChildren(cellType, true);
            Assert.That(cells, Has.Length.EqualTo(20));
            var roundedSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Menu/Common/RoundedSquare.png");
            var circleSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Menu/Common/Circle.png");
            Assert.That(roundedSprite, Is.Not.Null, "The approved rounded pill sprite must exist.");
            Assert.That(circleSprite, Is.Not.Null, "The approved black inset circle sprite must exist.");
            for (int column = 1; column <= 10; column++)
            {
                var cell = _prefab.transform.Find($"Column {column} Drum");
                var pill = _prefab.transform.Find($"Saved Slot Fret {column}");
                Assert.That(cell, Is.Not.Null);
                Assert.That(pill, Is.Not.Null);
                var pillImage = pill.GetComponent<Image>();
                var pillSize = WorldSize(pill.GetComponent<RectTransform>());
                var cellSize = WorldSize(cell.GetComponent<RectTransform>());
                Assert.That(pillImage.sprite, Is.EqualTo(roundedSprite), "The outer pill uses rounded geometry.");
                Assert.That(pillImage.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(pillSize.x, Is.GreaterThan(pillSize.y));
                Assert.That(pillSize.x, Is.GreaterThan(cellSize.x * 0.8f));
                Assert.That(pillSize.x, Is.LessThan(cellSize.x));

                var inset = pill.Find("Fret Number Background");
                Assert.That(inset, Is.Not.Null);
                var insetRect = inset.GetComponent<RectTransform>();
                var insetImage = inset.GetComponent<Image>();
                Assert.That(insetRect.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(insetRect.anchorMax, Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(insetRect.anchoredPosition, Is.EqualTo(Vector2.zero));
                Assert.That(insetRect.sizeDelta, Is.EqualTo(new Vector2(38f, 38f)));
                var insetSize = WorldSize(insetRect);
                Assert.That(pillSize.x, Is.GreaterThan(insetSize.x),
                    "The displayed colored pill must be wider than its black number circle.");
                Assert.That(pillSize.x / pillSize.y, Is.GreaterThan(1f),
                    "The displayed pill, not its source sprite, must be wider than it is tall.");
                Assert.That(pillSize.y - insetSize.y, Is.GreaterThan(pillSize.y * 0.4f),
                    "The 38-unit marker leaves visible colored padding above and below its number circle.");
                Assert.That(insetImage.raycastTarget, Is.False,
                    "The decorative marker must not intercept assignment-cell input.");
                Assert.That(insetSize.x / insetSize.y, Is.EqualTo(1f).Within(0.01f),
                    "The displayed black inset must remain circular.");
                Assert.That(insetImage.sprite, Is.EqualTo(circleSprite));
                Assert.That(insetImage.preserveAspect, Is.True);
                Assert.That(insetImage.color, Is.EqualTo(Color.black), "The number circle must not inherit the slot tint.");
                Assert.That(insetImage, Is.Not.SameAs(pillImage));

                var text = inset.GetComponentInChildren(_tmpTextType, true);
                Assert.That(text, Is.Not.Null);
                var numberRect = ((Component)text).GetComponent<RectTransform>();
                Assert.That(numberRect.anchoredPosition, Is.EqualTo(new Vector2(0f, 1f)),
                    "Only the numeral is raised; its black circle stays centered.");
                Assert.That(numberRect.sizeDelta, Is.EqualTo(Vector2.zero));
                Assert.That((string)text.GetType().GetProperty("text").GetValue(text), Is.EqualTo(column.ToString()));
                Assert.That((Color)text.GetType().GetProperty("color").GetValue(text), Is.EqualTo(Color.white));
                Assert.That(Convert.ToSingle(text.GetType().GetProperty("fontSize").GetValue(text)), Is.EqualTo(25f));
                Assert.That(text.GetType().GetProperty("alignment").GetValue(text).ToString(), Does.Contain("Center"));

                var serialized = new SerializedObject(cell.GetComponent(cellType));
                Assert.That(serialized.FindProperty("_fretPill").objectReferenceValue, Is.SameAs(pillImage),
                    "Runtime slot colors must tint the outer pill, never the black number circle.");
            }
        }

        [Test]
        public void EveryCellBindsInheritedSelectionVisualToItsFocusVisual()
        {
            var cellType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutCell"))
                .First(type => type != null);
            var cells = _prefab.GetComponentsInChildren(cellType, true);
            Assert.That(cells, Has.Length.EqualTo(20));
            foreach (var cell in cells)
            {
                var serialized = new SerializedObject(cell);
                var selected = serialized.FindProperty("_selectedVisual");
                var focus = serialized.FindProperty("_focusVisual");
                Assert.That(selected, Is.Not.Null, cell.name + " inherited selection property");
                Assert.That(selected.objectReferenceValue, Is.Not.Null, cell.name + " inherited selection visual");
                Assert.That(selected.objectReferenceValue, Is.EqualTo(cell.transform.Find("Selected Background")?.gameObject));
                Assert.That(focus.objectReferenceValue, Is.EqualTo(selected.objectReferenceValue));
            }
        }

        [Test]
        public void DiscardConfirmationUsesSharedDialogInsteadOfLocalPanel()
        {
            Assert.That(_prefab.transform.Find("Discard Confirmation"), Is.Null);
        }

        [Test]
        public void AssignmentPopupHasFullCanvasBackdropRoundedPanelAndHeader()
        {
            var popup = Rect(_prefab.transform, "Assignment Popup");
            Assert.That(popup.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(popup.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(popup.GetComponent<Image>().raycastTarget, Is.True);
            Assert.That(popup.GetComponent<Button>(), Is.Not.Null);

            var panel = Rect(popup, "Assignment Popup Panel");
            Assert.That(panel.GetComponent<Image>().type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(panel.GetComponent<Image>().sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Menu/Common/DialogBackground.png")));
            var outline = panel.GetComponent<Outline>();
            Assert.That(outline, Is.Not.Null);
            Assert.That(outline.effectDistance, Is.EqualTo(new Vector2(1f, -1f)));
            Assert.That(panel.GetComponent<Mask>().showMaskGraphic, Is.True);
            var header = Rect(panel, "Assignment Popup Header Band");
            Assert.That(header.GetComponent<Image>().sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Menu/Common/DialogHeader.png")));
            Assert.That(header.GetComponent<Image>().type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(header.GetComponentInChildren(_tmpTextType, true).GetType()
                .GetProperty("text").GetValue(header.GetComponentInChildren(_tmpTextType, true)), Is.EqualTo("Assign A Piece"));
            Assert.That(popup.GetComponentsInChildren<Button>(true).Count(button =>
                button.transform.parent.name.StartsWith("Choice ")), Is.EqualTo(10));
        }
    }
}
