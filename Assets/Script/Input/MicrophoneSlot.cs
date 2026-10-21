using System;
using YARG.Core.Audio;

namespace YARG.Input
{
    public sealed class MicrophoneSlot
    {
        public SerializedMic Identity { get; }
        public MicDevice Device { get; internal set; }
        public int NeedleId { get; internal set; }
        private float _sensitivityOffsetDb;
        public float SensitivityOffsetDb
        {
            get => _sensitivityOffsetDb;
            internal set => _sensitivityOffsetDb = float.IsNaN(value) || float.IsInfinity(value)
                ? 0f : Math.Clamp(value, -50f, 50f);
        }
        public bool IsConnected => Device != null && Device.IsConnected;

        public MicrophoneSlot(SerializedMic identity, int needleId = 0, float sensitivityOffsetDb = 0f)
        {
            Identity = identity;
            SensitivityOffsetDb = sensitivityOffsetDb;
            NeedleId = needleId >= 1 && needleId <= 7 ? needleId : 0;
        }
    }
}
