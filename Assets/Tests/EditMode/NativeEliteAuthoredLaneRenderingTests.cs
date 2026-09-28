using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using YARG.Core.Chart;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.EditMode
{
    public sealed class NativeEliteAuthoredLaneRenderingTests
    {
        private static readonly Type PLAYER_TYPE = Type.GetType(
            "YARG.Gameplay.Player.EliteDrumsPlayer, Assembly-CSharp");

        [Test]
        public void AuthoredHandLaneStartsAtFirstMemberAndFiltersNonHandLanes()
        {
            Assert.That(PLAYER_TYPE, Is.Not.Null);
            var firstSource = new EliteDrumSourceDefinition("first", 0, (int) EliteDrumPad.Snare,
                480, 0, 1d);
            var secondSource = new EliteDrumSourceDefinition("second", 1, (int) EliteDrumPad.Snare,
                960, 0, 2d);
            var record = new EliteDrumNativeAuthoredLaneRecord(0,
                PhraseType.EliteDrums_SnareLane, 480, 1440, new[] { firstSource, secondSource });
            var firstNote = CreateNote(EliteDrumPad.Snare, 480, 1d, firstSource);
            var laterNote = CreateNote(EliteDrumPad.Snare, 960, 2d, secondSource);

            Assert.That(InvokePolicy("IsNativeAuthoredHandLane", record), Is.True);
            Assert.That(InvokeStartPolicy(record, firstNote), Is.True,
                "The authored strip should spawn when its first source member enters the highway.");
            Assert.That(InvokeStartPolicy(record, laterNote), Is.False,
                "Later lane members must not create duplicate strips.");

            var oneMember = new EliteDrumNativeAuthoredLaneRecord(1,
                PhraseType.EliteDrums_SnareLane, 480, 960, new[] { firstSource });
            var kick = new EliteDrumNativeAuthoredLaneRecord(2,
                PhraseType.EliteDrums_KickLane, 480, 1440,
                new[]
                {
                    new EliteDrumSourceDefinition("kick1", 2, (int) EliteDrumPad.Kick, 480, 0),
                    new EliteDrumSourceDefinition("kick2", 3, (int) EliteDrumPad.Kick, 960, 0),
                });
            var pedal = new EliteDrumNativeAuthoredLaneRecord(3,
                PhraseType.EliteDrums_HatPedalLane, 480, 1440,
                new[]
                {
                    new EliteDrumSourceDefinition("pedal1", 4, (int) EliteDrumPad.HatPedal, 480, 0),
                    new EliteDrumSourceDefinition("pedal2", 5, (int) EliteDrumPad.HatPedal, 960, 0),
                });
            Assert.That(InvokePolicy("IsNativeAuthoredHandLane", oneMember), Is.False);
            Assert.That(InvokePolicy("IsNativeAuthoredHandLane", kick), Is.False);
            Assert.That(InvokePolicy("IsNativeAuthoredHandLane", pedal), Is.False);
            var malformed = new EliteDrumNativeAuthoredLaneRecord(4,
                PhraseType.EliteDrums_SnareLane, 480, 1440,
                new EliteDrumSourceDefinition[] { firstSource, null });
            Assert.That(InvokePolicy("IsNativeAuthoredHandLane", malformed), Is.False);
            Assert.That(InvokeStartPolicy(malformed, firstNote), Is.False);
        }

        [Test]
        public void AuthoredPhraseBoundsAreClampedToSurvivingMemberOnsets()
        {
            var first = new EliteDrumSourceDefinition("first", 0, (int) EliteDrumPad.HiHat,
                960, 0, 1d);
            var last = new EliteDrumSourceDefinition("last", 1, (int) EliteDrumPad.HiHat,
                1920, 0, 2d);
            var record = new EliteDrumNativeAuthoredLaneRecord(0,
                PhraseType.EliteDrums_HiHatLane, 480, 2880, new[] { last, first });
            var survivors = new Dictionary<EliteDrumSourceDefinition, EliteDrumNote>
            {
                [first] = CreateNote(EliteDrumPad.HiHat, 960, 1d, first),
                [last] = CreateNote(EliteDrumPad.HiHat, 1920, 2d, last),
            };
            Assert.That(InvokeTimeRange(record, survivors), Is.EqualTo((1d, 2d)),
                "Visual lane endpoints must follow actual member gems, not marker bounds.");
            survivors.Remove(last);
            Assert.That(InvokeTimeRange(record, survivors), Is.Null,
                "A dropped source must not render a lane the engine rejects.");
        }

        private static (double Start, double End)? InvokeTimeRange(EliteDrumNativeAuthoredLaneRecord record,
            Dictionary<EliteDrumSourceDefinition, EliteDrumNote> survivors) =>
            ((double Start, double End)?) PLAYER_TYPE.GetMethod("GetNativeAuthoredLaneTimeRange",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { record, survivors });

        private static EliteDrumNote CreateNote(EliteDrumPad pad, uint tick, double time,
            EliteDrumSourceDefinition source) => new(pad, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, tick, false, source);

        private static bool InvokeStartPolicy(EliteDrumNativeAuthoredLaneRecord record, EliteDrumNote note) =>
            (bool) PLAYER_TYPE.GetMethod("IsNativeAuthoredLaneStart", BindingFlags.Static |
                BindingFlags.NonPublic).Invoke(null, new object[] { record, note });

        private static bool InvokePolicy(string methodName, EliteDrumNativeAuthoredLaneRecord record) =>
            (bool) PLAYER_TYPE.GetMethod(methodName, BindingFlags.Static |
                BindingFlags.NonPublic).Invoke(null, new object[] { record });
    }
}
