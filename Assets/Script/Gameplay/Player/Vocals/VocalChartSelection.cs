using System.Linq;
using YARG.Core.Chart;
using YARG.Player;

namespace YARG.Gameplay.Player
{
    public static class VocalChartSelection
    {
        /// <summary>
        /// True when the Harmony chart has at least one phrase across any part.
        /// Older solo-only songs (e.g. Creep) load 3 empty HARM placeholder parts;
        /// this checks for actual phrase content, not just Parts.Count.
        /// </summary>
        public static bool HasHarmonyContent(SongChart chart) =>
            chart.Harmony.Parts.Any(p => p.NotePhrases.Count > 0);

        /// <summary>
        /// Resolves which vocal multitrack to use for a given profile.
        /// - Non-Free profiles: delegates to the chart's instrument-based resolver
        ///   (Solo → Vocals, Harmony → Harmony, including empty HARM parts for
        ///   HarmonyIndex selection).
        /// - Free/Party profiles: prefer Harmony if it has phrases, else Solo.
        /// </summary>
        public static VocalsTrack ResolveMultitrack(SongChart chart, YargProfile profile)
        {
            if (!profile.IsFreeVocals)
                return chart.GetVocalsTrack(profile.CurrentInstrument);

            return HasHarmonyContent(chart) ? chart.Harmony : chart.Vocals;
        }
    }
}
