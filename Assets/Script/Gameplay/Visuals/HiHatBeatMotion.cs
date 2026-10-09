using System;
using UnityEngine;
using YARG.Core.Chart;

// pattern: Functional Core
namespace YARG.Gameplay.Visuals
{
    public static class HiHatBeatMotion
    {
        public static double EvaluateBeatPhase(double visualTime, SyncTrack sync)
        {
            if (sync == null || sync.Resolution == 0 || sync.Tempos.Count == 0 ||
                double.IsNaN(visualTime) || double.IsInfinity(visualTime)) return 0d;
            var tempo = sync.Tempos.LowerBoundElement(visualTime) ?? sync.Tempos[0];
            if (tempo.SecondsPerBeat <= 0d) return 0d;
            // Fractional signed ticks keep motion smooth through preroll and tempo changes.
            double tick = tempo.Tick + (visualTime - tempo.Time) * sync.Resolution / tempo.SecondsPerBeat;
            return tick / sync.Resolution;
        }

        public static double EvaluateNotePhase(double visualTime, double noteTime, SyncTrack sync)
        {
            // The highway reaches the strike line when VisualTime equals NoteRef.Time. Convert
            // both times rather than using the rounded note tick, so arrival is exactly neutral.
            return EvaluateBeatPhase(visualTime, sync) - EvaluateBeatPhase(noteTime, sync);
        }

        public static float EvaluateAngle(double phase, float degrees)
        {
            if (double.IsNaN(phase) || double.IsInfinity(phase) ||
                float.IsNaN(degrees) || float.IsInfinity(degrees)) return 0f;
            phase -= Math.Floor(phase);
            double sway = Math.Sin(phase * Math.PI * 2d);
            // Smoothstep the signed sine: linger near the edges with smooth turnarounds,
            // while crossing center 1.5 times faster than the original sine.
            sway *= 1.5d - 0.5d * sway * sway;
            return (float) sway * Math.Abs(degrees);
        }

        public static float EvaluateArrivalFade(double remainingBeats, float fadeBeats)
        {
            if (double.IsNaN(remainingBeats) || double.IsInfinity(remainingBeats) ||
                float.IsNaN(fadeBeats) || float.IsInfinity(fadeBeats) || fadeBeats <= 0f) return 0f;
            double t = Math.Min(Math.Max(remainingBeats / fadeBeats, 0d), 1d);
            // Keep a larger excursion through the approach, then decelerate into neutral.
            // Smoothstep preserves zero slope at arrival; the remap avoids a long near-static tail.
            t *= 2d - t;
            return (float) (t * t * (3d - 2d * t));
        }

        public static void EvaluateRockPose(double beatPhase, double remainingBeats, float fadeBeats,
            Vector3 restPosition, Quaternion restRotation, Vector3 backPosition, Quaternion backRotation,
            Vector3 forwardPosition, Quaternion forwardRotation, out Vector3 position, out Quaternion rotation)
        {
            position = restPosition;
            rotation = restRotation;
            float fade = EvaluateArrivalFade(remainingBeats, fadeBeats);
            if (fade == 0f || double.IsNaN(beatPhase) || double.IsInfinity(beatPhase)) return;
            // Full forward/back cycle is two quarter-note beats, with peaks on successive beats.
            beatPhase -= Math.Floor(beatPhase / 2d) * 2d;
            double wave = -Math.Cos(beatPhase * Math.PI);
            float excursion = (float) Math.Abs(wave);
            // Ease through each authored pose. The endpoints can have unequal displacement;
            // zero slope at neutral avoids a velocity jump when switching between them.
            excursion = excursion * excursion * (3f - 2f * excursion);
            float amount = excursion * fade;
            position = Vector3.Lerp(restPosition, wave >= 0d ? forwardPosition : backPosition, amount);
            rotation = Quaternion.Slerp(restRotation, wave >= 0d ? forwardRotation : backRotation, amount);
        }

        public static void EvaluatePose(double phase, float degrees, Vector3 restPosition,
            Quaternion restRotation, Vector3 scale, Vector3 pivotLocal,
            out Vector3 position, out Quaternion rotation)
        {
            var pivotOffset = Vector3.Scale(scale, pivotLocal);
            var fixedTip = restPosition + restRotation * pivotOffset;
            // Rock sideways in the assembly's plane, compensating translation to keep the tip
            // fixed. The imported mesh origin is at the base, not at the cymbal's mount.
            rotation = Quaternion.AngleAxis(EvaluateAngle(phase, degrees), Vector3.forward) * restRotation;
            position = fixedTip - rotation * pivotOffset;
        }
    }
}
