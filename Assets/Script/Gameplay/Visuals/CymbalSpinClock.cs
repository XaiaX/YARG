// pattern: Imperative Shell
using System.Collections.Generic;
using UnityEngine;
using YARG.Core.Chart;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Shares one phase sample and one rotation per period at each song timestamp.</summary>
    public sealed class CymbalSpinClock
    {
        private SyncTrack _sync;
        private double _time = double.NaN;
        private double _beat;
        private readonly Dictionary<float, Quaternion> _rotations = new();

        public Quaternion Evaluate(SyncTrack sync, double visualTime, float period, bool leftyFlip)
        {
            // Key by the actual visual time rather than frame count: pause holds
            // exactly, and seek/chart replacement invalidates even in the same frame.
            if (!ReferenceEquals(sync, _sync) || visualTime != _time)
            {
                _sync = sync;
                _time = visualTime;
                _beat = HiHatBeatMotion.EvaluateBeatPhase(visualTime, sync);
                _rotations.Clear();
            }
            if (!_rotations.TryGetValue(period, out var rotation))
            {
                rotation = Quaternion.AngleAxis(-CymbalSpinMotion.EvaluateAngle(_beat, period), Vector3.forward);
                _rotations.Add(period, rotation);
            }
            // A unit Z rotation's conjugate reverses it without another trig call.
            return leftyFlip ? new Quaternion(0f, 0f, -rotation.z, rotation.w) : rotation;
        }
    }
}