using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class EliteAuthoredGemIntegrationTests
    {
        const string THEME_PATH = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";

        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        private static GameObject Model(string type, bool starPower = false)
        {
            var theme = AssetDatabase.LoadAssetAtPath<GameObject>(THEME_PATH)
                .GetComponent(Production("YARG.Themes.ThemeComponent"));
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var noteType = Enum.Parse(Production("YARG.Themes.ThemeNoteType"), type);
            var models = (IDictionary) theme.GetType().GetMethod("GetNoteModelsForVisualStyle")
                .Invoke(theme, new[] { style, (object) starPower });
            return (GameObject) models[noteType];
        }

        [TestCase("Normal", "Gem surface", "Metal bottom")]
        [TestCase("Ghost", "Ghost surface", "Ghost bottom")]
        [TestCase("Accent", "Gem surface", "White bottom")]
        [TestCase("EliteFlatFlam", "Gem surface", "Metal bottom")]
        [TestCase("EliteFlatFlamGhost", "Ghost surface", "Ghost bottom")]
        [TestCase("EliteFlatFlamAccent", "Gem surface", "White bottom")]
        [TestCase("EliteVFlam", "Gem surface", "Metal bottom")]
        [TestCase("EliteVFlamGhost", "Ghost surface", "Ghost bottom")]
        [TestCase("EliteVFlamAccent", "Gem surface", "White bottom")]
        public void DynamicsUseBakedEliteStudyGeometry(string type, string surface, string bottom)
        {
            var model = Model(type);
            Assert.That(model, Is.Not.Null, type);
            Assert.That(model.GetComponentsInChildren<MeshRenderer>().Select(r => r.name),
                Does.Contain(surface).And.Contain(bottom));
            Assert.That(model.GetComponentsInChildren<Animation>(true), Is.Empty,
                "Categorical chart dynamics should use a fixed endpoint without a per-note clip player.");
        }

        [Test]
        public void EliteAccentsShareOnePetalMesh()
        {
            var meshes = new[] { "CymbalAccent", "ClosedHiHatAccent", "OpenHiHatAccent" }
                .Select(type => Model(type).GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Thorns").sharedMesh)
                .ToArray();
            Assert.That(meshes.Distinct().Count(), Is.EqualTo(1));
        }
        [Test]
        public void RegularAndPetalOpenHatsHaveTheSameRimHeight()
        {
            static float RimHeight(GameObject model)
            {
                var top = model.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Top");
                var noteSpace = model.transform.worldToLocalMatrix * top.transform.localToWorldMatrix;
                return top.sharedMesh.vertices.Min(v => noteSpace.MultiplyPoint3x4(v).y);
            }

            Assert.That(RimHeight(Model("OpenHiHat")), Is.EqualTo(RimHeight(Model("OpenHiHatAccent"))).Within(1e-5f));
        }
        [TestCase("EliteFlatFlam", "Normal")]
        [TestCase("EliteFlatFlamGhost", "Ghost")]
        [TestCase("EliteFlatFlamAccent", "Accent")]
        [TestCase("EliteVFlam", "Normal")]
        [TestCase("EliteVFlamGhost", "Ghost")]
        [TestCase("EliteVFlamAccent", "Accent")]
        public void FusedFlamIsWiderWithTheSameHeightAndDepth(string type, string singleType)
        {
            static Vector3 Size(GameObject model)
            {
                var points = model.GetComponentsInChildren<MeshFilter>().SelectMany(filter =>
                    filter.sharedMesh.vertices.Select(vertex => model.transform.InverseTransformPoint(
                        filter.transform.TransformPoint(vertex)))).ToArray();
                var bounds = new Bounds(points[0], Vector3.zero);
                foreach (var point in points) bounds.Encapsulate(point);
                return Vector3.Scale(model.transform.localScale, bounds.size);
            }

            var flam = Size(Model(type));
            var single = Size(Model(singleType));
            Assert.That(flam.x, Is.EqualTo(single.x * 1.5f).Within(.00002f), "Width");
            Assert.That(flam.y, Is.EqualTo(single.y).Within(.00002f), "Height");
            Assert.That(flam.z, Is.EqualTo(single.z).Within(.00002f), "Depth");
        }
    }
}
