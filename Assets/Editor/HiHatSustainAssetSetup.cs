// pattern: Imperative Shell
using UnityEditor;
using UnityEngine;

namespace YARG.Editor
{
    /// <summary>Authors the runtime material through Unity so shader/texture references survive player builds.</summary>
    public static class HiHatSustainAssetSetup
    {
        public static void CreateMaterial()
        {
            const string path = "Assets/Resources/HiHatSustain.mat";
            AssetDatabase.ImportAsset("Assets/Art/Shaders/Gameplay/HiHatSustain.shader",
                ImportAssetOptions.ForceSynchronousImport);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Shaders/Gameplay/HiHatSustain.shader");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Gameplay/Notes/SustainSecondary.png");
            if (shader == null || texture == null) throw new System.InvalidOperationException("Sustain source assets are missing.");
            if (material == null)
            {
                material = new Material(shader) { name = "HiHatSustain" };
                material.SetTexture("_MainTex", texture);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            // Remove the old texture binding through Unity serialization, keeping
            // material and texture GUIDs intact instead of hand-editing asset YAML.
            using (var serialized = new SerializedObject(material))
            {
                var textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
                for (int i = textures.arraySize - 1; i >= 0; i--)
                    if (textures.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue == "_NoiseTex")
                        textures.DeleteArrayElementAtIndex(i);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            material.SetFloat("_PatternStrength", .75f);
            material.SetFloat("_WaveStrength", .4f);
            material.SetVector("_GlowLevels", new Vector4(.35f, .7f, 0, 0));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            if (!material.HasProperty("_PatternStrength") || !material.HasProperty("_WaveStrength") || material.HasProperty("_NoiseTex"))
                throw new System.InvalidOperationException("Hi-hat shimmer shader did not import correctly.");
            Debug.Log("Hi-hat sustain material uses mirrored shimmer and dark sound waves.");
        }
    }
}
