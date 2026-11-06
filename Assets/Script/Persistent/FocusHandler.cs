// pattern: Imperative Shell
using UnityEngine;
using YARG.Core.Audio;
using YARG.Settings;

namespace YARG
{
    /// <summary>
    /// Handles behavior that reacts to the application window gaining or losing focus,
    /// such as muting audio and capping the framerate while unfocused.
    /// </summary>
    public class FocusHandler : MonoSingleton<FocusHandler>
    {
        // Tracks whether audio was muted because the window lost focus,
        // so it can be restored when focus returns.
        private bool _mutedFromFocusLoss;

        public bool HasFocus { get; private set; }

        protected override void SingletonAwake()
        {
            HasFocus = Application.isFocused;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            HasFocus = hasFocus;
            if (SettingsManager.Settings == null) return;
            FramePacing.Apply(SettingsManager.Settings, hasFocus);
            if (!hasFocus)
            {
                if (SettingsManager.Settings.MuteOnFocusLoss.Value && !_mutedFromFocusLoss)
                {
                    GlobalAudioHandler.SetMasterVolume(0);
                    _mutedFromFocusLoss = true;
                }

            }
            else
            {
                if (_mutedFromFocusLoss)
                {
                    GlobalAudioHandler.SetMasterVolume(SettingsManager.Settings.MasterMusicVolume.Value);
                    _mutedFromFocusLoss = false;
                }

            }
        }
    }
}
