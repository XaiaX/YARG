using System;
using System.Drawing;
using System.Reflection;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.EditMode
{
    public class EliteDrumsGameplayColorTests
    {
        private static Type Element => Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp", true);
        private static Type Player => Type.GetType("YARG.Gameplay.Player.EliteDrumsPlayer, Assembly-CSharp", true);
        private const BindingFlags INTERNAL_STATIC = BindingFlags.NonPublic | BindingFlags.Static;

        private static EliteDrumNote Note(EliteDrumPad pad, bool flam = false,
            bool doubleKick = false, EliteDrumsHatState hat = EliteDrumsHatState.Indifferent,
            EliteDrumsHatPedalType pedal = EliteDrumsHatPedalType.Stomp) =>
            new(pad, DrumNoteType.Neutral, hat, pedal, flam, DrumNoteFlags.None,
                NoteFlags.None, EliteDrumsChannelFlag.None, 1d, 480, doubleKick);

        private static (Color Body, Color Emission, Color Metal) Colors(ColorProfile.EliteDrumsColors colors,
            EliteDrumNote note, bool split = false, bool star = false, bool miss = false) =>
            ((Color, Color, Color)) Element.GetMethod("GetGemColors", INTERNAL_STATIC)
                .Invoke(null, new object[] { colors, note, split, star, miss });

        [Test]
        public void GemBodyEmissionAndMetalUseDedicatedRolesAndMissPrecedence()
        {
            var colors = new ColorProfile.EliteDrumsColors
            {
                Tom1Note = Color.Aqua, Tom1Starpower = Color.Brown,
                LeftCrashNote = Color.Coral, Miss = Color.Fuchsia,
                Metal = Color.Gold, MetalStarPower = Color.Indigo
            };
            var tom = Note(EliteDrumPad.Tom1);
            Assert.That(Colors(colors, tom), Is.EqualTo((Color.Aqua, Color.Aqua, Color.Gold)));
            Assert.That(Colors(colors, tom, star: true), Is.EqualTo((Color.Brown, Color.Aqua, Color.Indigo)));
            Assert.That(Colors(colors, tom, star: true, miss: true),
                Is.EqualTo((Color.Fuchsia, Color.Aqua, Color.Indigo)));
            Assert.That(Colors(colors, Note(EliteDrumPad.LeftCrash)).Body, Is.EqualTo(Color.Coral));
            Assert.That(colors.Tom1Note, Is.EqualTo(Color.Aqua), "Consumers must not mutate shared providers.");
        }

        [Test]
        public void SplitHandFlamRetainsPadRoleWhileKickFlamRemainsDedicated()
        {
            var colors = new ColorProfile.EliteDrumsColors
            {
                Tom2Note = Color.Aqua, HandFlamNote = Color.Brown,
                KickNote = Color.Coral, DoubleKickNote = Color.Gold, KickFlamNote = Color.Indigo
            };
            Assert.That(Colors(colors, Note(EliteDrumPad.Tom2, flam: true)).Body, Is.EqualTo(Color.Brown));
            Assert.That(Colors(colors, Note(EliteDrumPad.Tom2, flam: true), split: true).Body, Is.EqualTo(Color.Aqua));
            Assert.That(Colors(colors, Note(EliteDrumPad.Kick)).Body, Is.EqualTo(Color.Coral));
            Assert.That(Colors(colors, Note(EliteDrumPad.Kick, doubleKick: true)).Body, Is.EqualTo(Color.Gold));
            Assert.That(Colors(colors, Note(EliteDrumPad.Kick, flam: true), split: true).Body, Is.EqualTo(Color.Indigo));
        }

        [Test]
        public void HatStatesAndPlayablePedalEventsHaveIndependentRoles()
        {
            var colors = new ColorProfile.EliteDrumsColors
            {
                HatIndifferentNote = Color.Aqua, HatOpenNote = Color.Brown,
                HatClosedNote = Color.Coral, StompNote = Color.Gold, SplashNote = Color.Indigo
            };
            Assert.That(Colors(colors, Note(EliteDrumPad.HiHat)).Body, Is.EqualTo(Color.Aqua));
            Assert.That(Colors(colors, Note(EliteDrumPad.HiHat, hat: EliteDrumsHatState.Open)).Body, Is.EqualTo(Color.Brown));
            Assert.That(Colors(colors, Note(EliteDrumPad.HiHat, hat: EliteDrumsHatState.Closed)).Body, Is.EqualTo(Color.Coral));
            Assert.That(Colors(colors, Note(EliteDrumPad.HatPedal)).Body, Is.EqualTo(Color.Gold));
            Assert.That(Colors(colors, Note(EliteDrumPad.HatPedal, pedal: EliteDrumsHatPedalType.Splash)).Body,
                Is.EqualTo(Color.Indigo));
        }

        [Test]
        public void LaneColorUsesAssociatedNoteRatherThanSharedFretIdentity()
        {
            var colors = new ColorProfile.EliteDrumsColors { Tom3Note = Color.Aqua, RightCrashNote = Color.Brown };
            var laneColor = Player.GetMethod("GetLaneColor", INTERNAL_STATIC);
            var fretIndex = Player.GetMethod("GetColorIndex", BindingFlags.Public | BindingFlags.Static);
            Assert.That(fretIndex.Invoke(null, new object[] { (int) EliteDrumPad.Tom3 }),
                Is.EqualTo(fretIndex.Invoke(null, new object[] { (int) EliteDrumPad.RightCrash })));
            Assert.That(laneColor.Invoke(null, new object[] { colors, Note(EliteDrumPad.Tom3) }), Is.EqualTo(Color.Aqua));
            Assert.That(laneColor.Invoke(null, new object[] { colors, Note(EliteDrumPad.RightCrash) }), Is.EqualTo(Color.Brown));
            Assert.That(fretIndex.Invoke(null, new object[] { (int) EliteDrumPad.Kick }),
                Is.EqualTo((int) ColorProfile.EliteDrumsFret.Kick));
        }
    }
}
