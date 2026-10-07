using System;
using System.Drawing;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class EliteFretCopyTargetTests
    {
        [TestCase(EliteDrumsColorRole.Snare, "Snare")]
        [TestCase(EliteDrumsColorRole.HatIndifferent, "Hat")]
        [TestCase(EliteDrumsColorRole.HatOpen, "Hat")]
        [TestCase(EliteDrumsColorRole.HatClosed, "Hat")]
        [TestCase(EliteDrumsColorRole.Stomp, "Hat")]
        [TestCase(EliteDrumsColorRole.Splash, "Hat")]
        [TestCase(EliteDrumsColorRole.LeftCrash, "LeftCrashTom1")]
        [TestCase(EliteDrumsColorRole.Tom1, "LeftCrashTom1")]
        [TestCase(EliteDrumsColorRole.Ride, "RideTom2")]
        [TestCase(EliteDrumsColorRole.Tom2, "RideTom2")]
        [TestCase(EliteDrumsColorRole.RightCrash, "RightCrashTom3")]
        [TestCase(EliteDrumsColorRole.Tom3, "RightCrashTom3")]
        [TestCase(EliteDrumsColorRole.Kick, "Kick")]
        [TestCase(EliteDrumsColorRole.KickFlam, "Kick")]
        [TestCase(EliteDrumsColorRole.DoubleKick, "DoubleKick")]
        public void ChosenIdentityCopiesOnlyToItsSharedPosition(EliteDrumsColorRole role, string target)
        {
            var preset = new ColorProfile("Elite copy fixture", false);
            var fields = typeof(ColorProfile.EliteDrumsColors).GetFields()
                .Where(field => field.FieldType == typeof(Color)).ToArray();
            int index = 0;
            foreach (var field in fields)
                field.SetValue(preset.EliteDrums, Color.FromArgb(255, ++index, index, index));
            var before = fields.ToDictionary(field => field.Name, field => (Color) field.GetValue(preset.EliteDrums));
            var chosen = before[role + "Note"];
            Assert.That(EliteColorPresetEditModeTests.Invoke("GetEliteCopyTarget", role), Is.EqualTo(target));
            Assert.That(EliteColorPresetEditModeTests.Invoke("CopyEliteNoteToFret", preset, role), Is.True);
            foreach (var field in fields)
            {
                bool copied = new[] { target + "Fret", target + "FretInner", target + "Particles" }.Contains(field.Name);
                Assert.That(field.GetValue(preset.EliteDrums), Is.EqualTo(copied ? chosen : before[field.Name]), field.Name);
            }
        }

        [TestCase(EliteDrumsColorRole.HandFlam)]
        [TestCase(EliteDrumsColorRole.Wildcard)]
        public void IdentityWithoutFixedPositionDoesNotMutatePreset(EliteDrumsColorRole role)
        {
            var preset = new ColorProfile("Elite copy fixture", false);
            var fields = typeof(ColorProfile.EliteDrumsColors).GetFields();
            var before = fields.Select(field => field.GetValue(preset.EliteDrums)).ToArray();
            Assert.That(EliteColorPresetEditModeTests.Invoke("GetEliteCopyTarget", role), Is.Null);
            Assert.That(EliteColorPresetEditModeTests.Invoke("CopyEliteNoteToFret", preset, role), Is.False);
            Assert.That(fields.Select(field => field.GetValue(preset.EliteDrums)).ToArray(), Is.EqualTo(before));
        }
    }
}
