// pattern: Imperative Shell
using System;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class ModularPedalVisualTests
    {
        [TestCase("HatStomp", 2)]
        [TestCase("HatStomp", 3)]
        [TestCase("HatSplash", 2)]
        [TestCase("HatSplash", 3)]
        public void ContinuousFootHasMirroredEndsAndTheAuthoredFaceMaterials(string name, int count)
        {
            const float TOLERANCE = .0001f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/" + name + ".prefab");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var modular = Type.GetType("YARG.Gameplay.Visuals.ModularPedalVisual, Assembly-CSharp", true);
                modular.GetMethod("Configure").Invoke(instance.GetComponentInChildren(modular), new object[] { count });
                var foot = instance.GetComponentsInChildren<MeshFilter>().Single(filter => filter.name == "Base");
                var toRoot = instance.transform.worldToLocalMatrix * foot.transform.localToWorldMatrix;
                var vertices = foot.sharedMesh.vertices.Select(point => toRoot.MultiplyPoint3x4(point)).ToArray();
                var toe = vertices.Where(point => Mathf.Abs(point.z + .0905f) < TOLERANCE).ToArray();
                Assert.That(toe, Is.Not.Empty);
                float plateauHalfWidth = count == 3 ? .2f : 0;
                Assert.That(toe.Min(point => point.x), Is.EqualTo(-plateauHalfWidth).Within(TOLERANCE));
                Assert.That(toe.Max(point => point.x), Is.EqualTo(plateauHalfWidth).Within(TOLERANCE));
                Assert.That(toe.Max(point => point.y) - toe.Min(point => point.y), Is.GreaterThan(.016f));

                var shoulder = vertices.Where(point => point.x >= plateauHalfWidth - TOLERANCE &&
                        Mathf.Abs(point.y + .006f) < TOLERANCE)
                    .OrderBy(point => point.x)
                    .GroupBy(point => Mathf.RoundToInt(point.x / TOLERANCE))
                    .Select(slice => slice.OrderBy(point => point.z).First()).ToArray();
                var slopes = shoulder.Zip(shoulder.Skip(1), (inner, outer) =>
                    (outer.z - inner.z) / (outer.x - inner.x)).ToArray();
                Assert.That(slopes.Length, Is.GreaterThan(2));
                for (int i = 1; i < slopes.Length; i++)
                    Assert.That(slopes[i], Is.GreaterThanOrEqualTo(slopes[i - 1] - TOLERANCE),
                        "The front edge should keep a convex arc from the toe to the outer end.");

                var materials = foot.GetComponent<MeshRenderer>().sharedMaterials;
                Assert.That(materials.Select(material => material.name), Is.EqualTo(name == "HatSplash"
                    ? new[] { "ElitePedalMetal", "EliteHiHatDark", "EliteAccentHOPOGlow" }
                    : new[] { "ElitePedalMetal", "ElitePedalInsetMetal" }));
                Assert.That(foot.sharedMesh.subMeshCount, Is.EqualTo(materials.Length));

                int slopeSlot = name == "HatSplash" ? 2 : 1;
                var slope = foot.sharedMesh.GetTriangles(slopeSlot).Select(index => vertices[index])
                    .Where(point => Mathf.Abs(point.x) <= plateauHalfWidth + TOLERANCE && point.z > -.082f && point.z < -.061f)
                    .ToArray();
                Assert.That(slope, Is.Not.Empty, "The bevel is inset from the front wall.");
                Assert.That(slope.Max(point => point.y) - slope.Min(point => point.y), Is.GreaterThan(.007f));
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [TestCase("HatStomp")]
        [TestCase("HatSplash")]
        public void AuthoredCapsCoverOuterGlowEdgesAndUseDrumMetalTexture(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/" + name + ".prefab");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var type = Type.GetType("YARG.Gameplay.Visuals.ModularPedalVisual, Assembly-CSharp", true);
                var component = instance.GetComponentInChildren(type);
                var top = instance.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Top");
                var basePart = instance.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Base");
                var drumMetal = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Art/Materials/Gameplay/Notes/Rectangular/NoteMetal.mat");
                Assert.That(basePart.sharedMaterial.GetTexture("_BaseMap"),
                    Is.SameAs(drumMetal.GetTexture("_BaseMap")),
                    "The pedal frame should use the drum's painted metal highlights.");
                foreach (int count in new[] { 3, 2, 4, 1, 3 })
                {
                    type.GetMethod("Configure").Invoke(component, new object[] { count });
                    var colors = top.GetComponent<MeshFilter>().sharedMesh.colors;
                    Assert.That(colors, Is.Not.Empty, "Repeated beam sections must retain their opacity gradient.");
                    Assert.That(colors.Min(c => c.a), Is.EqualTo(.02f).Within(.005f));
                    Assert.That(colors.Max(c => c.a), Is.EqualTo(.98f).Within(.005f));
                    var caps = instance.GetComponentsInChildren<MeshRenderer>()
                        .Where(r => r.name.StartsWith("End")).OrderBy(r => r.bounds.center.x).ToArray();
                    Assert.That(caps.Length, Is.EqualTo(2));
                    Assert.That(caps[0].bounds.min.x, Is.EqualTo(top.bounds.min.x).Within(.00001f));
                    Assert.That(caps[1].bounds.max.x, Is.EqualTo(top.bounds.max.x).Within(.00001f));
                    foreach (var cap in caps)
                    {
                        Assert.That(cap.sharedMaterial, Is.SameAs(basePart.sharedMaterial));
                        Assert.That(cap.bounds.max.y, Is.GreaterThan(top.bounds.max.y));
                        Assert.That(cap.bounds.min.z, Is.LessThan(top.bounds.min.z));
                        Assert.That(cap.bounds.max.z, Is.GreaterThan(top.bounds.max.z));
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [Test]
        public void RepeatingAndReusingSectionsPreservesSourceAndRenderer()
        {
            var type = Type.GetType("YARG.Gameplay.Visuals.ModularPedalVisual, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, "Pedal models need lane-sized sections, not stretched points.");
            var root = new GameObject("Pedal");
            var source = new Mesh();
            source.vertices = new[] { new Vector3(-.2f, 0, 0), new Vector3(.2f, 0, 0),
                new Vector3(0, 0, -.1f) };
            source.triangles = new[] { 0, 1, 2 };
            source.RecalculateBounds();
            try
            {
                var filter = root.AddComponent<MeshFilter>();
                var renderer = root.AddComponent<MeshRenderer>();
                filter.sharedMesh = source;
                var component = root.AddComponent(type);
                var leftEnd = new GameObject("Left end").transform;
                var rightEnd = new GameObject("Right end").transform;
                leftEnd.SetParent(root.transform, false);
                rightEnd.SetParent(root.transform, false);
                var leftField = type.GetField("_leftEnd", BindingFlags.Instance | BindingFlags.NonPublic);
                var rightField = type.GetField("_rightEnd", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(leftField, Is.Not.Null, "End caps must remain at the outer ends, not repeat per lane.");
                Assert.That(rightField, Is.Not.Null);
                leftField.SetValue(component, leftEnd);
                rightField.SetValue(component, rightEnd);
                type.GetField("_sections", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(component, new[] { filter });
                type.GetField("_sectionWidth", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(component, .4f);
                var configure = type.GetMethod("Configure");
                foreach (int count in new[] { 3, 2, 4, 1, 2 })
                {
                    configure.Invoke(component, new object[] { count });
                    Assert.That(filter.sharedMesh.vertexCount, Is.EqualTo(count*3));
                    Assert.That(filter.sharedMesh.bounds.size.x, Is.EqualTo(count*.4f).Within(.00001f));
                    Assert.That(filter.sharedMesh.bounds.center.x, Is.EqualTo(0).Within(.00001f));
                    Assert.That(filter.sharedMesh.bounds.min.z, Is.EqualTo(-.1f).Within(.00001f));
                    Assert.That(root.GetComponent<MeshRenderer>(), Is.SameAs(renderer));
                    Assert.That(source.vertexCount, Is.EqualTo(3));
                    Assert.That(leftEnd.localPosition.x, Is.EqualTo(-(count-1)*.2f).Within(.00001f));
                    Assert.That(rightEnd.localPosition.x, Is.EqualTo((count-1)*.2f).Within(.00001f));
                    Assert.That(root.GetComponentsInChildren<Transform>().Length, Is.EqualTo(3),
                        "Only the two outer caps should exist, regardless of section count.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }
}
