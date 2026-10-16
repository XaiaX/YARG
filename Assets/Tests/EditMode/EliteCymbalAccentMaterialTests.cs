using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class EliteCymbalAccentMaterialTests
    {
        private const string THEME_PATH = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";
        private const string CRESCENT_MATERIAL_PATH = "Assets/Art/Materials/Gameplay/Notes/Rectangular/CymbalGhostCenter.mat";
        private const int CRESCENT_MATERIAL_INDEX = 4;

        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        [Test]
        public void EliteCymbalAccentUsesItsAuthoredCrescentMaterialAtSlotFive()
        {
            var themePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(THEME_PATH);
            var expectedCrescentMaterial = AssetDatabase.LoadAssetAtPath<Material>(CRESCENT_MATERIAL_PATH);
            Assert.That(themePrefab, Is.Not.Null);
            Assert.That(expectedCrescentMaterial, Is.Not.Null);

            var theme = themePrefab.GetComponent(Production("YARG.Themes.ThemeComponent"));
            Assert.That(theme, Is.Not.Null);
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var noteType = Enum.Parse(Production("YARG.Themes.ThemeNoteType"), "CymbalAccent");
            var models = (IDictionary) theme.GetType().GetMethod("GetNoteModelsForVisualStyle")
                .Invoke(theme, new[] { style, (object) false });
            Assert.That(models.Contains(noteType), Is.True, "The authored Elite cymbal-accent model is missing.");

            var accent = (GameObject) models[noteType];
            Assert.That(accent.transform.parent.name, Is.EqualTo("Elite Lane Notes"));
            var renderers = accent.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                Assert.That(materials.Length, Is.GreaterThan(CRESCENT_MATERIAL_INDEX), renderer.name);
                Assert.That(materials[CRESCENT_MATERIAL_INDEX], Is.SameAs(expectedCrescentMaterial),
                    $"Elite CymbalAccent renderer {renderer.name} must retain its authored crescent material in slot 5.");
            }
        }
    }
}
