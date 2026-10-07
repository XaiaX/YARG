using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class ElitePreviewEditModeTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).First(t => t != null);

        [Test]
        public void EliteGhostNotes_DoNotUseClassicDrumStripEmission()
        {
            var fakeNote = Production("YARG.Settings.Preview.FakeNote");
            var method = fakeNote.GetMethod("GetStripEmission", System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic);
            var modeType = Production("YARG.Core.GameMode");
            var noteType = Production("YARG.Themes.ThemeNoteType");
            var profile = new ColorProfile("preview ghost emission regression");
            profile.FourLaneDrums.GhostStripEmission = 25f;
            profile.FiveLaneDrums.GhostStripEmission = 75f;

            float Emission(string mode, string note) => (float)method.Invoke(null, new[]
            {
                Enum.Parse(modeType, mode), Enum.Parse(noteType, note), profile,
            });

            Assert.That(Emission("EliteDrums", "Ghost"), Is.EqualTo(-1f));
            Assert.That(Emission("EliteDrums", "CymbalGhost"), Is.EqualTo(-1f));
            Assert.That(Emission("FourLaneDrums", "Ghost"), Is.EqualTo(0.25f));
            Assert.That(Emission("FiveLaneDrums", "Ghost"), Is.EqualTo(0.75f));
        }

        [Test]
        public void SyntheticGenerator_CoversEveryRoleAndDynamicsThenStops()
        {
            var type = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
            var generator = Activator.CreateInstance(type);
            var create = type.GetMethod("CreateNote");
            var roles = new HashSet<EliteDrumsColorRole>();
            var models = new HashSet<string>();
            var flamRoles = new HashSet<EliteDrumsColorRole>();
            var firstCycle = new List<EliteDrumsColorRole>();
            for (int i = 0; i < 118; i++)
            {
                var note = create.Invoke(generator, new object[] { i * 0.2d, null });
                Assert.That(note, Is.Not.Null);
                var descriptor = note.GetType().GetField("EliteDescriptor").GetValue(note);
                var role = (EliteDrumsColorRole)descriptor.GetType().GetProperty("Role").GetValue(descriptor);
                roles.Add(role);
                models.Add(note.GetType().GetField("NoteType").GetValue(note).ToString());
                if ((float) descriptor.GetType().GetProperty("Width").GetValue(descriptor) == 2f / 3f)
                    flamRoles.Add(role);
                if (i < 59) firstCycle.Add(role);
                else Assert.That(role, Is.EqualTo(firstCycle[i - 59]), $"Repeated sample {i}");
            }
            Assert.That(flamRoles, Is.SupersetOf(new[] { EliteDrumsColorRole.Snare,
                EliteDrumsColorRole.Tom1, EliteDrumsColorRole.Tom2, EliteDrumsColorRole.Tom3 }));
            var tomGenerator = Activator.CreateInstance(type);
            object tomFlam = null;
            for (int i = 0; i <= 57; i++) tomFlam = create.Invoke(tomGenerator, new object[] { 5d, null });
            var extras = ((System.Collections.IEnumerable)type.GetMethod("CreateChordNotes").Invoke(
                tomGenerator, new[] { (object)5d, tomFlam, null })).Cast<object>().ToArray();
            Assert.That(extras.Length, Is.EqualTo(1));
            var extraDescriptor = extras[0].GetType().GetField("EliteDescriptor").GetValue(extras[0]);
            Assert.That(extraDescriptor.GetType().GetProperty("Role").GetValue(extraDescriptor), Is.EqualTo(EliteDrumsColorRole.Tom2));
            Assert.That(roles.Count, Is.EqualTo(Enum.GetValues(typeof(EliteDrumsColorRole)).Length));
            Assert.That(models, Does.Contain("CymbalAccent"));
            Assert.That(models, Does.Contain("CymbalGhost"));
            Assert.That(models, Does.Contain("Accent"));
            Assert.That(models, Does.Contain("Ghost"));
            Assert.That(models, Does.Contain("Wildcard"));
            Assert.That(type.GetProperty("HasNext").GetValue(generator), Is.True);
            Assert.That(create.Invoke(generator, new object[] { 25d, null }), Is.Not.Null);
        }

        [Test]
        public void SyntheticDescriptor_SharedPositionsPreserveIndependentRolesAndSplitBars()
        {
            var type = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
            var modelType = Production("YARG.Themes.ThemeNoteType");
            var describe = type.GetMethod("Describe");
            object Describe(EliteDrumsColorRole role) => describe.Invoke(null,
                new[] { (object)role, Enum.Parse(modelType, "Normal") });
            object Get(object descriptor, string name) => descriptor.GetType().GetProperty(name).GetValue(descriptor);
            var crash = Describe(EliteDrumsColorRole.LeftCrash);
            var tom = Describe(EliteDrumsColorRole.Tom1);
            Assert.That(Get(crash, "Fret"), Is.EqualTo(Get(tom, "Fret")));
            Assert.That(Get(crash, "Role"), Is.Not.EqualTo(Get(tom, "Role")));
            Assert.That(Get(crash, "ModelType").ToString(), Is.EqualTo("Cymbal"));
            Assert.That(Get(tom, "ModelType").ToString(), Is.EqualTo("Normal"));
            var kick = Describe(EliteDrumsColorRole.Kick);
            var doubleKick = Describe(EliteDrumsColorRole.DoubleKick);
            Assert.That(Get(kick, "IsBar"), Is.True);
            Assert.That(Get(kick, "Width"), Is.EqualTo(1f));
            Assert.That(Get(doubleKick, "Width"), Is.EqualTo(0.5f));
            Assert.That(Get(doubleKick, "Offset"), Is.EqualTo(0.5f));
        }
    }
}
