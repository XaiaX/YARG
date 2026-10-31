// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteClosedHatMaterialTests
    {
        private const string FOLDER = "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/";
        private const string DARK_PATH = "Assets/Art/Materials/Gameplay/Notes/Rectangular/EliteHiHatDark.mat";
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        private static GameObject ThemeModel(string name)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab");
            var theme = root.GetComponent(Production("YARG.Themes.ThemeComponent"));
            var models = (IDictionary)theme.GetType().GetMethod("GetNoteModelsForVisualStyle")
                .Invoke(theme, new object[] { Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums"), false });
            return (GameObject)models[Enum.Parse(Production("YARG.Themes.ThemeNoteType"), name)];
        }

        private static int[] AuthoredDarkSlots(string name)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FOLDER + name + ".prefab");
            var top = model.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Top");
            var dark = AssetDatabase.LoadAssetAtPath<Material>(DARK_PATH);
            return top.sharedMaterials.Select((material, index) => (material, index))
                .Where(entry => entry.material == dark).Select(entry => entry.index).ToArray();
        }

        [TestCase("ClosedHiHatGhost")]
        [TestCase("ClosedHiHatAccent")]
        public void GameplayThemeRetainsAuthoredDarkRimMaterial(string name)
        {
            var dark = AssetDatabase.LoadAssetAtPath<Material>(DARK_PATH);
            var top = ThemeModel(name).GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Top");
            var slots = AuthoredDarkSlots(name);
            Assert.That(slots, Is.Not.Empty, "The model must include its authored fixed-dark Top region.");
            foreach (int slot in slots)
                Assert.That(top.sharedMaterials[slot], Is.SameAs(dark), name + " theme rim material");
        }

        [TestCase("ClosedHiHat")]
        public void ClosedHatSurfaceRetainsConeShineCoordinates(string name)
        {
            var model = ThemeModel(name);
            var top = model.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Top");
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Materials/Gameplay/Notes/Rectangular/EliteCymbalMiddleInvertedLayers.mat");
            int slot = name == "ClosedHiHat" ? 1 : Array.IndexOf(top.sharedMaterials, material);
            Assert.That(top.sharedMaterials[slot], Is.SameAs(material), "colored top surface material");
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));
            var mesh = top.GetComponent<MeshFilter>().sharedMesh;
            var uv = mesh.uv;
            float maximumRadius = mesh.GetTriangles(slot).Distinct()
                .Max(index => Vector2.Distance(uv[index], new Vector2(.5f, .5f)));
            var surfaceUV = mesh.uv2;
            Assert.That(surfaceUV.Length, Is.EqualTo(mesh.vertexCount), "independent full-disc color coordinates");
            var surfaceRadii = mesh.GetTriangles(slot).Distinct()
                .Select(index => Vector2.Distance(surfaceUV[index], new Vector2(.5f, .5f))).ToArray();
            Assert.That(surfaceRadii.Max(), Is.GreaterThan(name == "ClosedHiHatAccent" ? .37f : .45f),
                "color texture spans the intended surface inside the dark outer band");
            Assert.That(surfaceRadii.Max(), Is.LessThanOrEqualTo(.50001f));
            if (name == "ClosedHiHat")
                Assert.That(surfaceRadii.Min(), Is.LessThan(.00001f), "full-disc color texture is centered");
            // The cone ends at UV radius .25; leave a small margin before its white ring.
            Assert.That(maximumRadius, Is.LessThanOrEqualTo(.23501f), name + " colored top texture footprint");
        }

        [TestCase("ClosedHiHatGhost", false)]
        [TestCase("ClosedHiHatGhost", true)]
        [TestCase("ClosedHiHatAccent", false)]
        [TestCase("ClosedHiHatAccent", true)]
        public void GameplayColorUpdatesKeepAuthoredRimDark(string name, bool starPower)
        {
            var owned = new GameObject("Owned closed-hat color test");
            var ownedMaterials = new HashSet<Material>();
            try
            {
                var groupType = Production("YARG.Gameplay.Visuals.NoteGroup");
                var group = (Component)groupType.GetMethod("CreateNoteGroupFromTheme", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { owned.transform, ThemeModel(name) });
                var top = owned.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Top");
                var dark = AssetDatabase.LoadAssetAtPath<Material>(DARK_PATH);
                var expected = dark.color;
                var expectedEmission = dark.GetColor("_EmissionColor");
                groupType.GetMethod("Initialize").Invoke(group, null);
                foreach (var renderer in owned.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && !EditorUtility.IsPersistent(material)) ownedMaterials.Add(material);
                groupType.GetMethod("SetColorWithEmission").Invoke(group, new object[] { Color.cyan, Color.magenta });
                groupType.GetMethod("SetMetalColor").Invoke(group,
                    new object[] { starPower ? new Color(1f, .8f, .1f) : Color.white });
                var darkSlots = AuthoredDarkSlots(name);
                Assert.That(darkSlots, Is.Not.Empty, name + " fixed-dark Top region");
                foreach (int slot in darkSlots)
                {
                    var material = top.sharedMaterials[slot];
                    Debug.Log($"[Closed hat rim] {name}; SP={starPower}; slot={slot}; material={material.name}; before={expected}; after={material.color}");
                    Assert.That(Vector4.Distance(material.color, expected), Is.LessThan(.00001f), name + " rim tint");
                    Assert.That(Vector4.Distance(material.GetColor("_EmissionColor"), expectedEmission),
                        Is.LessThan(.00001f), name + " rim emission");
                }
            }
            finally
            {
                Object.DestroyImmediate(owned);
                foreach (var material in ownedMaterials) Object.DestroyImmediate(material);
            }
        }

        [TestCase("Cymbal", 2)]
        [TestCase("OpenHiHat", 3)]
        public void NeutralCymbalPeakUsesExistingAccentChromeMaterial(string name, int peakSlot)
        {
            var metal = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Materials/Gameplay/Notes/Rectangular/CymbalMetal.mat");
            var authored = AssetDatabase.LoadAssetAtPath<GameObject>(FOLDER + name + ".prefab");
            foreach (var model in new[] { authored, ThemeModel(name) })
            {
                var top = model.GetComponentsInChildren<MeshRenderer>(true).Single(renderer => renderer.name == "Top");
                Assert.That(top.sharedMaterials.Length, Is.GreaterThan(peakSlot), "separate small chrome peak slot");
                Assert.That(top.sharedMaterials[peakSlot], Is.SameAs(metal), "small neutral cymbal chrome peak");
            }
            var accent = ThemeModel(name + "Accent").GetComponentsInChildren<MeshRenderer>(true)
                .Single(renderer => renderer.name == "Top");
            Assert.That(accent.sharedMaterials, Does.Contain(metal), "same chrome as the accent peak");
        }

        [TestCase("Cymbal", 2, 0, false)]
        [TestCase("Cymbal", 2, 0, true)]
        [TestCase("OpenHiHat", 3, 1, false)]
        [TestCase("OpenHiHat", 3, 1, true)]
        public void NeutralCymbalPeakFollowsMetalPalette(string name, int peakSlot, int rimSlot, bool starPower)
        {
            var owned = new GameObject("Owned neutral cymbal chrome palette test");
            var ownedMaterials = new HashSet<Material>();
            try
            {
                var groupType = Production("YARG.Gameplay.Visuals.NoteGroup");
                var group = (Component)groupType.GetMethod("CreateNoteGroupFromTheme", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { owned.transform, ThemeModel(name) });
                var top = owned.GetComponentsInChildren<MeshRenderer>(true).Single(renderer => renderer.name == "Top");
                groupType.GetMethod("Initialize").Invoke(group, null);
                foreach (var renderer in owned.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && !EditorUtility.IsPersistent(material)) ownedMaterials.Add(material);
                groupType.GetMethod("SetColorWithEmission").Invoke(group, new object[] { Color.cyan, Color.magenta });
                var expected = starPower ? new Color(.9f, .7f, .1f) : new Color(.2f, .6f, .8f);
                groupType.GetMethod("SetMetalColor").Invoke(group, new object[] { expected });
                Assert.That(top.sharedMaterials.Length, Is.GreaterThan(peakSlot), "separate small chrome peak slot");
                Assert.That(Vector4.Distance(top.sharedMaterials[peakSlot].color, expected), Is.LessThan(.00001f),
                    "peak follows the profile metal color");
                Assert.That(Vector4.Distance(top.sharedMaterials[peakSlot].GetColor("_EmissionColor"), expected),
                    Is.LessThan(.00001f), "peak follows the profile metal emission color");
                Assert.That(Vector4.Distance(top.sharedMaterials[peakSlot].color, top.sharedMaterials[rimSlot].color),
                    Is.LessThan(.00001f), "peak and chrome rim use the same metal palette");
            }
            finally
            {
                Object.DestroyImmediate(owned);
                foreach (var material in ownedMaterials) Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ClosedHatRimAndCenterMaterialLayoutMatchesDesign()
        {
            var body = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Materials/Gameplay/Notes/Rectangular/EliteCymbalMiddleInvertedLayers.mat");
            var glow = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Materials/Gameplay/Notes/Rectangular/EliteAccentHOPOGlow.mat");
            var dark = AssetDatabase.LoadAssetAtPath<Material>(DARK_PATH);
            foreach (bool authored in new[] { false, true })
            {
                var normal = authored ? AssetDatabase.LoadAssetAtPath<GameObject>(FOLDER + "ClosedHiHat.prefab") : ThemeModel("ClosedHiHat");
                var accent = authored ? AssetDatabase.LoadAssetAtPath<GameObject>(FOLDER + "ClosedHiHatAccent.prefab") : ThemeModel("ClosedHiHatAccent");
                var normalTop = normal.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Top");
                var accentTop = accent.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Top");
                Assert.That(normalTop.sharedMaterials[0], Is.SameAs(body), "neutral outer rim");
                Assert.That(normalTop.sharedMaterials[1], Is.SameAs(body), "neutral colored top");
                var bubble = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Art/Materials/Gameplay/Notes/Rectangular/CymbalMiddle.mat");
                Assert.That(accentTop.sharedMaterials, Is.EquivalentTo(new[] { glow, glow, dark, bubble }),
                    "ghost-derived white center/rim, fixed-dark borders and original colored bubble surface");
                var mesh = accentTop.GetComponent<MeshFilter>().sharedMesh;
                var uv = mesh.uv2;
                var glowRadii = accentTop.sharedMaterials.Select((material, slot) => (material, slot))
                    .Where(entry => entry.material == glow)
                    .Select(entry => mesh.GetTriangles(entry.slot).Distinct()
                        .Select(index => Vector2.Distance(uv[index], new Vector2(.5f,.5f)) * 2f).ToArray()).ToArray();
                Assert.That(glowRadii, Has.Length.EqualTo(2));
                Assert.That(glowRadii.Count(radii => radii.Max() <= .51501f), Is.EqualTo(1), "white center inside dark outline");
                Assert.That(glowRadii.Count(radii => radii.Min() >= .96f * Mathf.Cos(Mathf.PI/32f) - .00001f), Is.EqualTo(1), "white outer rim below the dark seam");
                int darkSlot = Array.IndexOf(accentTop.sharedMaterials, dark);
                var darkRadii = mesh.GetTriangles(darkSlot).Distinct()
                    .Select(index => Vector2.Distance(uv[index], new Vector2(.5f,.5f)) * 2f).ToArray();
                Assert.That(darkRadii.Min(), Is.InRange(.514f,.516f), "dark ring starts at the white center border");
                Assert.That(darkRadii.Max(), Is.GreaterThan(.99f), "dark seam continues over the upper rim edge");
            }
        }

        [TestCase("ClosedHiHat", false)]
        [TestCase("ClosedHiHat", true)]
        [TestCase("ClosedHiHatAccent", false)]
        [TestCase("ClosedHiHatAccent", true)]
        public void ClosedHatRimFollowsItsIntendedPalette(string name, bool starPower)
        {
            var owned = new GameObject("Owned closed-hat rim palette test");
            var ownedMaterials = new HashSet<Material>();
            try
            {
                var type = Production("YARG.Gameplay.Visuals.NoteGroup");
                var group = (Component)type.GetMethod("CreateNoteGroupFromTheme", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { owned.transform, ThemeModel(name) });
                var top = owned.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Top");
                var authored = AssetDatabase.LoadAssetAtPath<GameObject>(FOLDER + name + ".prefab")
                    .GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Top");
                var expectedRimMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Art/Materials/Gameplay/Notes/Rectangular/" + (name == "ClosedHiHat" ?
                        "EliteCymbalMiddleInvertedLayers.mat" : "EliteAccentHOPOGlow.mat"));
                int rimSlot = Array.IndexOf(authored.sharedMaterials, expectedRimMaterial);
                Assert.That(rimSlot, Is.GreaterThanOrEqualTo(0), "authored colored/glowing rim slot");
                type.GetMethod("Initialize").Invoke(group, null);
                foreach (var renderer in owned.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && !EditorUtility.IsPersistent(material)) ownedMaterials.Add(material);
                var color = starPower ? new Color(.1f, .7f, .9f) : new Color(.8f, .3f, .05f);
                var colorNoStarPower = new Color(.2f, .4f, .6f);
                var metal = starPower ? new Color(.9f, .7f, .1f) : new Color(.7f, .8f, .9f);
                type.GetMethod("SetColorWithEmission").Invoke(group, new object[] { color, colorNoStarPower });
                type.GetMethod("SetMetalColor").Invoke(group, new object[] { metal });
                var expected = name == "ClosedHiHat" ? color : metal;
                var expectedEmission = expected;
                var actualColor = top.sharedMaterials[rimSlot].color;
                Assert.That(Vector3.Distance(new Vector3(actualColor.r, actualColor.g, actualColor.b),
                    new Vector3(expected.r, expected.g, expected.b)), Is.LessThan(.00001f), "rim palette");
                var actualEmission = top.sharedMaterials[rimSlot].GetColor("_EmissionColor");
                Assert.That(Vector3.Distance(new Vector3(actualEmission.r, actualEmission.g, actualEmission.b),
                    new Vector3(expectedEmission.r, expectedEmission.g, expectedEmission.b)),
                    Is.LessThan(.00001f), "rim emission palette");
                if (name == "ClosedHiHat")
                {
                    Assert.That(Vector4.Distance(actualEmission, top.sharedMaterials[1].GetColor("_EmissionColor")),
                        Is.LessThan(.00001f), "rim and matching top emission, including alpha");
                    Assert.That(Vector4.Distance(top.sharedMaterials[0].color, top.sharedMaterials[1].color),
                        Is.LessThan(.00001f), "rim and its matching top region");
                }
                if (name == "ClosedHiHatAccent")
                {
                    var dark = AssetDatabase.LoadAssetAtPath<Material>(DARK_PATH);
                    int darkSlot = Array.IndexOf(AssetDatabase.LoadAssetAtPath<GameObject>(FOLDER + name + ".prefab")
                        .GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Top").sharedMaterials, dark);
                    Assert.That(darkSlot, Is.GreaterThanOrEqualTo(0));
                    Assert.That(Vector4.Distance(top.sharedMaterials[darkSlot].color, dark.color), Is.LessThan(.00001f),
                        "accent center outline and top/rim seam are unaffected by the metal palette");
                    foreach (int slot in authored.sharedMaterials.Select((material, index) => (material,index))
                        .Where(entry => entry.material == expectedRimMaterial).Select(entry => entry.index))
                    {
                        Assert.That(Vector4.Distance(top.sharedMaterials[slot].color, metal), Is.LessThan(.00001f),
                            "both center and rim follow the profile metal color");
                        Assert.That(Vector4.Distance(top.sharedMaterials[slot].GetColor("_EmissionColor"), metal),
                            Is.LessThan(.00001f), "both white regions retain reactive emission");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(owned);
                foreach (var material in ownedMaterials) Object.DestroyImmediate(material);
            }
        }
    }
}