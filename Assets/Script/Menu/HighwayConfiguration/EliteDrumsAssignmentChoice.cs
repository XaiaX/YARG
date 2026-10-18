using System;
using YARG.Core.Game;

namespace YARG.Menu.HighwayConfiguration
{
    public enum EliteDrumsAssignmentChoiceState
    {
        Unassigned,
        Current,
        AssignedElsewhere,
        Command
    }

    /// <summary>Display-only assignment markers evaluated against the supplied current draft.</summary>
    public static class EliteDrumsAssignmentChoice
    {
        public static EliteDrumsAssignmentChoiceState Classify(
            EliteDrumsLayoutPreset draft, int column, bool drum, string choiceId)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (column < 0 || column >= draft.Slots.Length)
                throw new ArgumentOutOfRangeException(nameof(column));
            if (!IsAssignment(choiceId)) return EliteDrumsAssignmentChoiceState.Command;

            if (OccupiesRow(draft.Slots[column], drum, choiceId))
                return EliteDrumsAssignmentChoiceState.Current;

            foreach (var slot in draft.Slots)
            {
                if (OccupiesRow(slot, true, choiceId) || OccupiesRow(slot, false, choiceId))
                    return EliteDrumsAssignmentChoiceState.AssignedElsewhere;
            }
            return EliteDrumsAssignmentChoiceState.Unassigned;
        }

        /// <summary>Decorates the caption, never the stable choice ID used to execute the command.</summary>
        public static string FormatLabel(
            EliteDrumsLayoutPreset draft, int column, bool drum, string choiceId, string caption)
        {
            return Classify(draft, column, drum, choiceId) switch
            {
                EliteDrumsAssignmentChoiceState.Unassigned => choiceId is "kick" or "kick2"
                    ? caption
                    : $"[ {caption} ]",
                EliteDrumsAssignmentChoiceState.Current => $"* {caption} *",
                _ => caption
            };
        }

        private static bool IsAssignment(string id) => id is
            "snare" or "tom1" or "tom2" or "tom3" or "left-crash" or "ride" or "right-crash" or
            "hi-hat" or "kick" or "kick1" or "kick2";

        private static bool OccupiesRow(EliteDrumsLayoutSlot slot, bool drum, string id)
        {
            switch (slot.Kind)
            {
                case EliteDrumsLayoutSlotKind.HiHat:
                    // The pedal caption and cymbal caption share the same assignment identity.
                    return id == "hi-hat";
                case EliteDrumsLayoutSlotKind.Kick:
                    return drum && id == "kick";
                case EliteDrumsLayoutSlotKind.Kick1:
                    return drum && id == "kick1";
                case EliteDrumsLayoutSlotKind.Kick2:
                    return drum && id == "kick2";
                case EliteDrumsLayoutSlotKind.Ordinary:
                    return string.Equals(drum ? slot.Drum : slot.Cymbal, id, StringComparison.Ordinal);
                default:
                    return false;
            }
        }
    }
}
