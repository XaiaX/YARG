// pattern: Functional Core
namespace YARG.Input
{
    public sealed class MicrophoneBindingSettings
    {
        public int NeedleId { get; }
        public float SensitivityOffsetDb { get; }

        public MicrophoneBindingSettings(int needleId = 0, float sensitivityOffsetDb = 0f)
        {
            NeedleId = MicrophoneSetupValues.NormalizeNeedle(needleId);
            SensitivityOffsetDb = MicrophoneSetupValues.NormalizeSensitivity(sensitivityOffsetDb);
        }
    }
}
