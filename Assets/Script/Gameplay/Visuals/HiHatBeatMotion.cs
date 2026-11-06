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

    }
}
