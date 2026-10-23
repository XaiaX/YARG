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
