// pattern: Imperative Shell
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class EliteCymbalClearanceTests
    {
        private static Type Production(string name) => Type.GetType("YARG.Themes." + name + ", Assembly-CSharp", true);

        private static GameObject Model(string style, string type)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab");
            var theme = root.GetComponent(Production("ThemeComponent"));
            var models = (IDictionary)theme.GetType().GetMethod("GetNoteModelsForVisualStyle")
                .Invoke(theme, new[] { Enum.Parse(Production("VisualStyle"), style), (object)false });
            return (GameObject)models[Enum.Parse(Production("ThemeNoteType"), type)];
        }

        private static Vector3[] Vertices(GameObject model, MeshFilter part) => part.sharedMesh.vertices
            .Select(v => model.transform.InverseTransformPoint(part.transform.TransformPoint(v))).ToArray();

        // Intersect actual triangle surfaces, not their axis-aligned bounds.
        private static float Height(Vector3[] vertices, int[] triangles, float x, float z, bool highest)
        {
            float hit = highest ? float.NegativeInfinity : float.PositiveInfinity;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var ab = vertices[triangles[i + 1]] - a;
                var ac = vertices[triangles[i + 2]] - a;
                float determinant = ab.x * ac.z - ab.z * ac.x;
                if (Mathf.Abs(determinant) < 1e-10f) continue;
                float u = ((x - a.x) * ac.z - (z - a.z) * ac.x) / determinant;
                float v = (ab.x * (z - a.z) - ab.z * (x - a.x)) / determinant;
                if (u < -1e-5f || v < -1e-5f || u + v > 1.00001f) continue;
                float y = a.y + u * ab.y + v * ac.y;
                hit = highest ? Mathf.Max(hit, y) : Mathf.Min(hit, y);
            }
            Assert.That(float.IsInfinity(hit), Is.False, $"No surface at ({x}, {z}).");
            return hit;
        }

        [TestCase("Cymbal")]
        [TestCase("CymbalGhost")]
        [TestCase("CymbalAccent")]
        [TestCase("ClosedHiHat")]
        [TestCase("ClosedHiHatGhost")]
        [TestCase("ClosedHiHatAccent")]
        [TestCase("OpenHiHat")]
        [TestCase("OpenHiHatGhost")]
        [TestCase("OpenHiHatAccent")]
        public void BaseInteriorClearsSimultaneousKickSurface(string type)
        {
            var model = Model("EliteDrums", type);
            var kick = Model("EliteDrums", "Kick");
            var part = model.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Base");
            var kickPart = kick.GetComponentInChildren<MeshFilter>();
            var vertices = Vertices(model, part);
            var kickVertices = Vertices(kick, kickPart);
            foreach (float x in new[] { -.08f, 0f, .08f })
            foreach (float z in new[] { .025f, .04f, .055f })
            {
                float bottom = Height(vertices, part.sharedMesh.triangles, x, z, false);
                float bar = Height(kickVertices, kickPart.sharedMesh.triangles, x, z, true);
                Assert.That(bottom - bar, Is.GreaterThan(.005f),
                    $"{type} base interior intersects or nearly coincides with the kick at ({x}, {z}).");
            }
        }

        [TestCase("CymbalGhost")]
        [TestCase("OpenHiHatGhost")]
        public void GhostTopUsesLegacyGhostCenterAndRegularColorSurround(string type)
        {
            var model = Model("EliteDrums", type);
            var top = model.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Top");
            int surround = Array.FindIndex(top.sharedMaterials, m => m.name == "CymbalMiddle");
            int center = Array.FindIndex(top.sharedMaterials, m => m.name == "CymbalGhostCenter");
            Assert.That(surround, Is.GreaterThanOrEqualTo(0));
            Assert.That(center, Is.GreaterThanOrEqualTo(0));
            var mesh = top.GetComponent<MeshFilter>().sharedMesh;
            float highest = mesh.vertices.Max(v => top.transform.TransformPoint(v).y);
            Assert.That(mesh.GetTriangles(center).Max(i => top.transform.TransformPoint(mesh.vertices[i]).y),
                Is.EqualTo(highest).Within(.00001f), "The color-reactive ghost material must cover the cone tip.");
            var note = model.GetComponent(Production("ThemeNote"));
            var entries = ((IEnumerable)note.GetType().GetProperty("ColoredMaterials").GetValue(note)).Cast<object>();
            var indices = entries.Where(e => (Renderer)e.GetType().GetField("Mesh").GetValue(e) == top)
                .Select(e => (int)e.GetType().GetField("MaterialIndex").GetValue(e)).ToArray();
            Assert.That(indices, Is.EquivalentTo(new[] { surround, center }), "Both regions must receive note and Star Power color.");
            var baseRenderer = model.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Base");
            Assert.That(baseRenderer.sharedMaterials.Select(m => m.name), Does.Contain("EliteHiHatDark"));
        }

        [TestCase("OpenHiHat")]
        [TestCase("OpenHiHatGhost")]
        [TestCase("OpenHiHatAccent")]
        public void OpenHatLowerRimIsFixedDarkInTheImportedMesh(string type)
        {
            var model = Model("EliteDrums", type);
            var top = model.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Top");
            var mesh = top.GetComponent<MeshFilter>().sharedMesh;
            var uv = mesh.uv;
            int wallFaces = 0;
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                var indices = mesh.GetTriangles(slot);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var a = uv[indices[i]]; var b = uv[indices[i + 1]]; var c = uv[indices[i + 2]];
                    bool outer = new[] { a, b, c }.All(p => (p * 2f - Vector2.one).magnitude >= .9587f);
                    if (!outer) continue;
                    wallFaces++;
                    Assert.That(top.sharedMaterials[slot].name, Is.EqualTo("EliteHiHatDark"),
                        "Unity may reorder FBX material slots; the outer wall and bevel faces must still receive fixed dark.");
                }
            }
            Assert.That(wallFaces, Is.GreaterThanOrEqualTo(128));
        }

        [TestCase("Cymbal")]
        [TestCase("CymbalGhost")]
        [TestCase("CymbalAccent")]
        [TestCase("ClosedHiHat")]
        [TestCase("ClosedHiHatGhost")]
        [TestCase("ClosedHiHatAccent")]
        [TestCase("OpenHiHat")]
        [TestCase("OpenHiHatGhost")]
        [TestCase("OpenHiHatAccent")]
        public void RearRimRiseDoesNotOverwhelmTheUpperProfile(string type)
        {
            var model = Model("EliteDrums", type);
            var top = model.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Top");
            var vertices = Vertices(model, top);
            float front = vertices.Min(v => v.z), back = vertices.Max(v => v.z);
            float frontY = vertices.Where(v => v.z < front + .00001f).Min(v => v.y);
            float backY = vertices.Where(v => v.z > back - .00001f).Min(v => v.y);
            Assert.That((backY - frontY) / (back - front), Is.LessThan(.34f),
                "Excess rear lift exposes the back rim and makes pointed cymbals read as round discs.");
        }
    }
}
