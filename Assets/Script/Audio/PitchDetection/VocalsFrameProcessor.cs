#nullable enable
using System;
using UnityEngine;
using YARG.Core.Audio;

namespace YARG.Audio.PitchDetection
{
    /// <summary>Observations for one completed frame; never used to decide gameplay output.</summary>
    public readonly struct FrameDiagnostic
    {
        public double FrameEndTime { get; }
        public float Amplitude { get; }
        public bool GateOpen { get; }
        public string Detection { get; }
        public bool Hit { get; }
        public int OutputCount { get; }

        public FrameDiagnostic(double frameEndTime, float amplitude, bool gateOpen, string detection,
            bool hit, int outputCount)
        {
            FrameEndTime = frameEndTime;
            Amplitude = amplitude;
            GateOpen = gateOpen;
            Detection = detection;
            Hit = hit;
            OutputCount = outputCount;
        }
    }

    /// <summary>
    /// Stateful 40 ms sample framing and pitch decisions shared by the live microphone and offline measurements.
    /// Times are an approximation: a partial frame uses the completing read's clock; no driver latency is compensated.
    /// The backlog is sampled before each read, and only the samples consumed within that read reduce it.
    /// </summary>
    public sealed class VocalsFrameProcessor
    {
        private const float HIT_THRESHOLD_DB = 25f;
        private const float SILENCE_FLOOR_DB = -160f;
        private const float CALIBRATION_GAIN = 180f;
        private const float UNAVAILABLE_VALUE = -1f;
        private const int AMPLITUDE_STRIDE = 4;

        private readonly int _sampleRate;
        private readonly float[] _frameBuffer;
        private readonly PitchTracker _pitchTracker;
        private int _frameSamples;
        private float? _lastAmplitude;
        private float? _lastPitch;

        /// <summary>Optional per-completed-frame observer, including frames that produce no output.</summary>
        public Action<FrameDiagnostic>? Diagnostics { get; set; }

        public VocalsFrameProcessor(int sampleRate)
        {
            if (sampleRate <= 0 || checked(sampleRate * MicDevice.RECORD_PERIOD_MS) % 1000 != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "40 ms must contain an integral number of samples");
            }

            _sampleRate = sampleRate;
            _frameBuffer = new float[checked(sampleRate * MicDevice.RECORD_PERIOD_MS / 1000)];
            _pitchTracker = new PitchTracker(sampleRate);
        }

        /// <param name="backlogBytesBeforeRead">Source backlog in bytes, queried before this read.</param>
        /// <param name="readTimeBeforeRead">Input clock queried before this read, in seconds.</param>
        public void Process(float[] samples, int count, long backlogBytesBeforeRead, double readTimeBeforeRead,
            float sensitivity, Action<MicOutputFrame, FrameDiagnostic> output)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (count < 0 || count > samples.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (backlogBytesBeforeRead < 0) throw new ArgumentOutOfRangeException(nameof(backlogBytesBeforeRead));

            long backlogSamples = backlogBytesBeforeRead / sizeof(float);
            int offset = 0;
            while (offset < count)
            {
                int samplesToCopy = Math.Min(count - offset, _frameBuffer.Length - _frameSamples);
                Array.Copy(samples, offset, _frameBuffer, _frameSamples, samplesToCopy);
                _frameSamples += samplesToCopy;
                offset += samplesToCopy;

                // Intentionally preserve the live worker's per-read approximation, not an audio-sample clock.
                double frameEndTime = readTimeBeforeRead - Math.Max(0L, backlogSamples - offset) / (double) _sampleRate;
                if (_frameSamples == _frameBuffer.Length)
                {
                    AnalyzeFrame(frameEndTime, sensitivity, output);
                    _frameSamples = 0;
                }
            }
        }

        public void Reset()
        {
            _lastPitch = null;
            _lastAmplitude = null;
            _frameSamples = 0;
            Array.Clear(_frameBuffer, 0, _frameBuffer.Length);
            _pitchTracker.Reset();
        }

        private void AnalyzeFrame(double frameEndTime, float sensitivity,
            Action<MicOutputFrame, FrameDiagnostic> output)
        {
            float amplitude = MeasureAmplitude(_frameBuffer);
            bool hit = _lastAmplitude is { } previous && amplitude - previous >= HIT_THRESHOLD_DB;
            _lastAmplitude = amplitude;
            bool gateOpen = amplitude >= sensitivity;
            string detection = "Silent";
            int outputCount = hit ? 1 : 0;
            float? pitch = null;
            if (!gateOpen)
            {
                _lastPitch = null;
            }
            else
            {
                float? fresh = _pitchTracker.ProcessBuffer(_frameBuffer);
                detection = fresh.HasValue ? "Fresh" : _lastPitch.HasValue ? "Held" : "Silent";
                _lastPitch = fresh ?? _lastPitch;
                pitch = _lastPitch;
                if (pitch.HasValue) outputCount++;
            }

            var diagnostic = new FrameDiagnostic(frameEndTime, amplitude, gateOpen, detection, hit, outputCount);
            if (hit)
            {
                output(new MicOutputFrame(frameEndTime - MicDevice.RECORD_PERIOD_MS / 2000.0,
                    true, UNAVAILABLE_VALUE, UNAVAILABLE_VALUE), diagnostic);
            }
            if (pitch is { } value)
            {
                output(new MicOutputFrame(frameEndTime, false, value, amplitude), diagnostic);
            }
            Diagnostics?.Invoke(diagnostic);
        }

        private static float MeasureAmplitude(ReadOnlySpan<float> samples)
        {
            float sumOfSquares = 0f;
            int sampleCount = 0;
            for (int i = 0; i < samples.Length; i += AMPLITUDE_STRIDE)
            {
                sumOfSquares += samples[i] * samples[i];
                sampleCount++;
            }

            float rootMeanSquare = Mathf.Sqrt(sumOfSquares / sampleCount);
            float decibels = 20f * Mathf.Log10(rootMeanSquare * CALIBRATION_GAIN);
            return decibels < SILENCE_FLOOR_DB || float.IsNaN(decibels) ? SILENCE_FLOOR_DB : decibels;
        }
    }
}
