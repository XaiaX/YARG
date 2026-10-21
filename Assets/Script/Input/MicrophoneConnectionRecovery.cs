using System;
using System.Collections.Generic;
using YARG.Core.Audio;

namespace YARG.Input
{
    public static class MicrophoneConnectionRecovery
    {
        public static bool DisconnectUnavailable(IReadOnlyList<MicrophoneSlot> slots)
        {
            bool changed = false;
            foreach (var slot in slots)
            {
                if (slot.Device == null || slot.IsConnected) continue;
                slot.Device.Dispose();
                slot.Device = null;
                changed = true;
            }
            return changed;
        }

        public static bool Resolve(IReadOnlyList<MicrophoneSlot> slots, Func<string, int, MicDevice> create)
        {
            bool changed = false;
            foreach (var slot in slots)
            {
                if (slot.Device != null) continue;
                slot.Device = create(slot.Identity.BaseName, slot.Identity.Channel);
                changed |= slot.Device != null;
            }
            return changed;
        }
    }
}
