using System;
using UnityEditor;
using UnityEngine;

namespace YARG.Editor
{
    /// <summary>Repairs the Done template reference using Unity's prefab serializer.</summary>
    public static class DifficultySelectPrefabRepair
    {
        public static void RepairDoneTemplate()
        {
            const string menuPath = "Assets/Prefabs/Menu/DifficultySelect/DifficultySelectMenu.prefab";
            const string rowPath = "Assets/Prefabs/Menu/DifficultySelect/DifficultyItemColored.prefab";
            var row = AssetDatabase.LoadAssetAtPath<GameObject>(rowPath);
            if (row == null) throw new InvalidOperationException("Missing authored colored difficulty row.");
            var root = PrefabUtility.LoadPrefabContents(menuPath);
            try
            {
                var menu = root.GetComponent<YARG.Menu.DifficultySelect.DifficultySelectMenu>();
                var serialized = new SerializedObject(menu);
                var template = serialized.FindProperty("_coloredItemPrefab");
                Debug.Log($"[DifficultyDoneRepair] Before repair valid={template.objectReferenceValue != null}, instanceId={template.objectReferenceInstanceIDValue}");
                template.objectReferenceValue = row;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, menuPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[DifficultyDoneRepair] Reassigned Done to authored colored difficulty row.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
