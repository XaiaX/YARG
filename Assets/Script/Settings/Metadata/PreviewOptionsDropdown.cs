using System;
using YARG.Localization;
using YARG.Settings.Types;

namespace YARG.Settings.Metadata
{
    /// <summary>Creates the independent preview-visual toggles shared by preview editors.</summary>
    public static class PreviewOptionsDropdown
    {
        private const string HEADER = "Preview Options";
        private const string STAR_POWER_NOTES = "Star Power Notes";
        private const string STAR_POWER_ACTIVE = "Star Power Active";
        private const string GROOVE = "Groove";
        private const string LEFTY_FLIP = "Lefty Flip";
        private const string STATE_ON = "On";
        private const string STATE_OFF = "Off";

        public readonly struct State
        {
            public readonly bool StarPowerNotes;
            public readonly bool StarPowerActive;
            public readonly bool Groove;
            public readonly bool LeftyFlip;

            public State(bool starPowerNotes, bool starPowerActive, bool groove, bool leftyFlip)
            {
                StarPowerNotes = starPowerNotes;
                StarPowerActive = starPowerActive;
                Groove = groove;
                LeftyFlip = leftyFlip;
            }
        }

        private sealed class OptionsDropdownSetting : DropdownSetting<string>
        {
            private readonly Func<State> _getState;

            public OptionsDropdownSetting(Func<State> getState, Action<string> onChange)
                : base(HEADER, null, localizable: false)
            {
                _getState = getState;
                Add(STAR_POWER_NOTES);
                Add(STAR_POWER_ACTIVE);
                Add(GROOVE);
                Add(LEFTY_FLIP);
                OnChange = value =>
                {
                    onChange?.Invoke(value);
                    SetValueWithoutNotify(HEADER);
                };
            }

            public override string ValueToString(string value) => DisplayValue(value, _getState());
        }

        public static DropdownSetting<string> Create(Func<State> getState, Action<string> onChange) =>
            new OptionsDropdownSetting(getState, onChange);

        public static string Caption => Localize.Key("Settings.PresetSetting.PreviewVisuals.Visuals.Name");

        public static string DisplayValue(string value, State state)
        {
            bool on;
            string key;
            switch (value)
            {
                case STAR_POWER_NOTES:
                    on = state.StarPowerNotes;
                    key = "Settings.PresetSetting.PreviewVisuals.StarPowerNotes.Name";
                    break;
                case STAR_POWER_ACTIVE:
                    on = state.StarPowerActive;
                    key = "Settings.PresetSetting.PreviewVisuals.StarPowerActive.Name";
                    break;
                case GROOVE:
                    on = state.Groove;
                    key = "Settings.PresetSetting.PreviewVisuals.Groove.Name";
                    break;
                case LEFTY_FLIP:
                    on = state.LeftyFlip;
                    key = "Settings.PresetSetting.PreviewVisuals.LeftyFlip.Name";
                    break;
                default:
                    return value;
            }

            return Localize.Key(key) + ": " + Localize.Key("Menu.Common." + (on ? STATE_ON : STATE_OFF));
        }
    }
}
