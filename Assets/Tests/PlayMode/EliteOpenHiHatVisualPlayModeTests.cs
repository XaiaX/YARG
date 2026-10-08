using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

// pattern: Imperative Shell

namespace YARG.Tests.PlayMode
{
    public sealed class EliteOpenHiHatVisualPlayModeTests
    {
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        [UnityTest]
        public IEnumerator ActualRectangularCymbalsRecolorTopsWhileBasesRemainNeutral()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab");
            var theme = prefab.GetComponent(Production("YARG.Themes.ThemeComponent"));
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var models = (IDictionary) theme.GetType().GetMethod("GetNoteModelsForVisualStyle")
                .Invoke(theme, new[] { style, (object) false });
            var groupType = Production("YARG.Gameplay.Visuals.NoteGroup");
            var parent = new GameObject("Open hi-hat runtime material test");
            parent.SetActive(false);
            try
            {
                foreach (string name in new[] { "Cymbal", "CymbalAccent", "CymbalGhost",
                    "OpenHiHat", "OpenHiHatAccent", "OpenHiHatGhost",
                    "ClosedHiHat", "ClosedHiHatAccent", "ClosedHiHatGhost" })
                {
                    var type = Enum.Parse(Production("YARG.Themes.ThemeNoteType"), name);
                    var group = (Component) groupType.GetMethod("CreateNoteGroupFromTheme")
                        .Invoke(null, new[] { (object) parent.transform, models[type] });
                    groupType.GetMethod("Initialize").Invoke(group, null);
                    var baseRenderer = group.transform.GetComponentsInChildren<MeshRenderer>(true)
                        .SingleOrDefault(r => r.name == "Base");
                    var baseColors = baseRenderer == null ? null : baseRenderer.sharedMaterials.Select(m => m.color).ToArray();
                    foreach (var color in new[] { Color.white, new Color(0.9f, 0.8f, 0.75f) })
                    {
                        groupType.GetMethod("SetColorWithEmission").Invoke(group, new object[] { color, Color.yellow });
                        groupType.GetMethod("SetMetalColor").Invoke(group, new object[] { Color.magenta });
                        if (baseRenderer != null) Assert.That(baseRenderer.sharedMaterials.Select(m => m.color).ToArray(), Is.EqualTo(baseColors),
                            $"{name}: chrome base must remain neutral through lane and Star Power recoloring.");
                        var note = group.GetComponentInChildren(Production("YARG.Themes.ThemeNote"), true);
                        foreach (string field in new[] { "ColoredMaterials", "ColoredMetalMaterials" })
                        {
                            var entries = ((IEnumerable) note.GetType().GetProperty(field).GetValue(note)).Cast<object>();
                            foreach (var entry in entries)
                            {
                                var renderer = (MeshRenderer) entry.GetType().GetField("Mesh").GetValue(entry);
                                int index = (int) entry.GetType().GetField("MaterialIndex").GetValue(entry);
                                float addition = (float) entry.GetType().GetField("EmissionAddition").GetValue(entry);
                                var expected = field == "ColoredMetalMaterials" ? Color.magenta
                                    : color + new Color(addition, addition, addition);
                                Assert.That(Vector4.Distance(renderer.sharedMaterials[index].color, expected),
                                    Is.LessThan(0.00001f), $"{name}: the top must update {field}.");
                            }
                        }
                    }
                }
                yield return null;
            }
            finally
            {
                foreach (var renderer in parent.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var material in renderer.sharedMaterials.Distinct())
                        if (!AssetDatabase.Contains(material)) UnityEngine.Object.Destroy(material);
                UnityEngine.Object.Destroy(parent);
            }
            yield return null;
        }
    }
}