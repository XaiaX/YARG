// pattern: Imperative Shell
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class MultiplierTextWarmupTests
    {
        [TestCase("TMPro.TextMeshPro")]
        [TestCase("TMPro.TextMeshProUGUI")]
        public void CacheHasGeneratedGlyphsAndTextGeometryBeforeFirstActivation(string textTypeName)
        {
            var textType = Type.GetType(textTypeName + ", Unity.TextMeshPro", true);
            var fontType = Type.GetType("TMPro.TMP_FontAsset, Unity.TextMeshPro", true);
            var helperType = Type.GetType("YARG.Helpers.UI.MultiplierTextHelper, Assembly-CSharp", true);
            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/Fonts/Unbounded-Medium.ttf");
            Assert.That(sourceFont, Is.Not.Null);

            var root = new GameObject("Multiplier text warmup regression", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object font = null;
            try
            {
                // A fresh runtime font reproduces the empty dynamic atlas in a built player.
                // Never clear or add characters to the repository's serialized font asset.
                font = (UnityEngine.Object) fontType.GetMethod("CreateFontAsset", new[] { typeof(Font) })
                    .Invoke(null, new object[] { sourceFont });
                Assert.That(font, Is.Not.Null);
                var glyphs = (IDictionary) fontType.GetProperty("characterLookupTable").GetValue(font);
                Assert.That(glyphs.Contains((uint) '2'), Is.False, "Fixture must start without multiplier glyphs.");

                var templateObject = new GameObject("Disabled multiplier template", typeof(RectTransform));
                templateObject.transform.SetParent(root.transform, false);
                var template = (Behaviour) templateObject.AddComponent(textType);
                textType.GetProperty("font").SetValue(template, font);
                textType.GetProperty("text").SetValue(template, string.Empty);
                template.enabled = false;

                var cache = (Array) helperType.GetMethod("CreateMultiplierTextCache")
                    .MakeGenericMethod(textType).Invoke(null, new object[] { 4, template, false });

                foreach (var character in "23468x")
                    Assert.That(glyphs.Contains((uint) character), Is.True,
                        $"Glyph '{character}' must be ready before gameplay enables a multiplier.");

                foreach (var value in cache)
                {
                    if (value == null) continue;
                    var text = (Behaviour) value;
                    Assert.That(text.enabled, Is.False, "Prewarming must not display every multiplier.");
                    var textInfo = textType.GetProperty("textInfo").GetValue(text);
                    Assert.That((int) textInfo.GetType().GetField("characterCount").GetValue(textInfo), Is.EqualTo(2),
                        "The cache must contain parsed number/x geometry before activation, not just object instances.");
                    Assert.That((int) textInfo.GetType().GetField("materialCount").GetValue(textInfo), Is.GreaterThan(0));
                    var meshInfo = (Array) textInfo.GetType().GetField("meshInfo").GetValue(textInfo);
                    var firstMesh = meshInfo.GetValue(0);
                    Assert.That((int) firstMesh.GetType().GetField("vertexCount").GetValue(firstMesh), Is.GreaterThan(0));
                    if (textTypeName == "TMPro.TextMeshProUGUI")
                    {
                        var renderer = text.GetComponent<CanvasRenderer>();
                        var renderedMesh = renderer.GetMesh();
                        Assert.That(renderedMesh == null || renderedMesh.vertexCount == 0, Is.True,
                            "Prepared UI multipliers must not submit visible meshes while disabled.");
                    }
                    else
                    {
                        Assert.That(text.GetComponent<MeshFilter>().sharedMesh, Is.Null,
                            "Prepared 3D multipliers must stay detached until selected.");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (font != null)
                {
                    var material = (UnityEngine.Object) fontType.GetProperty("material").GetValue(font);
                    var atlases = (Array) fontType.GetProperty("atlasTextures").GetValue(font);
                    foreach (UnityEngine.Object atlas in atlases)
                        if (atlas != null) UnityEngine.Object.DestroyImmediate(atlas);
                    if (material != null) UnityEngine.Object.DestroyImmediate(material);
                    UnityEngine.Object.DestroyImmediate(font);
                }
            }
        }
    }
}
