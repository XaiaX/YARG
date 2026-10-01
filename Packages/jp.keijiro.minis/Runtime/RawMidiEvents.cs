using System;
using System.Collections.Generic;

namespace Minis
{
    /// <summary>
    /// Receives selected raw MIDI messages directly from the RtMidi input thread.
    /// Timestamp is RtMidi's per-port delta since the previous received event, not wall-clock time.
    /// </summary>
    public static class RawMidiEvents
    {
        /// <summary>Port name, RtMidi timestamp delta in seconds, and original status/data bytes.</summary>
        public static event Action<string, double, byte, byte, byte> MessageReceived;
        public static event Action<string> PortOpened;
        public static event Action<string> PortClosed;

        internal static bool HasSubscribers => MessageReceived != null;

        internal static void NotifyPortOpened(string portName) => PortOpened?.Invoke(portName);
        internal static void NotifyPortClosed(string portName) => PortClosed?.Invoke(portName);

        internal static void Publish(string portName, double timestamp, byte status, byte data1, byte data2)
        {
            MessageReceived?.Invoke(portName, timestamp, status, data1, data2);
        }
    }
}
