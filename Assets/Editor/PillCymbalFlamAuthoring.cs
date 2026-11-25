// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YARG.Themes;
using Object = UnityEngine.Object;

namespace YARG.Editor
{
    public static class PillCymbalFlamAuthoring
    {
        private const string HATS = "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/";
        private const string SINGLE_MESHES = "Assets/Art/Meshes/Gameplay/Notes/Rectangular/EliteHiHats";
        private const string MESHES = SINGLE_MESHES + "/PillFlams";
        private const string MATERIALS = "Assets/Art/Materials/Gameplay/Notes/Rectangular/";
        private const string THEME = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";

        private static T Save<T>(T generated, string path) where T : Object
        {
            generated.name = Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!existing)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, existing);
            Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static MeshRenderer Attach(MeshFilter parent, string name, Mesh mesh, Material material)
        {
            var child = new GameObject(name) { layer = parent.gameObject.layer };
            child.transform.SetParent(parent.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            var original = parent.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = original.shadowCastingMode;
            renderer.receiveShadows = original.receiveShadows;
            renderer.sharedMaterial = material;
            return renderer;
        }

        internal static void Assemble(GameObject root, Func<string, Mesh, Mesh> storeMesh,
            Func<Material, Material> storePulse, Material chrome, Material dark)
        {
            var note = root.GetComponent<ThemeNote>();
            var filters = root.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.name);
            bool ghost = note.NoteType is ThemeNoteType.CymbalGhost or ThemeNoteType.OpenHiHatGhost or ThemeNoteType.ClosedHiHatGhost;
            var baseRenderer = filters["Base"].GetComponent<MeshRenderer>();
            // The ghost texture dims the deck inside radius .315. The outer reflection supplies the other dynamics.
            var spineRadii = ghost ? new Vector2(.253f, .295f) : new Vector2(.350f, .405f);
            var parts = PillCymbalFlamMesh.Build(root, spineRadii);
            foreach (var pair in parts.Replacements)
                pair.Key.sharedMesh = storeMesh(pair.Key.name, pair.Value);
            var spine = Attach(filters["Top"], "Spine", storeMesh("Spine", parts.Spine),
                ghost ? baseRenderer.sharedMaterial : chrome);
            var marker = Attach(filters["Base"], "Flat marker", storeMesh("Flat marker", parts.FlatMarker), dark);
            marker.gameObject.SetActive(false);
            if (note.OpenHatPulse)
            {
                var well = note.OpenHatPulse;
                var bounds = well.GetComponent<MeshFilter>().sharedMesh.bounds;
                var pulse = new Material(well.sharedMaterial);
                pulse.SetVector("_Disc", new Vector4(bounds.center.x, bounds.center.z,
                    1f / bounds.extents.x, 1f / bounds.extents.z));
                well.sharedMaterial = storePulse(pulse);
            }
            var metal = note.ColoredMetalMaterials.ToList();
            var spineBinding = metal.Single(s => s.Mesh == baseRenderer && s.MaterialIndex == 0);
            spineBinding.Mesh = spine;
            metal.Add(spineBinding);
            var fields = new SerializedObject(note);
            fields.FindProperty("_flatFlamMarker").objectReferenceValue = marker.gameObject;
            VolumeHiHatAuthoring.SetSlots(fields, "_coloredMetalMaterials", metal);
            fields.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ThemeNote BakeModel(string articulation, string dynamic,
            Dictionary<string, Mesh> meshes, Material chrome, Material ghost)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(HATS + "Volume" + articulation + dynamic + ".prefab");
            var root = Object.Instantiate(source);
            try
            {
                string typeName = articulation + "Flam" + dynamic;
                root.name = "Volume" + typeName;
                var note = root.GetComponent<ThemeNote>();
                Mesh Persist(string piece, Mesh mesh)
                {
                    // The open cymbal is the same turned lid at a different seat height.
                    // Share its local-space geometry with the indifferent cymbal.
                    string owner = piece switch
                    {
                        "Base" or "Interior" or "Base Glow" or "Flat marker" => "Pill",
                        "Top" or "Spine" or "Accent ring" when articulation == "OpenHiHat" => "CymbalFlam" + dynamic,
                        _ => typeName
                    };
                    string filename = owner + " " + piece;
                    if (meshes.TryGetValue(filename, out var cached))
                    {
                        Object.DestroyImmediate(mesh);
                        return cached;
                    }
                    var asset = Save(mesh, MESHES + "/" + filename + ".asset");
                    meshes.Add(filename, asset);
                    return asset;
                }

                Assemble(root, Persist, m => Save(m, MATERIALS + root.name + "Pulse.mat"), chrome, ghost);
                var fields = new SerializedObject(note);
                fields.FindProperty("<NoteType>k__BackingField").intValue = (int) Enum.Parse(typeof(ThemeNoteType), typeName);
                fields.ApplyModifiedPropertiesWithoutUndo();
                var saved = PrefabUtility.SaveAsPrefabAsset(root, HATS + root.name + ".prefab");
                return saved.GetComponent<ThemeNote>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("YARG/Elite Drums/Bake joined cymbal flams")]
        public static void Bake()
        {
            if (!AssetDatabase.IsValidFolder(MESHES)) AssetDatabase.CreateFolder(SINGLE_MESHES, "PillFlams");
            var chrome = AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + "CymbalMetal.mat");
            var ghost = AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + "EliteHiHatDark.mat");
            if (!chrome || !ghost) throw new InvalidDataException("Missing cymbal metal or ghost material.");
            var meshes = new Dictionary<string, Mesh>();
            var models = new List<ThemeNote>();
            foreach (string articulation in new[] { "Cymbal", "OpenHiHat", "ClosedHiHat" })
            foreach (string dynamic in new[] { "", "Ghost", "Accent" })
                models.Add(BakeModel(articulation, dynamic, meshes, chrome, ghost));

            var theme = PrefabUtility.LoadPrefabContents(THEME);
            try
            {
                var fields = new SerializedObject(theme.GetComponent<ThemeComponent>());
                var list = fields.FindProperty("_eliteVolumeCymbalFlams");
                list.arraySize = models.Count;
                for (int i = 0; i < models.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = models[i];
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(theme, THEME);
            }
            finally { PrefabUtility.UnloadPrefabContents(theme); }
            AssetDatabase.SaveAssets();
            Debug.Log("Pill cymbal flams baked and registered: " + models.Count);
        }
    }
}
