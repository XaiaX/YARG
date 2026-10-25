using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YARG.Core.Chart;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

// pattern: Imperative Shell

namespace YARG.Tests.EditMode
{
    public sealed class EliteOpenHiHatVisualTests
    {
        private const string THEME_PATH = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";
        private const BindingFlags STATIC_INTERNAL = BindingFlags.Static | BindingFlags.NonPublic;
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static Type NoteType => Production("YARG.Themes.ThemeNoteType");
        private static object TypeValue(string name) => Enum.Parse(NoteType, name);

        [TestCase(DrumNoteType.Neutral, 8)]
        [TestCase(DrumNoteType.Accent, 9)]
        [TestCase(DrumNoteType.Ghost, 10)]
        public void OpenHiHatUsesDistinctGeometryWithMatchingDynamics(DrumNoteType dynamics, int expected)
        {
            var element = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
            var select = element.GetMethod("GetGemGroup", STATIC_INTERNAL);
            var split = element.GetMethod("GetSplitGroup", STATIC_INTERNAL);
            var note = Note(EliteDrumPad.HiHat, dynamics, EliteDrumsHatState.Open);
            Assert.That(select.Invoke(null, new object[] { note, true }), Is.EqualTo(expected));
            Assert.That(split.Invoke(null, new object[] { note, true }), Is.EqualTo(expected),
                "Split flams must retain the open hi-hat state on both visual heads.");
        }

        [TestCase(DrumNoteType.Neutral, 11)]
        [TestCase(DrumNoteType.Accent, 12)]
        [TestCase(DrumNoteType.Ghost, 13)]
        public void ClosedHiHatUsesDistinctGeometryWithMatchingDynamics(DrumNoteType dynamics, int expected)
        {
            var element = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
            var select = element.GetMethod("GetGemGroup", STATIC_INTERNAL);
            var split = element.GetMethod("GetSplitGroup", STATIC_INTERNAL);
            var note = Note(EliteDrumPad.HiHat, dynamics, EliteDrumsHatState.Closed);
            Assert.That(select.Invoke(null, new object[] { note, true }), Is.EqualTo(expected));
            Assert.That(select.Invoke(null, new object[] { note, false }),
                Is.EqualTo(dynamics == DrumNoteType.Neutral ? 0 : expected));
            note.IsFlam = true;
            Assert.That(split.Invoke(null, new object[] { note, true }), Is.EqualTo(expected),
                "Split flams must keep explicit closed-hat geometry on both heads.");
            Assert.That(select.Invoke(null, new object[] { note, true }), Is.EqualTo(expected),
                "Closed-hat flams retain their explicit hat model and dynamics.");
        }

        [TestCase(DrumNoteType.Neutral, 1)]
        [TestCase(DrumNoteType.Accent, 5)]
        [TestCase(DrumNoteType.Ghost, 6)]
        public void IndifferentHatsAndOtherCymbalsUseOrdinaryModels(DrumNoteType dynamics, int expected)
        {
            var select = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement").GetMethod("GetGemGroup", STATIC_INTERNAL);
            foreach (var pad in new[] { EliteDrumPad.HiHat, EliteDrumPad.LeftCrash, EliteDrumPad.Ride, EliteDrumPad.RightCrash })
            {
                Assert.That(select.Invoke(null, new object[] { Note(pad, dynamics), true }), Is.EqualTo(expected));
                if (pad != EliteDrumPad.HiHat)
                    Assert.That(select.Invoke(null, new object[] { Note(pad, dynamics, EliteDrumsHatState.Closed), true }),
                        Is.EqualTo(expected), "Only hi-hat notes may use closed-hat models.");
            }
        }

        [TestCase("Normal", "ClosedHiHat", "Cymbal")]
        [TestCase("Accent", "ClosedHiHatAccent", "CymbalAccent")]
        [TestCase("Ghost", "ClosedHiHatGhost", "CymbalGhost")]
        public void ClosedAndIndifferentPreviewKeepIndependentModels(string dynamics, string closed, string ordinary)
        {
            var describe = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator").GetMethod("Describe");
            foreach (var role in new[] { EliteDrumsColorRole.HatClosed, EliteDrumsColorRole.HatIndifferent })
            {
                var descriptor = describe.Invoke(null, new[] { (object) role, TypeValue(dynamics) });
                Assert.That(descriptor.GetType().GetProperty("ModelType").GetValue(descriptor).ToString(),
                    Is.EqualTo(role == EliteDrumsColorRole.HatClosed ? closed : ordinary));
                Assert.That(descriptor.GetType().GetProperty("Fret").GetValue(descriptor), Is.EqualTo(2));
            }
        }

