// pattern: Imperative Shell
using YARG.Core;
using YARG.Core.Game;
using YARG.Menu.Settings;
using YARG.Settings.Preview;
using Tuning = YARG.Settings.SettingsManager.SettingContainer;

namespace YARG.Settings.Metadata
{
    internal sealed class ExperimentalTab : MetadataTab
    {
        private readonly TrackPreviewBuilder _drums = new()
        {
            StartingGameMode = GameMode.EliteDrums,
            EliteLayoutOverride = EliteDrumsLayoutPreset.Full,
            UseVolumeCymbals = true
        };

        internal ExperimentalTab() : base("Experimental", "Beaker", new CharacterPreviewBuilder()) { }

        protected override IPreviewBuilder PreviewBuilder =>
            _drums.GlowPreview == EliteGlowPreview.None ? base.PreviewBuilder : _drums;

        public override void OnTabExit() => _drums.GlowPreview = EliteGlowPreview.None;

        public override void OnSettingSelected(string unlocalizedName)
        {
            var sequence = unlocalizedName switch
            {
                nameof(Tuning.EliteOpenHatGhostGlow) or nameof(Tuning.EliteOpenHatNeutralGlow) or
                nameof(Tuning.EliteOpenHatAccentGlow) or nameof(Tuning.EliteOpenHatGlowMinimum) or
                nameof(Tuning.EliteOpenHatShadowMinimum) or nameof(Tuning.EliteOpenHatShadowPeak) or
                nameof(Tuning.ResetOpenHatGlow) => EliteGlowPreview.OpenHats,
                nameof(Tuning.EliteCymbalFlamGlowMinimum) or nameof(Tuning.EliteCymbalFlamGlowMaximum) => EliteGlowPreview.CymbalFlams,
                nameof(Tuning.ElitePedalBeamEffect) => EliteGlowPreview.Pedals,
                _ => EliteGlowPreview.None
            };
            if (_drums.GlowPreview == sequence) return;
            _drums.GlowPreview = sequence;
            SettingsMenu.Instance?.RefreshPreview();
        }
    }
}
