// pattern: Functional Core

using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core;
using YARG.Core.Extensions;
using YARG.Core.Game;

namespace YARG.Menu.Maestro
{
    public static class MaestroSelectionRules
    {
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
