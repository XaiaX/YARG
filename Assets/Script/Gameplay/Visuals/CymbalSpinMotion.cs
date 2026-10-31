using System;

// pattern: Functional Core
namespace YARG.Gameplay.Visuals
{
    /// <summary>Song-time rotation in the cymbal's circular, pre-deformation frame.</summary>
    public static class CymbalSpinMotion
    {
        public const float DEFAULT_BEATS_PER_REVOLUTION = 4f;

        public static float EvaluateAngle(double beat, float beatsPerRevolution = DEFAULT_BEATS_PER_REVOLUTION)
        {
            if (double.IsNaN(beat) || double.IsInfinity(beat)) return 0f;
            if (float.IsNaN(beatsPerRevolution) || float.IsInfinity(beatsPerRevolution) || beatsPerRevolution <= 0f)
                beatsPerRevolution = DEFAULT_BEATS_PER_REVOLUTION;
            // Modulo first avoids overflow for large beat values and tiny positive periods.
            double phase = beat % beatsPerRevolution;
            if (phase < 0d) phase += beatsPerRevolution;
            float angle = (float)(phase / beatsPerRevolution * 360d);
            return angle >= 360f ? 0f : angle;
        }
    }
}