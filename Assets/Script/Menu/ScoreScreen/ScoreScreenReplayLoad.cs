using System;
using System.Collections.Generic;
using YARG.Core;

namespace YARG.Menu.ScoreScreen
{
    /// <summary>
    /// Small, side-effect-free seam for replay consumers that must select chart outputs from
    /// replay provenance before loading the chart. Keeping this orchestration separate makes the
    /// ordering testable without constructing the score-screen MonoBehaviour.
    /// </summary>
    public static class ScoreScreenReplayLoad
    {
        public static Result<TReplay, TChart> TryReadThenLoadChart<TReplay, TChart>(
            Func<(bool Success, TReplay Data)> readReplay,
            Func<TReplay, IReadOnlyCollection<Instrument>> getChartOutputs,
            Func<IReadOnlyCollection<Instrument>, TChart> loadChart)
            where TChart : class
        {
            if (readReplay is null) throw new ArgumentNullException(nameof(readReplay));
            if (getChartOutputs is null) throw new ArgumentNullException(nameof(getChartOutputs));
            if (loadChart is null) throw new ArgumentNullException(nameof(loadChart));

            var replay = readReplay();
            if (!replay.Success)
            {
                return new Result<TReplay, TChart>(false, default, null);
            }

            var outputs = getChartOutputs(replay.Data);
            var chart = loadChart(outputs);
            return new Result<TReplay, TChart>(chart != null, replay.Data, chart);
        }

        public sealed class Result<TReplay, TChart>
            where TChart : class
        {
            internal Result(bool success, TReplay data, TChart chart)
            {
                Success = success;
                Data = data;
                Chart = chart;
            }

            public bool Success { get; }
            public TReplay Data { get; }
            public TChart Chart { get; }
        }
    }
}
