using System.Collections.Generic;

namespace YARG.Input
{
    public static class MicrophoneNeedleAssignments
    {
        private const int NEEDLE_COUNT = 7;

        public static int[] Resolve(IReadOnlyList<MicrophoneSlot> slots)
        {
            var result = new int[slots.Count];
            var reserved = new bool[NEEDLE_COUNT + 1];
            foreach (var slot in slots)
            {
                if (slot.NeedleId > 0 && slot.NeedleId <= NEEDLE_COUNT)
                    reserved[slot.NeedleId] = true;
            }

            var used = (bool[]) reserved.Clone();
            for (int i = 0; i < slots.Count; i++)
            {
                int id = slots[i].NeedleId;
                if (id <= 0 || id > NEEDLE_COUNT)
                {
                    id = FirstAvailable(used);
                    if (id == 0)
                    {
                        // If there are more microphones than designs, repeat the automatic sequence.
                        used = (bool[]) reserved.Clone();
                        id = FirstAvailable(used);
                        if (id == 0) id = i % NEEDLE_COUNT + 1;
                    }
                    used[id] = true;
                }
                result[i] = id;
            }
            return result;
        }

        private static int FirstAvailable(bool[] used)
        {
            for (int id = 1; id <= NEEDLE_COUNT; id++)
            {
                if (!used[id]) return id;
            }
            return 0;
        }
    }
}
