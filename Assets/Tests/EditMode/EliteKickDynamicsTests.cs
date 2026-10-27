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

namespace YARG.Tests.EditMode
{
    public sealed class EliteKickDynamicsTests
    {
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        private static object Read(object obj, string name) => obj.GetType().GetField(name).GetValue(obj);
        private static object Property(object obj, string name) => obj.GetType().GetProperty(name).GetValue(obj);

        [Test]
        public void KickStrengthAndFlamSelectSixDistinctModels()
        {
            var select = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement").GetMethod("GetGemGroup", STATIC);
            var groups = new HashSet<int>();
            foreach (bool flam in new[] { false, true })
            foreach (var strength in new[] { DrumNoteType.Neutral, DrumNoteType.Ghost, DrumNoteType.Accent })
            {
                var note = new EliteDrumNote(EliteDrumPad.Kick, strength, EliteDrumsHatState.Indifferent,
                    EliteDrumsHatPedalType.Stomp, flam, DrumNoteFlags.None, NoteFlags.None,
                    EliteDrumsChannelFlag.None, 1d, 480, false);
                groups.Add((int)select.Invoke(null, new object[] { note, true }));
            }
            Assert.That(groups.Count, Is.EqualTo(6));
        }

        [TestCase("Bars")]
        [TestCase("Combined")]
        [TestCase("Split")]
        public void PreviewKickBlockAlternatesFeetThenShowsThreeFlams(string mode)
        {
            var type = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
            var generator = Activator.CreateInstance(type, new object[] { Enum.Parse(typeof(EliteDrumsLayoutKickMode), mode) });
            int count = (int)type.GetField("NOTE_COUNT").GetRawConstantValue() / 2;
            var kicks = new List<string>();
            var indices = new List<int>();
            for (int i = 0; i < count; i++)
            {
                var note = type.GetMethod("CreateNote").Invoke(generator, new object[] { (double)i, null });
                var extras = ((IEnumerable)type.GetMethod("CreateChordNotes").Invoke(generator,
                    new object[] { (double)i, note, null })).Cast<object>().ToArray();
                var role = Property(Read(note, "EliteDescriptor"), "Role").ToString();
                if (role != "Kick" && role != "DoubleKick" && role != "KickFlam") continue;
                if (extras.Any(extra => Property(Read(extra, "EliteDescriptor"), "Role").ToString() is "Stomp" or "Splash")) continue;
                string model = Read(note, "NoteType").ToString().Replace("DedicatedLane", "");
                kicks.Add(role + "/" + model);
                indices.Add(i);
            }
            Assert.That(kicks, Is.EqualTo(new[] { "Kick/KickGhost", "DoubleKick/KickGhost", "Kick/Kick",
                "DoubleKick/Kick", "Kick/KickAccent", "DoubleKick/KickAccent", "KickFlam/KickFlamGhost",
                "KickFlam/KickFlam", "KickFlam/KickFlamAccent" }));
            Assert.That(indices.Last() - indices.First(), Is.EqualTo(8), "Kick comparison must be contiguous.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OptionalKickModelsPreserveCustomThemeAndStarPowerFallbacks(bool authored)
        {
            var noteType = Production("YARG.Themes.ThemeNoteType");
            object TypeValue(string name) => Enum.Parse(noteType, name);
            IDictionary Dictionary() => (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(noteType, typeof(GameObject)));
            var regular = Dictionary(); var stars = Dictionary();
            var owned = new List<GameObject>();
            GameObject Model(string name) { var obj = new GameObject(name); owned.Add(obj); return obj; }
            try
            {
                regular.Add(TypeValue("Kick"), Model("wide"));
                regular.Add(TypeValue("DedicatedLaneKick"), Model("dedicated"));
                stars.Add(TypeValue("Kick"), Model("wide star"));
                stars.Add(TypeValue("DedicatedLaneKick"), Model("dedicated star"));
                if (authored) regular.Add(TypeValue("KickFlamAccent"), Model("authored accent flam"));
                var result = Production("YARG.Themes.ThemeNoteModelFallbacks").GetMethod("ResolveEliteModels")
                    .Invoke(null, new object[] { regular, stars });
                var resolved = (IDictionary)Read(result, "Item1");
                var resolvedStars = (IDictionary)Read(result, "Item2");
                foreach (var prefix in new[] { "", "DedicatedLane" })
                foreach (var suffix in new[] { "Ghost", "Accent", "Flam", "FlamGhost", "FlamAccent" })
                {
                    var type = TypeValue(prefix + "Kick" + suffix);
                    if (authored && prefix == "" && suffix == "FlamAccent")
                    {
                        Assert.That(resolved[type], Is.SameAs(regular[type]));
                        Assert.That(resolvedStars.Contains(type), Is.False);
                    }
                    else
                    {
                        Assert.That(resolved[type], Is.SameAs(regular[TypeValue(prefix + "Kick")]));
                        Assert.That(resolvedStars[type], Is.SameAs(stars[TypeValue(prefix + "Kick")]));
                    }
                }
                Assert.That(regular.Count, Is.EqualTo(authored ? 3 : 2));
                Assert.That(stars.Count, Is.EqualTo(2));
            }
            finally { foreach (var obj in owned) UnityEngine.Object.DestroyImmediate(obj); }
        }

        [Test]
        public void DedicatedKickRoutingKeepsAllSixModelsDistinct()
        {
            var method = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement").GetMethod("GetKickGroup", STATIC);
            Assert.That(method, Is.Not.Null);
            var groups = new HashSet<int>();
            foreach (bool flam in new[] { false, true })
            foreach (var strength in new[] { DrumNoteType.Neutral, DrumNoteType.Ghost, DrumNoteType.Accent })
            {
                var note = new EliteDrumNote(EliteDrumPad.Kick, strength, EliteDrumsHatState.Indifferent,
                    EliteDrumsHatPedalType.Stomp, flam, DrumNoteFlags.None, NoteFlags.None,
                    EliteDrumsChannelFlag.None, 1d, 480, false);
                int wide = (int)method.Invoke(null, new object[] { note, false });
                int dedicated = (int)method.Invoke(null, new object[] { note, true });
                Assert.That(dedicated, Is.Not.EqualTo(wide));
                groups.Add(dedicated);
            }
            Assert.That(groups.Count, Is.EqualTo(6));
        }

        [Test]
        public void RectangularThemeProvidesSixSeparateKickAssembliesAndReactiveChrome()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab");
            var elite = prefab.transform.Find("Elite Lane Notes");
            var type = Production("YARG.Themes.ThemeNote");
            var notes = elite.GetComponentsInChildren(type, true).Where(note => !(bool)Property(note, "StarPowerVariant")).ToDictionary(note => Property(note, "NoteType").ToString());
            foreach (var name in new[] { "Kick", "KickGhost", "KickAccent", "KickFlam", "KickFlamGhost", "KickFlamAccent" })
            {
                Assert.That(notes.ContainsKey(name), Is.True, name);
                var root = ((Component)notes[name]).transform;
                var meshes = root.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(meshes.Length, Is.GreaterThanOrEqualTo(name.Contains("Flam") ? 4 : 3), name);
                if (name.Contains("Accent"))
                    Assert.That(((IEnumerable)Property(notes[name], "ColoredMetalMaterials")).Cast<object>().Count(),
                        Is.GreaterThanOrEqualTo(name.Contains("Flam") ? 4 : 3));
            }
        }
    }
}
