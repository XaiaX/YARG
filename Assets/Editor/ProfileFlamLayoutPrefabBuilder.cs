// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using YARG.Localization;
using YARG.Menu.ProfileList;

namespace YARG.Editor
{
    /// <summary>Authors the profile flam selector using the neighboring native dropdown row.</summary>
    public static class ProfileFlamLayoutPrefabBuilder
    {
        private const string PREFAB = "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab";

        [MenuItem("YARG/Authoring/Add profile flam layout")]
        public static void Build()
        {
            var root = PrefabUtility.LoadPrefabContents(PREFAB);
            try
            {
                var sidebar = root.GetComponentInChildren<ProfileSidebar>(true);
                if (sidebar == null) throw new InvalidOperationException("Profile sidebar is missing.");
                var data = new SerializedObject(sidebar);
                var content = (GameObject)data.FindProperty("_sidebarContent").objectReferenceValue;
                var template = content.transform.Find("Star Power Activation Type");
                if (template == null) throw new InvalidOperationException("The profile dropdown template is missing.");
                var row = content.transform.Find("Flam Layout");
                if (row == null)
                {
                    row = UnityEngine.Object.Instantiate(template, content.transform, false);
                    row.name = "Flam Layout";
                }
                int templateIndex = template.GetSiblingIndex();
                row.SetSiblingIndex(templateIndex + (row.GetSiblingIndex() > templateIndex ? 1 : 0));

                var label = row.Find("Option Name").GetComponent<TextMeshProUGUI>();
                label.text = "FLAM LAYOUT";
                var localize = label.GetComponent<LocalizeText>();
                if (localize == null) localize = label.gameObject.AddComponent<LocalizeText>();
                var labelData = new SerializedObject(localize);
                labelData.FindProperty("_localizationKey").stringValue = "Menu.ProfileList.FlamLayout";
                labelData.ApplyModifiedPropertiesWithoutUndo();

                var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
                var dropdownData = new SerializedObject(dropdown);
                dropdownData.FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls").arraySize = 0;
                dropdownData.ApplyModifiedPropertiesWithoutUndo();
                dropdown.onValueChanged.RemoveAllListeners();
                UnityEventTools.AddVoidPersistentListener(dropdown.onValueChanged, sidebar.ChangeFlamLayout);
                dropdown.ClearOptions();
                dropdown.AddOptions(new List<string> { "V", "Bucket", "Parallel" });
                dropdown.SetValueWithoutNotify(0);

                data.Update();
                data.FindProperty("_flamLayoutDropdown").objectReferenceValue = dropdown;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB);
                AssetDatabase.SaveAssets();
                Debug.Log("PROFILE_FLAM_LAYOUT_AUTHORING_COMPLETE");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
