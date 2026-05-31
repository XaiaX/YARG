using System.Linq;
using YARG.Core.Chart;
using YARG.Core.Game;

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
        /// True when the Solo Vocals chart has at least one phrase. Mirror of
        /// <see cref="HasHarmonyContent"/> for the Solo side, used by the Solo
        /// preference's fallback.
        /// </summary>
        public static bool HasSoloContent(SongChart chart) =>
            chart.Vocals.Parts.Any(p => p.NotePhrases.Count > 0);

        /// <summary>
        /// Resolves which vocal multitrack to use for a given profile.
        /// - Non-Free profiles: delegates to the chart's instrument-based resolver
        ///   (Solo → Vocals, Harmony → Harmony, including empty HARM parts for
        ///   HarmonyIndex selection).
        /// - Free/Party profiles: honor the sticky chart preference with graceful
        ///   fallback. Resolution is read-only — it never writes the preference,
        ///   which is what lets it spring back when a later song supports it.
        /// </summary>
        public static VocalsTrack ResolveMultitrack(SongChart chart, YargProfile profile)
        {
            if (!profile.IsFreeVocals)
                return chart.GetVocalsTrack(profile.CurrentInstrument);

            // Free / Party Vocals: honor the sticky chart preference with graceful
            // fallback. Resolution is read-only — it never writes the preference,
            // which is what lets it spring back when a later song supports it.
            bool harmony = HasHarmonyContent(chart);
            bool solo = HasSoloContent(chart);

            return profile.PartyVocalsChartPreference switch
            {
                PartyVocalsChartPreference.Solo => solo ? chart.Vocals : chart.Harmony,
                // Auto (and any future/unknown value): prefer Harmony, else Solo.
                _                                => harmony ? chart.Harmony : chart.Vocals,
            };
        }
    }
}
