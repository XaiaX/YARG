using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace YARG.Tests.PresetPreview
{
    public sealed class RectangularThemeCymbalAccentMaterialTests
    {
        private const BindingFlags INSTANCE_FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string THEME_PATH = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";
        private const string CYMBAL_GHOST_CENTER_PATH = "Assets/Art/Materials/Gameplay/Notes/Rectangular/CymbalGhostCenter.mat";

        [Test]
        public void FiveLaneCymbalAccentUsesLitCenterMaterialAndPreservesExistingBindings()
        {
            var themePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(THEME_PATH);
            Assert.That(themePrefab, Is.Not.Null, $"Production theme prefab not found: {THEME_PATH}");

            var instantiatedTheme = Object.Instantiate(themePrefab);
            try
            {
                var gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .Single(assembly => assembly.GetName().Name == "Assembly-CSharp");
                var themeComponentType = gameAssembly.GetType("YARG.Themes.ThemeComponent", true);
                var visualStyleType = gameAssembly.GetType("YARG.Themes.VisualStyle", true);
                var themeNoteType = gameAssembly.GetType("YARG.Themes.ThemeNoteType", true);
                var theme = instantiatedTheme.GetComponent(themeComponentType);
                Assert.That(theme, Is.Not.Null, "Production theme prefab must expose its authoring component");

                var getModels = themeComponentType.GetMethod("GetNoteModelsForVisualStyle", INSTANCE_FLAGS);
                var fiveLaneStyle = Enum.Parse(visualStyleType, "FiveLaneDrums");
                var fourLaneStyle = Enum.Parse(visualStyleType, "FourLaneDrums");
                var cymbalAccentType = Enum.Parse(themeNoteType, "CymbalAccent");
                var fiveLaneModels = (System.Collections.IDictionary)getModels.Invoke(theme,
                    new[] { fiveLaneStyle, (object)false });
                Assert.That(fiveLaneModels.Contains(cymbalAccentType), Is.True,
                    "Five-lane theme must resolve the CymbalAccent model");
                var fiveLaneAccent = (GameObject)fiveLaneModels[cymbalAccentType];
                var fiveLaneRenderer = fiveLaneAccent.GetComponentInChildren<MeshRenderer>(true);
                Assert.That(fiveLaneRenderer, Is.Not.Null, "Five-lane CymbalAccent model must have a renderer");

                var fourLaneModels = (System.Collections.IDictionary)getModels.Invoke(theme,
                    new[] { fourLaneStyle, (object)false });
                Assert.That(fourLaneModels.Contains(cymbalAccentType), Is.True,
                    "Four-lane theme must resolve its existing CymbalAccent model");
                var fourLaneAccent = (GameObject)fourLaneModels[cymbalAccentType];
                var fourLaneRenderer = fourLaneAccent.GetComponentInChildren<MeshRenderer>(true);
                Assert.That(fourLaneRenderer, Is.Not.Null, "Four-lane CymbalAccent model must have a renderer");

                var fiveLaneMaterials = fiveLaneRenderer.sharedMaterials;
                var fourLaneMaterials = fourLaneRenderer.sharedMaterials;
                Assert.That(fiveLaneMaterials, Has.Length.EqualTo(5), "Five-lane cymbal accent needs five material slots");
                Assert.That(fourLaneMaterials.Length, Is.GreaterThanOrEqualTo(4),
                    "Existing four-lane cymbal accent must retain the four shared material slots");

                for (var slot = 0; slot < 4; slot++)
                {
                    Assert.That(fiveLaneMaterials[slot], Is.SameAs(fourLaneMaterials[slot]),
                        $"Five-lane cymbal-accent slot {slot} must preserve the existing four-lane binding");
                }

                var expectedCenterMaterial = AssetDatabase.LoadAssetAtPath<Material>(CYMBAL_GHOST_CENTER_PATH);
                Assert.That(expectedCenterMaterial, Is.Not.Null, $"Expected center material not found: {CYMBAL_GHOST_CENTER_PATH}");
                Assert.That(fiveLaneMaterials[4], Is.SameAs(expectedCenterMaterial),
                    "Fifth material slot must resolve to CymbalGhostCenter, not a renderer default");
                Assert.That(fiveLaneMaterials[4].shader.name, Is.EqualTo("Shader Graphs/YargLit"),
                    "CymbalGhostCenter must use the project's YARG-lit shader");
                Assert.That(fiveLaneMaterials.Any(material => material == null || material.shader == null ||
                                                               material.shader.name == "Universal Render Pipeline/Lit"),
                    Is.False, "No five-lane cymbal-accent slot may be missing or use the default URP Lit shader");
            }
            finally
            {
                Object.DestroyImmediate(instantiatedTheme);
            }
        }
    }
}
