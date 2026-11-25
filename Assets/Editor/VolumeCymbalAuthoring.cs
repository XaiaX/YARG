// pattern: Imperative Shell
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YARG.Themes;
using Object = UnityEngine.Object;

namespace YARG.Editor
{
    // The offline study's Blender exporter supplies the sampled 5x surface
    // and the split Ghost bow. No mesh generation runs during gameplay.
    public static class VolumeCymbalAuthoring
    {
        private const string THEME = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";
        private const string PREFABS = "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/";
        private const string MESHES = "Assets/Art/Meshes/Gameplay/Notes/Rectangular/EliteHiHats/";
        private const string MATERIALS = "Assets/Art/Materials/Gameplay/Notes/Rectangular/";

#pragma warning disable CS0649 // Fields populated by Blender export via JsonUtility.
        [Serializable] private class Anchor { public Vector2 uv; public Vector3 p; }
        [Serializable] private class Vertex { public Vector3 p, n; public Vector2 uv, uv2; }
        [Serializable] private class Part { public int[] indices; }
        [Serializable] private class Geometry { public Vertex[] vertices; public Part[] parts; }
        [Serializable] private class Seat { public float scale, height; }
        [Serializable] private class Study
        {
            public Anchor[] anchors;
            public Seat[] seats;
            public Geometry ghost, ring;
        }
#pragma warning restore CS0649

        private static Matrix4x4 Basis(Vector3[] points)
        {
            var m = Matrix4x4.identity;
            for (int i = 0; i < 3; i++) m.SetColumn(i, points[i + 1] - points[0]);
            m.SetColumn(3, new Vector4(points[0].x, points[0].y, points[0].z, 1));
            return m;
        }

        private static Matrix4x4 StudioToMesh(Anchor[] anchors, Mesh source)
        {
            var bow = source.GetTriangles(1).Concat(source.GetTriangles(2)).Distinct().ToArray();
            var uv = source.uv2;
            var vertices = source.vertices;
            int Match(Anchor a) => bow.First(i => (uv[i] - a.uv).sqrMagnitude < 1e-9f);
            var landmarks = new[] { new Vector2(.06f,.5f), new Vector2(.94f,.5f),
                new Vector2(.5f,.94f), new Vector2(.5f,.5f) }
                .Select(coord => anchors.Single(a => (a.uv - coord).sqrMagnitude < 1e-9f)).ToArray();
            var toMesh = Basis(landmarks.Select(a => vertices[Match(a)]).ToArray())
                * Basis(landmarks.Select(a => a.p).ToArray()).inverse;
            foreach (var a in anchors)
                if (Vector3.Distance(toMesh.MultiplyPoint3x4(a.p), vertices[Match(a)]) > .00001f)
                    throw new InvalidDataException("Studio spokes do not match the imported neutral Top.");
            return toMesh;
        }

        private static Mesh SaveMesh(Geometry geometry, Matrix4x4 studio, string name)
        {
            var normalMatrix = studio.inverse.transpose;
            var mesh = new Mesh {name = name};
            var points = geometry.vertices.Select(v => studio.MultiplyPoint3x4(v.p)).ToArray();
            var normals = geometry.vertices.Select(v => normalMatrix.MultiplyVector(v.n).normalized).ToArray();
            mesh.vertices = points;
            mesh.normals = normals;
            mesh.uv = geometry.vertices.Select(v => v.uv).ToArray();
            mesh.uv2 = geometry.vertices.Select(v => v.uv2).ToArray();
            mesh.subMeshCount = geometry.parts.Length;
            for (int part = 0; part < geometry.parts.Length; part++)
            {
                var indices = geometry.parts[part].indices;
                for (int t = 0; t < indices.Length; t += 3)
                {
                    int a = indices[t], b = indices[t+1], c = indices[t+2];
                    if (Vector3.Dot(Vector3.Cross(points[b] - points[a], points[c] - points[a]),
                            normals[a] + normals[b] + normals[c]) < 0)
                        (indices[t+1], indices[t+2]) = (c, b);
                }
                mesh.SetTriangles(indices, part);
            }
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            string path = MESHES + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        private static MeshFilter Filter(GameObject root, string name) =>
            root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == name);

