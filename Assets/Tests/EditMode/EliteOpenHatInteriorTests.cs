// pattern: Imperative Shell
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class EliteOpenHatInteriorTests
    {
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        [TestCase("OpenHiHat")]
        [TestCase("OpenHiHatGhost")]
        [TestCase("OpenHiHatAccent")]
        public void ExposedInteriorAndTapGlowStayOnTheBaseAndFollowNoteColor(string type)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab");
            var theme = root.GetComponent(Production("YARG.Themes.ThemeComponent"));
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var noteType = Enum.Parse(Production("YARG.Themes.ThemeNoteType"), type);
            var models = (IDictionary)theme.GetType().GetMethod("GetNoteModelsForVisualStyle")
                .Invoke(theme, new[] { style, (object)false });
            var model = (GameObject)models[noteType];
            var renderers = model.GetComponentsInChildren<MeshRenderer>();
            var baseRenderer = renderers.Single(r => r.name == "Base");
            var glow = renderers.SingleOrDefault(r => r.name == "Base Glow");
            Assert.That(glow, Is.Not.Null, "Open hats need a stationary tap-style glow over the colored inset.");
            var top = renderers.Single(r => r.name == "Top");
            Assert.That(glow.transform.IsChildOf(top.transform.parent), Is.False,
                "The recessed interior must not sway with the upper cymbal.");
            var materials = baseRenderer.sharedMaterials.Select(m => m.name).ToArray();
            Assert.That(materials, Does.Contain("EliteOpenHatInterior"));
            Assert.That(materials, Does.Contain("EliteHiHatDark"));
            Assert.That(materials, Does.Contain("CymbalMetal"));
            var tap = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Materials/Gameplay/Notes/Circular/TapNoteInside.mat");
            Assert.That(glow.sharedMaterial.shader, Is.SameAs(tap.shader));
            Assert.That(glow.sharedMaterial.GetFloat("_Emission_Strength"), Is.GreaterThan(1));
            var note = model.GetComponent(Production("YARG.Themes.ThemeNote"));
            var colored = ((IEnumerable)note.GetType().GetProperty("ColoredMaterials").GetValue(note)).Cast<object>().ToArray();
            foreach (var renderer in new[] { baseRenderer, glow })
            for (int i = 0; i < renderer.sharedMaterials.Length; i++)
            {
                string name = renderer.sharedMaterials[i].name;
                bool registered = colored.Any(e => (MeshRenderer)e.GetType().GetField("Mesh").GetValue(e) == renderer
                    && (int)e.GetType().GetField("MaterialIndex").GetValue(e) == i);
                Assert.That(registered, Is.EqualTo(name == "EliteOpenHatInterior" || name == "EliteOpenHatGlow"),
                    "Only the interior and glow should take lane color; the dark border stays fixed.");
            }
        }
    }
}
