// pattern: Imperative Shell
using YARG.Audio.BASS;
using YARG.Core.Audio;

namespace YARG.Input
{
    /// <summary>Saved microphone presentation/settings with its current owned capture, if available.</summary>
    public sealed class MicrophoneBinding
    {
        public SerializedMic Identity { get; internal set; }
        public MicDevice Device { get; internal set; }
        public int NeedleId { get; internal set; }
        public float SensitivityOffsetDb { get; internal set; }
        public bool IsConnected => Device is BassMicDevice bass ? bass.IsConnected : Device != null;

        internal MicrophoneBinding(SerializedMic identity, MicrophoneBindingSettings settings = null)
        {
            Identity = identity;
            NeedleId = MicrophoneSetupValues.NormalizeNeedle(settings?.NeedleId ?? 0);
            SensitivityOffsetDb = MicrophoneSetupValues.NormalizeSensitivity(settings?.SensitivityOffsetDb ?? 0f);
        }

        internal MicrophoneBindingSettings Settings() => new(NeedleId, SensitivityOffsetDb);
    }
}
