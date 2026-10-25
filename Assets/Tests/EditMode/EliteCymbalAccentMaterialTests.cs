using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class EliteCymbalAccentMaterialTests
    {
        private const string THEME_PATH = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        [TestCase("CymbalAccent")]
        [TestCase("ClosedHiHatAccent")]
        [TestCase("OpenHiHatAccent")]
        public void AccentTopKeepsColoredPointsChromeAndDarkContoursSeparate(string type)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(THEME_PATH);
            var theme = prefab.GetComponent(Production("YARG.Themes.ThemeComponent"));
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var noteType = Enum.Parse(Production("YARG.Themes.ThemeNoteType"), type);
            var models = (IDictionary) theme.GetType().GetMethod("GetNoteModelsForVisualStyle")
                .Invoke(theme, new[] { style, (object) false });
            var accent = (GameObject) models[noteType];
            Assert.That(accent.transform.parent.name, Is.EqualTo("Elite Lane Notes"));
            var top = accent.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Top");
            var materials = top.GetComponent<MeshRenderer>().sharedMaterials;
            Assert.That(materials.Select(m => m.name), Is.EquivalentTo(new[]
                { "CymbalMiddle", "CymbalMetal", "EliteHiHatDark" }));
            for (int i = 0; i < materials.Length; i++)
                Assert.That(top.sharedMesh.GetIndexCount(i), Is.GreaterThan(0), materials[i].name);
        }

        [TestCase("Cymbal")]
        [TestCase("CymbalGhost")]
        [TestCase("CymbalAccent")]
        [TestCase("OpenHiHat")]
        [TestCase("OpenHiHatGhost")]
        [TestCase("OpenHiHatAccent")]
        public void TallCymbalTipsSampleTheTextureCenter(string type)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/" + type + ".prefab");
            var top = prefab.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Top");
            var vertices = top.sharedMesh.vertices;
            var uvs = top.sharedMesh.uv;
            float highest = vertices.Max(v => top.transform.TransformPoint(v).y);
            var tipIndices = Enumerable.Range(0, vertices.Length)
                .Where(i => top.transform.TransformPoint(vertices[i]).y > highest - 0.000001f).ToArray();
            Assert.That(tipIndices, Is.Not.Empty);
            foreach (int index in tipIndices)
                Assert.That(Vector2.Distance(uvs[index], new Vector2(0.5f, 0.5f)), Is.LessThan(0.0001f),
                    "Texture projection must remove tilt before mapping the cone; height must not shift the texture ring.");
        }
    }
}
