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
        private Type _profileSidebarType;
        private Type _localizeTextType;
        private Type _localizeType;
        private Type _runtimeNavigatableType;
        private Type _layoutMenuType;

        [SetUp]
        public void LoadProductionPrefab()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Assert.That(source, Is.Not.Null, "Generate the production prefab before visual geometry tests.");
            _canvas = new GameObject("Visual prefab test canvas", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_canvas.transform).sizeDelta = new Vector2(1920f, 1080f);
            _prefab = UnityEngine.Object.Instantiate(source, _canvas.transform, false);
            Canvas.ForceUpdateCanvases();
            _tmpTextType = RuntimeType("TMPro.TMP_Text");
            _profileSidebarType = RuntimeType("YARG.Menu.ProfileList.ProfileSidebar");
            _localizeTextType = RuntimeType("YARG.Localization.LocalizeText");
            _localizeType = RuntimeType("YARG.Localization.Localize");
            _runtimeNavigatableType = RuntimeType("YARG.Menu.Navigation.RuntimeNavigatable");
            _layoutMenuType = RuntimeType("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
        }

        [TearDown]
        public void Cleanup()
        {
            if (_canvas != null) UnityEngine.Object.DestroyImmediate(_canvas);
        }

        private static Type RuntimeType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

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
        public void LayoutTransformActionsUseImportStyleWithinLeftSection()
        {
            var notice = Rect(_prefab.transform, "Built In Notice");
            var divider = Rect(_prefab.transform, "Lefty Flip Divider");
            float leftSectionRight = divider.anchorMin.x - 0.009f;
            Assert.That(notice.anchorMin.x, Is.EqualTo(0.0225f).Within(0.0001f));
            Assert.That(notice.anchorMax.x, Is.EqualTo(leftSectionRight).Within(0.0001f));
            Assert.That(notice.anchorMin.y, Is.EqualTo(0.245f).Within(0.0001f));
            Assert.That(notice.anchorMax.y, Is.EqualTo(0.425f).Within(0.0001f));
            var noticeTexts = notice.GetComponentsInChildren(_tmpTextType, true);
            Assert.That(noticeTexts, Is.Not.Empty);
            foreach (var noticeText in noticeTexts)
            {
                var noticeContent = (string)noticeText.GetType().GetProperty("text").GetValue(noticeText);
                Assert.That(noticeContent, Does.Contain("built-in preset"));
                Assert.That(noticeContent, Does.Contain("cannot modify"));
                Assert.That(noticeText.GetType().GetProperty("alignment").GetValue(noticeText).ToString(), Does.Contain("Center"));
                Assert.That(noticeText.GetType().GetProperty("textWrappingMode").GetValue(noticeText).ToString(), Does.Contain("Normal"));
            }
            var import = Rect(_prefab.transform, "Import");
            var importButton = import.GetComponentInChildren<Button>(true);
            var importText = import.GetComponentInChildren(_tmpTextType, true);
            string[] labels = { "Shift Left", "Reverse", "Shift Right" };
            float priorRight = float.NegativeInfinity;
            foreach (var label in labels)
            {
                var wrapper = Rect(_prefab.transform, label);
                Assert.That(wrapper, Is.Not.Null);
                Assert.That(wrapper.gameObject.activeSelf, Is.False, "Runtime chooses actions or built-in notice.");
                var button = wrapper.GetComponentInChildren<Button>(true);
                Assert.That(button.image.sprite, Is.EqualTo(importButton.image.sprite));
                Assert.That(button.image.color, Is.EqualTo(importButton.image.color));
                Assert.That(wrapper.rect.height, Is.EqualTo(56f).Within(0.01f));
                Assert.That(button.GetComponent<RectTransform>().rect.height, Is.EqualTo(50f).Within(0.01f));
                Assert.That(wrapper.anchorMin.y, Is.EqualTo(0.3438889f).Within(0.0001f));
                var wrapperCorners = new Vector3[4];
                wrapper.GetWorldCorners(wrapperCorners);
                Assert.That(wrapperCorners[0].x, Is.GreaterThanOrEqualTo(priorRight));
                priorRight = wrapperCorners[2].x;
                float sectionRightWorld = _prefab.transform.TransformPoint(new Vector3(
                    ((RectTransform)_prefab.transform).rect.xMin + leftSectionRight * 1920f, 0f)).x;
                Assert.That(wrapperCorners[2].x, Is.LessThanOrEqualTo(sectionRightWorld + 0.01f));
                var text = wrapper.GetComponentInChildren(_tmpTextType, true);
                Assert.That(AssetDatabase.GetAssetPath((UnityEngine.Object)_tmpTextType.GetProperty("font").GetValue(text)),
                    Is.EqualTo("Assets/Art/Fonts/Barlow/EliteDrumsTransform Barlow SDF.asset"));
                Assert.That(_tmpTextType.GetProperty("fontStyle").GetValue(text),
                    Is.EqualTo(_tmpTextType.GetProperty("fontStyle").GetValue(importText)), label + " fontStyle");
                Assert.That(Convert.ToSingle(_tmpTextType.GetProperty("fontSize").GetValue(text)),
                    Is.InRange(18f, 24f), label + " readable shared font size");
                var corners = new Vector3[4];
                wrapper.GetWorldCorners(corners);
                foreach (var corner in corners)
                    Assert.That(notice.rect.Contains(notice.InverseTransformPoint(corner)), Is.True,
                        label + " must occupy only the built-in notice region.");
            }
        }

        private void AssertScopedPopupBand(Transform band, Transform authored, string assetName, float height)
        {
            var image = band.GetComponent<Image>();
            string path = "Assets/Art/Menu/EliteLayout/" + assetName + ".png";
            Assert.That(AssetDatabase.GetAssetPath(image.sprite), Is.EqualTo(path));
            Assert.That(image.sprite, Is.Not.SameAs(authored.GetComponent<Image>().sprite));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(File.ReadAllBytes(
                AssetDatabase.GetAssetPath(authored.GetComponent<Image>().sprite))),
                "Scoped artwork preserves the original pixels without changing its importer.");
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(image.fillCenter, Is.True);
            Assert.That(image.sprite.border.x, Is.GreaterThan(0f));
            Assert.That(image.sprite.border.z, Is.GreaterThan(0f));
            Assert.That(band.GetComponent<RectTransform>().rect.height, Is.EqualTo(height).Within(0.01f));
            var backing = band.parent.Find(band.name + " Opaque Backing");
            Assert.That(backing, Is.Not.Null);
            var backingImage = backing.GetComponent<Image>();
            Assert.That(AssetDatabase.GetAssetPath(backingImage.sprite),
                Is.EqualTo(path.Replace(".png", "Backing.png")));
            Assert.That(backingImage.color, Is.EqualTo(new Color(1f / 255f, 8f / 255f, 14f / 255f, 1f)));
            Assert.That(backingImage.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(backingImage.raycastTarget, Is.False);
        }

        [Test]
        public void TransformActionLabelsAndEqualGeometry()
        {
            var wrappers = new[] { "Shift Left", "Reverse", "Shift Right" }
                .Select(name => Rect(_prefab.transform, name)).ToArray();
            var texts = wrappers.Select(wrapper => wrapper.GetComponentInChildren(_tmpTextType, true)).ToArray();
            var layout = RuntimeType("YARG.Menu.HighwayConfiguration.EliteDrumsTransformButtonLayout");
            // The generated prefab is authored in English; no localization singleton is mounted here.
            string[] captions = { "Shift Left", "Swap", "Shift Right" };
            string[] methods = { "ShiftLeftLabel", "ReverseLabel", "ShiftRightLabel" };
            for (int i = 0; i < 3; i++)
            {
                string caption = captions[i];
                string expected = i == 0 ? "\u2190 " + caption : i == 1 ? "\u21C4  " + caption + "  \u21C6" : caption + " \u2192";
                Assert.That(_tmpTextType.GetProperty("text").GetValue(texts[i]), Is.EqualTo(expected));
                Assert.That(layout.GetMethod(methods[i]).Invoke(null, new object[] { "Mixed Case" }),
                    Is.EqualTo(i == 0 ? "\u2190 Mixed Case" : i == 1 ? "\u21C4  Mixed Case  \u21C6" : "Mixed Case \u2192"));
            }
            AssertEqualCaptionGeometry(wrappers, texts);

            // Fit a longer caption on the instantiated copy; never change prefab or shared fonts.
            _tmpTextType.GetProperty("text").SetValue(texts[1], "\u21C4 Reverse columns \u21C6");
            var typedTexts = Array.CreateInstance(_tmpTextType, 3);
            for (int i = 0; i < 3; i++) typedTexts.SetValue(texts[i], i);
            float right = Rect(_prefab.transform, "Lefty Flip Divider").anchorMin.x - 0.009f;
            Assert.That(layout.GetMethod("Apply").Invoke(null, new object[] {
                wrappers, typedTexts, (RectTransform)_prefab.transform, 0.0225f, right, 12f, 24f, 18f }), Is.True);
            AssertEqualCaptionGeometry(wrappers, texts);
            Assert.That(wrappers[0].anchorMin.x * 1920f - wrappers[0].rect.width / 2f,
                Is.GreaterThanOrEqualTo(0.0225f * 1920f - 0.01f));
            Assert.That(wrappers[2].anchorMax.x * 1920f + wrappers[2].rect.width / 2f,
                Is.LessThanOrEqualTo(right * 1920f + 0.01f));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void TransformApplyUsesReadableSharedSizeOrWrappedFit(bool wrap, bool impossible)
        {
            var section = new GameObject("Synthetic transform section", typeof(RectTransform))
                .GetComponent<RectTransform>();
            section.SetParent(_canvas.transform, false);
            var source = Rect(_prefab.transform, "Reverse").GetComponentInChildren(_tmpTextType, true);
            var wrappers = new RectTransform[3];
            var texts = new Component[3];
            var typedTexts = Array.CreateInstance(_tmpTextType, 3);
            for (int i = 0; i < 3; i++)
            {
                wrappers[i] = new GameObject("Synthetic action " + i, typeof(RectTransform)).GetComponent<RectTransform>();
                wrappers[i].SetParent(section, false);
                wrappers[i].anchorMin = wrappers[i].anchorMax = new Vector2(0.2f + i * 0.3f, 0.4f);
                wrappers[i].sizeDelta = new Vector2(73f + i, 56f);
                wrappers[i].anchoredPosition = new Vector2(7f + i, 9f);
                texts[i] = UnityEngine.Object.Instantiate(source.gameObject, wrappers[i], false).GetComponent(_tmpTextType);
                _tmpTextType.GetProperty("text").SetValue(texts[i], i == 1
                    ? "Reverse all assigned highway columns" : "Shift columns");
                typedTexts.SetValue(texts[i], i);
            }
            Vector2 Preferred(Component text, float width) => (Vector2)_tmpTextType.GetMethod("GetPreferredValues",
                new[] { typeof(string), typeof(float), typeof(float) }).Invoke(text, new object[] {
                    _tmpTextType.GetProperty("text").GetValue(text), width, Mathf.Infinity });
            float Measure(float size)
            {
                foreach (var text in texts) _tmpTextType.GetProperty("fontSize").SetValue(text, size);
                return texts.Max(text => Preferred(text, Mathf.Infinity).x) + 32f;
            }
            float fullWidth = Measure(24f);
            float floorWidth = Measure(18f);
            float targetWidth = wrap ? 32f + (floorWidth - 32f) * 0.55f : (fullWidth + floorWidth) * 0.5f;
            section.sizeDelta = new Vector2(targetWidth * 3f + 24f, 400f);
            if (wrap)
                foreach (var text in texts)
                    _tmpTextType.GetProperty("textWrappingMode").SetValue(text,
                        Enum.Parse(_tmpTextType.GetProperty("textWrappingMode").PropertyType, "Normal"));
            float wrappedHeight = texts.Max(text => Preferred(text, targetWidth - 32f).y);
            if (wrap)
            {
                Assert.That(wrappedHeight, Is.GreaterThan(texts.Max(text => Preferred(text, Mathf.Infinity).y)),
                    "The synthetic caption must genuinely require multiple lines at the readable floor.");
                foreach (var wrapper in wrappers)
                    wrapper.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, wrappedHeight + (impossible ? 3f : 4f));
            }
            var anchors = wrappers.Select(rect => rect.anchorMin).ToArray();
            var positions = wrappers.Select(rect => rect.anchoredPosition).ToArray();
            var sizes = wrappers.Select(rect => rect.sizeDelta).ToArray();
            var layout = RuntimeType("YARG.Menu.HighwayConfiguration.EliteDrumsTransformButtonLayout");
            Assert.That(layout.GetMethod("Apply").Invoke(null, new object[] {
                wrappers, typedTexts, section, 0f, 1f, 12f, 24f, 18f }), Is.EqualTo(!impossible));
            for (int i = 0; i < 3; i++)
            {
                float size = Convert.ToSingle(_tmpTextType.GetProperty("fontSize").GetValue(texts[i]));
                Assert.That(size, Is.EqualTo(Convert.ToSingle(_tmpTextType.GetProperty("fontSize").GetValue(texts[0]))));
                Assert.That(size, Is.GreaterThanOrEqualTo(18f).And.LessThan(24f));
                Assert.That(_tmpTextType.GetProperty("enableAutoSizing").GetValue(texts[i]), Is.False);
                if (impossible)
                {
                    Assert.That(wrappers[i].anchorMin, Is.EqualTo(anchors[i]));
                    Assert.That(wrappers[i].anchorMax, Is.EqualTo(anchors[i]));
                    Assert.That(wrappers[i].anchoredPosition, Is.EqualTo(positions[i]));
                    Assert.That(wrappers[i].sizeDelta, Is.EqualTo(sizes[i]), "Failure must not move or resize wrappers.");
                    continue;
                }
                Assert.That(_tmpTextType.GetProperty("textWrappingMode").GetValue(texts[i]).ToString(),
                    Is.EqualTo(wrap ? "Normal" : "NoWrap"));
                Assert.That(wrappers[i].rect.width, Is.EqualTo(wrappers[0].rect.width).Within(0.001f));
                Assert.That(wrappers[i].rect.width, Is.EqualTo(targetWidth).Within(0.1f));
                Assert.That(wrappers[i].anchoredPosition.y, Is.EqualTo(positions[i].y));
                Assert.That(wrappers[i].sizeDelta.y, Is.EqualTo(sizes[i].y));
                if (wrap)
                {
                    Assert.That(size, Is.EqualTo(18f));
                    Assert.That(Preferred(texts[i], targetWidth - 32f).y,
                        Is.LessThanOrEqualTo(wrappers[i].rect.height - 4f + 0.001f));
                }
                else Assert.That(Preferred(texts[i], Mathf.Infinity).x + 32f,
                    Is.LessThanOrEqualTo(wrappers[i].rect.width + 0.01f));
                if (i > 0) Assert.That((wrappers[i].anchorMin.x - wrappers[i - 1].anchorMin.x) * section.rect.width,
                    Is.EqualTo(wrappers[i].rect.width + 12f).Within(0.01f));
            }
            if (!impossible)
            {
                Assert.That(wrappers[0].anchorMin.x * section.rect.width - wrappers[0].rect.width / 2f,
                    Is.GreaterThanOrEqualTo(-0.01f));
                Assert.That(wrappers[2].anchorMax.x * section.rect.width + wrappers[2].rect.width / 2f,
                    Is.LessThanOrEqualTo(section.rect.width + 0.01f));
            }
        }

        private void AssertEqualCaptionGeometry(RectTransform[] wrappers, Component[] texts)
        {
            float longest = texts.Max(text => ((Vector2)_tmpTextType.GetMethod("GetPreferredValues", new[] { typeof(string) })
                .Invoke(text, new[] { _tmpTextType.GetProperty("text").GetValue(text) })).x);
            float fontSize = Convert.ToSingle(_tmpTextType.GetProperty("fontSize").GetValue(texts[0]));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(wrappers[i].rect.width, Is.EqualTo(longest + 32f).Within(0.1f),
                    "Every action reserves the longest caption plus the full horizontal padding.");
                Assert.That(_tmpTextType.GetProperty("fontSize").GetValue(texts[i]), Is.EqualTo(fontSize));
                Assert.That(fontSize, Is.InRange(18f, 24f));
                Assert.That(_tmpTextType.GetProperty("enableAutoSizing").GetValue(texts[i]), Is.False);
                Assert.That(_tmpTextType.GetProperty("textWrappingMode").GetValue(texts[i]).ToString(), Is.EqualTo("NoWrap"));
                Assert.That(texts[i].GetComponent<RectTransform>().rect.width, Is.GreaterThanOrEqualTo(longest),
                    "Even the longest caption fits the actual inner text rect without wrapping or clipping.");
                if (i > 0) Assert.That((wrappers[i].anchorMin.x - wrappers[i - 1].anchorMin.x) * 1920f,
                    Is.EqualTo(wrappers[i].rect.width + 12f).Within(0.1f));
            }
        }

        [Test]
        public void TransformArrowFontCoverage()
        {
            var fontType = RuntimeType("TMPro.TMP_FontAsset");
            var labels = AssetDatabase.LoadAssetAtPath("Assets/Art/Fonts/Barlow/EliteDrumsTransform Barlow SDF.asset", fontType);
            var arrows = AssetDatabase.LoadAssetAtPath("Assets/Art/Fonts/NotoSans/EliteDrumsTransform Arrows SDF.asset", fontType);
            Assert.That(labels, Is.Not.Null);
            Assert.That(arrows, Is.Not.Null);
            var importText = Rect(_prefab.transform, "Import").GetComponentInChildren(_tmpTextType, true);
            var original = (UnityEngine.Object)_tmpTextType.GetProperty("font").GetValue(importText);
            Assert.That(labels, Is.Not.SameAs(original));
            var face = fontType.GetProperty("faceInfo");
            var originalFace = face.GetValue(original);
            var copiedFace = face.GetValue(labels);
            foreach (string property in new[] { "familyName", "styleName" })
                Assert.That(copiedFace.GetType().GetProperty(property).GetValue(copiedFace),
                    Is.EqualTo(originalFace.GetType().GetProperty(property).GetValue(originalFace)),
                    "The scoped copy preserves the actual source prefab font face: " + property);
            var labelMaterial = (Material)fontType.GetProperty("material").GetValue(labels);
            var labelAtlas = ((Texture2D[])fontType.GetProperty("atlasTextures").GetValue(labels))[0];
            Assert.That(labelMaterial.mainTexture, Is.SameAs(labelAtlas));
            foreach (string name in new[] { "Shift Left", "Reverse", "Shift Right" })
            {
                var text = Rect(_prefab.transform, name).GetComponentInChildren(_tmpTextType, true);
                Assert.That(_tmpTextType.GetProperty("font").GetValue(text), Is.SameAs(labels));
                var sharedMaterial = (Material)_tmpTextType.GetProperty("fontSharedMaterial").GetValue(text);
                Assert.That(sharedMaterial, Is.Not.Null);
                Assert.That(sharedMaterial.mainTexture, Is.SameAs(labelAtlas),
                    name + " shared SDF material must use the dedicated Barlow atlas, not the original atlas.");
            }
            var originalFallbacks = ((System.Collections.IEnumerable)fontType.GetProperty("fallbackFontAssetTable")
                .GetValue(original)).Cast<UnityEngine.Object>().ToArray();
            var fallbacks = ((System.Collections.IEnumerable)fontType.GetProperty("fallbackFontAssetTable")
                .GetValue(labels)).Cast<UnityEngine.Object>().ToArray();
            Assert.That(fallbacks, Is.EqualTo(new[] { arrows }.Concat(originalFallbacks)));
            Assert.That(originalFallbacks, Has.None.SameAs(arrows));
            Assert.That(fontType.GetProperty("atlasPopulationMode").GetValue(arrows).ToString(), Is.EqualTo("Static"));
            Assert.That(fontType.GetProperty("isMultiAtlasTexturesEnabled").GetValue(arrows), Is.False);
            var textures = (Texture2D[])fontType.GetProperty("atlasTextures").GetValue(arrows);
            Assert.That(textures, Has.Length.EqualTo(1));
            var material = (Material)fontType.GetProperty("material").GetValue(arrows);
            Assert.That(material.mainTexture, Is.SameAs(textures[0]));
            fontType.GetMethod("ReadFontAssetDefinition").Invoke(arrows, null);
            var characters = ((System.Collections.IEnumerable)fontType.GetProperty("characterTable").GetValue(arrows))
                .Cast<object>().ToArray();
            uint[] codepoints = { 0x2190, 0x2192, 0x21C4, 0x21C6 };
            Assert.That(characters.Select(character => (uint)character.GetType().GetProperty("unicode").GetValue(character)),
                Is.EquivalentTo(codepoints), "The static fallback contains exactly the four approved arrows.");
            foreach (var character in characters)
            {
                var glyph = character.GetType().GetProperty("glyph").GetValue(character);
                Assert.That(glyph, Is.Not.Null);
                Assert.That((uint)glyph.GetType().GetProperty("index").GetValue(glyph), Is.GreaterThan(0u));
                Assert.That((int)glyph.GetType().GetProperty("atlasIndex").GetValue(glyph), Is.Zero);
                var rect = glyph.GetType().GetProperty("glyphRect").GetValue(glyph);
                int X(string name) => (int)rect.GetType().GetProperty(name).GetValue(rect);
                Assert.That(X("width"), Is.GreaterThan(0));
                Assert.That(X("height"), Is.GreaterThan(0));
                Assert.That(X("x"), Is.GreaterThanOrEqualTo(0));
                Assert.That(X("y"), Is.GreaterThanOrEqualTo(0));
                Assert.That(X("x") + X("width"), Is.LessThanOrEqualTo(textures[0].width));
                Assert.That(X("y") + X("height"), Is.LessThanOrEqualTo(textures[0].height));
                Assert.That(textures[0].GetPixels(X("x"), X("y"), X("width"), X("height")).Any(pixel => pixel.a > 0f), Is.True);
            }
            string[] paths = new[] { AssetDatabase.GetAssetPath(original),
                "Assets/Art/Fonts/NotoSans/NotoSansJP-Regular SDF.asset",
                "Assets/TextMesh Pro/Resources/TMP Settings.asset" };
            foreach (string path in paths) Assert.That(File.Exists(path), Is.True, path);
            var before = paths.Select(File.ReadAllBytes).ToArray();
            var builder = RuntimeType("YARG.Editor.EliteDrumsTransformFontBuilder");
            Assert.That(builder.GetMethod("EnsureTransformFont").Invoke(null, new object[] { original }), Is.SameAs(labels));
            for (int i = 0; i < paths.Length; i++) Assert.That(File.ReadAllBytes(paths[i]), Is.EqualTo(before[i]), paths[i]);
            Assert.That(((System.Collections.IEnumerable)fontType.GetProperty("fallbackFontAssetTable").GetValue(original))
                .Cast<UnityEngine.Object>(), Is.EqualTo(originalFallbacks));
        }

        [Test]
        public void ProfileControlsPrefabContract()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab");
            Assert.That(source, Is.Not.Null);
            var sourceSidebar = (Component)source.GetComponentInChildren(_profileSidebarType, true);
            var sourceToggleData = new SerializedObject(sourceSidebar);
            var sourceToggle = (Toggle)sourceToggleData.FindProperty("_leftyFlipToggle").objectReferenceValue;
            Assert.That(sourceToggle, Is.Not.Null);

            var row = _prefab.transform.Find("Lefty Flip");
            Assert.That(row, Is.Not.Null, "The editor clones the authored profile Lefty Flip control.");
            var toggle = row.GetComponentInChildren<Toggle>(true);
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle, Is.Not.SameAs(sourceToggle));
            var eventData = new SerializedObject(toggle);
            Assert.That(eventData.FindProperty("onValueChanged.m_PersistentCalls.m_Calls").arraySize, Is.Zero,
                "The editor copy must not retain profile-menu persistent callbacks.");
            Assert.That(sourceToggle.onValueChanged.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(sourceToggle.onValueChanged.GetPersistentMethodName(0), Is.EqualTo("ChangeLeftyFlip"),
                "The source profile toggle keeps its profile-setting callback.");
            var label = row.GetComponentsInChildren(_tmpTextType, true).Cast<Component>()
                .Single(text => text.GetComponent(_localizeTextType) != null);
            var localized = new SerializedObject(label.GetComponent(_localizeTextType));
            Assert.That(localized.FindProperty("_localizationKey").stringValue,
                Is.EqualTo("Menu.DifficultySelect.LeftyFlip"));

            var sourceGraphicRect = sourceToggle.graphic.rectTransform;
            var rowRect = row.GetComponent<RectTransform>();
            var toggleRect = toggle.GetComponent<RectTransform>();
            Assert.That(toggleRect.sizeDelta, Is.EqualTo(sourceGraphicRect.rect.size),
                "The clone uses the authored checkbox graphic dimensions, not the stretched source row size.");
            Assert.That(toggleRect.rect.width, Is.EqualTo(sourceGraphicRect.rect.width).Within(0.01f));
            Assert.That(toggleRect.rect.height, Is.EqualTo(sourceGraphicRect.rect.height).Within(0.01f));
            Assert.That(toggleRect.localScale, Is.EqualTo(sourceToggle.transform.localScale));
            Assert.That(toggleRect.anchorMax.x, Is.EqualTo(1f).Within(0.000001f));
            Assert.That(toggleRect.pivot.x, Is.EqualTo(1f).Within(0.000001f));
            var divider = Rect(_prefab.transform, "Lefty Flip Divider");
            Assert.That(divider, Is.Not.Null);
            Assert.That(divider.GetComponent<Image>().raycastTarget, Is.False);
            float gridRight = 0.0225f + 9f * 0.061f + 0.056f;
            float leftSectionRight = divider.anchorMin.x - 0.009f;
            Assert.That(rowRect.anchorMin.x, Is.EqualTo(divider.anchorMin.x + 0.009f).Within(0.0001f));
            Assert.That(rowRect.anchorMax.x, Is.EqualTo(gridRight).Within(0.0001f));
            Assert.That(toggleRect.anchorMin.x, Is.EqualTo(1f));
            Assert.That(toggleRect.anchoredPosition.x, Is.EqualTo(-15f).Within(0.01f));
            var labelText = (Component)label;
            var labelRect = (RectTransform)_tmpTextType.GetProperty("rectTransform").GetValue(labelText);
            var key = _tmpTextType.GetProperty("text").GetValue(labelText);
            var preferredValues = (Vector2)_tmpTextType.GetMethod("GetPreferredValues", new[] { typeof(string) })
                .Invoke(labelText, new[] { key });
            float labelPreferredWidth = preferredValues.x;
            Assert.That(labelRect.sizeDelta.x, Is.EqualTo(labelPreferredWidth + 12f).Within(1f),
                "The label width fits its localized preferred size plus the toggle gap.");
            Assert.That(labelRect.anchorMin.x, Is.Zero);
            Assert.That(labelRect.anchorMax.x, Is.Zero);
            Assert.That(leftSectionRight, Is.LessThan(rowRect.anchorMin.x));
            Assert.That(toggleRect.rect.width, Is.EqualTo(sourceGraphicRect.rect.width).Within(0.01f));
            Assert.That(toggle.gameObject.activeSelf, Is.True,
                "The serialized control is active; the generated menu root intentionally starts inactive.");
            Assert.That(toggle.enabled, Is.True);
            Assert.That(row.GetComponentsInChildren(_runtimeNavigatableType, true), Is.Empty,
                "Copied profile navigation must not own editor controls.");
            var menuComponent = _prefab.GetComponent(_layoutMenuType);
            var menuData = new SerializedObject(menuComponent);
            Assert.That(menuData.FindProperty("_leftyFlipToggle").objectReferenceValue, Is.SameAs(toggle),
                "The cloned control is owned through the editor menu's serialized field.");

            var sourceRow = sourceToggle.transform;
            while (sourceRow != null && sourceRow.name != "Lefty Flip") sourceRow = sourceRow.parent;
            Assert.That(sourceRow, Is.Not.Null);
            var sourceLabel = sourceRow.GetComponentsInChildren(_tmpTextType, true).Cast<Component>()
                .Single(text => text.name == "Option Name");
            Assert.That(_tmpTextType.GetProperty("text").GetValue(sourceLabel), Is.Not.EqualTo("Menu.DifficultySelect.LeftyFlip"),
                "Generating the editor copy must not rewrite the source profile label.");
            var sourceRowImages = sourceRow.GetComponentsInChildren<Image>(true);
            Assert.That(sourceRowImages.Any(image => !image.transform.IsChildOf(sourceToggle.transform)), Is.True,
                "The production profile row keeps its authored horizontal decoration.");
            Assert.That(row.GetComponentsInChildren<Image>(true)
                .Where(image => image.transform != toggle.transform
                    && !image.transform.IsChildOf(toggle.transform)), Is.Empty,
                "Only decoration outside the cloned Toggle is removed from the editor row.");
            Assert.That(toggle.GetComponent<Image>().sprite, Is.EqualTo(sourceToggle.GetComponent<Image>().sprite));
            Assert.That(toggle.graphic, Is.Not.Null, "The authored on overlay must survive clone cleanup.");
            Assert.That(((Image)toggle.graphic).sprite, Is.EqualTo(((Image)sourceToggle.graphic).sprite));
            Assert.That(toggle.graphic.transform.IsChildOf(toggle.transform), Is.True);
            var focus = toggle.transform.Find("Lefty Flip Focus");
            Assert.That(focus, Is.Not.Null);
            Assert.That(focus.GetComponent<Image>().raycastTarget, Is.False);
            Assert.That(focus.gameObject.activeSelf, Is.False);
            Assert.That(toggle.isOn, Is.EqualTo(sourceToggle.isOn));
            Assert.That(toggle.interactable, Is.EqualTo(sourceToggle.interactable));
            foreach (var property in new[] { "font", "fontSize", "fontStyle", "fontWeight", "color", "alignment" })
                Assert.That(_tmpTextType.GetProperty(property).GetValue(label),
                    Is.EqualTo(_tmpTextType.GetProperty(property).GetValue(sourceLabel)), property);
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
        public void AssignmentPopupHasFullCanvasBackdropRoundedPanelAndHeader() => AssignmentPopupDifficultyStyleContract();

        [Test]
        public void AssignmentPopupDifficultyStyleContract()
        {
            const string difficultyMenuPath = "Assets/Prefabs/Menu/DifficultySelect/DifficultySelectMenu.prefab";
            const string difficultyItemPath = "Assets/Prefabs/Menu/DifficultySelect/DifficultyItem.prefab";
            const string coloredItemPath = "Assets/Prefabs/Menu/DifficultySelect/DifficultyItemColored.prefab";

            var authoredMenu = AssetDatabase.LoadAssetAtPath<GameObject>(difficultyMenuPath);
            var authoredItem = AssetDatabase.LoadAssetAtPath<GameObject>(difficultyItemPath);
            var authoredColoredItem = AssetDatabase.LoadAssetAtPath<GameObject>(coloredItemPath);
            Assert.That(authoredMenu, Is.Not.Null);
            Assert.That(authoredItem, Is.Not.Null);
            Assert.That(authoredColoredItem, Is.Not.Null);

            var popup = Rect(_prefab.transform, "Assignment Popup");
            Assert.That(popup.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(popup.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(popup.GetComponent<Image>().raycastTarget, Is.True);
            Assert.That(popup.GetComponent<Button>(), Is.Not.Null);

            var panel = Rect(popup, "Assignment Popup Panel");
            Assert.That(panel.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(panel.anchorMax, Is.EqualTo(panel.anchorMin));
            Assert.That(panel.pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(panel.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(panel.sizeDelta.x, Is.GreaterThan(0f));
            Assert.That(panel.sizeDelta.y, Is.GreaterThan(0f));
            var authoredSurface = authoredItem.transform.Find("Background").GetComponent<Image>();
            var panelSurface = panel.GetComponent<Image>();
            Assert.That(panelSurface.color.a, Is.Zero, "The hit surface must not fill the rounded outer corners.");
            Assert.That(panelSurface.raycastTarget, Is.True);
            Assert.That(panelSurface.canvasRenderer.cullTransparentMesh, Is.False);
            var listSurface = Rect(panel, "Dark List Surface");
            Assert.That(listSurface.offsetMin, Is.EqualTo(new Vector2(0f, 15f)));
            Assert.That(listSurface.offsetMax, Is.EqualTo(new Vector2(0f, -80f)));
            Assert.That(listSurface.GetComponent<Image>().raycastTarget, Is.False);
            var layoutType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                assembly.GetType("YARG.Menu.HighwayConfiguration.AssignmentPopupLayout")).First(type => type != null);
            Assert.That(popup.GetComponent(layoutType), Is.Not.Null, "Runtime visible captions determine the explicit card size.");
            Assert.That(panel.GetComponent<Button>(), Is.Not.Null, "Panel surface blocks backdrop clicks.");
            Assert.That(panel.GetComponent<Outline>(), Is.Null, "Use the difficulty surface, not a dialog outline.");
            Assert.That(panel.GetComponent<Mask>(), Is.Null, "The scrolling viewport owns row clipping.");

            var authoredHeader = authoredMenu.transform.Find("Menu/Header");
            var header = Rect(panel, "Assignment Popup Header Band");
            AssertScopedPopupBand(header, authoredHeader, "AssignmentPopupHeader", 80f);
            Assert.That(header.GetComponent<Image>().material,
                Is.EqualTo(authoredHeader.GetComponent<Image>().material));
            var title = header.GetComponentInChildren(_tmpTextType, true);
            Assert.That(title.GetType().GetProperty("text").GetValue(title), Is.EqualTo("Assign A Piece"));
            Assert.That(title.GetType().GetProperty("font").GetValue(title),
                Is.EqualTo(authoredHeader.GetComponentInChildren(_tmpTextType, true).GetType()
                    .GetProperty("font").GetValue(authoredHeader.GetComponentInChildren(_tmpTextType, true))));
            Assert.That(title.GetType().GetProperty("fontMaterial").GetValue(title), Is.Not.Null);

            var authoredFooter = authoredMenu.transform.Find("Menu/Footer");
            var footer = panel.Find("Footer(Clone)");
            Assert.That(footer, Is.Not.Null);
            AssertScopedPopupBand(footer, authoredFooter, "AssignmentPopupFooter", 15f);
            Assert.That(footer.GetComponent<Image>().raycastTarget, Is.False);

            var viewport = Rect(panel, "Assignment Choices Viewport");
            Assert.That(viewport.offsetMin, Is.EqualTo(new Vector2(0f, 15f)));
            Assert.That(viewport.offsetMax, Is.EqualTo(new Vector2(0f, -80f)));
            Assert.That(viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            var scroll = panel.GetComponent<ScrollRect>();
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll.viewport, Is.EqualTo(viewport));
            Assert.That(scroll.content, Is.EqualTo(viewport.Find("Assignment Choices").GetComponent<RectTransform>()));
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.movementType, Is.EqualTo(ScrollRect.MovementType.Clamped));
            Assert.That(viewport.rect.width, Is.GreaterThan(0f), "The row labels need a nonzero wrapping width.");

            var authoredSelection = authoredItem.transform.Find("Selected Background").GetComponent<Image>();
            var choices = scroll.content.GetComponentsInChildren<Button>(true);
            Assert.That(choices, Has.Length.EqualTo(10));
            foreach (var button in choices)
            {
                var row = button.transform.parent;
                Assert.That(row.name, Does.StartWith("Choice "));
                Assert.That(row.Find("Header").gameObject.activeSelf, Is.False);
                var rowBackground = row.Find("Background").GetComponent<Image>();
                Assert.That(rowBackground.sprite, Is.EqualTo(authoredSurface.sprite));
                var selection = row.Find("Selected Background").GetComponent<Image>();
                Assert.That(selection.sprite, Is.EqualTo(authoredSelection.sprite));
                Assert.That(row.GetComponent<LayoutElement>().preferredHeight, Is.EqualTo(64f));
                var label = row.Find("Body").GetComponent(_tmpTextType);
                Assert.That(label.GetType().GetProperty("font").GetValue(label), Is.Not.Null);
                Assert.That(label.GetType().GetProperty("fontMaterial").GetValue(label), Is.Not.Null);
                Assert.That(((RectTransform)row.Find("Body")).rect.width, Is.GreaterThan(0f),
                    "Difficulty row labels must fit within the scrolling viewport.");
                Assert.That(button.GetComponent(AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                    assembly.GetType("YARG.Menu.HighwayConfiguration.AssignmentPopupRow")).First(type => type != null)), Is.Not.Null);
            }
        }
    }
}
