// pattern: Imperative Shell
using UnityEngine;
using YARG.Settings;

namespace YARG
{
    /// <summary>One owner for effective frame pacing, independent of visual quality presets.</summary>
    public static class FramePacing
    {
        public static bool HasFocus => FocusHandler.Instance != null
            ? FocusHandler.Instance.HasFocus : Application.isFocused;

        public static void Apply() => Apply(SettingsManager.Settings, HasFocus);

        public static void Apply(SettingsManager.SettingContainer settings, bool hasFocus)
        {
            if (settings == null) return;
            bool background = !hasFocus && settings.BackgroundFpsCap.Value > 0;
            QualitySettings.vSyncCount = !background && settings.VSync.Value ? 1 : 0;
            int cap = background ? settings.BackgroundFpsCap.Value : settings.FpsCap.Value;
            // Unity's documented unlimited sentinel is -1; the UI uses 0.
            Application.targetFrameRate = cap > 0 ? cap : -1;
        }
    }
}
