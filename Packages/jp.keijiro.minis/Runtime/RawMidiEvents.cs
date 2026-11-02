// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Minis
{
    /// <summary>One physical message, before all-channel and channel-specific device fan-out.</summary>
    public readonly struct RawMidiMessage
    {
        public long PortId { get; }
        public string PortName { get; }
        public double DriverDeltaSeconds { get; }
        public long ReceivedTimestamp { get; }
        public byte Status { get; }
        public byte Data1 { get; }
        public byte Data2 { get; }
        public int DataLength => (Status & 0xF0) == 0xD0 ? 1 : 2;

        internal static bool TryCreate(long portId, string portName, double driverDeltaSeconds,
            byte status, byte data1, byte data2, int messageLength, out RawMidiMessage message)
        {
            message = default;
            int kind = status & 0xF0;
            if (kind != 0x80 && kind != 0x90 && kind != 0xA0 && kind != 0xB0 && kind != 0xD0)
                return false;
            int dataLength = kind == 0xD0 ? 1 : 2;
            if (messageLength < dataLength + 1 || data1 > 127 || (dataLength == 2 && data2 > 127))
                return false;
            message = new RawMidiMessage(portId, portName, driverDeltaSeconds, status, data1,
                dataLength == 1 ? (byte)0 : data2);
            return true;
        }

        internal RawMidiMessage(long portId, string portName, double driverDeltaSeconds,
            byte status, byte data1, byte data2)
        {
            PortId = portId;
            PortName = portName;
            DriverDeltaSeconds = driverDeltaSeconds;
            ReceivedTimestamp = Stopwatch.GetTimestamp();
            Status = status;
            Data1 = data1;
            Data2 = data2;
        }
    }

    /// <summary>Bounded first-message staging while Unity creates its channel devices.
    /// SourceReady runs before ChannelReady; published records therefore have resolvable identities.</summary>
    internal sealed class RawMidiDelivery
    {
        private readonly object _sync = new object();
        private readonly Queue<RawMidiMessage> _pending = new Queue<RawMidiMessage>();
        private readonly bool[] _readyChannels = new bool[16];
        private readonly long[] _dropped = new long[16];
        private readonly long _portId;
        private readonly int _capacity;
        private bool _allChannelsReady;

        internal RawMidiDelivery(long portId, int capacity = 512)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _portId = portId;
            _capacity = capacity;
        }

        internal void Enqueue(RawMidiMessage message)
        {
            lock (_sync)
            {
                if (_pending.Count >= _capacity) _dropped[message.Status & 15]++;
                else _pending.Enqueue(message);
                FlushReady();
            }
        }

        internal void ChannelReady(int channel)
        {
            lock (_sync)
            {
                if (channel == -1) _allChannelsReady = true;
                else if (channel >= 0 && channel < 16) _readyChannels[channel] = true;
                FlushReady();
            }
        }

        // Callbacks remain under this per-port lock so a main-thread readiness flush
        // cannot overtake an RtMidi callback and reverse note/CC receipt order.
        private void FlushReady()
        {
            if (!_allChannelsReady) return;
            for (int channel = 0; channel < 16; channel++)
            {
                if (!_readyChannels[channel] || _dropped[channel] == 0) continue;
                long count = _dropped[channel];
                _dropped[channel] = 0;
                RawMidiEvents.PublishDropped(_portId, channel, count);
            }
            while (_pending.Count > 0 && _readyChannels[_pending.Peek().Status & 15])
                RawMidiEvents.PublishDetailed(_pending.Dequeue());
        }
    }

    /// <summary>
    /// Receives selected raw MIDI messages directly from the RtMidi input thread.
    /// Timestamp is RtMidi's per-port delta since the previous received event, not wall-clock time.
    /// </summary>
    public static class RawMidiEvents
    {
        /// <summary>Port name, RtMidi timestamp delta in seconds, and original status/data bytes.</summary>
        public static event Action<string, double, byte, byte, byte> MessageReceived;
        /// <summary>Receipt-clock evidence and transient source identity. May run on the RtMidi
        /// thread or the main thread for initially staged messages; subscribers must synchronize.
        /// PortId identifies this open connection only; it is never a persistent binding identifier.</summary>
        public static event Action<RawMidiMessage> DetailedMessageReceived;
        /// <summary>Port, zero-based channel, and count lost in bounded source-readiness staging.</summary>
        public static event Action<long, int, long> DetailedMessagesDropped;
        public static event Action<string> PortOpened;
        public static event Action<string> PortClosed;

        internal static bool HasSubscribers => MessageReceived != null || DetailedMessageReceived != null;
        internal static bool HasDetailedSubscribers => DetailedMessageReceived != null;

        internal static void NotifyPortOpened(string portName) => PortOpened?.Invoke(portName);
        internal static void NotifyPortClosed(string portName) => PortClosed?.Invoke(portName);

        internal static void Publish(long portId, string portName, double timestamp, byte status, byte data1, byte data2)
        {
            PublishDetailed(new RawMidiMessage(portId, portName, timestamp, status, data1, data2));
            PublishLegacy(portName, timestamp, status, data1, data2);
        }

        internal static void PublishDetailed(RawMidiMessage message) => DetailedMessageReceived?.Invoke(message);
        internal static void PublishLegacy(string portName, double timestamp, byte status, byte data1, byte data2) =>
            MessageReceived?.Invoke(portName, timestamp, status, data1, data2);
        internal static void PublishDropped(long portId, int channel, long count) =>
            DetailedMessagesDropped?.Invoke(portId, channel, count);
    }
}
