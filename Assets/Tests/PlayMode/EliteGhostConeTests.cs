// pattern: Imperative Shell
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteGhostConeTests
    {
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        [TestCase("CymbalGhost")]
        [TestCase("ClosedHiHatGhost")]
        [TestCase("OpenHiHatGhost")]
        public void GhostRegionsStayDarkWhenRuntimeAppliesNoteOrStarPowerColor(string modelName)
        {
            var theme = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab")
                .GetComponent(Production("YARG.Themes.ThemeComponent"));
            var models = (IDictionary)theme.GetType().GetMethod("GetNoteModelsForVisualStyle")
                .Invoke(theme, new[] { Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums"), (object)false });
            var source = (GameObject)models[Enum.Parse(Production("YARG.Themes.ThemeNoteType"), modelName)];
            var parent = new GameObject("Owned ghost cone color check");
            var groupType = Production("YARG.Gameplay.Visuals.NoteGroup");
            try
            {
                var group = (Component)groupType.GetMethod("CreateNoteGroupFromTheme")
                    .Invoke(null, new object[] { parent.transform, source });
                groupType.GetMethod("Initialize").Invoke(group, null);
                var top = group.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Top");
                var darkRegions = group.GetComponentsInChildren<MeshRenderer>()
                    .SelectMany(r => r.sharedMaterials).Where(m => m.name.StartsWith("CymbalGhostCenter")).ToArray();
                Assert.That(darkRegions.Length, Is.GreaterThanOrEqualTo(modelName == "ClosedHiHatGhost" ? 1 : 2));
                var surround = top.sharedMaterials.Single(m => m.name.StartsWith("CymbalMiddle"));
                Color? previous = null;
                foreach (var input in new[] { new Color(.9f, .8f, .75f), new Color(.75f, .85f, 1f) })
                {
                    // Gameplay supplies either note RGB or SP RGB through this same production method.
                    groupType.GetMethod("SetColorWithEmission").Invoke(group, new object[] { input, input });
                    foreach (var center in darkRegions)
                    {
                        Assert.That(Vector3.Distance(new Vector3(center.color.r, center.color.g, center.color.b),
                            new Vector3(input.r - .7f, input.g - .7f, input.b - .7f)), Is.LessThan(.00001f),
                            "Every dark region must receive the same attenuated note RGB.");
                        Assert.That(center.color.maxColorComponent, Is.LessThanOrEqualTo(.31f),
                            "Assigning a ghost material alone does not darken the runtime-overridden body color.");
                        Assert.That(new Vector3(surround.color.r, surround.color.g, surround.color.b),
                            Is.EqualTo(new Vector3(input.r, input.g, input.b)), "The surrounding bowl must retain full note RGB.");
                        Assert.That(center.GetColor("_EmissionColor").maxColorComponent, Is.EqualTo(0f),
                            "The dark cone must not glow back toward full brightness.");
                        if (previous.HasValue) Assert.That(center.color, Is.Not.EqualTo(previous.Value),
                            "The dark cone must still respond to note/Star Power RGB, rather than become fixed black.");
                    }
                    previous = darkRegions[0].color;
                }
            }
            finally
            {
                foreach (var material in parent.GetComponentsInChildren<MeshRenderer>(true)
                    .SelectMany(r => r.sharedMaterials).Distinct())
                    if (material != null && !AssetDatabase.Contains(material)) UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }
    }
}
