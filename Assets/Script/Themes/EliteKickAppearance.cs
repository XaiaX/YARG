using System;
using System.Collections.ObjectModel;
using System.Drawing;
using YARG.Core.Game;

namespace YARG.Themes
{
    // pattern: Functional Core
    public static class EliteKickAppearance
    {
        public static readonly ReadOnlyCollection<ThemeNoteType> OptionalTypes = Array.AsReadOnly(new[]
        {
            ThemeNoteType.KickGhost, ThemeNoteType.KickAccent, ThemeNoteType.KickFlam,
            ThemeNoteType.KickFlamGhost, ThemeNoteType.KickFlamAccent,
            ThemeNoteType.DedicatedLaneKickGhost, ThemeNoteType.DedicatedLaneKickAccent,
            ThemeNoteType.DedicatedLaneKickFlam, ThemeNoteType.DedicatedLaneKickFlamGhost,
            ThemeNoteType.DedicatedLaneKickFlamAccent
        });

        public static bool IsDedicated(ThemeNoteType type) => type == ThemeNoteType.DedicatedLaneKick ||
            type >= ThemeNoteType.DedicatedLaneKickGhost && type <= ThemeNoteType.DedicatedLaneKickFlamAccent;

        public static bool IsKick(ThemeNoteType type) => type == ThemeNoteType.Kick ||
            IsDedicated(type) || OptionalTypes.Contains(type);

        public static ThemeNoteType Select(bool accent, bool ghost, bool flam, bool dedicated)
        {
            if (dedicated)
                return flam ? accent ? ThemeNoteType.DedicatedLaneKickFlamAccent : ghost
                    ? ThemeNoteType.DedicatedLaneKickFlamGhost : ThemeNoteType.DedicatedLaneKickFlam
                    : accent ? ThemeNoteType.DedicatedLaneKickAccent : ghost
                        ? ThemeNoteType.DedicatedLaneKickGhost : ThemeNoteType.DedicatedLaneKick;
            return flam ? accent ? ThemeNoteType.KickFlamAccent : ghost ? ThemeNoteType.KickFlamGhost : ThemeNoteType.KickFlam
                : accent ? ThemeNoteType.KickAccent : ghost ? ThemeNoteType.KickGhost : ThemeNoteType.Kick;
        }

        public static (Color Lower, Color Upper) FlamColors(ColorProfile.EliteDrumsColors colors,
            bool starPower, bool missed) => missed ? (colors.Miss, colors.Miss) : starPower
                ? (colors.KickStarpower, colors.DoubleKickStarpower) : (colors.KickNote, colors.DoubleKickNote);
    }
}
