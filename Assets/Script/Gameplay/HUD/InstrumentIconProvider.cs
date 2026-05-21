using UnityEngine;
using YARG.Core;
using YARG.Core.Engine;
using YARG.Core.Logging;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;
using YARG.Player;

namespace YARG.Gameplay.HUD
{
    public static class InstrumentIconProvider
    {
        public static string GetInstrumentSprite(this EngineManager.EngineContainer container)
        {
            return GetInstrumentSprite(container.Instrument, container.HarmonyIndex, false);
        }

        public static string GetInstrumentSprite(this YargPlayer player)
        {
            var isMissingDevice = player.IsMissingInputDevice || player.IsMissingMicrophone;
            return GetInstrumentSprite(player.Profile.CurrentInstrument, player.Profile.HarmonyIndex, isMissingDevice);
        }

        private static string GetInstrumentSprite(Instrument instrument, int harmonyIndex, bool isMissingDevice)
        {
            if (isMissingDevice)
            {
                return $"NoInstrumentIcons[{instrument.ToResourceName()}]";
            }

            if (instrument == Instrument.Harmony)
            {
                return $"HarmonyVocalsIcons[{harmonyIndex + 1}]";
            }

            return $"InstrumentIcons[{instrument.ToResourceName()}]";
        }

        public static Color GetHarmonyColor(this YargPlayer player)
        {
            return GetHarmonyColor(player.Profile.CurrentInstrument, player.Profile.HarmonyIndex, player.IsMissingInputDevice || player.IsMissingMicrophone, player.SittingOut);
        }

        public static Color GetHarmonyColor(this EngineManager.EngineContainer container)
        {
            return GetHarmonyColor(container.Instrument, container.HarmonyIndex, false, false);
        }

        private static Color GetHarmonyColor(Instrument instrument, int harmonyIndex, bool isMissingDevice, bool isSittingOut)
        {
            if (isMissingDevice)
            {
                // NoInstrumentIcons are coloured to begin with - don't override that.
                return Color.white;
            }
            if (isSittingOut)
            {
                return Color.gray;
            }
            if (instrument != Instrument.Harmony)
            {
                return Color.white;
            }

            // Free Vocals engines register with sentinel HarmonyIndex = -1 (they aren't locked
            // to a single HARM line). Fall back to a neutral color rather than indexing with -1.
            if (harmonyIndex < 0 || harmonyIndex >= VocalTrack.Colors.Length)
            {
                if (harmonyIndex >= VocalTrack.Colors.Length)
                {
                    YargLogger.LogWarning("PlayerNameDisplay", $"Harmony index {harmonyIndex} is out of bounds.");
                }
                return Color.white;
            }

            return VocalTrack.Colors[harmonyIndex];
        }
    }
}
