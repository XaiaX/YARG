using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class EliteDrumsAssignmentChoiceTests
    {
        private static Type Helper => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("YARG.Menu.HighwayConfiguration.EliteDrumsAssignmentChoice"))
            .First(type => type != null);

        private static string State(EliteDrumsLayoutPreset draft, int column, bool drum, string id) =>
            Helper.GetMethod("Classify", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { draft, column, drum, id }).ToString();

        private static string Label(EliteDrumsLayoutPreset draft, int column, bool drum, string id, string caption) =>
            (string) Helper.GetMethod("FormatLabel", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { draft, column, drum, id, caption });

        [TestCase("snare", true)]
        [TestCase("tom1", true)]
        [TestCase("tom2", true)]
        [TestCase("tom3", true)]
        [TestCase("left-crash", false)]
        [TestCase("ride", false)]
        [TestCase("right-crash", false)]
        public void OrdinaryAssignmentsHaveAbsentCurrentAndElsewhereMarkers(string id, bool drum)
        {
            var draft = new EliteDrumsLayoutPreset();
            string caption = id.Replace('-', ' ').ToUpperInvariant();
            Assert.That(Label(draft, 0, drum, id, caption), Is.EqualTo($"[ {caption} ]"));
            draft.Slots[0] = EliteDrumsLayoutSlot.Ordinary(drum ? id : null, drum ? null : id);
            Assert.That(State(draft, 0, drum, id), Is.EqualTo("Current"));
            Assert.That(Label(draft, 0, drum, id, caption), Is.EqualTo($"* {caption} *"));
            Assert.That(Label(draft, 1, drum, id, caption), Is.EqualTo(caption));
            Assert.That(State(draft, 0, !drum, id), Is.EqualTo("AssignedElsewhere"));
            Assert.That(State(draft, 1, !drum, id), Is.EqualTo("AssignedElsewhere"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PairedOrdinaryColumnKeepsRowOccupancyIndependent(bool drum)
        {
            var draft = new EliteDrumsLayoutPreset();
            draft.Slots[0] = EliteDrumsLayoutSlot.Ordinary("tom1", "left-crash");
            string current = drum ? "tom1" : "left-crash";
            string opposite = drum ? "left-crash" : "tom1";
            Assert.That(State(draft, 0, drum, current), Is.EqualTo("Current"));
            Assert.That(State(draft, 0, drum, opposite), Is.EqualTo("AssignedElsewhere"));
            Assert.That(State(draft, 0, drum, drum ? "tom2" : "ride"), Is.EqualTo("Unassigned"));
            draft.Slots[0] = EliteDrumsLayoutSlot.Ordinary(drum ? null : "tom1", drum ? "left-crash" : null);
            Assert.That(State(draft, 0, drum, current), Is.EqualTo("Unassigned"), "An empty row has no current assignment.");
        }

        [TestCase(true, "HI-HAT PEDAL")]
        [TestCase(false, "HI-HAT")]
        public void HiHatCaptionsShareOneIdentityOnBothRows(bool drum, string caption)
        {
            var draft = new EliteDrumsLayoutPreset();
            Assert.That(Label(draft, 0, drum, "hi-hat", caption), Is.EqualTo($"[ {caption} ]"));
            draft.Slots[0] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat);
            Assert.That(Label(draft, 0, drum, "hi-hat", caption), Is.EqualTo($"* {caption} *"));
            Assert.That(Label(draft, 1, drum, "hi-hat", caption), Is.EqualTo(caption));
            Assert.That(State(draft, 0, drum, "hi-hat-pedal"), Is.EqualTo("Command"),
                "The pedal caption is not a replacement command ID.");
        }

        [TestCase(EliteDrumsLayoutSlotKind.Kick, "kick")]
        [TestCase(EliteDrumsLayoutSlotKind.Kick1, "kick1")]
        [TestCase(EliteDrumsLayoutSlotKind.Kick2, "kick2")]
        public void KickKindsAreDistinctAndCurrentOnlyOnTheDrumRow(EliteDrumsLayoutSlotKind kind, string id)
        {
            var draft = new EliteDrumsLayoutPreset();
            foreach (bool drum in new[] { true, false })
            {
                Assert.That(State(draft, 0, drum, id), Is.EqualTo("Unassigned"));
                Assert.That(Label(draft, 0, drum, id, id.ToUpperInvariant()),
                    Is.EqualTo(id is "kick" or "kick2" ? id.ToUpperInvariant() : $"[ {id.ToUpperInvariant()} ]"));
            }
            draft.Slots[0] = EliteDrumsLayoutSlot.Exclusive(kind);
            Assert.That(State(draft, 0, true, id), Is.EqualTo("Current"));
            Assert.That(State(draft, 0, false, id), Is.EqualTo("AssignedElsewhere"));
            foreach (bool drum in new[] { true, false })
            {
                Assert.That(State(draft, 1, drum, id), Is.EqualTo("AssignedElsewhere"));
                foreach (string other in new[] { "kick", "kick1", "kick2" }.Where(other => other != id))
                    Assert.That(State(draft, 0, drum, other), Is.EqualTo("Unassigned"));
            }
        }

        [TestCase(EliteDrumsLayoutSlotKind.Kick, "kick", "KICK")]
        [TestCase(EliteDrumsLayoutSlotKind.Kick1, "kick1", "[ KICK1 ]")]
        [TestCase(EliteDrumsLayoutSlotKind.Kick2, "kick2", "KICK2")]
        public void OptionalKickMarkers(EliteDrumsLayoutSlotKind kind, string id, string absentLabel)
        {
            var draft = new EliteDrumsLayoutPreset();
            string caption = id.ToUpperInvariant();
            Assert.That(Label(draft, 0, true, id, caption), Is.EqualTo(absentLabel));

            draft.Slots[0] = EliteDrumsLayoutSlot.Exclusive(kind);
            Assert.That(Label(draft, 0, true, id, caption), Is.EqualTo($"* {caption} *"));

            draft.Slots[0] = EliteDrumsLayoutSlot.Ordinary(null, null);
            draft.Slots[1] = EliteDrumsLayoutSlot.Exclusive(kind);
            Assert.That(Label(draft, 0, true, id, caption), Is.EqualTo(caption));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SplitKicksDoNotImplyACombinedKick(bool drum)
        {
            var draft = new EliteDrumsLayoutPreset();
            draft.Slots[0] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            draft.Slots[1] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            Assert.That(State(draft, 0, drum, "kick1"), Is.EqualTo(drum ? "Current" : "AssignedElsewhere"));
            Assert.That(State(draft, 1, drum, "kick2"), Is.EqualTo(drum ? "Current" : "AssignedElsewhere"));
            Assert.That(State(draft, 0, drum, "kick2"), Is.EqualTo("AssignedElsewhere"));
            Assert.That(State(draft, 1, drum, "kick1"), Is.EqualTo("AssignedElsewhere"));
            Assert.That(State(draft, 0, drum, "kick"), Is.EqualTo("Unassigned"));
        }

        [TestCase(true, "tom1")]
        [TestCase(false, "left-crash")]
        public void CurrentAssignmentTakesPrecedenceOverAnotherOccurrence(bool drum, string id)
        {
            var draft = new EliteDrumsLayoutPreset();
            draft.Slots[0] = EliteDrumsLayoutSlot.Ordinary(drum ? id : null, drum ? null : id);
            draft.Slots[1] = draft.Slots[0].Copy();
            Assert.That(State(draft, 1, drum, id), Is.EqualTo("Current"));
        }

        [Test]
        public void EveryCallUsesTheSuppliedDraftWithoutChangingIdsOrKickEligibility()
        {
            var original = new EliteDrumsLayoutPreset();
            original.Slots[0] = EliteDrumsLayoutSlot.Ordinary(null, "left-crash");
            original.Slots[1] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
            var originalSlots = original.Slots.Select(slot => slot.Copy()).ToArray();
            var originalId = original.Id;
            var choices = EliteDrumsLayoutEditor.GetKickChoices(original, 0).ToArray();
            string[] ids = { "left-crash", "hi-hat", "kick", "kick1", "kick2", "clear", "cancel" };
            string[] savedIds = (string[]) ids.Clone();
            foreach (string id in ids) Label(original, 0, false, id, "CAPTION");
            Assert.That(ids, Is.EqualTo(savedIds));
            Assert.That(original.Id, Is.EqualTo(originalId));
            Assert.That(EliteDrumsLayoutEditor.GetKickChoices(original, 0), Is.EqualTo(choices));
            for (int i = 0; i < originalSlots.Length; i++)
            {
                Assert.That(original.Slots[i].Kind, Is.EqualTo(originalSlots[i].Kind));
                Assert.That(original.Slots[i].Drum, Is.EqualTo(originalSlots[i].Drum));
                Assert.That(original.Slots[i].Cymbal, Is.EqualTo(originalSlots[i].Cymbal));
            }
            var moved = EliteDrumsLayoutEditor.Place(original, 2, "left-crash");
            Assert.That(Label(moved, 0, false, "left-crash", "LEFT CRASH"), Is.EqualTo("LEFT CRASH"));
            Assert.That(Label(moved, 2, false, "left-crash", "LEFT CRASH"), Is.EqualTo("* LEFT CRASH *"));
            var cleared = EliteDrumsLayoutEditor.ClearRow(moved, 2, false);
            Assert.That(Label(cleared, 2, false, "left-crash", "LEFT CRASH"), Is.EqualTo("[ LEFT CRASH ]"));
            Assert.That(Label(original, 0, false, "left-crash", "LEFT CRASH"), Is.EqualTo("* LEFT CRASH *"));
        }

        [TestCase("clear", "CLEAR")]
        [TestCase("cancel", "CANCEL")]
        public void CommandsAreNeverDecorated(string id, string caption)
        {
            var draft = new EliteDrumsLayoutPreset();
            foreach (bool drum in new[] { true, false })
            {
                Assert.That(State(draft, 0, drum, id), Is.EqualTo("Command"));
                Assert.That(Label(draft, 0, drum, id, caption), Is.EqualTo(caption));
            }
        }
    }
}
