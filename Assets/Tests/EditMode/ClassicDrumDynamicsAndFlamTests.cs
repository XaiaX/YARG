using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YARG.Core.Chart;

namespace YARG.Tests.EditMode
{
    // pattern: Imperative Shell
    public sealed class ClassicDrumDynamicsAndFlamTests
    {
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        [TestCase("FourLaneDrums", "Ghost", .37f)]
        [TestCase("FourLaneDrums", "CymbalGhost", .37f)]
        [TestCase("FiveLaneDrums", "Ghost", .63f)]
        [TestCase("FiveLaneDrums", "CymbalGhost", .63f)]
        [TestCase("FourLaneDrums", "KickGhost", -1f)]
        [TestCase("FourLaneDrums", "DedicatedLaneKickGhost", -1f)]
        [TestCase("FiveLaneDrums", "KickGhost", -1f)]
        [TestCase("FiveLaneDrums", "DedicatedLaneKickGhost", -1f)]
        public void ClassicStripEmissionAppliesOnlyToHandGhostModels(string mode, string model, float expected)
        {
            var colors = new YARG.Core.Game.ColorProfile("custom classic ghost kick strip");
            colors.FourLaneDrums.GhostStripEmission = 37f;
            colors.FiveLaneDrums.GhostStripEmission = 63f;
            var method = Production("YARG.Settings.Preview.FakeNote").GetMethod("GetStripEmission", STATIC);
            var actual = method.Invoke(null, new object[]
            {
                Enum.Parse(typeof(YARG.Core.GameMode), mode),
                Enum.Parse(Production("YARG.Themes.ThemeNoteType"), model), colors
            });
            Assert.That(actual, Is.EqualTo(expected), "Hand ghost models retain their profile strip emission.");
        }

        public class PreviewRandom : DispatchProxy
        {
            public readonly Queue<int> Values = new();
            protected override object Invoke(MethodInfo method, object[] args) => Values.Count > 0
                ? Math.Clamp(Values.Dequeue(), (int)args[0], (int)args[1] - 1) : (int)args[0];
        }

        private static object Random(params int[] values)
        {
            var create = typeof(DispatchProxy).GetMethods(STATIC)
                .Single(method => method.Name == "Create" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);
            var random = create.MakeGenericMethod(Production("YARG.Settings.Preview.IFakeNoteRandom"), typeof(PreviewRandom)).Invoke(null, null);
            foreach (int value in values) ((PreviewRandom)random).Values.Enqueue(value);
            return random;
        }

