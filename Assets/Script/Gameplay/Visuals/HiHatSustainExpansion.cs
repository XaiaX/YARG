// pattern: Functional Core
using System.Collections.Generic;
using YARG.Core.Chart;

namespace YARG.Gameplay.Visuals
{
    /// <summary>A visual widening origin, independent of note hits, scoring and glow.</summary>
    public readonly struct HiHatSustainExpansion
    {
        public bool IsValid { get; }
        public int FirstIndex { get; }
        public int LastIndex { get; }
        public double OriginTime { get; }
        public bool Immediate { get; }

        private HiHatSustainExpansion(int first, int last, double time, bool immediate)
        {
            IsValid = true;
            FirstIndex = first;
            LastIndex = last;
            OriginTime = time;
            Immediate = immediate;
        }

        public bool Contains(int index) => IsValid && index >= FirstIndex && index <= LastIndex;

        public static HiHatSustainExpansion Activate(HiHatSustainExpansion previous, int index,
            int lastIndex, double time, bool immediate)
        {
            bool continues = previous.Contains(index) && previous.LastIndex == lastIndex;
            return new HiHatSustainExpansion(continues ? previous.FirstIndex : index, lastIndex,
                continues ? previous.OriginTime : time, immediate || continues && previous.Immediate);
        }

        public static int[] FindRunEnds(IReadOnlyList<EliteHiHatSustain> sustains)
        {
            var ends = new int[sustains.Count];
            for (int i = sustains.Count - 1; i >= 0; i--)
                ends[i] = i + 1 < sustains.Count && !sustains[i].HasFade && sustains[i].EndTick == sustains[i + 1].StartTick
                    ? ends[i + 1] : i;
            return ends;
        }

        public static bool HasVisiblePedal(EliteHiHatSustain sustain)
        {
            foreach (var generator in sustain.Generators)
            foreach (var sibling in generator.ParentOrSelf.AllNotes)
                if (sibling.OriginalPad == (int)EliteDrumNote.EliteDrumPad.HatPedal && !sibling.IsInvisibleTerminator)
                    return true;
            return false;
        }
    }
}
