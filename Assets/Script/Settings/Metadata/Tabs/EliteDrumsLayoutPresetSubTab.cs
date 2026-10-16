using UnityEngine;
using YARG.Core.Game;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Settings.Customization;

namespace YARG.Settings.Metadata
{
    public sealed class EliteDrumsLayoutPresetSubTab : PresetSubTab
    {
        private EliteDrumsLayoutPreset _preset;
        public override CustomContent CustomContent => CustomContentManager.EliteDrumsLayouts;

        public EliteDrumsLayoutPresetSubTab() : base(nameof(EliteDrumsLayoutPreset)) { }

        public override void SetPresetReference(object preset) => _preset = (EliteDrumsLayoutPreset)preset;

        public override void BuildSettingTab(Transform container, NavigationGroup navigation)
        {
            var validation = EliteDrumsLayoutValidator.Validate(_preset);
            SpawnRawHeader(container, Localize.Key("Menu.EliteLayout.ColorsSeparate"));
            SpawnRawHeader(container, validation.IsRuntimeSupported ? Localize.Key("Menu.EliteLayout.Ready") :
                validation.IsComplete ? Localize.Key("Menu.EliteLayout.Unsupported") :
                Localize.Key("Menu.EliteLayout.Unassigned"));
        }
    }
}
