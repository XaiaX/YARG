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
    public static class VolumeHiHatAuthoring
    {
        private const string PREFABS = "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/";
        private const string MESHES = "Assets/Art/Meshes/Gameplay/Notes/Rectangular/EliteHiHats/";
        private const string MATERIALS = "Assets/Art/Materials/Gameplay/Notes/Rectangular/";
        private const string THEME = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";
        public const float GHOST_AREA = .5f, ACCENT_INNER_AREA = .5f;

        private static MeshFilter Filter(GameObject root, string name) =>
            root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == name);

        private static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + name + ".mat")
            ?? throw new FileNotFoundException("Missing cymbal material " + name);

        private static Mesh BuildClosedTop(MeshFilter top, ThemeNoteType type, float fraction)
        {
            var neutral = Filter(AssetDatabase.LoadAssetAtPath<GameObject>(PREFABS + "VolumeCymbal.prefab"), "Top");
            // The conical prefabs supply uniformly scaled tops. Cancel only their thickness
            // difference when flattening them, while keeping the authored diameter and seat.
            float thicknessScale = neutral.transform.localScale.y / top.transform.localScale.y;
            return ClosedHiHatDisc.Build(neutral.sharedMesh, type, fraction, thicknessScale);
        }

        // Gallery callers own the returned transient mesh and the instantiated prefab.
        public static Mesh ApplyClosedArea(GameObject root, float fraction)
        {
            if (EditorUtility.IsPersistent(root))
                throw new ArgumentException("Instantiate the model before changing its disc area.");
            var top = Filter(root, "Top");
            var mesh = BuildClosedTop(top, root.GetComponent<ThemeNote>().NoteType, fraction);
            top.sharedMesh = mesh;
            return mesh;
        }

        private static Mesh SaveMesh(Mesh generated)
        {
            string path = MESHES + generated.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(generated);
            return existing;
        }

        internal static void SetSlots(SerializedObject settings, string field, List<MeshEmissionMaterialIndex> slots)
        {
            var list = settings.FindProperty(field);
            list.arraySize = slots.Count;
            for (int i = 0; i < slots.Count; i++)
            {
                var item = list.GetArrayElementAtIndex(i);
                var slot = slots[i];
                item.FindPropertyRelative("Mesh").objectReferenceValue = slot.Mesh;
                item.FindPropertyRelative("MaterialIndex").intValue = slot.MaterialIndex;
                item.FindPropertyRelative("EmissionMultiplier").floatValue = slot.EmissionMultiplier;
                item.FindPropertyRelative("EmissionAddition").floatValue = slot.EmissionAddition;
                item.FindPropertyRelative("ColorMultiplier").floatValue = slot.ColorMultiplier;
            }
        }

        private static void Close(MeshFilter top, ThemeNoteType type,
            List<MeshEmissionMaterialIndex> lane, List<MeshEmissionMaterialIndex> metal)
        {
            if (top.transform.Find("Accent ring") is Transform ring) Object.DestroyImmediate(ring.gameObject);
            var renderer = top.GetComponent<MeshRenderer>();
            var oldMaterials = renderer.sharedMaterials;
            bool ghost = type == ThemeNoteType.ClosedHiHatGhost;
            var materials = new List<Material> {ghost ? oldMaterials[0] : oldMaterials[1], oldMaterials[1], oldMaterials[2]};
            if (type != ThemeNoteType.ClosedHiHat)
            {
                var dark = Material("EliteHiHatDark");
                materials.Add(dark);
                if (type == ThemeNoteType.ClosedHiHatAccent)
                {
                    materials[2] = dark;
                    materials.Add(Material("EliteAccentHOPOGlow"));
                }
            }
            top.sharedMesh = SaveMesh(BuildClosedTop(top, type,
                type == ThemeNoteType.ClosedHiHatGhost ? GHOST_AREA : ACCENT_INNER_AREA));
            renderer.sharedMaterials = materials.ToArray();
            // Ghost already inherits its classic dim rim and its color binding from the conical top.
            if (ghost) return;
            var edge = lane.Single(slot => slot.Mesh == renderer && slot.MaterialIndex == 1);
            edge.MaterialIndex = 0;
            lane.Add(edge);
            metal.RemoveAll(slot => slot.Mesh == renderer && slot.MaterialIndex == 0);
        }

        // Intersect triangles on the vertical line through the base center. The front of these
        // models is tilted, so the lowest vertex alone gives a different seat for each radius.
        private static (float floor, float roof) CenterHeight(MeshFilter filter, Transform root, Vector3 center)
        {
            var mesh = filter.sharedMesh;
            var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var points = mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
            var tris = mesh.triangles;
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            for (int i = 0; i < tris.Length; i += 3)
            {
                var a = points[tris[i]];
                var ab = points[tris[i+1]] - a;
                var ac = points[tris[i+2]] - a;
                var p = center - a;
                float det = ab.x * ac.z - ab.z * ac.x;
                if (Mathf.Abs(det) < 1e-10f) continue;
                float u = (p.x * ac.z - p.z * ac.x) / det;
                float v = (ab.x * p.z - ab.z * p.x) / det;
                if (u < -.00001f || v < -.00001f || u + v > 1.00001f) continue;
                float y = a.y + ab.y * u + ac.y * v;
                lo = Mathf.Min(lo, y);
                hi = Mathf.Max(hi, y);
            }
            if (float.IsInfinity(lo)) throw new InvalidDataException("No center surface on " + filter.name);
            return (lo, hi);
        }

        private static Vector3 BaseCenter(MeshFilter basis, Transform root)
        {
            var points = basis.sharedMesh.vertices;
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(p);
            return root.InverseTransformPoint(basis.transform.TransformPoint(bounds.center));
        }

        private static float Gap(GameObject root)
        {
            var basis = Filter(root, "Base");
            var center = BaseCenter(basis, root.transform);
            return CenterHeight(Filter(root, "Top"), root.transform, center).floor
                - CenterHeight(basis, root.transform, center).roof;
        }

        private static Vector3 UndersideHub(Mesh mesh, int underside)
        {
            var uv = mesh.uv2;
            var verts = mesh.vertices;
            return verts[mesh.GetTriangles(underside).First(i => (uv[i] - new Vector2(.5f, .5f)).sqrMagnitude < 1e-9f)];
        }

        private static MeshRenderer CopyLight(MeshFilter source, Transform oldParent, Transform newParent,
            Vector3 offset, string name)
        {
            var child = Object.Instantiate(source.gameObject, newParent, false);
            child.name = name;
            var transform = child.transform;
            transform.localPosition = oldParent.InverseTransformPoint(source.transform.position) + offset;
            transform.localRotation = Quaternion.Inverse(oldParent.rotation) * source.transform.rotation;
            var relative = oldParent.worldToLocalMatrix * source.transform.localToWorldMatrix;
            transform.localScale = relative.lossyScale;
            return child.GetComponent<MeshRenderer>();
        }

        private static Mesh BakeShadowWell(Mesh well, ThemeNote[] cymbals)
        {
            var bounds = well.bounds;
            // In the well's local axes +Z faces the player. Offset the lid footprint
            // towards the back, leaving a lit crescent along the front of the basin.
            var radii = cymbals.Select(note =>
            {
                var top = Filter(note.gameObject, "Top");
                var vertices = top.sharedMesh.vertices;
                var face = top.sharedMesh.GetTriangles(1);
                float diameter = face.Max(i => vertices[i].x) - face.Min(i => vertices[i].x);
                return diameter * top.transform.localScale.x / bounds.size.x;
            }).ToArray();
            var points = well.vertices;
            var colors = new Color32[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i] - bounds.center;
                float distance = new Vector2(p.x / bounds.extents.x, p.z / bounds.extents.z + .36f).magnitude;
                byte LightAt(float radius) => (byte)Mathf.RoundToInt(255f * Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(radius - .12f, radius + .12f, distance)));
                colors[i] = new Color32(LightAt(radii[0]), LightAt(radii[1]), LightAt(radii[2]), 255);
            }
            // The multiply pass only needs positions and color. Share it across all three hats.
            var mesh = new Mesh {name = "Volume Open Hat Shadow Well", vertices = points, colors32 = colors};
            mesh.triangles = well.triangles;
            mesh.bounds = bounds;
            return SaveMesh(mesh);
        }

        private static Material BakePulseMaterial(ThemeNoteType type, Mesh well, float peak, int glowRole)
        {
            var shader = Shader.Find("YARG/Elite Open Hat Pulse");
            if (!shader) throw new InvalidDataException("Missing open-hat pulse shader.");
            string path = MATERIALS + "Volume" + type + "Pulse.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            var bounds = well.bounds;
            material.SetVector("_Disc", new Vector4(bounds.center.x, bounds.center.z,
                1f / bounds.extents.x, 1f / bounds.extents.z));
            material.SetFloat("_FloorGain", .12f / peak);
            material.SetFloat("_GlowRole", glowRole);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static MeshRenderer Open(GameObject root, GameObject heightReference, GameObject standardOpen,
            Mesh shadowWell, List<MeshEmissionMaterialIndex> lane)
        {
            var top = Filter(root, "Top");
            var basis = Filter(root, "Base");
            float originalGap = Gap(heightReference);
            // The smaller Ghost exposes more of the well. Tune its clearance and the
            // three well layers together, independently of the cymbal and sustain.
            var (gapScale, wellBrightness, glowRole) = heightReference.GetComponent<ThemeNote>().NoteType switch
            {
                ThemeNoteType.OpenHiHatGhost  => (.675f, .26f, 0),
                ThemeNoteType.OpenHiHat       => (1.30f, .47f, 1),
                ThemeNoteType.OpenHiHatAccent => (1.365f, .72f, 2),
                _ => throw new InvalidDataException("Expected an open hi-hat reference.")
            };
            float targetGap = originalGap * gapScale;
            top.transform.position += root.transform.TransformVector(Vector3.up * (targetGap - Gap(root)));
            if (Mathf.Abs(Gap(root) - targetGap) > .00001f || targetGap <= 0)
                throw new InvalidDataException("Open-hat clearance did not match its target.");

            var openTop = Filter(standardOpen, "Top");
            var openBase = Filter(standardOpen, "Base");
            var sourceGlow = Filter(standardOpen, "Base Glow");
            var sourceLight = Filter(standardOpen, "Light");
            var oldSlots = standardOpen.GetComponent<ThemeNote>().ColoredMaterials.ToArray();
            void Bind(MeshRenderer to, MeshFilter from, int slot = 0)
            {
                var binding = oldSlots.Single(s => s.Mesh.gameObject == from.gameObject && s.MaterialIndex == slot);
                binding.Mesh = to;
                binding.MaterialIndex = 0;
                // The procedural glow shader derives its emission from the lane color.
                // This tint also scales the opaque backing and the light under the lid.
                binding.ColorMultiplier = wellBrightness;
                lane.Add(binding);
            }

            // The imported glow is the exact original inner base surface. Give its opaque
            // lane-colored backing and transparent effect separate depths over the common chrome.
            var interior = CopyLight(sourceGlow, openBase.transform, basis.transform,
                Vector3.up * .0003f, "Interior");
            interior.sharedMaterial = openBase.GetComponent<MeshRenderer>().sharedMaterials[0];
            Bind(interior, openBase);
            var glow = CopyLight(sourceGlow, openBase.transform, basis.transform, Vector3.up * .00065f, "Base Glow");
            Bind(glow, sourceGlow);

            // Composite on the exact well surface, before the sustain and other transparent effects.
            var pulse = CopyLight(sourceGlow, openBase.transform, basis.transform, Vector3.up * .00065f, "Well pulse");
            pulse.GetComponent<MeshFilter>().sharedMesh = shadowWell;
            pulse.sharedMaterial = BakePulseMaterial(heightReference.GetComponent<ThemeNote>().NoteType,
                sourceGlow.sharedMesh, wellBrightness, glowRole);
            glow.sortingOrder = -2;
            pulse.sortingOrder = -1;

            var hubShift = UndersideHub(top.sharedMesh, 0) - UndersideHub(openTop.sharedMesh, 1);
            var lamp = CopyLight(sourceLight, openTop.transform, top.transform, hubShift, "Light");
            Bind(lamp, sourceLight);
            var center = BaseCenter(basis, root.transform);
            var aperture = CenterHeight(top, root.transform, center).floor
                - CenterHeight(glow.GetComponent<MeshFilter>(), root.transform, center).roof;
            if (aperture <= .001f) throw new InvalidDataException("Open gap is occluded by the glow surface.");
            Debug.Log($"VOLUME_HAT_GAP {root.name}: original {originalGap:F6}, target {targetGap:F6}, measured {Gap(root):F6}, luminous gap {aperture:F6}");
            return pulse;
        }

        public static void Bake()
        {
            var themeRoot = PrefabUtility.LoadPrefabContents(THEME);
            try
            {
                var theme = themeRoot.GetComponent<ThemeComponent>();
                var legacy = theme.GetNoteModelsForVisualStyle(VisualStyle.EliteDrums, false);
                var indifferentNotes = new[] {"Ghost", "", "Accent"}.Select(suffix =>
                    AssetDatabase.LoadAssetAtPath<GameObject>(PREFABS + "VolumeCymbal" + suffix + ".prefab")
                        .GetComponent<ThemeNote>()).ToArray();
                var models = new List<ThemeNote>(indifferentNotes);
                var standardOpen = legacy[ThemeNoteType.OpenHiHat];
                var shadowWell = BakeShadowWell(Filter(standardOpen, "Base Glow").sharedMesh, indifferentNotes);

                foreach (bool open in new[] {false, true})
                foreach (var indifferent in indifferentNotes)
                {
                    string suffix = indifferent.NoteType.ToString().Substring("Cymbal".Length);
                    var type = (ThemeNoteType) Enum.Parse(typeof(ThemeNoteType), (open ? "OpenHiHat" : "ClosedHiHat") + suffix);
                    var root = Object.Instantiate(indifferent.gameObject);
                    try
                    {
                        root.name = "Volume" + type;
                        var note = root.GetComponent<ThemeNote>();
                        var lane = note.ColoredMaterials.ToList();
                        var metal = note.ColoredMetalMaterials.ToList();
                        var settings = new SerializedObject(note);
                        if (open)
                            settings.FindProperty("<OpenHatPulse>k__BackingField").objectReferenceValue =
                                Open(root, legacy[type], standardOpen, shadowWell, lane);
                        else Close(Filter(root, "Top"), type, lane, metal);
                        settings.FindProperty("<NoteType>k__BackingField").intValue = (int) type;
                        SetSlots(settings, "_coloredMaterials", lane);
                        SetSlots(settings, "_coloredMetalMaterials", metal);
                        settings.ApplyModifiedPropertiesWithoutUndo();
                        models.Add(PrefabUtility.SaveAsPrefabAsset(root, PREFABS + root.name + ".prefab").GetComponent<ThemeNote>());
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                var props = new SerializedObject(theme);
                var options = props.FindProperty("_eliteVolumeCymbals");
                options.arraySize = models.Count;
                for (int i = 0; i < models.Count; i++) options.GetArrayElementAtIndex(i).objectReferenceValue = models[i];
                props.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(themeRoot, THEME);
            }
            finally { PrefabUtility.UnloadPrefabContents(themeRoot); }
            AssetDatabase.SaveAssets();
            Debug.Log("VOLUME_HATS_BAKED: nine experimental notes, one shared base and three top sizes.");
        }
    }
}
