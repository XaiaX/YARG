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
            var highlights = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/PlayMode/Note_Shader.png");
            if (highlights == null) throw new System.InvalidOperationException("Cymbal highlight texture is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = "HiHatSustain" };
                material.SetTexture("_MainTex", texture);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_NoiseTex", highlights);
            material.SetFloat("_PatternStrength", .75f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            Debug.Log($"Hi-hat highlight binding: {material.GetTexture("_NoiseTex")?.name}, asset={AssetDatabase.GetAssetPath(highlights)}");
            if (material.GetTexture("_NoiseTex") != highlights)
                throw new System.InvalidOperationException("Failed to assign the cymbal highlight texture.");
        }
    }
}