        [Test]
        public void OrdinaryHatsOtherCymbalsAndUnsplitFlamsRetainTheirModels()
        {
            var select = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement")
                .GetMethod("GetGemGroup", STATIC_INTERNAL);
            int Group(EliteDrumNote note, bool cymbals = true) =>
                (int) select.Invoke(null, new object[] { note, cymbals });
            Assert.That(Group(Note(EliteDrumPad.HiHat)), Is.EqualTo(1));
            Assert.That(Group(Note(EliteDrumPad.LeftCrash, hat: EliteDrumsHatState.Open)), Is.EqualTo(1));
            Assert.That(Group(Note(EliteDrumPad.HatPedal, hat: EliteDrumsHatState.Open)), Is.EqualTo(15));
            Assert.That(Group(Note(EliteDrumPad.HiHat, hat: EliteDrumsHatState.Open), false), Is.EqualTo(0));
            var flam = Note(EliteDrumPad.HiHat, hat: EliteDrumsHatState.Open);
            flam.IsFlam = true;
            Assert.That(Group(flam), Is.EqualTo(8));
        }

        [TestCase("Normal", "OpenHiHat")]
        [TestCase("Accent", "OpenHiHatAccent")]
        [TestCase("Ghost", "OpenHiHatGhost")]
        public void SyntheticPreviewSelectsOpenGeometry(string dynamics, string expected)
        {
            var descriptor = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator")
                .GetMethod("Describe").Invoke(null, new[] { (object) EliteDrumsColorRole.HatOpen, TypeValue(dynamics) });
            Assert.That(descriptor.GetType().GetProperty("ModelType").GetValue(descriptor).ToString(),
                Is.EqualTo(expected));
        }

