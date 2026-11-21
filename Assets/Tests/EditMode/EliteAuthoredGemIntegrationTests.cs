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

        private static Vector3 Size(GameObject model)
        {
            var points = model.GetComponentsInChildren<MeshFilter>().SelectMany(filter =>
                filter.sharedMesh.vertices.Select(vertex => model.transform.InverseTransformPoint(
                    filter.transform.TransformPoint(vertex)))).ToArray();
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            return Vector3.Scale(model.transform.localScale, bounds.size);
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

        [TestCase("EliteFlatFlam")]
        [TestCase("EliteFlatFlamAccent")]
        public void FlatFlamHasAWideTrapezoidAndDarkensTowardTheEnds(string type)
        {
            var surface = Model(type).GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Gem surface");
            var mesh = surface.sharedMesh;
            var vertices = mesh.vertices;
            var metal = mesh.GetTriangles(1).Select(index => vertices[index]).ToArray();
            float top = metal.Max(vertex => vertex.y);
            var topEdge = metal.Where(vertex => Mathf.Abs(vertex.y - top) < .00001f).ToArray();
            float normalWidth = Model("Normal").GetComponentsInChildren<MeshFilter>()
                .Single(f => f.name == "Gem surface").sharedMesh.bounds.size.x;
            Assert.That(metal.Max(v => v.x) - metal.Min(v => v.x), Is.EqualTo(normalWidth).Within(.002f));
            Assert.That(topEdge.Max(v => v.x) - topEdge.Min(v => v.x),
                Is.InRange(normalWidth * .65f, normalWidth * .82f));
            var colors = mesh.colors;
            Assert.That(surface.GetComponent<MeshRenderer>().sharedMaterials[0].GetFloat("_FlamVertexTint"), Is.EqualTo(1));
            var lens = mesh.GetTriangles(0);
            float center = mesh.bounds.center.x;
            float radius = mesh.bounds.extents.x;
            Assert.That(lens.Where(i => Mathf.Abs(vertices[i].x - center) <= radius * .25f + .00001f)
                .Min(i => colors[i].r), Is.EqualTo(1).Within(.00001f));
            float bottom = lens.Min(i => vertices[i].y);
            float lensHeight = lens.Max(i => vertices[i].y) - bottom;
            var ends = lens.Where(i => Mathf.Abs(vertices[i].x - center) >= radius * .82f).ToArray();
            Assert.That(ends.Where(i => vertices[i].y >= bottom + lensHeight * .55f)
                .Max(i => colors[i].r), Is.EqualTo(Mathf.GammaToLinearSpace(.40f)).Within(.00001f));
            Assert.That(ends.Where(i => vertices[i].y <= bottom + lensHeight * .10f)
                .Min(i => colors[i].r), Is.EqualTo(Mathf.GammaToLinearSpace(.61f)).Within(.00001f));
        }

        [TestCase("EliteFlatFlam", "Gem surface")]
        [TestCase("EliteFlatFlamAccent", "Gem surface")]
        [TestCase("EliteFlatFlamGhost", "Ghost surface")]
        public void FlatFlamInsetHasItsOwnTintedMetalSlot(string type, string surfaceName)
        {
            var model = Model(type);
            var surface = model.GetComponentsInChildren<MeshFilter>().Single(f => f.name == surfaceName);
            var mesh = surface.sharedMesh;
            Assert.That(mesh.subMeshCount, Is.EqualTo(4));
            Assert.That(mesh.GetTriangles(3), Is.Not.Empty);
            var vertices = mesh.vertices;
            var rim = mesh.GetTriangles(1).Select(i => vertices[i]).ToArray();
            var face = mesh.GetTriangles(3).Select(i => vertices[i]).ToArray();
            Assert.That(rim.Max(v => v.y) - face.Max(v => v.y), Is.EqualTo(.005625f).Within(.00001f));
            float rimWidth = rim.Max(v => v.x) - rim.Min(v => v.x);
            float faceWidth = face.Max(v => v.x) - face.Min(v => v.x);
            Assert.That((rimWidth - faceWidth) * .5f, Is.EqualTo(.019488f).Within(.00001f));
            var renderer = surface.GetComponent<MeshRenderer>();
            var materials = renderer.sharedMaterials;
            Assert.That(materials[3].parent, Is.EqualTo(materials[1]));

            using var note = new SerializedObject(model.GetComponent(Production("YARG.Themes.ThemeNote")));
            var slots = note.FindProperty("_coloredMetalMaterials");
            var inset = Enumerable.Range(0, slots.arraySize).Select(slots.GetArrayElementAtIndex).Single(slot =>
                slot.FindPropertyRelative("Mesh").objectReferenceValue == renderer &&
                slot.FindPropertyRelative("MaterialIndex").intValue == 3);
            Assert.That(inset.FindPropertyRelative("ColorMultiplier").floatValue, Is.EqualTo(.73f));
        }

        [Test]
        public void VFlamOuterFrameIsTallerAndSpineRisesAboveTheGem()
        {
            var model = Model("EliteVFlam");
            var frame = model.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Outer metal").sharedMesh;
            var normal = Model("Normal").GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Gem surface").sharedMesh;
            Assert.That(frame.bounds.max.y, Is.EqualTo(normal.bounds.max.y + Size(Model("Normal")).y * .10f).Within(.001f));
            var mesh = model.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Gem surface").sharedMesh;
            var vertices = mesh.vertices;
            var spine = mesh.GetTriangles(1).Select(index => vertices[index]).ToArray();
            float center = mesh.bounds.center.x;
            float edge = spine.Max(v => v.x);
            var lens = mesh.GetTriangles(0).Select(index => vertices[index]).Where(v => Mathf.Abs(v.x - edge) < .00001f);
            Assert.That(spine.Where(v => Mathf.Abs(v.x - edge) < .00001f).Max(v => v.y),
                Is.GreaterThan(lens.Max(v => v.y) + .008f));
            var colors = mesh.colors;
            var lensIndices = mesh.GetTriangles(0);
            float innerEdge = mesh.bounds.extents.x * .125f;
            float darkest = Mathf.GammaToLinearSpace(.35f);
            Assert.That(lensIndices.Where(i => Mathf.Abs(vertices[i].x - center) <= innerEdge + .00001f)
                .Max(i => colors[i].r), Is.EqualTo(darkest).Within(.00001f));
            Assert.That(lensIndices.Where(i => Mathf.Abs(vertices[i].x - center) > innerEdge + .00001f)
                .Min(i => colors[i].r), Is.GreaterThan(darkest));
            float outerEdge = mesh.bounds.extents.x * .90f;
            Assert.That(lensIndices.Where(i => Mathf.Abs(vertices[i].x - center) >= outerEdge - .000001f)
                .Min(i => colors[i].r), Is.EqualTo(1).Within(.00001f));
            Assert.That(lensIndices.Any(i => Mathf.Abs(Mathf.Abs(vertices[i].x - center) - outerEdge) < .000001f),
                Is.True, "The fade must sample the 90% edge exactly.");
        }

        [Test]
        public void EliteAccentsShareOnePetalMesh()
        {
            var meshes = new[] { "CymbalAccent", "ClosedHiHatAccent", "OpenHiHatAccent" }
                .Select(type => Model(type).GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Thorns").sharedMesh)
                .ToArray();
            Assert.That(meshes.Distinct().Count(), Is.EqualTo(1));
        }

        [TestCase("EliteFlatFlam", 825)]
        [TestCase("EliteVFlam", 750)]
        public void FlamGradientUsesSparseGeometry(string type, int triangleBudget)
        {
            var mesh = Model(type).GetComponentsInChildren<MeshFilter>()
                .Single(f => f.name == "Gem surface").sharedMesh;
            var triangles = Enumerable.Range(0, mesh.subMeshCount).Sum(slot => (int) mesh.GetIndexCount(slot) / 3);
            Assert.That(triangles, Is.LessThanOrEqualTo(triangleBudget));
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
        public void FusedFlamPreservesWidthAndDepthRatiosAndTheAuthoredHeight(string type, string singleType)
        {
            var flam = Size(Model(type));
            var single = Size(Model(singleType));
            Assert.That(flam.x, Is.EqualTo(single.x * 1.5f).Within(.00002f), "Width");
            bool tallerFrame = type.StartsWith("EliteVFlam", StringComparison.Ordinal) && singleType != "Accent";
            float height = single.y * (tallerFrame ? 1.10f : 1f);
            Assert.That(flam.y, Is.EqualTo(height).Within(.00002f), "Height");
            Assert.That(flam.z, Is.EqualTo(single.z).Within(.00002f), "Depth");
        }
    }
}
