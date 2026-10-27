// pattern: Imperative Shell
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class EliteKickTopologyTests
    {
        private const string ROOT = "Assets/Prefabs/Gameplay/Visual/EliteDrums/Kicks/";

        private static Vector3[] Vertices(GameObject root, MeshFilter filter) => filter.sharedMesh.vertices
            .Select(v => root.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();

        [TestCase("Kick")]
        [TestCase("KickGhost")]
        [TestCase("KickAccent")]
        [TestCase("KickFlam")]
        [TestCase("KickFlamGhost")]
        [TestCase("KickFlamAccent")]
        [TestCase("KickFlamStack")]
        [TestCase("KickFlamGhostStack")]
        [TestCase("KickFlamAccentStack")]
        public void EveryBeamAndChromeTriangleCanFollowMaximumCurvature(string name)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + name + ".prefab");
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name.Contains("housing")) continue;
                var vertices = Vertices(root, filter);
                var triangles = filter.sharedMesh.triangles;
                float maximum = 0;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var x = new[] { vertices[triangles[i]].x, vertices[triangles[i + 1]].x, vertices[triangles[i + 2]].x };
                    maximum = Mathf.Max(maximum, x.Max() - x.Min());
                }
                // Full-width fitting can enlarge the 2.078-unit asset to 2.2 units.
                // At radius 2 this bounds the chord error to about 0.00011 units.
                Assert.That(maximum * (2.2f / 2.078f), Is.LessThanOrEqualTo(.04f), filter.name);
            }
        }

        [TestCase("KickGhost")]
        [TestCase("KickAccent")]
        public void SingleDynamicsKeepNeutralSocketCrossSections(string name)
        {
            Vector3[] Beam(string asset)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + asset + ".prefab");
                return Vertices(root, root.GetComponentsInChildren<MeshFilter>().Single(f => f.name.StartsWith("Lower beam")));
            }
            var neutral = Beam("Kick");
            var variant = Beam(name);
            foreach (float end in new[] { neutral.Min(v => v.x), neutral.Max(v => v.x) })
            {
                var a = neutral.Where(v => Mathf.Abs(v.x - end) < .00001f).ToArray();
                var b = variant.Where(v => Mathf.Abs(v.x - end) < .00001f).ToArray();
                Assert.That(b.Length, Is.GreaterThan(0));
                foreach (var axis in new[] { 1, 2 })
                {
                    Assert.That(b.Min(v => v[axis]), Is.EqualTo(a.Min(v => v[axis])).Within(.00001f));
                    Assert.That(b.Max(v => v[axis]), Is.EqualTo(a.Max(v => v[axis])).Within(.00001f));
                }
            }
        }

        [TestCase("KickFlam")]
        [TestCase("KickFlamGhost")]
        [TestCase("KickFlamAccent")]
        public void WideFlamsLeaveTwoLaneGapAndDedicatedFlamsStayStacked(string name)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + name + ".prefab");
            var beams = root.GetComponentsInChildren<MeshFilter>().Where(f => f.name.Contains("beam")).ToArray();
            Assert.That(beams.Length, Is.EqualTo(2));
            var left = beams.SingleOrDefault(f => f.name.StartsWith("Left beam"));
            Assert.That(left, Is.Not.Null, "Wide flams must use separated left/right beams.");
            var right = beams.Single(f => f.name.StartsWith("Right beam"));
            var a = Vertices(root, left); var b = Vertices(root, right);
            // A five-lane highway has a lane pitch of one fifth its full width.
            float width = root.GetComponentsInChildren<MeshFilter>().SelectMany(f => Vertices(root, f)).Max(v => v.x)
                - root.GetComponentsInChildren<MeshFilter>().SelectMany(f => Vertices(root, f)).Min(v => v.x);
            Assert.That(b.Min(v => v.x) - a.Max(v => v.x), Is.GreaterThan(width * 2 / 5));
            Assert.That(a.Min(v => v.y), Is.EqualTo(b.Min(v => v.y)).Within(.00001f));
            var stack = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + name + "Stack.prefab");
            Assert.That(stack, Is.Not.Null);
            var lower = Vertices(stack, stack.GetComponentsInChildren<MeshFilter>().Single(f => f.name.StartsWith("Lower beam")));
            var upper = Vertices(stack, stack.GetComponentsInChildren<MeshFilter>().Single(f => f.name.StartsWith("Upper beam")));
            Assert.That(upper.Min(v => v.y), Is.GreaterThan(lower.Max(v => v.y)));
        }

        [TestCase("KickAccent", 1)]
        [TestCase("KickFlamAccent", 2)]
        [TestCase("KickFlamAccentStack", 2)]
        public void AccentChromeUsesTheBeamTopWithoutAnOverlay(string name, int count)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + name + ".prefab");
            var filters = root.GetComponentsInChildren<MeshFilter>();
            Assert.That(filters.Any(f => f.name.Contains("chrome strip")), Is.False, "Remove the separate rail.");
            var tops = filters.Where(f => f.name.Contains("chrome top")).ToArray();
            Assert.That(tops.Length, Is.EqualTo(count));
            foreach (var top in tops)
            {
                Assert.That(top.GetComponent<Renderer>().sharedMaterial.name, Is.EqualTo("KickNoteMetal"));
                var vertices = Vertices(root, top);
                Assert.That(vertices.Max(v => v.z) - vertices.Min(v => v.z), Is.GreaterThan(.025f),
                    "Chrome must cover the broad top, not a narrow floating rail.");
                var triangles = top.sharedMesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]];
                    var b = vertices[triangles[i + 1]];
                    var c = vertices[triangles[i + 2]];
                    var cross = Vector3.Cross(b - a, c - a);
                    if (cross.magnitude < 1e-12f) continue;
                    Assert.That(Mathf.Abs(cross.y) / cross.magnitude, Is.GreaterThan(.85f), "The shallow crown must remain upward-facing.");
                }
            }
        }

        [TestCase("KickAccent", 1)]
        [TestCase("KickFlamAccent", 2)]
        [TestCase("KickFlamAccentStack", 2)]
        public void AccentHasDarkShouldersOutsideTheChrome(string name, int count)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + name + ".prefab");
            var filters = root.GetComponentsInChildren<MeshFilter>();
            var borders = filters.Where(f => f.name.Contains("dark shoulder")).ToArray();
            Assert.That(borders.Length, Is.EqualTo(count));
            foreach (var border in borders)
            {
                Assert.That(border.GetComponent<Renderer>().sharedMaterial.name, Is.EqualTo("EliteHiHatDark"));
                var top = filters.Single(f => f.name == border.name.Replace("dark shoulder", "chrome top"));
                var a = Vertices(root, border);
                var allTop = Vertices(root, top);
                // The source contains microscopic underside slivers; measure the visible upper surface.
                var b = allTop.Where(v => v.y > allTop.Max(p => p.y) - .01f).ToArray();
                Assert.That(a.Min(v => v.z), Is.LessThan(b.Min(v => v.z) - .009f));
                Assert.That(a.Max(v => v.z), Is.GreaterThan(b.Max(v => v.z) + .009f));
            }
        }

        [TestCase("KickAccent")]
        [TestCase("KickFlamAccent")]
        [TestCase("KickFlamAccentStack")]
        public void AccentChromeHasAShallowRaisedCrown(string name)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + name + ".prefab");
            foreach (var top in root.GetComponentsInChildren<MeshFilter>().Where(f => f.name.Contains("chrome top")))
            {
                var vertices = Vertices(root, top);
                float center = (vertices.Min(v => v.x) + vertices.Max(v => v.x)) * .5f;
                var middle = vertices.Where(v => Mathf.Abs(v.x - center) < .04f).ToArray();
                float rise = middle.Max(v => v.y) - middle.Min(v => v.y);
                Assert.That(rise, Is.InRange(.0025f, .006f), "A modest transverse crown must rise above its unchanged chrome edges.");
            }
        }

        [TestCase("KickFlam")]
        [TestCase("KickFlamGhost")]
        [TestCase("KickFlamAccent")]
        public void SplitFlamsHaveInnerSocketsAndKeepTwoLaneClearance(string name)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + name + ".prefab");
            var filters = root.GetComponentsInChildren<MeshFilter>();
            var inner = filters.Where(f => f.name.Contains("inner housing")).ToArray();
            Assert.That(inner.Length, Is.EqualTo(2));
            var left = Vertices(root, inner.Single(f => f.name.StartsWith("Left")));
            var right = Vertices(root, inner.Single(f => f.name.StartsWith("Right")));
            float width = filters.SelectMany(f => Vertices(root, f)).Max(v => v.x)
                - filters.SelectMany(f => Vertices(root, f)).Min(v => v.x);
            Assert.That(right.Min(v => v.x) - left.Max(v => v.x), Is.EqualTo(width * .43f).Within(.0001f));
            foreach (string side in new[] { "Left", "Right" })
            {
                var beam = Vertices(root, filters.Single(f => f.name.StartsWith(side + " beam")));
                float cut = side == "Left" ? beam.Max(v => v.x) : beam.Min(v => v.x);
                var housing = Vertices(root, inner.Single(f => f.name.StartsWith(side)));
                Assert.That(cut, Is.EqualTo(side == "Left" ? housing.Min(v => v.x) : housing.Max(v => v.x)).Within(.00001f));
                var socket = beam.Where(v => Mathf.Abs(v.x - cut) < .00001f).ToArray();
                var neutral = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Kick.prefab");
                var neutralBeam = Vertices(neutral, neutral.GetComponentsInChildren<MeshFilter>().Single(f => f.name.StartsWith("Lower beam")));
                var reference = neutralBeam.Where(v => Mathf.Abs(v.x - neutralBeam.Max(p => p.x)) < .00001f).ToArray();
                foreach (int axis in new[] { 1, 2 })
                {
                    Assert.That(socket.Min(v => v[axis]), Is.EqualTo(reference.Min(v => v[axis])).Within(.00001f));
                    Assert.That(socket.Max(v => v[axis]), Is.EqualTo(reference.Max(v => v[axis])).Within(.00001f));
                }
            }
        }

        [Test]
        public void GhostHasNarrowRegularBeam()
        {
            var ghost = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "KickGhost.prefab");
            var beam = ghost.GetComponentsInChildren<MeshFilter>().Single(f => f.name.StartsWith("Lower beam"));
            var middle = Vertices(ghost, beam).Where(v => Mathf.Abs(v.x) < .1f).ToArray();
            Assert.That(middle.Max(v => v.z) - middle.Min(v => v.z), Is.LessThan(.04f));
            Assert.That(beam.GetComponent<Renderer>().sharedMaterial.name, Is.EqualTo("KickNoteMiddle"));
        }
    }
}
