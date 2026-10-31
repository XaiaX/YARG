using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YARG.Core.Game;

// pattern: Imperative Shell
namespace YARG.Tests.EditMode
{
    public sealed class CymbalSpinAnimationTests
    {
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<UnityEngine.Object> _owned = new();
        private static Type Production(string name) => Type.GetType("YARG.Gameplay.Visuals." + name + ", Assembly-CSharp", true);
        private static Type Geometry => Production("EliteDrumsRuntimeGeometry");
        private static Type ModelType => Geometry.GetNestedType("ModelGeometry", BindingFlags.NonPublic);

        private static float Angle(double beat, float duration = 4f)
        {
            var type = Type.GetType("YARG.Gameplay.Visuals.CymbalSpinMotion, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, "Accented cymbals need deterministic quarter-beat rotation.");
            return (float)type.GetMethod("EvaluateAngle").Invoke(null, new object[] { beat, duration });
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var item in _owned) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            _owned.Clear();
            Geometry.GetMethod("ClearSessionCache", STATIC).Invoke(null, null);
        }

        [TestCase(0d, 0f)]
        [TestCase(.5d, 45f)]
        [TestCase(1d, 90f)]
        [TestCase(2d, 180f)]
        [TestCase(4d, 0f)]
        [TestCase(-.5d, 315f)]
        [TestCase(1000000000.5d, 45f)]
        public void RotationMakesOneSmoothTurnEveryFourQuarterBeats(double phase, float expected)
        {
            Assert.That(Angle(phase), Is.EqualTo(expected).Within(.00001f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidDurationsUseTheFourBeatDefault(float duration)
        {
            Assert.That(Angle(1d, duration), Is.EqualTo(90f));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidPhaseReturnsTheRestPose(double phase)
        {
            Assert.That(Angle(phase), Is.Zero);
        }

        [Test]
        public void RotationIsPeriodicAndContinuousAcrossPrerollAndWrapping()
        {
            foreach (double beat in new[] { -123.125, -4.001, -.001, .001, 17.5, 100000.125 })
            {
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(Angle(beat), Angle(beat + 4))), Is.LessThan(.0001f));
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(Angle(beat), Angle(beat + .0001))), Is.LessThan(.011f));
            }
            Assert.That(Angle(2d, 8f), Is.EqualTo(90f), "The serialized period remains tunable.");
        }

        private GameObject Object(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent == null) _owned.Add(go);
            else go.transform.SetParent(parent, false);
            return go;
        }

