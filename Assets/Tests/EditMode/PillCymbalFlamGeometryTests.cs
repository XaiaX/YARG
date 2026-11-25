// pattern: Imperative Shell
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class PillCymbalFlamGeometryTests
    {
        private const string HATS = "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/Volume";

        [TestCase("Cymbal")]
        [TestCase("OpenHiHat")]
        [TestCase("ClosedHiHat")]
        public void JoinedBellsKeepTheSingleSeatAndEqualPeaksAcrossDynamics(string articulation)
        {
            foreach (string dynamic in new[] { "Ghost", "", "Accent" })
            {
                var single = AssetDatabase.LoadAssetAtPath<GameObject>(HATS + articulation + dynamic + ".prefab");
                var flam = AssetDatabase.LoadAssetAtPath<GameObject>(HATS + articulation + "Flam" + dynamic + ".prefab");
                Assert.That(flam, Is.Not.Null, articulation + dynamic);
                var seat = BoundsOf(Points(single, "Base"));
                var pill = BoundsOf(Points(flam, "Base"));
                Assert.That(pill.size.x, Is.EqualTo(seat.size.x * 1.5f).Within(.0001f));
                Assert.That(pill.size.y, Is.EqualTo(seat.size.y).Within(.0001f));
                Assert.That(pill.size.z, Is.EqualTo(seat.size.z).Within(.0001f));
                Assert.That(Vector3.Distance(pill.center, seat.center), Is.LessThan(.0001f));

                var tips = Points(flam, "Top", 2);
                var left = BoundsOf(tips.Where(p => p.x < pill.center.x).ToArray());
                var right = BoundsOf(tips.Where(p => p.x > pill.center.x).ToArray());
                Assert.That(right.center.x - pill.center.x, Is.EqualTo(pill.center.x - left.center.x).Within(.0002f));
                Assert.That(Vector3.Distance(left.size, right.size), Is.LessThan(.0002f));
                Assert.That(right.center.y, Is.EqualTo(left.center.y).Within(.0002f));
                Assert.That(right.center.z, Is.EqualTo(left.center.z).Within(.0002f));
                Assert.That(left.center.y, Is.EqualTo(BoundsOf(Points(single, "Top", 2)).center.y).Within(.0002f));
                var marker = BoundsOf(Points(flam, "Flat marker"));
                Assert.That(marker.center.x, Is.EqualTo(pill.center.x).Within(.0002f));
                Assert.That(marker.max.z, Is.LessThan(pill.center.z), "Only the player-facing base gets a stripe");
                Assert.That(Part(flam, "Flat marker").gameObject.activeSelf, Is.False);
                var spine = Part(flam, "Spine").sharedMesh;
                Assert.That(spine.normals.Length, Is.EqualTo(spine.vertexCount));
                Assert.That(spine.normals.All(n => Mathf.Abs(1f - n.sqrMagnitude) < .001f), Is.True,
                    flam.name + " center metal needs valid lighting normals before HDR bloom");
                if (dynamic == "Ghost")
                {
                    var baseMetal = Part(flam, "Base").GetComponent<MeshRenderer>().sharedMaterial;
                    Assert.That(Part(flam, "Spine").GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(baseMetal));
                    var center = new Vector2(.5f, .5f);
                    Assert.That(spine.uv.All(uv => Vector2.Distance(center, uv) <= .315f), Is.True,
                        flam.name + " center strip samples the dimmed top of the base texture");
                    var singleCap = BoundsOf(Points(single, "Top", 3));
                    var seam = BoundsOf(Points(flam, "Spine"));
                    var dark = Points(flam, "Top", 3);
                    foreach (var peak in new[] { left, right })
                    {
                        bool isLeft = peak.center.x < pill.center.x;
                        var cap = BoundsOf(dark.Where(p => (p.x < pill.center.x) == isLeft).ToArray());
                        var min = singleCap.min;
                        var max = singleCap.max;
                        min.x = peak.center.x - singleCap.extents.x;
                        max.x = peak.center.x + singleCap.extents.x;
                        if (isLeft) max.x = Mathf.Min(max.x, seam.min.x);
                        else min.x = Mathf.Max(min.x, seam.max.x);
                        Assert.That(Vector3.Distance(cap.min, min), Is.LessThan(.0002f), flam.name + " ghost cap minimum");
                        Assert.That(Vector3.Distance(cap.max, max), Is.LessThan(.0002f), flam.name + " ghost cap maximum");
                    }
                }
                if (articulation == "OpenHiHat")
                {
                    var well = Part(flam, "Well pulse");
                    var glow = BoundsOf(Points(flam, "Base Glow"));
                    var mask = BoundsOf(Points(flam, "Well pulse"));
                    Assert.That(Vector3.Distance(mask.size, glow.size), Is.LessThan(.0001f));
                    Assert.That(Vector3.Distance(mask.center, glow.center), Is.LessThan(.0001f));
                    Assert.That(well.sharedMesh.colors32.Length, Is.EqualTo(well.sharedMesh.vertexCount));
                    var disc = well.GetComponent<MeshRenderer>().sharedMaterial.GetVector("_Disc");
                    Assert.That(disc.z * well.sharedMesh.bounds.extents.x, Is.EqualTo(1f).Within(.0002f));
                }
            }
        }

        [TestCase("Cymbal")]
        [TestCase("OpenHiHat")]
        [TestCase("ClosedHiHat")]
        public void IntermediateBellSizesStayJoinedAndKeepTheirSeat(string articulation)
        {
            var author = AppDomain.CurrentDomain.GetAssemblies().Select(a =>
                a.GetType("YARG.Editor.PillCymbalFlamVelocity")).First(t => t != null).GetMethod("CreateSample");
            var type = Enum.Parse(author.GetParameters()[0].ParameterType, articulation);
            float previousWidth = 0f;
            foreach (float velocity in new[] { 1f, 25f, 50f, 75f, 96f, 100f, 112f, 126f, 127f })
            {
                using var sample = (IDisposable) author.Invoke(null, new object[] { type, velocity });
                var note = (GameObject) sample.GetType().GetProperty("Root").GetValue(sample);
                var top = BoundsOf(Points(note, "Top"));
                Assert.That(top.size.x, Is.GreaterThan(previousWidth), note.name);
                previousWidth = top.size.x;
                Assert.That(Points(note, "Spine"), Is.Not.Empty);
                string suffix = velocity == 1 ? "Ghost" : velocity == 100 ? "" : velocity == 127 ? "Accent" : null;
                if (suffix != null)
                {
                    var single = AssetDatabase.LoadAssetAtPath<GameObject>(HATS + articulation + suffix + ".prefab");
                    var singleTip = BoundsOf(Points(single, "Top", 2));
                    var tips = BoundsOf(Points(note, "Top", 2));
                    Assert.That(tips.center.y, Is.EqualTo(singleTip.center.y).Within(.0002f), note.name);
                    Assert.That(tips.center.z, Is.EqualTo(singleTip.center.z).Within(.0002f), note.name);
                }
            }
        }

        private static MeshFilter Part(GameObject note, string name) =>
            note.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == name);

        private static Vector3[] Points(GameObject note, string name, int slot = -1)
        {
            var part = Part(note, name);
            var mesh = part.sharedMesh;
            var points = mesh.vertices;
            var toNote = note.transform.worldToLocalMatrix * part.transform.localToWorldMatrix;
            return (slot < 0 ? mesh.triangles : mesh.GetTriangles(slot)).Select(i =>
                toNote.MultiplyPoint3x4(points[i])).ToArray();
        }

        private static Bounds BoundsOf(Vector3[] points)
        {
            Assert.That(points, Is.Not.Empty);
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            return bounds;
        }
    }
}
