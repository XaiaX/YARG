// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YARG.Themes;
using Object = UnityEngine.Object;

namespace YARG.Editor
{
    // A transient editor sample owns the meshes and pulse material created for its requested size.
    // Example: using var sample = PillCymbalFlamVelocity.CreateSample(ThemeNoteType.OpenHiHat, 50);
    // Position sample.Root in a comparison scene and dispose it after rendering.
    public static class PillCymbalFlamVelocity
    {
        private const string HATS = "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/Volume";
        private const string MATERIALS = "Assets/Art/Materials/Gameplay/Notes/Rectangular/";

        public sealed class Sample : IDisposable
        {
            public GameObject Root { get; }
            private readonly List<Object> _owned = new();
            internal Sample(GameObject root) => Root = root;
            internal T Own<T>(T asset) where T : Object
            {
                _owned.Add(asset);
                return asset;
            }
            public void Dispose()
            {
                if (Root) Object.DestroyImmediate(Root);
                foreach (var asset in _owned)
                    if (asset) Object.DestroyImmediate(asset);
                _owned.Clear();
            }
        }

        private static GameObject Single(ThemeNoteType type, string suffix) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(HATS + type + suffix + ".prefab");

        private static MeshFilter Top(GameObject model) =>
            model.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "Top");

        // Study size independently of markers. Gameplay prefabs supply the three discrete decorated models.
        public static Sample CreateSample(ThemeNoteType articulation, float velocity)
        {
            if (articulation != ThemeNoteType.Cymbal && articulation != ThemeNoteType.OpenHiHat &&
                articulation != ThemeNoteType.ClosedHiHat)
                throw new ArgumentOutOfRangeException(nameof(articulation));
            if (!(velocity >= 1 && velocity <= 127))
                throw new ArgumentOutOfRangeException(nameof(velocity));

            var neutral = Single(articulation, "");
            var ghost = Top(Single(articulation, "Ghost"));
            var accent = Top(Single(articulation, "Accent"));
            var root = Object.Instantiate(neutral);
            var sample = new Sample(root);
            try
            {
                var top = Top(root);
                float neutralScale = top.transform.localScale.x;
                bool softer = velocity <= 100;
                float t = softer ? (velocity-1f)/99f : (velocity-100f)/27f;
                float volume = (softer ? 1f : 2f) + t;
                float scale = Mathf.Pow(volume/3f, 1f/3f);
                var lo = softer ? ghost : Top(neutral);
                var hi = softer ? Top(neutral) : accent;
                float physical = Mathf.InverseLerp(lo.transform.localScale.x, hi.transform.localScale.x, scale);
                top.transform.localPosition = Vector3.Lerp(lo.transform.localPosition, hi.transform.localPosition, physical);
                top.transform.localScale = Vector3.one * scale;
                if (articulation == ThemeNoteType.ClosedHiHat)
                {
                    // The enlarged single closed face already almost fills the common base at Accent.
                    // Blend the physical chrome lip radius to its fitted endpoint while preserving the
                    // neutral closed body's perpendicular thickness at every diameter.
                    var source = Top(Single(ThemeNoteType.Cymbal, "")).sharedMesh;
                    float shellWidth = source.GetSubMesh(0).bounds.size.x;
                    float loWidth = lo.sharedMesh.GetSubMesh(0).bounds.size.x * lo.transform.localScale.x;
                    float hiWidth = hi.sharedMesh.GetSubMesh(0).bounds.size.x * hi.transform.localScale.x;
                    float lipScale = Mathf.Lerp(loWidth, hiWidth, physical) / (shellWidth * scale);
                    top.sharedMesh = sample.Own(ClosedHiHatDisc.Build(source, articulation, .5f, neutralScale/scale,
                        lipScale));
                }

                var chrome = AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + "CymbalMetal.mat");
                var dark = AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + "EliteHiHatDark.mat");
                PillCymbalFlamAuthoring.Assemble(root, (_, mesh) => sample.Own(mesh),
                    material => sample.Own(material), chrome, dark);
                root.name = $"Pill {articulation} size {velocity:0.##}";
                return sample;
            }
            catch
            {
                sample.Dispose();
                throw;
            }
        }
    }
}
