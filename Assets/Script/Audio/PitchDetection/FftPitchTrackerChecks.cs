// pattern: Imperative Shell
#if UNITY_EDITOR
using System;
using YARG.Audio.PitchDetection;
using UnityEngine;

namespace YARG.Editor
{
    public static class FftPitchTrackerChecks
    {
        public static void Run()
        {
            foreach (int sampleRate in new[] { 44100, 48000 })
            {
                var detector = new FftPitchTracker(sampleRate);
                int frameSize = sampleRate * 40 / 1000;
                foreach (double hz in new[] { 55.0, 82.41, 220.0, 1046.50, 1600.0 })
                {
                    detector.Reset();
                    var samples = Tone(sampleRate, frameSize * 4, hz, missingFundamental: false);
                    if (detector.ProcessBuffer(samples.AsSpan(0, frameSize)) != null)
                        throw new InvalidOperationException("Pitch emitted before the first window filled.");
                    CheckPitch(detector.ProcessBuffer(samples.AsSpan(frameSize)), hz);
                    detector.Reset();
                    for (int start = 0; start < samples.Length - 1;)
                    {
                        int count = Math.Min(137, samples.Length - 1 - start);
                        detector.ProcessBuffer(samples.AsSpan(start, count));
                        start += count;
                    }
                    // Supply the rest of a full hop so the chunked run produces another result.
                    CheckPitch(detector.ProcessBuffer(Tone(sampleRate, frameSize, hz, false, samples.Length - 1)), hz);
                }

                detector.Reset();
                CheckPitch(detector.ProcessBuffer(Tone(sampleRate, frameSize * 4, 220, true)), 220);
                CheckPitch(detector.ProcessBuffer(Tone(sampleRate, frameSize * 4, 330, false)), 330);
                detector.Reset();
                if (detector.ProcessBuffer(new float[frameSize * 4]) != null)
                    throw new InvalidOperationException("Silence returned a pitch.");
            }

            Debug.Log("FFT pitch checks passed at 44.1 and 48 kHz.");
        }

        private static float[] Tone(int sampleRate, int count, double hz, bool missingFundamental, int start = 0)
        {
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                double phase = 2 * Math.PI * hz * (start + i) / sampleRate;
                samples[i] = missingFundamental
                    ? (float) (0.45 * Math.Sin(2 * phase) + 0.3 * Math.Sin(3 * phase))
                    : (float) (0.6 * Math.Sin(phase) + 0.25 * Math.Sin(2 * phase));
            }
            return samples;
        }

        private static void CheckPitch(float? actual, double expected)
        {
            if (actual is not float hz || Math.Abs(1200 * Math.Log(hz / expected, 2)) > 30)
                throw new InvalidOperationException($"Expected {expected:F2} Hz, got {actual}.");
        }
    }
}
#endif
