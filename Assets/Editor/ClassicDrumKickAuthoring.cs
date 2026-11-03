// pattern: Imperative Shell
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YARG.Themes;
using YARG.Gameplay.Visuals;

namespace YARG.Editor
{
    /// <summary>Copies neutral Elite kick-flam assemblies into the classic galleries.</summary>
    public static class ClassicDrumKickAuthoring
    {
        private const string THEME_PATH = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";

        [MenuItem("YARG/Authoring/Add classic kick flams")]
        public static void Build()
        {
            var root = PrefabUtility.LoadPrefabContents(THEME_PATH);
            try
            {
                var theme = root.GetComponent<ThemeComponent>();
                var source = theme.GetNoteModelsForVisualStyle(VisualStyle.EliteDrums, false);
                foreach (var style in new[] { VisualStyle.FourLaneDrums, VisualStyle.FiveLaneDrums })
                {
                    var gallery = root.transform.Find(style == VisualStyle.FourLaneDrums
                        ? "Four Lane Notes" : "Five Lane Notes");
                    var models = theme.GetNoteModelsForVisualStyle(style, false);
                    foreach (var type in new[] { ThemeNoteType.KickFlam, ThemeNoteType.DedicatedLaneKickFlam })
                    {
                        // A rerun never overwrites a later authored/editor experiment.
                        if (models.ContainsKey(type)) continue;
                        var baselineType = EliteKickAppearance.IsDedicated(type)
                            ? ThemeNoteType.DedicatedLaneKick : ThemeNoteType.Kick;
                        if (!source.TryGetValue(type, out var original) || !models.TryGetValue(baselineType, out var baseline))
                            throw new InvalidOperationException($"Missing kick assembly or baseline for {style}/{type}");
                        var copy = UnityEngine.Object.Instantiate(original, gallery, false);
                        copy.name = type.ToString();
                        copy.transform.localPosition = baseline.transform.localPosition + new Vector3(-6f, 0f, 0f);
                        float width = Width(copy.transform);
                        float expected = Width(baseline.transform);
                        if (width <= 0f || expected <= 0f)
                            throw new InvalidOperationException($"Empty kick geometry for {style}/{type}");
                        var scale = copy.transform.localScale;
                        copy.transform.localScale = new Vector3(scale.x * expected / width, scale.y, scale.z);
                        if (type == ThemeNoteType.KickFlam) BindSplitGeometry(copy);
                        models.Add(type, copy);
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, THEME_PATH);
                AssetDatabase.SaveAssets();
                Debug.Log("CLASSIC_DRUM_KICK_FLAM_AUTHORING_COMPLETE");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void BindSplitGeometry(GameObject model)
        {
            float Center(MeshFilter part) => model.transform.InverseTransformPoint(
                part.transform.TransformPoint(part.sharedMesh.bounds.center)).x;
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            var beams = filters.Where(part => part.name.Contains("beam")).OrderBy(Center).ToArray();
            var housings = filters.Where(part => part.name.Contains("housing")).OrderBy(Center).ToArray();
            if (beams.Length != 2 || housings.Length != 4)
                throw new InvalidOperationException("The authored split kick requires two beams and four housings.");
            model.AddComponent<ClassicKickFlamVisual>().SetAuthoredParts(new[]
            {
                housings[0], housings[1], beams[0], housings[3], housings[2], beams[1]
            });
        }

        private static float Width(Transform model)
        {
            var points = model.GetComponentsInChildren<MeshFilter>(true).SelectMany(filter =>
            {
                var bounds = filter.sharedMesh.bounds;
                var matrix = model.parent.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                return Enumerable.Range(0, 8).Select(index => matrix.MultiplyPoint3x4(bounds.center
                    + Vector3.Scale(bounds.extents, new Vector3((index & 1) == 0 ? -1f : 1f,
                        (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f))));
            }).ToArray();
            return points.Length == 0 ? 0f : points.Max(point => point.x) - points.Min(point => point.x);
        }
    }
}
