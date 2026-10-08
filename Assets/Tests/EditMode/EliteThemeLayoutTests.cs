using System;
using System.Collections;
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
    public sealed class EliteThemeLayoutTests
    {
        private const string PATH = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static object Style(string name)
        {
            var type = Production("YARG.Themes.VisualStyle");
            Assert.That(Enum.IsDefined(type, name), Is.True, "Native Elite needs an independent model style.");
            return Enum.Parse(type, name);
        }
        private static object NoteType(string name) => Enum.Parse(Production("YARG.Themes.ThemeNoteType"), name);
        private static IDictionary Models(Component component, string style, bool star = false) =>
            (IDictionary) component.GetType().GetMethod("GetNoteModelsForVisualStyle")
                .Invoke(component, new[] { Style(style), (object) star });

        [Test]
        public void EliteModelsBelongToIndependentParentWithMirroredGalleryAndAlignedBars()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(PATH);
            var comp = root.GetComponent(Production("YARG.Themes.ThemeComponent"));
            var elite = Models(comp, "EliteDrums");
            var four = Models(comp, "FourLaneDrums");
            var five = Models(comp, "FiveLaneDrums");
            var parent = root.transform.Find("Elite Lane Notes");
            Assert.That(parent, Is.Not.Null);
            Assert.That(elite.Count, Is.EqualTo(15));
            foreach (DictionaryEntry entry in elite)
            {
                var model = (GameObject) entry.Value;
                Assert.That(model.transform.parent, Is.SameAs(parent));
                Assert.That(model.transform.localScale.x, Is.GreaterThan(0), "Mirror the gallery, never the mesh.");
                CollectionAssert.DoesNotContain(four.Values, model);
                CollectionAssert.DoesNotContain(five.Values, model);
            }
            foreach (string suffix in new[] { "", "Accent", "Ghost" })
            {
                string drum = suffix == "" ? "Normal" : suffix;
                var names = new[] { drum, "Cymbal" + suffix, "OpenHiHat" + suffix, "ClosedHiHat" + suffix };
                var reference = ((GameObject) four[NoteType(drum)]).transform.position;
                float previous = reference.x;
                foreach (string name in names)
                {
                    var position = ((GameObject) elite[NoteType(name)]).transform.position;
                    Assert.That(position.x, Is.LessThan(previous));
                    Assert.That(position.z, Is.EqualTo(reference.z).Within(0.01f));
                    previous = position.x;
                }
            }
            var kick = (GameObject) elite[NoteType("Kick")];
            var dedicated = (GameObject) elite[NoteType("DedicatedLaneKick")];
            var wildcard = (GameObject) elite[NoteType("Wildcard")];
            var star = Models(comp, "EliteDrums", true);
            var wildStar = (GameObject) star[NoteType("Wildcard")];
            Assert.That(kick.transform.position.z,
                Is.EqualTo(((GameObject) four[NoteType("Kick")]).transform.position.z));
            Assert.That(dedicated.transform.position.z, Is.LessThan(kick.transform.position.z));
            Assert.That(wildcard.transform.position.z, Is.LessThan(dedicated.transform.position.z));
            Assert.That(wildStar.transform.position.z, Is.LessThan(wildcard.transform.position.z));
            Assert.That(wildStar.transform.parent, Is.SameAs(parent));
            Assert.That(five.Contains(NoteType("OpenHiHat")), Is.False);
            Assert.That(four.Contains(NoteType("ClosedHiHat")), Is.False);
        }

        [Test]
        public void LegacyThemeRetainsItsOwnFiveLaneModelsForElite()
        {
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PATH));
            root.SetActive(false);
            try
            {
                var comp = root.GetComponent(Production("YARG.Themes.ThemeComponent"));
                var field = comp.GetType().GetField("_eliteLaneNotes", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                field.SetValue(comp, null);
                var five = Models(comp, "FiveLaneDrums");
                var elite = Models(comp, "EliteDrums");
                Assert.That(elite.Count, Is.EqualTo(five.Count));
                foreach (DictionaryEntry entry in five) Assert.That(elite[entry.Key], Is.SameAs(entry.Value));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void LegacySupportedStylesDoesNotReplaceCustomThemeForElite()
        {
            var root = new GameObject("Legacy theme selection");
            root.SetActive(false);
            try
            {
                var presetType = Production("YARG.Themes.ThemePreset");
                var preset = Activator.CreateInstance(presetType, "Legacy custom", false);
                ((IList) presetType.GetField("SupportedStyles").GetValue(preset)).Add(Style("FiveLaneDrums"));
                var containerType = Production("YARG.Themes.ThemeContainer");
                var container = Activator.CreateInstance(containerType, root, true, null);
                var manager = root.AddComponent(Production("YARG.Themes.ThemeManager"));
                var containers = (IDictionary) manager.GetType().GetField("_themeContainers",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
                containers.Add(preset, container);
                Assert.That(manager.GetType().GetMethod("GetThemeContainer")
                    .Invoke(manager, new[] { preset, Style("EliteDrums") }), Is.SameAs(container));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void ElitePreviewUsesDedicatedModelStyleWhileFretsRemainFiveLane()
        {
            var player = Production("YARG.Settings.Preview.FakeTrackPlayer");
            var infos = (IDictionary) player.GetField("_gameModeInfos", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var info = infos[Enum.Parse(typeof(YARG.Core.GameMode), "EliteDrums")];
            Assert.That(info.GetType().GetField("NoteVisualStyle").GetValue(info), Is.EqualTo(Style("EliteDrums")));
        }

        [TestCase(EliteDrumsColorRole.Stomp)]
        [TestCase(EliteDrumsColorRole.Splash)]
        public void ElitePedalPreviewUsesAuthoredDedicatedKick(EliteDrumsColorRole role)
        {
            var descriptor = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator")
                .GetMethod("Describe").Invoke(null, new[] { (object) role, NoteType("Normal") });
            Assert.That(descriptor.GetType().GetProperty("ModelType").GetValue(descriptor).ToString(),
                Is.EqualTo("DedicatedLaneKick"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ThemeManagerKeepsLegacyWildcardAndStarPowerGeometry(bool authoredWildcard)
        {
            var legacy = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PATH));
            legacy.SetActive(false);
            var managerRoot = new GameObject("Legacy Elite manager");
            managerRoot.SetActive(false);
            var template = new GameObject("Native Elite template");
            template.SetActive(false);
            try
            {
                var comp = legacy.GetComponent(Production("YARG.Themes.ThemeComponent"));
                comp.GetType().GetField("_eliteLaneNotes", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(comp, null);
                var models = Models(comp, "FiveLaneDrums");
                foreach (var note in legacy.GetComponentsInChildren(Production("YARG.Themes.ThemeNote")))
                    if (note.GetType().GetProperty("NoteType").GetValue(note).ToString() == "Wildcard" &&
                        ((bool) note.GetType().GetProperty("StarPowerVariant").GetValue(note) || !authoredWildcard))
                        UnityEngine.Object.DestroyImmediate(note.gameObject);
                var kick = (GameObject) models[NoteType("Kick")];
                var marker = new Vector3(0.14f, 0.24f, 0.34f);
                kick.GetComponentInChildren<MeshFilter>().transform.localScale = marker;
                if (authoredWildcard)
                    ((GameObject) models[NoteType("Wildcard")]).GetComponentInChildren<MeshFilter>().transform.localScale = marker;
                var starKick = UnityEngine.Object.Instantiate(kick, kick.transform.parent);
                var starMarker = new Vector3(0.51f, 0.61f, 0.71f);
                starKick.GetComponentInChildren<MeshFilter>().transform.localScale = starMarker;
                var noteComp = starKick.GetComponent(Production("YARG.Themes.ThemeNote"));
                var serialized = new SerializedObject(noteComp);
                serialized.FindProperty("<StarPowerVariant>k__BackingField").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var manager = managerRoot.AddComponent(Production("YARG.Themes.ThemeManager"));
                var presetType = Production("YARG.Themes.ThemePreset");
                var preset = Activator.CreateInstance(presetType, "Legacy fallback", false);
                ((IList) presetType.GetField("SupportedStyles").GetValue(preset)).Add(Style("FiveLaneDrums"));
                var containerType = Production("YARG.Themes.ThemeContainer");
                var container = Activator.CreateInstance(containerType, legacy, true, null);
                var containers = (IDictionary) manager.GetType().GetField("_themeContainers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
                containers.Add(preset, container);
                manager.GetType().GetField("_defaultTheme", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, Activator.CreateInstance(containerType, AssetDatabase.LoadAssetAtPath<GameObject>(PATH), true, null));
                var elementType = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
                template.AddComponent(elementType);
                var themed = (GameObject) manager.GetType().GetMethod("CreateNotePrefabFromTheme")
                    .Invoke(manager, new[] { preset, Style("EliteDrums"), template, (object) "NativeElite" });
                var element = themed.GetComponent(elementType);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var groups = (Array) elementType.BaseType.GetField("NoteGroups", flags).GetValue(element);
                var stars = (Array) elementType.BaseType.GetField("StarPowerNoteGroups", flags).GetValue(element);
                Assert.That(((Component) groups.GetValue(14)).GetComponentInChildren<MeshFilter>().transform.localScale,
                    Is.EqualTo(marker), "Default fill must not replace the custom regular silhouette.");
                Assert.That(((Component) stars.GetValue(14)).GetComponentInChildren<MeshFilter>().transform.localScale,
                    Is.EqualTo(authoredWildcard ? marker : starMarker), "Keep authored wildcard shape, or the theme’s own SP kick fallback.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(template);
                UnityEngine.Object.DestroyImmediate(managerRoot);
                UnityEngine.Object.DestroyImmediate(legacy);
            }
        }

        [Test]
        public void ElitePreviewFactoryFitsEachRegularAndStarPowerBarToTheHighway()
        {
            var managerRoot = new GameObject("Elite preview theme manager");
            managerRoot.SetActive(false);
            GameObject preview = null;
            var managerType = Production("YARG.Themes.ThemeManager");
            var instanceField = managerType.BaseType.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = instanceField.GetValue(null);
            try
            {
                var manager = managerRoot.AddComponent(managerType);
                instanceField.SetValue(null, manager);
                var presetType = Production("YARG.Themes.ThemePreset");
                var preset = presetType.GetField("Default").GetValue(null);
                var container = Activator.CreateInstance(Production("YARG.Themes.ThemeContainer"),
                    AssetDatabase.LoadAssetAtPath<GameObject>(PATH), true, null);
                ((IDictionary) managerType.GetField("_themeContainers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(manager)).Add(preset, container);
                var fakeType = Production("YARG.Settings.Preview.FakeNote");
                preview = (GameObject) fakeType.GetMethod("CreateFakeNoteFromTheme")
                    .Invoke(null, new[] { preset, Style("EliteDrums") });
                var fake = preview.GetComponent(fakeType);
                foreach (string type in new[] { "Kick", "DedicatedLaneKick", "Wildcard" })
                    foreach (bool star in new[] { false, true })
                    {
                        var group = (Component) fakeType.GetMethod("FindNoteGroup", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(fake, new[] { NoteType(type), (object) star });
                        var renderers = group.GetComponentsInChildren<MeshRenderer>();
                        var bounds = renderers[0].bounds;
                        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                        Assert.That(bounds.size.x, Is.EqualTo(2f).Within(0.001f), $"{type} SP={star}: descriptor widths scale a full-highway baseline.");
                    }
            }
            finally
            {
                instanceField.SetValue(null, previous);
                if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
                UnityEngine.Object.DestroyImmediate(managerRoot);
            }
        }

        [Test]
        public void NativeWildcardUsesItsAuthoredModelInsteadOfKick()
        {
            var note = new EliteDrumNote(EliteDrumPad.Wildcard, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, 1d, 480, false);
            var select = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement").GetMethod("GetGemGroup",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(select.Invoke(null, new object[] { note, true }), Is.EqualTo(14));
        }
    }
}
