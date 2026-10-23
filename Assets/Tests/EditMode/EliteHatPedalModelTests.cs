using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using YARG.Core.Chart;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.EditMode
{
    public sealed class EliteHatPedalModelTests
    {
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static Type NoteType => Production("YARG.Themes.ThemeNoteType");
        private static object TypeValue(string name) => Enum.Parse(NoteType, name);

        [TestCase(5, 0, -0.6f, 0.8f, 2)]
        [TestCase(5, 2, 0f, 1.2f, 3)]
        [TestCase(5, 4, 0.6f, 0.8f, 2)]
        [TestCase(2, 0, 0f, 2f, 2)]
        [TestCase(2, 1, 0f, 2f, 2)]
        [TestCase(1, 0, 0f, 2f, 1)]
        public void PedalRegionCoversHatAndAvailableImmediateNeighbors(int lanes, int column,
            float expectedCenter, float expectedWidth, int expectedSections)
        {
            var method = Production("YARG.Gameplay.Visuals.EliteDrumsRuntimeGeometry")
                .GetMethod("GetPedalRegion", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] args = { column, lanes, 2f, 0f, 0f };
            int sections = (int)method.Invoke(null, args);
            Assert.That(sections, Is.EqualTo(expectedSections));
            Assert.That((float)args[3], Is.EqualTo(expectedCenter).Within(0.0001f));
            Assert.That((float)args[4], Is.EqualTo(expectedWidth).Within(0.0001f));
            args[0] = lanes - 1 - column;
            Assert.That((int)method.Invoke(null, args), Is.EqualTo(expectedSections));
            Assert.That((float)args[3], Is.EqualTo(-expectedCenter).Within(0.0001f));
            Assert.That((float)args[4], Is.EqualTo(expectedWidth).Within(0.0001f));
        }

        [Test]
        public void StompAndSplashChooseDifferentModelsFromEachOtherAndKick()
        {
            var select = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement")
                .GetMethod("GetGemGroup", BindingFlags.Static | BindingFlags.NonPublic);
            int Group(EliteDrumPad pad, EliteDrumsHatPedalType pedal) => (int) select.Invoke(null,
                new object[] { new EliteDrumNote(pad, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent,
                    pedal, false, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None,
                    1d, 480, false), true });
            var stomp = Group(EliteDrumPad.HatPedal, EliteDrumsHatPedalType.Stomp);
            var splash = Group(EliteDrumPad.HatPedal, EliteDrumsHatPedalType.Splash);
            Assert.That(stomp, Is.Not.EqualTo(splash));
            Assert.That(stomp, Is.Not.EqualTo(Group(EliteDrumPad.Kick, EliteDrumsHatPedalType.Stomp)));
            Assert.That(splash, Is.Not.EqualTo(Group(EliteDrumPad.Kick, EliteDrumsHatPedalType.Stomp)));
        }

        [TestCase(EliteDrumsColorRole.Stomp, "HatStomp")]
        [TestCase(EliteDrumsColorRole.Splash, "HatSplash")]
        public void PreviewChoosesThePedalSpecificModel(EliteDrumsColorRole role, string expected)
        {
            var descriptor = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator")
                .GetMethod("Describe").Invoke(null, new[] { (object) role, TypeValue("Normal") });
            Assert.That(descriptor.GetType().GetProperty("ModelType").GetValue(descriptor).ToString(),
                Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingPedalSlotsKeepTheCustomThemesOwnBars(bool authoredStomp)
        {
            var regular = new GameObject("Legacy regular bar");
            var star = new GameObject("Legacy star bar");
            var authored = new GameObject("Authored stomp");
            try
            {
                var models = Dictionary();
                var stars = Dictionary();
                models.Add(TypeValue("DedicatedLaneKick"), regular);
                stars.Add(TypeValue("DedicatedLaneKick"), star);
                if (authoredStomp) models.Add(TypeValue("HatStomp"), authored);
                var result = Production("YARG.Themes.ThemeNoteModelFallbacks").GetMethod("ResolveEliteModels")
                    .Invoke(null, new object[] { models, stars });
                var resolved = (IDictionary) result.GetType().GetField("Item1").GetValue(result);
                var resolvedStars = (IDictionary) result.GetType().GetField("Item2").GetValue(result);
                Assert.That(resolved[TypeValue("HatSplash")], Is.SameAs(regular));
                Assert.That(resolvedStars[TypeValue("HatSplash")], Is.SameAs(star));
                Assert.That(resolved[TypeValue("HatStomp")], Is.SameAs(authoredStomp ? authored : regular));
                if (authoredStomp)
                    Assert.That(resolvedStars.Contains(TypeValue("HatStomp")), Is.False,
                        "An authored regular stomp must keep its silhouette during Star Power.");
                else
                    Assert.That(resolvedStars[TypeValue("HatStomp")], Is.SameAs(star));
                Assert.That(models.Count, Is.EqualTo(authoredStomp ? 2 : 1));
                Assert.That(stars.Count, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(regular);
                UnityEngine.Object.DestroyImmediate(star);
                UnityEngine.Object.DestroyImmediate(authored);
            }
        }

        private static IDictionary Dictionary() => (IDictionary) Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(NoteType, typeof(GameObject)));
    }
}
