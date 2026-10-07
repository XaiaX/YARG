using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class EliteColorPresetEditModeTests
    {
        internal static Type EditorType => Type.GetType(
            "YARG.Settings.Metadata.PresetSubTab`1, Assembly-CSharp").MakeGenericType(typeof(ColorProfile));
        internal static object Invoke(string name, params object[] args) => EditorType
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        private static object Field(object value, string name) => value.GetType().GetField(name).GetValue(value);

        [Test]
        public void EliteMapsToDedicatedColorSectionButNotEngineSelector()
        {
            Assert.That(Invoke("ModeToSubSection", GameMode.EliteDrums), Is.EqualTo(nameof(ColorProfile.EliteDrums)));
            Assert.That(Invoke("SubSectionToLabel", nameof(ColorProfile.EliteDrums)), Is.EqualTo("Elite Drums"));
            var engineModes = (IEnumerable) EditorType.GetField("EngineInstrumentModes",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            foreach (var row in engineModes)
                Assert.That(Field(row, "Item3"), Is.Not.EqualTo(GameMode.EliteDrums));
        }

        [Test]
        public void GroupsCoverEveryEliteColorExactlyOnceAndKeepIdentitySeparateFromPosition()
        {
            var groups = ((IEnumerable) Invoke("BuildEliteDrumsGroups")).Cast<object>().ToArray();
            var names = groups.SelectMany(group => ((IEnumerable) Field(group, "SubGroups")).Cast<object>())
                .SelectMany(sub => (string[]) Field(sub, "FieldNames")).ToArray();
            var actual = typeof(ColorProfile.EliteDrumsColors).GetFields()
                .Where(field => field.FieldType == typeof(System.Drawing.Color)).Select(field => field.Name).ToArray();
            Assert.That(names, Is.EquivalentTo(actual.Except(new[] { "WildcardNote", "WildcardStarpower" })));
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length));
            foreach (EliteDrumsColorRole role in Enum.GetValues(typeof(EliteDrumsColorRole)))
            {
                if (role == EliteDrumsColorRole.Wildcard)
                {
                    Assert.That(groups.Any(value => (string) Field(value, "Name") == role.ToString()), Is.False);
                    continue;
                }
                var group = groups.Single(value => (string) Field(value, "Name") == role.ToString());
                Assert.That(Field(group, "CopyRole"), Is.EqualTo(role));
                var sub = ((IEnumerable) Field(group, "SubGroups")).Cast<object>().ToArray();
                Assert.That(Field(sub[0], "FieldNames"), Is.EqualTo(new[] { role + "Note", role + "Starpower" }));
                Assert.That(Field(sub[1], "FieldNames"), Is.EqualTo(new[] { role + "InputFret", role + "InputEffect" }));
            }
        }

        [Test]
        public void ActualEditorSettingEnumerationExcludesOnlyLegacyWildcardControls()
        {
            var editor = Activator.CreateInstance(EditorType, new object[] { null, null, false });
            var fields = ((IEnumerable) EditorType.GetField("_fields", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(editor)).Cast<object>().ToArray();
            var elite = fields.Where(info => ((FieldInfo) Field(info, "ParentField"))?.Name == nameof(ColorProfile.EliteDrums))
                .Select(info => ((FieldInfo) Field(info, "Field")).Name).ToArray();
            var expected = typeof(ColorProfile.EliteDrumsColors).GetFields()
                .Where(field => field.FieldType == typeof(System.Drawing.Color))
                .Select(field => field.Name).Except(new[] { "WildcardNote", "WildcardStarpower" });
            Assert.That(elite, Is.EquivalentTo(expected));
            Assert.That(elite, Does.Contain("Tom1InputEffect"));
            Assert.That(elite, Does.Contain("LeftCrashTom1Particles"));
            Assert.That(typeof(ColorProfile.EliteDrumsColors).GetField("WildcardNote"), Is.Not.Null);
            Assert.That(typeof(ColorProfile.EliteDrumsColors).GetField("WildcardStarpower"), Is.Not.Null);
        }

        [Test]
        public void CopyButtonIsOfferedOnlyOnEliteNotesNotInputOrMergedFretHeaders()
        {
            var groups = ((IEnumerable) Invoke("BuildEliteDrumsGroups")).Cast<object>().ToArray();
            var snare = groups.Single(group => (string) Field(group, "Name") == "Snare");
            var sections = ((IEnumerable) Field(snare, "SubGroups")).Cast<object>().ToArray();
            Assert.That(Invoke("ShouldShowCopyFromNote", nameof(ColorProfile.EliteDrums), snare, sections[0]), Is.True);
            Assert.That(Invoke("ShouldShowCopyFromNote", nameof(ColorProfile.EliteDrums), snare, sections[1]), Is.False);
            var merged = groups.Single(group => (string) Field(group, "Name") == "MergedLane1");
            var fret = ((IEnumerable) Field(merged, "SubGroups")).Cast<object>().Single();
            Assert.That(Invoke("ShouldShowCopyFromNote", nameof(ColorProfile.EliteDrums), merged, fret), Is.False);
        }

        [Test]
        public void EveryNoteFieldResolvesExactRoleAndStarPowerState()
        {
            foreach (EliteDrumsColorRole role in Enum.GetValues(typeof(EliteDrumsColorRole)))
            foreach (bool starPower in new[] { false, true })
            {
                var args = new object[] { role + (starPower ? "Starpower" : "Note"), null, null };
                Assert.That(Invoke("TryGetEliteNoteRole", args), Is.True);
                Assert.That(args[1], Is.EqualTo(role));
                Assert.That(args[2], Is.EqualTo(starPower));
            }
            foreach (var field in new[] { "HatFret", "KickParticles", "MetalStarPower", "NotARoleNote" })
                Assert.That(Invoke("TryGetEliteNoteRole", field, null, null), Is.False);
        }
    }
}
