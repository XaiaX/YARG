// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Chart;

namespace YARG.Tests.EditMode
{
    public sealed class EliteStarPowerVisualTests
    {
        private static Type Effect => Type.GetType("YARG.Gameplay.Visuals.TrackEffect, Assembly-CSharp", true);
        private static Type Kind => Type.GetType("YARG.Gameplay.Visuals.TrackEffectType, Assembly-CSharp", true);
        private static object NewEffect(double start, double end, string kind) =>
            Activator.CreateInstance(Effect, start, end, Enum.Parse(Kind, kind), true, true);
        private static object Get(object effect, string name) => Effect.GetProperty(name).GetValue(effect);
        private static IList Effects(params object[] effects)
        {
            var list = (IList) Activator.CreateInstance(typeof(List<>).MakeGenericType(Effect));
            foreach (var effect in effects) list.Add(effect);
            return list;
        }
        private static EliteDrumNote Cymbal(double time) => new(
            EliteDrumNote.EliteDrumPad.LeftCrash, DrumNoteType.Neutral,
            EliteDrumNote.EliteDrumsHatState.Indifferent, EliteDrumNote.EliteDrumsHatPedalType.Stomp,
            false, DrumNoteFlags.StarPowerActivator, NoteFlags.None,
            EliteDrumNote.EliteDrumsChannelFlag.None, time, (uint)(time * 960), false);

        [TestCase(0f)]
        [TestCase(4f)]
        [TestCase(-1f)]
        public void EliteFillReachesItsActivationNoteAtTheResolvedLane(float lane)
        {
            var fill = NewEffect(1, 2, "DrumFill");
            var orphan = NewEffect(4, 5, "DrumFill");
            var effects = Effects(fill, orphan);
            var method = Effect.GetMethod("ConfigureEliteDrumFills");
            Assert.That(method, Is.Not.Null, "Elite must bind its fill effects to its activation notes.");
            method.Invoke(null, new object[] { new List<EliteDrumNote> { Cymbal(2.25) }, effects,
                new Func<EliteDrumNote, float>(_ => lane), 5 });
            Assert.That(effects.Count, Is.EqualTo(1));
            Assert.That(Get(fill, "TimeEnd"), Is.EqualTo(2.25));
            Assert.That(Get(fill, "FillLanePosition"), Is.EqualTo(lane));
            Assert.That(Get(fill, "TotalLanes"), Is.EqualTo(5));
        }

        [Test]
        public void WildcardActivationUsesCenteredFullWidthLeadUp()
        {
            var note = new EliteDrumNote(
                EliteDrumNote.EliteDrumPad.Wildcard, DrumNoteType.Neutral,
                EliteDrumNote.EliteDrumsHatState.Indifferent, EliteDrumNote.EliteDrumsHatPedalType.Stomp,
                false, DrumNoteFlags.StarPowerActivator, NoteFlags.None,
                EliteDrumNote.EliteDrumsChannelFlag.None, 2.25, 2160, false);
            var fill = NewEffect(1, 2, "DrumFill");
            var effects = Effects(fill);
            Effect.GetMethod("ConfigureEliteDrumFills").Invoke(null, new object[]
            {
                new List<EliteDrumNote> { note }, effects,
                new Func<EliteDrumNote, float>(_ => 0f), 5
            });

            Assert.That(effects.Count, Is.EqualTo(1));
            Assert.That(Get(fill, "TimeEnd"), Is.EqualTo(2.25));
            Assert.That(Get(fill, "FillLanePosition"), Is.EqualTo(-1f),
                "Wildcard accepts any pad, so its lead-up must span the highway, as in classic drums.");
            Assert.That(Get(fill, "TotalLanes"), Is.EqualTo(5));
        }

        [TestCase("Solo")]
        [TestCase("Unison")]
        public void FillTargetSurvivesOverlappingEffects(string otherKind)
        {
            var fill = NewEffect(1, 4, "DrumFill");
            Effect.GetProperty("FillLanePosition").SetValue(fill, 3f);
            Effect.GetProperty("TotalLanes").SetValue(fill, 5);
            var lists = Array.CreateInstance(typeof(List<>).MakeGenericType(Effect), 1);
            lists.SetValue(Effects(fill, NewEffect(2, 3, otherKind)), 0);
            var sliced = (IList) Effect.GetMethod("SliceEffects").Invoke(null, new object[] { 10f, lists });
            Assert.That(sliced.Count, Is.GreaterThan(1));
            foreach (var effect in sliced)
            {
                if (!Get(effect, "EffectType").ToString().Contains("DrumFill")) continue;
                Assert.That(Get(effect, "FillLanePosition"), Is.EqualTo(3f));
                Assert.That(Get(effect, "TotalLanes"), Is.EqualTo(5));
            }
        }

        [Test]
        public void ActivationPulseKeepsRoleColorAndVariesOverTheBeat()
        {
            var element = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp", true);
            var method = element.GetMethod("GetActivationColor", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Elite activation notes need the regular drum pulse.");
            var original = Color.FromArgb(255, 240, 160, 80);
            Color Pulse(bool enabled, float phase) =>
                (Color) method.Invoke(null, new object[] { original, enabled, phase });
            Assert.That(Pulse(false, 0), Is.EqualTo(original));
            Assert.That(Pulse(true, 0).R, Is.EqualTo(0));
            Assert.That(Pulse(true, 1), Is.EqualTo(original));
            Assert.That(Pulse(true, 0.5f).R, Is.EqualTo(210));
        }
    }
}
