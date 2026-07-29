using System;

namespace YARG.Integration.Maestro
{
    /// <summary>
    /// Pure validation/normalization helpers for Maestro values. No Unity/scene state.
    /// Bounds mirror the existing UI/setting semantics so Maestro cannot invent new ranges.
    /// </summary>
    public static class MaestroValidation
    {
        // VolumeSetting range (SettingsManager.Settings): 0f..1f.
        public const float VolumeMin = 0f;
        public const float VolumeMax = 1f;

        // Highway Speed (NoteSpeed): 0..100; Highway Length: 0.1..10 — one-decimal semantics.
        public const float NoteSpeedMin = 0f;
        public const float NoteSpeedMax = 100f;
        public const float HighwayLengthMin = 0.1f;
        public const float HighwayLengthMax = 10f;

        public static float ClampVolume(float value)
            => Math.Clamp(value, VolumeMin, VolumeMax);

        public static float ClampNoteSpeed(float value)
            => Math.Clamp(value, NoteSpeedMin, NoteSpeedMax);

        public static float ClampHighwayLength(float value)
            => Math.Clamp(value, HighwayLengthMin, HighwayLengthMax);

        /// <summary>
        /// Normalizes a one-decimal highway value (speed/length) to a single decimal place,
        /// matching the existing UI input semantics.
        /// </summary>
        public static float NormalizeHighwayDecimal(float value)
            => (float) Math.Round(value, 1, MidpointRounding.AwayFromZero);

        /// <summary>True when <paramref name="value"/> is a finite number within [min, max].</summary>
        public static bool IsInRange(float value, float min, float max)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
    }
}
