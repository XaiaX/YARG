// pattern: Functional Core
using System;

namespace YARG.Persistent
{
    public readonly struct FrameTimingStatistics
    {
        public readonly double AverageMs, P95Ms, WorstMs;
        public readonly int OverBudget;

        private FrameTimingStatistics(double average, double p95, double worst, int overBudget)
        {
            AverageMs = average;
            P95Ms = p95;
            WorstMs = worst;
            OverBudget = overBudget;
        }

        // Work on a copy: callers retain the original chronological history for plotting.
        public static FrameTimingStatistics Calculate(double[] samples, int count, double budgetMs)
        {
            count = Math.Min(count, samples.Length);
            if (count <= 0) return default;
            var sorted = new double[count];
            double total = 0;
            int over = 0;
            for (int i = 0; i < count; i++)
            {
                sorted[i] = samples[i];
                total += samples[i];
                // Leave a little tolerance for ordinary timer jitter around the cap.
                if (budgetMs > 0 && samples[i] > budgetMs * 1.05) over++;
            }
            Array.Sort(sorted);
            return new FrameTimingStatistics(total / count, sorted[(int)Math.Ceiling(count * 0.95) - 1],
                sorted[count - 1], over);
        }

        public static double BudgetMs(int vsync, int targetFps, double refreshHz)
        {
            if (vsync > 0 && refreshHz > 0) return 1000 * vsync / refreshHz;
            if (targetFps > 0) return 1000.0 / targetFps;
            // Uncapped: a display reference, not a claimed application cap.
            return refreshHz > 0 ? 1000 / refreshHz : 0;
        }
    }
}
