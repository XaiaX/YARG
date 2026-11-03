// pattern: Functional Core
using System;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Inner kick-bar endpoints in column units, measured from the highway's left edge.</summary>
    internal static class ClassicKickFlamLayout
    {
        internal static (float Left, float Right) GetInnerColumns(int laneCount, int firstHandColumn,
            int secondHandColumn, bool fiveLane, bool lefty)
        {
            if (laneCount <= 0) return (0f, 0f);
            float ordinaryWidth = Math.Min(1.5f, laneCount * .5f);
            var ordinary = (ordinaryWidth, laneCount - ordinaryWidth);
            if (fiveLane || firstHandColumn < 0 || firstHandColumn >= laneCount ||
                secondHandColumn < 0 || secondHandColumn >= laneCount) return ordinary;

            bool Empty(int column) => column != firstHandColumn && column != secondHandColumn;
            int pair = -1;
            float nearest = float.PositiveInfinity;
            for (int column = 0; column < laneCount - 1; column++)
            {
                if (!Empty(column) || !Empty(column + 1)) continue;
                float distance = Math.Abs(column + 1f - laneCount * .5f);
                // Break equal center distances in authored orientation, so Lefty
                // mirrors an otherwise identical chord instead of choosing anew.
                if (distance < nearest || distance == nearest && lefty)
                {
                    nearest = distance;
                    pair = column;
                }
            }
            if (pair >= 0) return (pair + .5f, pair + 1.5f);
            int first = 0, last = laneCount - 1;
            while (first < laneCount && !Empty(first)) first++;
            while (last >= 0 && !Empty(last)) last--;
            return first < last ? (first + .5f, last + .5f) : ordinary;
        }
    }
}