        private static Material BakeBaseMetal(Material source, string name, float topTint)
        {
            // The top and side share a metal slot. The authored UVs put the top inside
            // radius .315 and reach the side of the base at .398. Bake the falloff once.
            string sourcePath = AssetDatabase.GetAssetPath(source.GetTexture("_BaseMap"));
            string texturePath = Path.Combine(Path.GetDirectoryName(sourcePath), name + ".png").Replace('\\', '/');
            var pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!pixels.LoadImage(File.ReadAllBytes(sourcePath))) throw new InvalidDataException(sourcePath);
                var colors = pixels.GetPixels32();
                int width = pixels.width, height = pixels.height;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var uv = new Vector2((x + .5f) / width - .5f, (y + .5f) / height - .5f);
                    float tint = Mathf.SmoothStep(topTint, 1f, Mathf.InverseLerp(.315f, .398f, uv.magnitude));
                    int i = y * width + x;
                    var c = colors[i];
                    colors[i] = new Color32((byte)Mathf.RoundToInt(c.r * tint), (byte)Mathf.RoundToInt(c.g * tint),
                        (byte)Mathf.RoundToInt(c.b * tint), c.a);
                }
                pixels.SetPixels32(colors);
                pixels.Apply(false);
                var encoded = pixels.EncodeToPNG();
                if (!File.Exists(texturePath) && !AssetDatabase.CopyAsset(sourcePath, texturePath))
                    throw new IOException("Could not copy base texture import settings.");
                if (!File.ReadAllBytes(texturePath).SequenceEqual(encoded))
                {
                    File.WriteAllBytes(texturePath, encoded);
                    AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                }
            }
            finally { Object.DestroyImmediate(pixels); }

            string path = MATERIALS + name + ".mat";
            if (!File.Exists(path) && !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
                throw new IOException("Could not create experimental base material.");
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            result.name = name;
            result.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            EditorUtility.SetDirty(result);
            return result;
        }

        private static Vector3 BaseOrigin(GameObject root, Matrix4x4 studio)
        {
            var top = Filter(root, "Top");
            var basis = Filter(root, "Base");
            var inverse = studio.inverse * top.transform.worldToLocalMatrix * basis.transform.localToWorldMatrix;
            var points = basis.sharedMesh.vertices.Select(inverse.MultiplyPoint3x4).ToArray();
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(p);
            if (Mathf.Abs(bounds.size.x - 2f) > .0001f)
                throw new InvalidDataException("Base diameter differs from the normalized study.");
            return new Vector3(bounds.center.x, bounds.center.y, bounds.min.z);
        }

        private static ThemeNote SaveVariant(GameObject source, Seat seat, Matrix4x4 studio, Vector3 origin,
            ThemeNoteType type, Mesh ghost, Mesh ring, Material dark, Material glow, Material baseMetal)
        {
            var root = Object.Instantiate(source);
            try
            {
                root.name = "Volume" + type;
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = source.transform.localRotation;
                root.transform.localScale = source.transform.localScale;
                var baseRenderer = Filter(root, "Base").GetComponent<MeshRenderer>();
                var baseMaterials = baseRenderer.sharedMaterials;
                baseMaterials[0] = baseMetal;
                baseRenderer.sharedMaterials = baseMaterials;
                var top = Filter(root, "Top");
                var topRenderer = top.GetComponent<MeshRenderer>();
                var childShift = studio.MultiplyPoint3x4(origin + Vector3.forward * seat.height)
                    - studio.MultiplyPoint3x4(Vector3.zero) * seat.scale;
                top.transform.localPosition += top.transform.localRotation * Vector3.Scale(top.transform.localScale, childShift);
                top.transform.localScale *= seat.scale;
                var settings = new SerializedObject(root.GetComponent<ThemeNote>());
                if (type == ThemeNoteType.CymbalGhost)
                {
                    top.sharedMesh = ghost;
                    var materials = topRenderer.sharedMaterials.Append(dark).ToArray();
                    materials[0] = AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + "CymbalGhostCenter.mat");
                    if (!materials[0]) throw new InvalidDataException("Missing classic ghost rim material.");
                    topRenderer.sharedMaterials = materials;
                    var ghostNote = root.GetComponent<ThemeNote>();
                    var lane = ghostNote.ColoredMaterials.ToList();
                    lane.Add(new MeshEmissionMaterialIndex { Mesh = topRenderer, MaterialIndex = 0, EmissionAddition = -.7f });
                    var metal = ghostNote.ColoredMetalMaterials.Where(s => s.Mesh != topRenderer || s.MaterialIndex != 0).ToList();
                    VolumeHiHatAuthoring.SetSlots(settings, "_coloredMaterials", lane);
                    VolumeHiHatAuthoring.SetSlots(settings, "_coloredMetalMaterials", metal);
                }
                if (type == ThemeNoteType.CymbalAccent)
                {
                    var materials = topRenderer.sharedMaterials;
                    materials[2] = glow;
                    topRenderer.sharedMaterials = materials;
                    var metalSlots = settings.FindProperty("_coloredMetalMaterials");
                    for (int i = metalSlots.arraySize - 1; i >= 0; i--)
                    {
                        var slot = metalSlots.GetArrayElementAtIndex(i);
                        if (slot.FindPropertyRelative("Mesh").objectReferenceValue == topRenderer &&
                            slot.FindPropertyRelative("MaterialIndex").intValue == 2)
                            metalSlots.DeleteArrayElementAtIndex(i);
                    }
                    var band = new GameObject("Accent ring");
                    band.transform.SetParent(top.transform, false);
                    band.AddComponent<MeshFilter>().sharedMesh = ring;
                    var ringRenderer = band.AddComponent<MeshRenderer>();
                    ringRenderer.sharedMaterials = new[] {glow, dark};
                }
                settings.FindProperty("<NoteType>k__BackingField").intValue = (int) type;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var saved = PrefabUtility.SaveAsPrefabAsset(root, PREFABS + root.name + ".prefab");
                var note = saved.GetComponent<ThemeNote>();
                if (note.NoteType != type || note.StarPowerVariant ||
                    !note.ColoredMaterials.All(s => s.Mesh.transform.IsChildOf(saved.transform)) ||
                    !note.ColoredMetalMaterials.All(s => s.Mesh.transform.IsChildOf(saved.transform)))
                    throw new InvalidDataException("Palette bindings are not local to " + saved.name);
                return note;
            }
            finally { Object.DestroyImmediate(root); }
        }

        public static void Bake()
        {
            var args = Environment.GetCommandLineArgs();
            int argument = Array.IndexOf(args, "-volumeCymbalGeometry");
            if (argument < 0) throw new ArgumentException("Pass -volumeCymbalGeometry with the Blender export path.");
            var study = JsonUtility.FromJson<Study>(File.ReadAllText(args[argument + 1]));
            var themeRoot = PrefabUtility.LoadPrefabContents(THEME);
            try
            {
                var theme = themeRoot.GetComponent<ThemeComponent>();
                var source = theme.GetNoteModelsForVisualStyle(VisualStyle.EliteDrums, false)[ThemeNoteType.Cymbal];
                var studio = StudioToMesh(study.anchors, Filter(source, "Top").sharedMesh);
                var origin = BaseOrigin(source, studio);
                var ghost = SaveMesh(study.ghost, studio, "Volume Ghost Bow");
                var ring = SaveMesh(study.ring, studio, "Volume Accent Ring");
                var dark = AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + "EliteHiHatDark.mat");
                var glow = AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + "EliteAccentHOPOGlow.mat");
                var originalMetal = Filter(source, "Base").GetComponent<MeshRenderer>().sharedMaterial;
                var baseMetal = BakeBaseMetal(originalMetal, "EliteVolumeCymbalBase", .64f);
                var ghostBase = BakeBaseMetal(originalMetal, "EliteVolumeCymbalGhostBase", .28f);
                if (!dark || !glow || !baseMetal) throw new InvalidDataException("Could not load the Elite cymbal materials.");
                var types = new[] {ThemeNoteType.CymbalGhost, ThemeNoteType.Cymbal, ThemeNoteType.CymbalAccent};
                var settings = new SerializedObject(theme);
                var options = settings.FindProperty("_eliteVolumeCymbals");
                options.arraySize = types.Length;
                for (int i = 0; i < types.Length; i++)
                {
                    var note = SaveVariant(source, study.seats[i], studio, origin, types[i], ghost, ring, dark, glow,
                        types[i] == ThemeNoteType.CymbalGhost ? ghostBase : baseMetal);
                    options.GetArrayElementAtIndex(i).objectReferenceValue = note;
                }
                settings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(themeRoot, THEME);
            }
            finally { PrefabUtility.UnloadPrefabContents(themeRoot); }
            AssetDatabase.SaveAssets();
            VolumeHiHatAuthoring.Bake();
            Debug.Log("VOLUME_CYMBALS_BAKED: nine independent profile-selectable models");
        }
    }
}
