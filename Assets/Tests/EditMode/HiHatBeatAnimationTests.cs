using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YARG.Core.Chart;

// pattern: Imperative Shell
namespace YARG.Tests.EditMode
{
    public sealed class HiHatBeatAnimationTests
    {
        private static readonly Vector3 REST = new(0f, 0.0022f, -0.0024f);
        private static Type Motion => Type.GetType("YARG.Gameplay.Visuals.HiHatBeatMotion, Assembly-CSharp", true);

        private static object Call(string name, params object[] arguments)
        {
            var method = Motion.GetMethod(name);
            Assert.That(method, Is.Not.Null, "The hat should rock around its tip instead of clamping.");
            return method.Invoke(null, arguments);
        }

        [TestCase(0d, 0f)]
        [TestCase(0.25d, 3f)]
        [TestCase(0.5d, 0f)]
        [TestCase(0.75d, -3f)]
        [TestCase(1.25d, 3f)]
        [TestCase(-0.25d, -3f)]
        public void SwayMovesSmoothlyThreeDegreesEachWayOncePerBeat(double phase, float expected)
        {
            Assert.That((float) Call("EvaluateAngle", phase, 3f), Is.EqualTo(expected).Within(0.00001f));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidBeatPhaseReturnsToTheRestAngle(double phase)
        {
            Assert.That(Call("EvaluateAngle", phase, 3f), Is.EqualTo(0f));
        }

        [Test]
        public void SwayDwellsLongerAtTheEdgesAndCrossesCenterFasterThanASine()
        {
            float At(double phase) => (float) Call("EvaluateAngle", phase, 3f);
            Assert.That(At(0.125d), Is.GreaterThan(2.6f), "Spend more of the beat near the edge.");
            Assert.That(At(0.375d), Is.EqualTo(At(0.125d)).Within(0.00001f));
            Assert.That(At(0.01d), Is.GreaterThan(0.27f), "Cross center faster than the old sine.");
            Assert.That(Math.Abs(At(0.249d) - 3f), Is.LessThan(0.00001f),
                "Ease smoothly into and out of the edge.");
            Assert.That(At(0.625d), Is.EqualTo(-At(0.125d)).Within(0.00001f));
        }

        [TestCase(0.125d)]
        [TestCase(1.0625d)]
        [TestCase(2.3125d)]
        public void EveryNotesOwnArrivalIsCenteredIncludingOffbeatAndTempoChanges(double arrival)
        {
            var sync = new SyncTrack(480u,
                new List<TempoChange> { new(60, 0, 0), new(120, 2, 960) },
                new List<TimeSignatureChange> { new(4, 4, 0, 0, 0, 0, 0, 0) }, new List<Beatline>());
            double phase = (double) Call("EvaluateNotePhase", arrival, arrival, sync);
            Assert.That(phase, Is.EqualTo(0d).Within(1e-10));
            Assert.That((float) Call("EvaluateAngle", phase, 3f), Is.EqualTo(0f).Within(0.00001f));
            // A quarter beat before an offbeat note reaches the line must reach an edge.
            double quarter = arrival < 2d ? 0.25d : 0.125d;
            double before = (double) Call("EvaluateNotePhase", arrival - quarter, arrival, sync);
            Assert.That((float) Call("EvaluateAngle", before, 3f), Is.EqualTo(-3f).Within(0.00001f));
        }

        [Test]
        public void NotePhaseRetainsMusicalTimingAcrossATempoBoundary()
        {
            var sync = new SyncTrack(480u,
                new List<TempoChange> { new(60, 0, 0), new(120, 2, 960) },
                new List<TimeSignatureChange> { new(4, 4, 0, 0, 0, 0, 0, 0) }, new List<Beatline>());
            Assert.That((double) Call("EvaluateNotePhase", 1.75d, 2.125d, sync),
                Is.EqualTo(-0.5d).Within(1e-10));
            Assert.That((double) Call("EvaluateNotePhase", -0.25d, 2.125d, sync),
                Is.EqualTo(-2.5d).Within(1e-10));
        }

        [Test]
        public void SwayKeepsItsTipFixedWithTiltedGeometryAndNonuniformScale()
        {
            var rotation = Quaternion.Euler(-85f, -180f, 0f);
            var scale = new Vector3(1.25f, 0.8f, 1.1f);
            var pivot = new Vector3(0.001f, 0.002f, 0.006f);
            var fixedTip = REST + rotation * Vector3.Scale(scale, pivot);
            for (int i = 0; i <= 100; i++)
            {
                var arguments = new object[] { i / 100d, 3f, REST, rotation, scale, pivot, null, null };
                Call("EvaluatePose", arguments);
                var position = (Vector3) arguments[6];
                var turned = (Quaternion) arguments[7];
                Assert.That(Vector3.Distance(position + turned * Vector3.Scale(scale, pivot), fixedTip),
                    Is.LessThan(0.0000001f), "The cymbal mount must not orbit as the top rocks.");
                Assert.That(Quaternion.Angle(rotation, turned), Is.LessThanOrEqualTo(3.001f));
                if (i is 0 or 50 or 100)
                {
                    Assert.That(Vector3.Distance(position, REST), Is.LessThan(0.0000001f));
                    Assert.That(Quaternion.Angle(turned, rotation), Is.LessThan(0.001f));
                }
            }
        }

        [Test]
        public void SharedBeatPhaseFollowsTempoChangesAndNegativeTimeWithoutTickRounding()
        {
            var sync = new SyncTrack(480u,
                new List<TempoChange> { new(60, 0, 0), new(120, 2, 960) },
                new List<TimeSignatureChange> { new(4, 4, 0, 0, 0, 0, 0, 0) }, new List<Beatline>());
            Assert.That((double) Call("EvaluateBeatPhase", 0.2501d, sync), Is.EqualTo(0.2501d).Within(1e-10));
            Assert.That((double) Call("EvaluateBeatPhase", 2.125d, sync), Is.EqualTo(2.25d).Within(1e-10));
            Assert.That((double) Call("EvaluateBeatPhase", -0.25d, sync), Is.EqualTo(-0.25d).Within(1e-10));
        }

        [TestCase(0d, 0f)]
        [TestCase(-1d, 0f)]
        [TestCase(0.5d, 0.40673828125f)]
        [TestCase(1d, 0.84375f)]
        [TestCase(2d, 1f)]
        [TestCase(4d, 1f)]
        [TestCase(double.NaN, 0f)]
        public void RockRetainsMotionThroughTheApproachAndEasesOutAtArrival(double remaining, float expected)
        {
            Assert.That((float) Call("EvaluateArrivalFade", remaining, 2f),
                Is.EqualTo(expected).Within(0.000001f));
        }

        [TestCase(0d, -1)]
        [TestCase(0.5d, 0)]
        [TestCase(1d, 1)]
        [TestCase(1.5d, 0)]
        [TestCase(2d, -1)]
        [TestCase(-1d, 1)]
        public void RockAlternatesAuthoredPosesOnSuccessiveBeats(double phase, int endpoint)
        {
            var restRotation = Quaternion.Euler(-85f, -180f, 0f);
            var backPosition = REST + new Vector3(0f, 0.0001f, -0.001f);
            var backRotation = Quaternion.Euler(-80f, -180f, 0f);
            var forwardPosition = REST + new Vector3(0f, 0.0002f, 0.002f);
            var forwardRotation = Quaternion.Euler(-92f, -180f, 0f);
            var arguments = new object[] { phase, 4d, 2f, REST, restRotation,
                backPosition, backRotation, forwardPosition, forwardRotation, null, null };
            Call("EvaluateRockPose", arguments);
            var expectedPosition = endpoint > 0 ? forwardPosition : endpoint < 0 ? backPosition : REST;
            var expectedRotation = endpoint > 0 ? forwardRotation : endpoint < 0 ? backRotation : restRotation;
            Assert.That(Vector3.Distance((Vector3) arguments[9], expectedPosition), Is.LessThan(0.0000001f));
            Assert.That(Quaternion.Angle((Quaternion) arguments[10], expectedRotation), Is.LessThan(0.05f));
        }

        [Test]
        public void RockSettlesWithZeroVelocityAtTheLineForOffbeatNotes()
        {
            var rotation = Quaternion.Euler(-85f, -180f, 0f);
            Vector3 At(double phase, double remaining)
            {
                var arguments = new object[] { phase, remaining, 2f, REST, rotation,
                    REST - Vector3.forward, Quaternion.Euler(-80f, -180f, 0f),
                    REST + Vector3.forward, Quaternion.Euler(-92f, -180f, 0f), null, null };
                Call("EvaluateRockPose", arguments);
                return (Vector3) arguments[9];
            }
            foreach (double phase in new[] { 0.125d, 0.625d, 1.25d })
            {
                Assert.That(At(phase, 0d), Is.EqualTo(REST));
                Assert.That(At(phase + 0.2d, -0.2d), Is.EqualTo(REST));
                float closeSpeed = Vector3.Distance(At(phase - 0.00001d, 0.00001d), REST) / 0.00001f;
                float furtherSpeed = Vector3.Distance(At(phase - 0.001d, 0.001d), REST) / 0.001f;
                Assert.That(closeSpeed, Is.LessThan(0.0002f));
                Assert.That(closeSpeed, Is.LessThan(furtherSpeed * 0.02f));
            }
        }

        [Test]
        public void OnlyTheStandardOpenTopUsesTheMeasuredConeTipAndAuthoredRestPose()
        {
            var type = Type.GetType("YARG.Gameplay.Visuals.HiHatBeatAnimation, Assembly-CSharp", true);
            var theme = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab");
            var components = theme.GetComponentsInChildren(type, true);
            Assert.That(components.Length, Is.EqualTo(1));
            var animation = components[0];
            Assert.That(animation.transform.parent.parent.name, Is.EqualTo("OpenHiHat Note"));
            var serialized = new SerializedObject(animation);
            foreach (var field in new[] { "_restPosition", "_restRotation", "_pivotLocal", "_swayDegrees",
                "_mode", "_fadeBeats", "_backPosition", "_backRotation", "_forwardPosition", "_forwardRotation" })
                Assert.That(serialized.FindProperty(field), Is.Not.Null, $"Configure {field} for tip-centered sway.");
            Assert.That(serialized.FindProperty("_restPosition").vector3Value, Is.EqualTo(REST));
            Assert.That(serialized.FindProperty("_swayDegrees").floatValue, Is.EqualTo(2f));
            Assert.That(serialized.FindProperty("_mode").enumValueIndex, Is.EqualTo(1));
            Assert.That(serialized.FindProperty("_fadeBeats").floatValue, Is.EqualTo(2f));
            Assert.That(serialized.FindProperty("_backPosition").vector3Value,
                Is.EqualTo(new Vector3(0f, 0.00220106752f, -0.00300104637f)));
            Assert.That(serialized.FindProperty("_forwardPosition").vector3Value,
                Is.EqualTo(new Vector3(0f, 0.00222244998f, -0.00112135429f)));
            var rotation = serialized.FindProperty("_restRotation").quaternionValue;
            Assert.That(Quaternion.Angle(rotation, animation.transform.localRotation), Is.LessThan(0.001f));
            var pivot = serialized.FindProperty("_pivotLocal").vector3Value;
            var mesh = animation.GetComponent<MeshFilter>().sharedMesh;
            float height = (rotation * Vector3.Scale(animation.transform.localScale, pivot)).y;
            foreach (var vertex in mesh.vertices)
                Assert.That((rotation * Vector3.Scale(animation.transform.localScale, vertex)).y,
                    Is.LessThanOrEqualTo(height + 0.0000001f));
            Assert.That(pivot.z, Is.GreaterThan(0.005f), "The fulcrum must be the cone tip, not the rim or object origin.");
        }
    }
}
