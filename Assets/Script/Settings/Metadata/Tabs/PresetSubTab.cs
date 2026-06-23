using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using YARG.Localization;
using YARG.Settings.Customization;

namespace YARG.Settings.Metadata
{
    public abstract class PresetSubTab : Tab
    {
        // When true, BuildSettingTab shows the dropdown and preview toggles but
        // hides the color/preset editing fields. Used for default/built-in profiles
        // so the user can still switch instrument previews.
        public bool HideFields { get; set; }

        // Prefabs needed for this tab type
        private static GameObject _headerPrefab;

        public abstract CustomContent CustomContent { get; }

        protected PresetSubTab(string name, string icon = "Generic", IPreviewBuilder previewBuilder = null)
            : base(name, icon, previewBuilder)
        {
        }

        public abstract void SetPresetReference(object preset);

        protected static void SpawnHeader(Transform container, string unlocalizedText)
        {
            if (_headerPrefab == null) {
                _headerPrefab = Addressables
                    .LoadAssetAsync<GameObject>("SettingTab/Header")
                    .WaitForCompletion();
            }
            // Spawn in the header
            var go = Object.Instantiate(_headerPrefab, container);

            // Set header text
            go.GetComponentInChildren<TextMeshProUGUI>().text =
                Localize.Key("Settings.Header", unlocalizedText);
        }
    }
}