        [TestCase(false, "KickGhost", "Kick")]
        [TestCase(false, "KickAccent", "Kick")]
        [TestCase(true, "KickGhost", "Kick")]
        [TestCase(true, "KickAccent", "Kick")]
        [TestCase(false, "KickFlam", "KickFlam")]
        [TestCase(true, "KickFlam", "KickFlam")]
        [TestCase(false, "KickFlamAccent", "KickFlam")]
        [TestCase(true, "KickFlamGhost", "KickFlam")]
        public void ClassicKickSpotlightsKeepFlamsAndIgnoreStrength(bool fiveLane, string model, string expected)
        {
            var type = Production("YARG.Settings.Preview.DrumFakeNoteGenerator");
            var generator = Activator.CreateInstance(type, new object[] { fiveLane });
            var note = type.GetMethod("CreateTypeSpotlightNote").Invoke(generator,
                new[] { (object)1d, Enum.Parse(Production("YARG.Themes.ThemeNoteType"), model), Random() });
            Assert.That(note.GetType().GetField("CenterNote").GetValue(note), Is.True);
            Assert.That(note.GetType().GetField("Fret").GetValue(note), Is.EqualTo(0));
            Assert.That(note.GetType().GetField("NoteType").GetValue(note).ToString(), Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HandFlamPreviewSamplesHaveNoOtherHandCompanion(bool fiveLane)
        {
            var type = Production("YARG.Settings.Preview.DrumFakeNoteGenerator");
            var generator = Activator.CreateInstance(type, new object[] { fiveLane });
            var note = type.GetMethod("CreateNote").Invoke(generator, new[] { (object)1d, Random(1, 0, 0) });
            var flam = note.GetType().GetField("ClassicHandFlam");
            Assert.That(flam, Is.Not.Null, "Classic preview must carry its own hand-flam appearance.");
            Assert.That(flam.GetValue(note), Is.True);
            var companions = ((IEnumerable)type.GetMethod("CreateChordNotes").Invoke(generator,
                new[] { (object)1d, note, Random() })).Cast<object>();
            Assert.That(companions.All(companion => (bool)companion.GetType().GetField("CenterNote").GetValue(companion)), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClassicKickModelsIgnoreStrengthAndKeepFlamDistinct(bool dedicated)
        {
            var selector = Production("YARG.Gameplay.Visuals.DrumsNoteElement").GetMethod("GetKickGroup", STATIC);
            Assert.That(selector, Is.Not.Null, "Classic kicks must route neutral/flam models, including dedicated lanes.");
            var groups = new HashSet<int>();
            foreach (var strength in new[] { DrumNoteType.Neutral, DrumNoteType.Ghost, DrumNoteType.Accent })
            {
                var note = new DrumNote(FourLaneDrumPad.Kick, strength, DrumNoteFlags.None, NoteFlags.None, 1d, 480);
                groups.Add((int)selector.Invoke(null, new object[] { note, dedicated }));
            }
            Assert.That(groups.Count, Is.EqualTo(1), "Classic kick hardware does not report velocity.");
            var flam = new DrumNote(FourLaneDrumPad.Kick, DrumNoteType.Accent, DrumNoteFlags.Flam, NoteFlags.None, 1d, 480);
            Assert.That(groups.Contains((int)selector.Invoke(null, new object[] { flam, dedicated })), Is.False,
                "A kick flam must retain its separate appearance.");
        }

        [TestCase("Four Lane Notes")]
        [TestCase("Five Lane Notes")]
        public void RectangularClassicGalleriesProvideSplitAndStackedKickFlams(string galleryName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab");
            var gallery = prefab.transform.Find(galleryName);
            var type = Production("YARG.Themes.ThemeNote");
            var notes = gallery.GetComponentsInChildren(type, true)
                .Where(note => !(bool)type.GetProperty("StarPowerVariant").GetValue(note))
                .ToDictionary(note => type.GetProperty("NoteType").GetValue(note).ToString());
            foreach (string name in new[] { "KickFlam", "DedicatedLaneKickFlam" })
            {
                Assert.That(notes.ContainsKey(name), Is.True, galleryName + "/" + name);
                var root = ((Component)notes[name]).transform;
                Assert.That(root.GetComponentsInChildren<MeshFilter>(true).Length, Is.GreaterThanOrEqualTo(3), name);
                var metals = (IEnumerable)type.GetProperty("ColoredMetalMaterials").GetValue(notes[name]);
                Assert.That(metals.Cast<object>().Count(), Is.GreaterThanOrEqualTo(1), name + " reactive metal");
                if (name == "KickFlam")
                    Assert.That(root.GetComponent(Production("YARG.Gameplay.Visuals.ClassicKickFlamVisual")), Is.Not.Null,
                        "The split classic model must expose independently fitted beam endpoints.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClassicOptionalKickFallbackKeepsThemeOwnedShapesAndStarPower(bool authored)
        {
            var noteType = Production("YARG.Themes.ThemeNoteType");
            object Kind(string name) => Enum.Parse(noteType, name);
            IDictionary Map() => (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(noteType, typeof(GameObject)));
            var regular = Map();
            var stars = Map();
            var owned = new List<GameObject>();
            GameObject Model(string name) { var model = new GameObject(name); owned.Add(model); return model; }
            try
            {
                regular.Add(Kind("Kick"), Model("theme wide"));
                regular.Add(Kind("DedicatedLaneKick"), Model("theme dedicated"));
                stars.Add(Kind("Kick"), Model("theme wide SP"));
                stars.Add(Kind("DedicatedLaneKick"), Model("theme dedicated SP"));
                if (authored) regular.Add(Kind("KickFlam"), Model("authored flam"));
                var resolve = Production("YARG.Themes.ThemeNoteModelFallbacks").GetMethod("ResolveClassicDrumModels", STATIC);
                Assert.That(resolve, Is.Not.Null, "Classic fallbacks must run before default theme models are filled.");
                var result = resolve.Invoke(null, new object[] { regular, stars });
                var resolved = (IDictionary)result.GetType().GetField("Item1").GetValue(result);
                var resolvedStars = (IDictionary)result.GetType().GetField("Item2").GetValue(result);
                foreach (string prefix in new[] { "", "DedicatedLane" })
                {
                    var kind = Kind(prefix + "KickFlam");
                    if (authored && prefix == "")
                    {
                        Assert.That(resolved[kind], Is.SameAs(regular[kind]));
                        Assert.That(resolvedStars.Contains(kind), Is.False, "Authored shape recolors in place for SP.");
                    }
                    else
                    {
                        Assert.That(resolved[kind], Is.SameAs(regular[Kind(prefix + "Kick")]));
                        Assert.That(resolvedStars[kind], Is.SameAs(stars[Kind(prefix + "Kick")]));
                    }
                }
                Assert.That(regular.Count, Is.EqualTo(authored ? 3 : 2), "The theme's source dictionary is immutable.");
                Assert.That(stars.Count, Is.EqualTo(2));
            }
            finally { foreach (var model in owned) UnityEngine.Object.DestroyImmediate(model); }
        }
    }
}