        private Mesh Mesh(string name, Vector3[] vertices, bool split = false)
        {
            var mesh = new Mesh { name = name };
            _owned.Add(mesh);
            mesh.vertices = vertices;
            mesh.subMeshCount = split ? 3 : 1;
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, split ? 2 : 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private (GameObject Model, Component Animation, Transform Rotor, Mesh Mesh) Model()
        {
            var root = Object("Owned spinning model");
            var baseChild = Object("Base", root.transform);
            baseChild.AddComponent<MeshFilter>().sharedMesh = Mesh("Cymbal Base", new[]
                { new Vector3(-1,0,-1), new Vector3(1,0,-1), new Vector3(1,0,1), new Vector3(-1,0,1) });
            var spin = Object("Spin Assembly", root.transform);
            var rotor = Object("Circular Rotor", spin.transform).transform;
            var top = Object("Top", rotor);
            top.transform.localPosition = Vector3.up * 3;
            var mesh = Mesh("Cymbal Accented Top", new[] { new Vector3(-1,-.2f,0), new Vector3(1,-.2f,0),
                new Vector3(1,.2f,0), new Vector3(-1,.2f,0) }, true);
            top.AddComponent<MeshFilter>().sharedMesh = mesh;
            var componentType = Type.GetType("YARG.Gameplay.Visuals.CymbalSpinAnimation, Assembly-CSharp");
            Assert.That(componentType, Is.Not.Null, "Spinning only the top must preserve the stationary base.");
            var animation = spin.AddComponent(componentType);
            var serialized = new SerializedObject(animation);
            serialized.FindProperty("_rotor").objectReferenceValue = rotor;
            serialized.FindProperty("_beatsPerRevolution").floatValue = 4f;
            serialized.FindProperty("_sweptBounds").boundsValue = new Bounds(Vector3.zero, new Vector3(8, 8, 1));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return (root, animation, rotor, mesh);
        }

        private static void Apply(Component animation, double phase) => animation.GetType()
            .GetMethod("ApplyBeatPhase").Invoke(animation, new object[] { phase });
        private static object Measure(Transform model) => Activator.CreateInstance(ModelType, INSTANCE,
            null, new object[] { model }, null);
        private static long Identity(object model) => (long)ModelType.GetField("Identity", INSTANCE).GetValue(model);

        [Test]
        public void SpinMovesOnlyItsRotorAndPoolDisableRestoresTheAuthoredPose()
        {
            var subject = Model();
            var original = subject.Mesh.vertices;
            var stationary = subject.Model.transform.Find("Base").localToWorldMatrix;
            Apply(subject.Animation, 1d);
            Assert.That(Quaternion.Angle(subject.Rotor.localRotation, Quaternion.AngleAxis(-90, Vector3.forward)), Is.LessThan(.001f));
            Assert.That(subject.Model.transform.Find("Base").localToWorldMatrix, Is.EqualTo(stationary));
            Assert.That(subject.Mesh.vertices, Is.EqualTo(original), "Loaded meshes stay immutable.");
            ((Behaviour)subject.Animation).enabled = false;
            // SendMessage routes through native runnable-behaviour checks in EditMode.
            // Invoke the managed callback; actual clone/disable callbacks are checked in PlayMode.
            subject.Animation.GetType().GetMethod("OnDisable", INSTANCE).Invoke(subject.Animation, null);
            Assert.That(Quaternion.Angle(subject.Rotor.localRotation, Quaternion.identity), Is.LessThan(.001f));
        }

        private (GameObject Root, YargProfile Profile) Player(bool lefty)
        {
            var root = Object("Owned player");
            root.SetActive(false);
            var player = root.AddComponent(Type.GetType("YARG.Gameplay.Player.EliteDrumsPlayer, Assembly-CSharp", true));
            var profile = new YargProfile { LeftyFlip = lefty };
            var yargPlayer = Activator.CreateInstance(Type.GetType("YARG.Player.YargPlayer, Assembly-CSharp", true),
                new object[] { profile, null });
            Type.GetType("YARG.Gameplay.Player.BasePlayer, Assembly-CSharp", true)
                .GetProperty("Player").SetValue(player, yargPlayer);
            return (root, profile);
        }

        private static void Bind(Component animation) => animation.GetType()
            .GetMethod("OnEnable", INSTANCE).Invoke(animation, null);

        private static void AssertDirection(Transform rotor, bool lefty)
        {
            // In the circular frame, clockwise takes the right-hand point downward.
            var point = rotor.localRotation * Vector3.right;
            Assert.That(point.x, Is.EqualTo(0f).Within(.00001f));
            Assert.That(point.y, Is.EqualTo(lefty ? 1f : -1f).Within(.00001f));
        }

        [Test]
        public void EachPlayersProfileControlsOnlyItsOwnSpinDirection()
        {
            var rightPlayer = Player(false);
            var leftPlayer = Player(true);
            var right = Model();
            var left = Model();
            right.Model.transform.SetParent(rightPlayer.Root.transform, false);
            left.Model.transform.SetParent(leftPlayer.Root.transform, false);
            Bind(right.Animation);
            Bind(left.Animation);
            Apply(right.Animation, 1d);
            Apply(left.Animation, 1d);
            AssertDirection(right.Rotor, false);
            AssertDirection(left.Rotor, true);

            rightPlayer.Profile.LeftyFlip = true;
            Apply(right.Animation, 1d);
            Apply(left.Animation, 1d);
            AssertDirection(right.Rotor, true);
            AssertDirection(left.Rotor, true);
        }

        [Test]
        public void PoolActivationRefreshesTheOwningPlayer()
        {
            var first = Player(true);
            var next = Player(false);
            var subject = Model();
            subject.Model.transform.SetParent(first.Root.transform, false);
            Bind(subject.Animation);
            Apply(subject.Animation, 1d);
            AssertDirection(subject.Rotor, true);

            subject.Model.transform.SetParent(next.Root.transform, false);
            Bind(subject.Animation);
            Apply(subject.Animation, 1d);
            AssertDirection(subject.Rotor, false);
        }

        [Test]
        public void CloningAnActiveSpinRestoresRestInsteadOfCapturingItsPhase()
        {
            var subject = Model();
            Apply(subject.Animation, .5d);
            var clone = UnityEngine.Object.Instantiate(subject.Model);
            _owned.Add(clone);
            var animation = clone.GetComponentsInChildren(subject.Animation.GetType(), true).Single();
            animation.GetType().GetMethod("OnEnable", INSTANCE).Invoke(animation, null);
            var rotor = (Transform)new SerializedObject(animation).FindProperty("_rotor").objectReferenceValue;
            Assert.That(Quaternion.Angle(rotor.localRotation, Quaternion.identity), Is.LessThan(.001f));
        }

        [Test]
        public void CachedGeometryIgnoresSpinPhaseButNotChangedSweepBounds()
        {
            Geometry.GetMethod("ClearSessionCache", STATIC).Invoke(null, null);
            var subject = Model();
            var baseline = Measure(subject.Model.transform);
            foreach (double phase in new[] { .125, 1d, 2.5, 3.99 })
            {
                Apply(subject.Animation, phase);
                Assert.That(Identity(Measure(subject.Model.transform)), Is.EqualTo(Identity(baseline)));
            }
            Assert.That(Geometry.GetField("ModelMeasurements", STATIC).GetValue(null), Is.EqualTo(1));
            var serialized = new SerializedObject(subject.Animation);
            serialized.FindProperty("_sweptBounds").boundsValue = new Bounds(Vector3.zero, Vector3.one * 10);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(Identity(Measure(subject.Model.transform)), Is.Not.EqualTo(Identity(baseline)));
        }

        [Test]
        public void FittingCoversEverySpinPhaseAndBaseContactsStayStationary()
        {
            var subject = Model();
            var geometry = Measure(subject.Model.transform);
            var contact = ModelType.GetMethod("ContactExtents", INSTANCE);
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                var arguments = new object[] { 1f, 0f, false, axis, false, null, null };
                contact.Invoke(geometry, arguments);
                float minimum = (float)arguments[5], maximum = (float)arguments[6];
                for (int i = 0; i < 32; i++)
                {
                    Apply(subject.Animation, i / 8d);
                    foreach (var point in subject.Mesh.vertices)
                    {
                        var top = subject.Model.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Top");
                        float projection = Vector3.Dot(subject.Model.transform.InverseTransformPoint(top.transform.TransformPoint(point)), axis);
                        Assert.That(projection, Is.InRange(minimum-.00001f, maximum+.00001f), "Precomputed bounds cover the full sweep.");
                    }
                }
            }
            var args = new object[] { 1f, 1f, 0f, true, Vector3.zero };
            var points = (Vector3[])ModelType.GetMethod("ParallelPoints", INSTANCE).Invoke(geometry, args);
            Assert.That(points, Has.Length.EqualTo(4));
            Assert.That(points.All(p => Math.Abs(p.y) < .00001f), Is.True,
                "Moving top material slots cannot be mistaken for a legacy base surface.");
        }
    }
}