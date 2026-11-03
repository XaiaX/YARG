// pattern: Functional Core
using System;
using System.Collections.Generic;
using YARG.Core.Audio;

namespace YARG.Input
{
    public static class MicrophoneSetupValues
    {
        public const int NEEDLE_COUNT = 7;

        public static int NormalizeNeedle(int value) => value >= 1 && value <= NEEDLE_COUNT ? value : 0;

        public static float NormalizeSensitivity(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? 0f : Math.Max(-50f, Math.Min(50f, value));

        public static InputDeviceInfo? SelectInput(SerializedMic identity, IReadOnlyList<SerializedMic> saved,
            IReadOnlyList<InputDeviceInfo> available)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            if (available == null) throw new ArgumentNullException(nameof(available));
            foreach (var candidate in available)
            {
                if (candidate.Channel == identity.Channel && !string.IsNullOrEmpty(identity.StableId) &&
                    MicDevice.ComputeStableId(candidate.DeviceId, candidate.Name) == identity.StableId)
                    return candidate;
            }
            foreach (var candidate in available)
            {
                if (candidate.Channel != identity.Channel || candidate.Name != identity.BaseName) continue;
                string candidateId = MicDevice.ComputeStableId(candidate.DeviceId, candidate.Name);
                bool reserved = false;
                foreach (var other in saved)
                {
                    if (other != null && !ReferenceEquals(other, identity) && other.Channel == candidate.Channel &&
                        !string.IsNullOrEmpty(other.StableId) && other.StableId == candidateId)
                    {
                        reserved = true;
                        break;
                    }
                }
                if (!reserved) return candidate;
            }
            return null;
        }

        public static int[] ResolveNeedles(IReadOnlyList<int> choices)
        {
            var used = new bool[NEEDLE_COUNT + 1];
            var result = new int[choices.Count];
            for (int i = 0; i < choices.Count; i++)
            {
                result[i] = NormalizeNeedle(choices[i]);
                if (result[i] > 0) used[result[i]] = true;
            }
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] > 0) continue;
                int design = 1;
                while (design <= NEEDLE_COUNT && used[design]) design++;
                if (design > NEEDLE_COUNT) design = i % NEEDLE_COUNT + 1;
                result[i] = design;
                used[design] = true;
            }
            return result;
        }
    }
}
