using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YARG.Themes;

namespace YARG.Editor
{
    /// <summary>Reports the authored Elite cymbal-accent renderer's fifth material without changing assets.</summary>
    public static class EliteCymbalAccentMaterialDiagnostics
    {
        public const string THEME_PATH = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";
        private const int FIFTH_MATERIAL_INDEX = 4;

        [MenuItem("YARG/Diagnose Elite Cymbal Accent Material")]
        public static void Diagnose()
        {
            var contents = PrefabUtility.LoadPrefabContents(THEME_PATH);
            try
            {
                var theme = contents.GetComponent<ThemeComponent>();
                if (theme == null)
                    throw new InvalidOperationException($"{THEME_PATH} has no ThemeComponent.");

                foreach (bool starPower in new[] { false, true })
                {
                    var models = theme.GetNoteModelsForVisualStyle(VisualStyle.EliteDrums, starPower);
                    if (!models.TryGetValue(ThemeNoteType.CymbalAccent, out var accent) || accent == null)
                    {
                        Debug.LogWarning($"Elite CymbalAccent model is missing (star power: {starPower}).", contents);
                        continue;
                    }

                    foreach (var renderer in accent.GetComponentsInChildren<Renderer>(true))
                    {
                        var materials = renderer.sharedMaterials;
                        string slot = materials.Length > FIFTH_MATERIAL_INDEX
                            ? materials[FIFTH_MATERIAL_INDEX] == null
                                ? "<null>"
                                : $"{materials[FIFTH_MATERIAL_INDEX].name} / {materials[FIFTH_MATERIAL_INDEX].shader?.name}"
                            : "<no fifth slot>";
                        Debug.Log(
                            $"Elite CymbalAccent starPower={starPower}; group={accent.name}; renderer={renderer.name}; " +
                            $"fifthMaterial={slot}; materials=[{string.Join(", ", materials.Select(material => material == null ? "<null>" : material.name))}]",
                            renderer);
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
