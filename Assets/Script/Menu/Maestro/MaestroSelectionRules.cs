// pattern: Functional Core

using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.Song;

namespace YARG.Menu.Maestro
{
    public static class MaestroSelectionRules
    {
        public const Modifier AccessibilityModifiers =
            Modifier.NoKicks | Modifier.NoHiHat | Modifier.UnpitchedOnly |
            Modifier.RangeCompress;

        public static bool IsAccessibilityModifier(Modifier modifier) =>
            (modifier & AccessibilityModifiers) != Modifier.None;

        public static bool SupportsLeftyFlip(GameMode mode) =>
            mode is GameMode.FiveFretGuitar or GameMode.SixFretGuitar
                or GameMode.FourLaneDrums or GameMode.FiveLaneDrums or GameMode.EliteDrums;

        /// <summary>
        /// Orders available native Elite Drums formats with Pro Drums as the preferred
        /// fallback, followed by 4-lane and 5-lane. The player's saved preference is
        /// still honored separately whenever it is available.
        /// </summary>
        public static IReadOnlyList<Instrument> OrderNativeInstruments(GameMode mode,
            IReadOnlyList<Instrument> available)
        {
            if (available == null)
                return Array.Empty<Instrument>();
            if (available.Count < 2 || mode != GameMode.EliteDrums)
                return available.ToArray();

            var ordered = new List<Instrument>(available.Count);
            Instrument[] priority =
            {
                Instrument.ProDrums,
                Instrument.FourLaneDrums,
                Instrument.FiveLaneDrums,
            };

            foreach (var instrument in priority)
            {
                if (available.Contains(instrument))
                    ordered.Add(instrument);
            }

            foreach (var instrument in available)
            {
                if (!ordered.Contains(instrument))
                    ordered.Add(instrument);
            }

            return ordered;
        }

        public static Instrument SelectNativeInstrumentFallback(Instrument preferred,
            GameMode mode, IReadOnlyList<Instrument> available)
        {
            if (available == null || available.Count == 0)
                return preferred;
            if (available.Contains(preferred))
                return preferred;

            return OrderNativeInstruments(mode, available)[0];
        }

        public static IReadOnlyList<Instrument> GetEliteDrumsDownchartTargets(GameMode mode)
        {
            return mode switch
            {
                GameMode.FourLaneDrums => new[] { Instrument.FourLaneDrums, Instrument.ProDrums },
                GameMode.FiveLaneDrums => new[] { Instrument.FiveLaneDrums },
                // MIDI Drumkit chooses an output format, not a forced-source row.
                GameMode.EliteDrums => Array.Empty<Instrument>(),
                _ => Array.Empty<Instrument>(),
            };
        }

        public static readonly IReadOnlyList<Instrument> MidiDrumOutputs = Array.AsReadOnly(new[]
        {
            Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums, Instrument.EliteDrums,
        });

        public static ResolvedDrumPlayback ResolveMidiDrums(SongEntry song, Instrument output,
            Difficulty tier, Modifier modifiers) => DrumOutputResolver.Resolve(song.AuthoredDrumSourceFacts,
                output, tier, (modifiers & Modifier.EnableEliteUpconversion) != 0,
                (modifiers & Modifier.Enable2xKicks) != 0, (modifiers & Modifier.PreferEliteDowncharts) != 0);

        public static bool IsMidiDrumTierPlayable(IReadOnlyList<SongEntry> songs, Instrument output,
            Difficulty tier, Modifier modifiers) => songs.Count > 0 && songs.All(song =>
                ResolveMidiDrums(song, output, tier, modifiers) != null);

        public static IReadOnlyList<Instrument> GetMidiDrumOutputs(IReadOnlyList<SongEntry> songs,
            Modifier modifiers) => MidiDrumOutputs.Where(output =>
                EnumExtensions<Difficulty>.Values.Any(tier => tier <= Difficulty.Expert &&
                    IsMidiDrumTierPlayable(songs, output, tier, modifiers))).ToArray();

        public static bool SupportsRangeShifts(GameMode mode) =>
            mode is GameMode.FiveFretGuitar or GameMode.ProKeys;

        public static bool HasNoRangeShifts(MaestroStagedPlayer player) =>
            SupportsRangeShifts(player.GameMode) &&
            (!player.RangeEnabled || (player.Modifiers & Modifier.RangeCompress) != Modifier.None);

        public static Modifier ToggleModifier(Modifier current, Modifier modifier, bool enabled)
        {
            if (!enabled)
                return current & ~modifier;

            return (current & ~ModifierConflicts.FromSingleModifier(modifier)) | modifier;
        }

        public static Difficulty SelectDifficultyFallback(Difficulty current,
            IReadOnlyList<Difficulty> available)
        {
            if (available == null || available.Count == 0 || available.Contains(current))
                return current;

            var ordered = EnumExtensions<Difficulty>.Values.ToArray();
            int currentIndex = Array.IndexOf(ordered, current);
            for (int index = currentIndex - 1; index >= 0; index--)
            {
                if (available.Contains(ordered[index]))
                    return ordered[index];
            }
            for (int index = Math.Max(currentIndex + 1, 0); index < ordered.Length; index++)
            {
                if (available.Contains(ordered[index]))
                    return ordered[index];
            }

            return available[0];
        }
    }
}