        [TestCase("OpenHiHat", "Cymbal")]
        [TestCase("OpenHiHatAccent", "CymbalAccent")]
        [TestCase("OpenHiHatGhost", "CymbalGhost")]
        [TestCase("ClosedHiHat", "Cymbal")]
        [TestCase("ClosedHiHatAccent", "CymbalAccent")]
        [TestCase("ClosedHiHatGhost", "CymbalGhost")]
        public void OldThemesKeepTheirOwnRegularAndStarPowerModels(string open, string ordinary)
        {
            var regular = new GameObject("Custom regular cymbal");
            var star = new GameObject("Custom star cymbal");
            try
            {
                var models = Dictionary();
                var stars = Dictionary();
                models.Add(TypeValue(ordinary), regular);
                stars.Add(TypeValue(ordinary), star);
                var result = Resolve(models, stars);
                Assert.That(result.Regular[TypeValue(open)], Is.SameAs(regular));
                Assert.That(result.Star[TypeValue(open)], Is.SameAs(star));
                Assert.That(models.Count, Is.EqualTo(1), "Resolution must not mutate caller dictionaries.");
                Assert.That(stars.Count, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(regular);
                UnityEngine.Object.DestroyImmediate(star);
            }
        }

        [TestCase("OpenHiHat")]
        [TestCase("ClosedHiHat")]
        public void AuthoredHatModelSurvivesStarPowerWithoutAnExplicitHatVariant(string hat)
        {
            var open = new GameObject("Authored open cymbal");
            var ordinaryStar = new GameObject("Ordinary star cymbal");
            try
            {
                var models = Dictionary();
                var stars = Dictionary();
                models.Add(TypeValue(hat), open);
                stars.Add(TypeValue("Cymbal"), ordinaryStar);
                var result = Resolve(models, stars);
                Assert.That(result.Regular[TypeValue(hat)], Is.SameAs(open));
                Assert.That(result.Star.Contains(TypeValue(hat)), Is.False,
                    "The regular open model must be recolored instead of swapping to a closed silhouette.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(open);
                UnityEngine.Object.DestroyImmediate(ordinaryStar);
            }
        }

        [TestCase("Cymbal")]
        [TestCase("CymbalGhost")]
        [TestCase("CymbalAccent")]
        [TestCase("OpenHiHat")]
        [TestCase("OpenHiHatGhost")]
        [TestCase("OpenHiHatAccent")]
        [TestCase("ClosedHiHat")]
        [TestCase("ClosedHiHatGhost")]
        [TestCase("ClosedHiHatAccent")]
        public void EliteCymbalsHaveIndependentColorMetalAndDarkRegions(string type)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(THEME_PATH);
            var noteType = Production("YARG.Themes.ThemeNote");
            var theme = prefab.GetComponent(Production("YARG.Themes.ThemeComponent"));
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var models = (IDictionary) theme.GetType().GetMethod("GetNoteModelsForVisualStyle")
                .Invoke(theme, new[] { style, (object) false });
            var model = (GameObject) models[TypeValue(type)];
            Assert.That(model.GetComponentsInChildren(noteType).Length, Is.EqualTo(1));
            var filters = model.GetComponentsInChildren<MeshFilter>();
            Assert.That(filters.Count(f => f.name == "Base"), Is.EqualTo(1));
            Assert.That(filters.Count(f => f.name == "Top"), Is.EqualTo(1));
            var note = model.GetComponent(noteType);
            var colored = ((IEnumerable) noteType.GetProperty("ColoredMaterials").GetValue(note)).Cast<object>().ToArray();
            var metal = ((IEnumerable) noteType.GetProperty("ColoredMetalMaterials").GetValue(note)).Cast<object>().ToArray();
            bool Contains(object[] entries, MeshRenderer renderer, int index) => entries.Any(entry =>
                (MeshRenderer) entry.GetType().GetField("Mesh").GetValue(entry) == renderer &&
                (int) entry.GetType().GetField("MaterialIndex").GetValue(entry) == index);
            Assert.That(colored, Is.Not.Empty);
            Assert.That(metal, Is.Not.Empty);
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            for (int index = 0; index < renderer.sharedMaterials.Length; index++)
            {
                string material = renderer.sharedMaterials[index].name;
                bool isColor = material == "CymbalMiddle" || material == "CymbalGhostCenter" || material == "EliteCymbalGapLight";
                Assert.That(Contains(colored, renderer, index), Is.EqualTo(isColor), type + "/" + renderer.name);
                Assert.That(Contains(metal, renderer, index), Is.EqualTo(material == "CymbalMetal"), type + "/" + renderer.name);
            }
            if (type.StartsWith("Open", StringComparison.Ordinal))
            {
                var light = filters.Single(f => f.name == "Light");
                Assert.That(light.transform.parent, Is.SameAs(filters.Single(f => f.name == "Top").transform.parent),
                    "The exposed underside light must move with the top.");
            }
        }

        [TestCase("Cymbal")]
        [TestCase("OpenHiHat")]
        [TestCase("ClosedHiHat")]
        public void EliteCymbalBaseUsesFiveLaneHighwayAnchorAfterRuntimeRootPlacement(string type)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(THEME_PATH);
            var theme = prefab.GetComponent(Production("YARG.Themes.ThemeComponent"));
            var fetch = theme.GetType().GetMethod("GetNoteModelsForVisualStyle");
            var style = Production("YARG.Themes.VisualStyle");
            var elite = (IDictionary) fetch.Invoke(theme, new[] { Enum.Parse(style, "EliteDrums"), (object) false });
            var five = (IDictionary) fetch.Invoke(theme, new[] { Enum.Parse(style, "FiveLaneDrums"), (object) false });
            var reference = (GameObject) five[TypeValue("Cymbal")];
            var model = UnityEngine.Object.Instantiate((GameObject) elite[TypeValue(type)]);
            try
            {
                // NoteGroup places the note root at zero; visual child offsets must survive that.
                model.transform.SetParent(null);
                model.transform.localPosition = Vector3.zero;
                var part = model.GetComponentsInChildren<MeshFilter>().Single(filter => filter.name == "Base");
                var original = reference.GetComponentInChildren<MeshFilter>();
                Vector3 RimMinimum(GameObject root, MeshFilter filter, int submesh)
                {
                    var vertices = filter.sharedMesh.vertices;
                    var points = filter.sharedMesh.GetTriangles(submesh).Distinct()
                        .Select(i => root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]))).ToArray();
                    return new Vector3(points.Min(v => v.x), points.Min(v => v.y), points.Min(v => v.z));
                }
                // The five-lane Note_Base ring defines the contact anchor for every shared Elite base.
                var expected = RimMinimum(reference, original, 2);
                var actual = part.sharedMesh.vertices.Select(v => model.transform.InverseTransformPoint(part.transform.TransformPoint(v)))
                        .Aggregate(new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), Vector3.Min);
                Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.00001f),
                    "The chrome rim must sit at the existing cymbal's height above the highway.");
                Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.00001f),
                    "The front rim must use the existing cymbal's offset from the note-placement origin.");
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
        }

        [TestCase("OpenHiHat")]
        [TestCase("ClosedHiHat")]
        [TestCase("HatStomp")]
        [TestCase("HatSplash")]
        [TestCase("OpenHiHatAccent")]
        [TestCase("OpenHiHatGhost")]
        [TestCase("ClosedHiHatAccent")]
        [TestCase("ClosedHiHatGhost")]
        public void HatsHaveReusablePartsAndStarPowerMetal(string type)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(THEME_PATH);
            var comp = prefab.GetComponent(Production("YARG.Themes.ThemeComponent"));
            var fetch = comp.GetType().GetMethod("GetNoteModelsForVisualStyle");
            var styles = Production("YARG.Themes.VisualStyle");
            var elite = (IDictionary) fetch.Invoke(comp, new[] { Enum.Parse(styles, "EliteDrums"), (object) false });
            var model = (GameObject) elite[TypeValue(type)];
            var noteType = Production("YARG.Themes.ThemeNote");
            Assert.That(model.GetComponentsInChildren(noteType).Length, Is.EqualTo(1));
            var filters = model.GetComponentsInChildren<MeshFilter>();
            Assert.That(filters.Any(f => f.name == "Base"), Is.True);
            Assert.That(filters.Any(f => f.name == "Top"), Is.True);
            var metalEntries = ((IEnumerable) noteType.GetProperty("ColoredMetalMaterials")
                .GetValue(model.GetComponent(noteType))).Cast<object>().ToArray();
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
                for (int index = 0; index < renderer.sharedMaterials.Length; index++)
                    if (renderer.sharedMaterials[index].name == "CymbalMetal")
                        Assert.That(metalEntries.Any(entry =>
                            (MeshRenderer) entry.GetType().GetField("Mesh").GetValue(entry) == renderer &&
                            (int) entry.GetType().GetField("MaterialIndex").GetValue(entry) == index), Is.True,
                            $"{type}/{renderer.name} chrome must respond to Star Power.");
            if (type == "OpenHiHatAccent")
                Assert.That(filters.Single(f => f.name == "Top").GetComponent<MeshRenderer>()
                    .sharedMaterials.Any(m => m.name == "CymbalMetal"), Is.True,
                    "The integrated accent cap must retain its chrome region.");
            foreach (string property in new[] { "ColoredMaterials", "ColoredMaterialsNoStarPower",
                "ColoredMetalMaterials", "ColoredSecondaryMaterials" })
                foreach (var entry in ((IEnumerable) noteType.GetProperty(property).GetValue(model.GetComponent(noteType))).Cast<object>())
                {
                    var renderer = (MeshRenderer) entry.GetType().GetField("Mesh").GetValue(entry);
                    Assert.That(renderer, Is.Not.Null);
                    Assert.That(renderer.transform.IsChildOf(model.transform), Is.True);
                    int index = (int) entry.GetType().GetField("MaterialIndex").GetValue(entry);
                    Assert.That(index, Is.InRange(0, renderer.sharedMaterials.Length - 1));
                }
        }

        [Test]
        public void ActualStackedModelsSurvivePoolCloningAndStarPower()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(THEME_PATH);
            var component = prefab.GetComponent(Production("YARG.Themes.ThemeComponent"));
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var fetch = component.GetType().GetMethod("GetNoteModelsForVisualStyle");
            var regular = fetch.Invoke(component, new[] { style, (object) false });
            var stars = fetch.Invoke(component, new[] { style, (object) true });
            var elementType = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var template = new GameObject("Open hi-hat template");
            GameObject clone = null;
            try
            {
                var element = template.AddComponent(elementType);
                elementType.GetMethod("SetThemeModels").Invoke(element, new[] { regular, stars });
                clone = UnityEngine.Object.Instantiate(template);
                var clonedElement = clone.GetComponent(elementType);
                var groups = (Array) elementType.BaseType.GetField("NoteGroups", flags).GetValue(clonedElement);
                var starGroups = (Array) elementType.BaseType.GetField("StarPowerNoteGroups", flags).GetValue(clonedElement);
                for (int i = 8; i <= 13; i++)
                {
                    var group = (Component) groups.GetValue(i);
                    Assert.That(starGroups.GetValue(i), Is.SameAs(group),
                        "Rectangular open cymbals use the same stacked geometry for Star Power recoloring.");
                    group.transform.localScale = Vector3.zero;
                }
                elementType.GetMethod("ResetGemGroups", flags).Invoke(clonedElement, null);
                for (int i = 8; i <= 13; i++)
                    Assert.That(((Component) groups.GetValue(i)).transform.localScale, Is.Not.EqualTo(Vector3.zero));
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        [TestCase("OpenHiHatAccent")]
        [TestCase("OpenHiHatGhost")]
        [TestCase("ClosedHiHatAccent")]
        [TestCase("ClosedHiHatGhost")]
        public void PreviewWithoutDynamicsModelsFallsBackToTheThemesOrdinaryCymbal(string open)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(THEME_PATH);
            var component = prefab.GetComponent(Production("YARG.Themes.ThemeComponent"));
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var all = (IDictionary) component.GetType().GetMethod("GetNoteModelsForVisualStyle")
                .Invoke(component, new[] { style, (object) false });
            var models = Dictionary();
            models.Add(TypeValue("Normal"), all[TypeValue("Normal")]);
            models.Add(TypeValue("Cymbal"), all[TypeValue("Cymbal")]);
            var fakeType = Production("YARG.Settings.Preview.FakeNote");
            var preview = (GameObject) fakeType.GetMethod("CreateFakeNoteFromModels")
                .Invoke(null, new object[] { models, null });
            try
            {
                var fake = preview.GetComponent(fakeType);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var pairs = ((IEnumerable) fakeType.GetField("_noteGroups", flags).GetValue(fake)).Cast<object>();
                var cymbal = pairs.Single(p => p.GetType().GetField("NoteType").GetValue(p).ToString() == "Cymbal");
                var expected = cymbal.GetType().GetField("Group").GetValue(cymbal);
                foreach (bool star in new[] { false, true })
                    Assert.That(fakeType.GetMethod("FindNoteGroup", flags).Invoke(fake,
                        new[] { TypeValue(open), (object) star }), Is.SameAs(expected));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(preview);
            }
        }

        private static EliteDrumNote Note(EliteDrumPad pad, DrumNoteType dynamics = DrumNoteType.Neutral,
            EliteDrumsHatState hat = EliteDrumsHatState.Indifferent) => new(pad, dynamics, hat,
                EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None, NoteFlags.None,
                EliteDrumsChannelFlag.None, 1d, 480, false);

        private static IDictionary Dictionary() => (IDictionary) Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(NoteType, typeof(GameObject)));

        private static (IDictionary Regular, IDictionary Star) Resolve(IDictionary models, IDictionary stars)
        {
            var resolver = Type.GetType("YARG.Themes.ThemeNoteModelFallbacks, Assembly-CSharp");
            Assert.That(resolver, Is.Not.Null, "Optional open hi-hat slots need a compatible theme fallback.");
            var result = resolver.GetMethod("ResolveHiHatModels").Invoke(null, new object[] { models, stars });
            return ((IDictionary) result.GetType().GetField("Item1").GetValue(result),
                (IDictionary) result.GetType().GetField("Item2").GetValue(result));
        }
    }
}