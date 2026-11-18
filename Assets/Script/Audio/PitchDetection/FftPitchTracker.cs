// pattern: Functional Core
#if UNITY_EDITOR
using System;

namespace YARG.Audio.PitchDetection
{
    /// <summary>Estimates one fundamental from the harmonic peaks of a trailing spectrum.</summary>
    internal sealed class FftPitchTracker
    {
        public const int WINDOW_MS = 60;
        public const int HOP_MS = 10;
        private const double MIN_HZ = 50;
        private const double MAX_HZ = 1600;
        private const int HARMONICS = 8;
        private const double MAX_PEAK_HZ = 4000;
        private const double MIN_WINDOW_RMS = 0.001;
        private const double MIN_PEAK_FRACTION = 0.05;

        private readonly float[] _history;
        private readonly double[] _window;
        private readonly double[] _real;
        private readonly double[] _imag;
        private readonly double[] _magnitudes;
        private readonly (double Hz, double Amplitude)[] _peaks;
        private readonly double[] _harmonicAmplitudes = new double[HARMONICS];
        private readonly double _binHz;
        private readonly int _hop;
        private int _writeIndex;
        private int _samplesToEstimate;

        public FftPitchTracker(int sampleRate)
        {
            if (sampleRate < 8000)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));

            int windowSize = sampleRate * WINDOW_MS / 1000;
            _hop = sampleRate * HOP_MS / 1000;
            int fftSize = 1;
            while (fftSize < 2 * windowSize)
                fftSize <<= 1;

            _history = new float[windowSize];
            _window = new double[windowSize];
            for (int i = 0; i < windowSize; i++)
                _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (windowSize - 1));

            _real = new double[fftSize];
            _imag = new double[fftSize];
            _binHz = (double) sampleRate / fftSize;
            int lastBin = Math.Min(fftSize / 2, (int) (MAX_PEAK_HZ / _binHz) + 1);
            _magnitudes = new double[lastBin + 1];
            _peaks = new (double, double)[lastBin];
            Reset();
        }

        public void Reset()
        {
            _writeIndex = 0;
            _samplesToEstimate = _history.Length;
        }

        public float? ProcessBuffer(ReadOnlySpan<float> samples)
        {
            float? latest = null;
            foreach (float sample in samples)
            {
                _history[_writeIndex] = sample;
                _writeIndex = (_writeIndex + 1) % _history.Length;
                if (--_samplesToEstimate == 0)
                {
                    latest = Estimate() ?? latest;
                    _samplesToEstimate = _hop;
                }
            }

            return latest;
        }

        private float? Estimate()
        {
            double energy = 0;
            int source = _writeIndex;
            for (int i = 0; i < _history.Length; i++)
            {
                double sample = _history[source];
                energy += sample * sample;
                _real[i] = sample * _window[i];
                source = (source + 1) % _history.Length;
            }

            if (energy < _history.Length * MIN_WINDOW_RMS * MIN_WINDOW_RMS)
                return null;

            Array.Clear(_real, _history.Length, _real.Length - _history.Length);
            Array.Clear(_imag, 0, _imag.Length);
            Transform(_real, _imag);
            int firstBin = Math.Max(2, (int) (MIN_HZ / _binHz));
            double maximum = 0;
            for (int bin = firstBin - 1; bin < _magnitudes.Length; bin++)
            {
                double magnitude = Math.Sqrt(_real[bin] * _real[bin] + _imag[bin] * _imag[bin]);
                _magnitudes[bin] = magnitude;
                if (bin >= firstBin)
                    maximum = Math.Max(maximum, magnitude);
            }

            int count = 0;
            for (int bin = firstBin; bin < _magnitudes.Length - 1; bin++)
            {
                double left = _magnitudes[bin - 1];
                double middle = _magnitudes[bin];
                double right = _magnitudes[bin + 1];
                if (middle <= maximum * MIN_PEAK_FRACTION || middle <= left || middle < right)
                    continue;
                double offset = 0.5 * (left - right) / (left - 2 * middle + right);
                _peaks[count++] = ((bin + offset) * _binHz, middle);
            }

            double bestScore = 0;
            double bestHz = 0;
            for (int i = 0; i < count; i++)
            {
                for (int divisor = 1; divisor <= HARMONICS; divisor++)
                {
                    double candidate = _peaks[i].Hz / divisor;
                    if (candidate < MIN_HZ - _binHz || candidate > MAX_HZ + _binHz)
                        continue;
                    candidate = Math.Clamp(candidate, MIN_HZ, MAX_HZ);

                    double score = ScoreHarmonics(candidate, count);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestHz = candidate;
                    }
                }
            }

            return bestScore > 0 ? (float) bestHz : (float?) null;
        }

        private double ScoreHarmonics(double candidate, int count)
        {
            Array.Clear(_harmonicAmplitudes, 0, HARMONICS);
            for (int i = 0; i < count; i++)
            {
                var peak = _peaks[i];
                int harmonic = (int) Math.Round(peak.Hz / candidate);
                if (harmonic < 1 || harmonic > HARMONICS)
                    continue;

                double expected = candidate * harmonic;
                double tolerance = Math.Max(1.5 * _binHz, expected * 0.012);
                double distance = Math.Abs(peak.Hz - expected);
                if (distance >= tolerance)
                    continue;

                double weighted = peak.Amplitude * (1 - distance / tolerance) / Math.Sqrt(harmonic);
                _harmonicAmplitudes[harmonic - 1] = Math.Max(_harmonicAmplitudes[harmonic - 1], weighted);
            }

            double score = 0;
            foreach (double amplitude in _harmonicAmplitudes)
                score += amplitude;
            return score;
        }

        private static void Transform(double[] real, double[] imag)
        {
            for (int i = 1, reversed = 0; i < real.Length; i++)
            {
                int bit = real.Length >> 1;
                while ((reversed & bit) != 0)
                {
                    reversed ^= bit;
                    bit >>= 1;
                }

                reversed ^= bit;
                if (i < reversed)
                    (real[i], real[reversed]) = (real[reversed], real[i]);
            }

            for (int length = 2; length <= real.Length; length <<= 1)
            {
                double stepReal = Math.Cos(-2 * Math.PI / length);
                double stepImag = Math.Sin(-2 * Math.PI / length);
                int half = length / 2;
                for (int start = 0; start < real.Length; start += length)
                {
                    double twiddleReal = 1;
                    double twiddleImag = 0;
                    for (int i = start; i < start + half; i++)
                    {
                        int other = i + half;
                        double re = real[other] * twiddleReal - imag[other] * twiddleImag;
                        double im = real[other] * twiddleImag + imag[other] * twiddleReal;
                        real[other] = real[i] - re;
                        imag[other] = imag[i] - im;
                        real[i] += re;
                        imag[i] += im;
                        (twiddleReal, twiddleImag) = (twiddleReal * stepReal - twiddleImag * stepImag,
                            twiddleReal * stepImag + twiddleImag * stepReal);
                    }
                }
            }
        }
    }
}
#endif
