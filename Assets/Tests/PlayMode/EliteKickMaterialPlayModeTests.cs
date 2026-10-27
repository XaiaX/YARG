using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using YARG.Core.Chart;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteKickMaterialPlayModeTests
    {
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        private static object Read(object obj, string name) => obj.GetType().GetField(name).GetValue(obj);
        private static object Property(object obj, string name) => obj.GetType().GetProperty(name).GetValue(obj);

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void GhostFlamKeepsIndependentColorsAndOpacityAcrossPoolReuse(bool dedicated, bool lefty)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab");
            var themeType = Production("YARG.Themes.ThemeNote");
            var source = prefab.transform.Find("Elite Lane Notes").GetComponentsInChildren(themeType, true)
                .FirstOrDefault(note => Property(note, "NoteType").ToString() == (dedicated ? "DedicatedLaneKickFlamGhost" : "KickFlamGhost"));
            Assert.That(source, Is.Not.Null, "Ghost flam model is required.");
            var host = new GameObject("kick material test");
            try
            {
                var groupType = Production("YARG.Gameplay.Visuals.NoteGroup");
                var group = (Component)groupType.GetMethod("CreateNoteGroupFromTheme").Invoke(null,
                    new object[] { host.transform, ((Component)source).gameObject });
                foreach (var colors in new[] { new[] { Color.red, Color.blue }, new[] { Color.green, Color.magenta }, new[] { Color.gray, Color.gray } })
                {
                    groupType.GetMethod("Initialize").Invoke(group, null);
                    var apply = groupType.GetMethod("SetKickFlamColors");
                    Assert.That(apply.GetParameters().Length, Is.EqualTo(3), "Flam colors must accept handedness.");
                    apply.Invoke(group, new object[] { colors[0], colors[1], lefty });
                    var note = group.GetComponentInChildren(themeType);
                    for (int side = 0; side < 2; side++)
                    {
                        var entries = ((IEnumerable)Property(note, side == 0 ? "ColoredMaterials" : "ColoredKick2Materials")).Cast<object>().ToArray();
                        Assert.That(entries, Is.Not.Empty);
                        foreach (var entry in entries)
                        {
                            var renderer = (Renderer)Read(entry, "Mesh");
                            var material = renderer.sharedMaterials[(int)Read(entry, "MaterialIndex")];
                            Assert.That(material.color.a, Is.EqualTo(.38f).Within(.001f));
                            var expected = colors[lefty && !dedicated ? 1 - side : side];
                            if (!dedicated)
                                Assert.That(Mathf.Sign(renderer.bounds.center.x), Is.EqualTo(side == 0 ? 1 : -1));
                            float addition = (float)Read(entry, "EmissionAddition");
                            Assert.That(material.color.r, Is.EqualTo(expected.r + addition).Within(.001f));
                            Assert.That(material.color.b, Is.EqualTo(expected.b + addition).Within(.001f));
                            Assert.That(material.GetFloat("_Surface"), Is.EqualTo(1));
                        }
                    }
                }
                AssertBeamTransmission(group.GetComponentsInChildren<MeshRenderer>().Single(r => r.name.StartsWith(dedicated ? "Lower beam" : "Right beam")));
            }
            finally
            {
                foreach (var renderer in host.GetComponentsInChildren<Renderer>())
                    foreach (var material in renderer.sharedMaterials)
                        if (!AssetDatabase.Contains(material)) UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void AssertBeamTransmission(MeshRenderer beam)
        {
            int layer = beam.gameObject.layer;
            var root = new GameObject("Ghost beam transparency camera");
            var camera = root.AddComponent<Camera>();
            var target = new RenderTexture(256, 128, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(256, 128, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            try
            {
                beam.gameObject.layer = 31;
                camera.cullingMask = 1 << 31;
                var crossSection = beam.GetComponent<MeshFilter>().sharedMesh.vertices
                    .Select(v => beam.transform.TransformPoint(v))
                    .Where(v => Mathf.Abs(v.x - beam.bounds.center.x) < .02f).ToArray();
                Assert.That(crossSection, Is.Not.Empty);
                var aim = new Vector3(beam.bounds.center.x,
                    (crossSection.Min(v => v.y) + crossSection.Max(v => v.y)) * .5f,
                    (crossSection.Min(v => v.z) + crossSection.Max(v => v.z)) * .5f);
                camera.transform.position = aim + Vector3.back;
                camera.transform.LookAt(aim);
                camera.orthographic = true;
                camera.orthographicSize = beam.bounds.size.y;
                camera.aspect = 2;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.targetTexture = target;
                float Sample(Color background)
                {
                    camera.backgroundColor = background;
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 256, 128), 0, 0);
                    pixels.Apply();
                    return pixels.GetPixel(128, 64).r;
                }
                float black = Sample(Color.black);
                float white = Sample(Color.white);
                Assert.That(1f - (white - black), Is.EqualTo(.38f).Within(.02f),
                    "The production shader must transmit background light, not merely store an alpha value.");
            }
            finally
            {
                beam.gameObject.layer = layer;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(root);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
