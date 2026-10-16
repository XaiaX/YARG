using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class EliteDrumsFakeNoteGeneratorSequenceTests
    {
        private const BindingFlags STATIC = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static object Read(object target, string name) => target.GetType().GetField(name).GetValue(target);

        private static object Property(object target, string name) =>
            target.GetType().GetProperty(name).GetValue(target);

        private static string Role(object note) => Property(Read(note, "EliteDescriptor"), "Role").ToString();

        private static string Dynamics(object note) => Read(note, "NoteType").ToString();

        private static (Type GeneratorType, object Generator, MethodInfo Create, MethodInfo Chord)
            NewGenerator(string kickMode)
        {
            var type = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
            var modeType = Production("YARG.Core.Game.EliteDrumsLayoutKickMode");
            var mode = Enum.Parse(modeType, kickMode);
            var generator = Activator.CreateInstance(type, new[] { mode });
            return (type, generator, type.GetMethod("CreateNote"), type.GetMethod("CreateChordNotes"));
        }

        private static List<(object Primary, object[] Companions)> Sequence(string mode)
        {
            var (_, generator, create, chord) = NewGenerator(mode);
            var result = new List<(object, object[])>();
            for (int i = 0; i < 63; i++)
            {
                var note = create.Invoke(generator, new object[] { i, null });
                var companions = ((IEnumerable)chord.Invoke(generator, new[] { (object)(double)i, note, null }))
                    .Cast<object>().ToArray();
                result.Add((note, companions));
            }
            return result;
        }

        [TestCase("Bars")]
        [TestCase("Combined")]
        [TestCase("Split")]
        public void RealGeneratorPedalSamplesHaveOnlyTheApprovedKickAndPedalCombinations(string kickMode)
        {
            var samples = Sequence(kickMode);
            string[] expected =
            {
                "Kick", "DoubleKick", "KickFlam", "Stomp", "Splash",
                "Kick+Stomp", "Kick+Splash", "DoubleKick+Stomp", "DoubleKick+Splash"
            };
            var pedalRoles = new[] { "Kick", "DoubleKick", "KickFlam", "Stomp", "Splash" };
            var actual = new List<string>();
            foreach (int index in new[] { 10, 11, 12, 13, 14 })
            {
                var (note, companions) = samples[index];
                string role = Role(note);
                Assert.That(role, Is.EqualTo(pedalRoles[index - 10]));
                if (role == "KickFlam")
                    Assert.That(companions.All(extra => Role(extra) == "KickFlam"), Is.True,
                        "KickFlam may keep its existing layout-specific split/half-bar companion, never a pedal.");
                actual.Add(role);
            }
            foreach (int index in new[] { 59, 60, 61, 62 })
            {
                var (note, companions) = samples[index];
                string role = Role(note);
                Assert.That(companions, Has.Length.EqualTo(1));
                Assert.That(Role(companions[0]), Is.EqualTo(index % 2 == 1 ? "Stomp" : "Splash"));
                actual.Add($"{role}+{Role(companions[0])}");
            }
            Assert.That(actual, Is.EqualTo(expected), "Pedal examples must occur once in the exact requested order.");
            foreach (int index in new[] { 10, 11, 13, 14 })
                Assert.That(samples[index].Companions, Is.Empty, "Standalone kick and pedal examples must remain alone.");
            if (kickMode == "Bars")
            {
                Assert.That(samples[12].Companions, Has.Length.EqualTo(1));
                Assert.That((bool)Property(Read(samples[12].Companions[0], "EliteDescriptor"), "IsBar"), Is.True,
                    "The accepted Bars KickFlam remains two half-bar notes.");
            }
            if (kickMode == "Split")
                Assert.That(Property(Read(samples[12].Companions.Single(), "EliteDescriptor"), "DedicatedLaneSource"), Is.EqualTo("kick2"));
            if (kickMode == "Combined")
                Assert.That(samples[12].Companions, Is.Empty);
        }

        [TestCase("Bars")]
        [TestCase("Combined")]
        [TestCase("Split")]
        public void TwoCompleteCyclesPreserveOriginalRolesDynamicsAndWrapAfter63Samples(string mode)
        {
            var (type, generator, create, chord) = NewGenerator(mode);
            Assert.That(type.GetField("NOTE_COUNT").GetRawConstantValue(), Is.EqualTo(126));
            string[] roles = { "Snare", "HatIndifferent", "HatOpen", "HatClosed", "LeftCrash", "Ride",
                "RightCrash", "Tom1", "Tom2", "Tom3", "Kick", "DoubleKick", "KickFlam", "Stomp",
                "Splash", "HandFlam", "Wildcard" };
            string[] flams = { "Snare", "HatIndifferent", "LeftCrash", "Ride", "RightCrash", "Tom1", "Tom2", "Tom3" };
            var signatures = new List<string>();
            string Signature(object note)
            {
                var descriptor = Read(note, "EliteDescriptor");
                return string.Join("/", Role(note), Dynamics(note), Property(descriptor, "Width"),
                    Property(descriptor, "Offset"), Property(descriptor, "DedicatedLaneSource"));
            }
            for (int i = 0; i < 126; i++)
            {
                Assert.That(type.GetProperty("HasNext").GetValue(generator), Is.True, $"Before sample {i}");
                var note = create.Invoke(generator, new object[] { (double)i, null });
                var companions = ((IEnumerable)chord.Invoke(generator, new object[] { (double)i, note, null }))
                    .Cast<object>().ToArray();
                int index = i % 63;
                if (index < 51)
                {
                    int roleIndex = index % 17;
                    Assert.That(Role(note), Is.EqualTo(roles[roleIndex]), $"Retained role {i}");
                    string dynamics = index / 17 == 1 ? "Accent" : index / 17 == 2 ? "Ghost" : "Normal";
                    string expectedModel = roleIndex == 1 || (roleIndex >= 4 && roleIndex <= 6)
                        ? dynamics == "Normal" ? "Cymbal" : "Cymbal" + dynamics
                        : roleIndex == 2 ? "OpenHiHat" + (dynamics == "Normal" ? "" : dynamics)
                        : roleIndex == 3 ? "ClosedHiHat" + (dynamics == "Normal" ? "" : dynamics)
                        : roleIndex == 10 || roleIndex == 11 ? "Kick"
                        : roleIndex == 12 ? mode == "Bars" ? "Kick" : mode == "Combined" ? "Accent" : "DedicatedLaneKick"
                        : roleIndex == 13 || roleIndex == 14 ? "DedicatedLaneKick"
                        : roleIndex == 16 ? "Wildcard" : dynamics;
                    Assert.That(Dynamics(note), Is.EqualTo(expectedModel), $"Retained dynamics {i}");
                }
                else if (index < 59)
                {
                    Assert.That(Role(note), Is.EqualTo(flams[index - 51]));
                    Assert.That(Property(Read(note, "EliteDescriptor"), "Width"), Is.EqualTo(2f / 3f));
                    Assert.That(companions, Has.Length.EqualTo(1));
                    Assert.That(Read(companions[0], "Time"), Is.EqualTo(i + 0.025d));
                }
                string signature = Signature(note) + ":" + string.Join(",", companions.Select(Signature));
                if (i < 63) signatures.Add(signature);
                else Assert.That(signature, Is.EqualTo(signatures[index]), $"Full cycle wrap {i}");
            }
            Assert.That(type.GetProperty("HasNext").GetValue(generator), Is.True,
                "The preview generator repeats indefinitely; NOTE_COUNT is two cycles, not a stop condition.");
            Assert.That(Signature(create.Invoke(generator, new object[] { 126d, null })),
                Is.EqualTo(signatures[0].Split(':')[0]), "The third cycle must restart at Snare.");
        }

        [Test]
        public void SplitKickFlamStillCreatesBothDedicatedKicksAndSpotlightsRemainPrimaryOnly()
        {
            var (type, generator, create, chord) = NewGenerator("Split");
            object kickFlam = null;
            for (int i = 0; i < 59; i++)
            {
                var note = create.Invoke(generator, new object[] { i, null });
                if (Role(note) == "KickFlam") { kickFlam = note; break; }
            }
            Assert.That(kickFlam, Is.Not.Null);
            var splitExtras = ((IEnumerable)chord.Invoke(generator,
                new[] { Read(kickFlam, "Time"), kickFlam, null })).Cast<object>().ToArray();
            Assert.That(splitExtras.Length, Is.EqualTo(1));
            Assert.That(Role(splitExtras[0]), Is.EqualTo("KickFlam"));
            Assert.That(Property(Read(splitExtras[0], "EliteDescriptor"), "DedicatedLaneSource"), Is.EqualTo("kick2"));

            var spotlightType = Production("YARG.Settings.Preview.FakeNoteSpotlight");
            var spotlight = Activator.CreateInstance(spotlightType, new object[] { 0, true, false, false });
            object spotlightNote = type.GetMethod("CreateSpotlightNote").Invoke(generator,
                new object[] { 100d, spotlight, null });
            Assert.That(Role(spotlightNote), Is.EqualTo("Kick"));
            Assert.That(((IEnumerable)chord.Invoke(generator,
                new object[] { 100d, spotlightNote, null })).Cast<object>(), Is.Empty,
                "Spotlights create only their selected primary note; no companion is implicitly spawned.");
        }

        [Test]
        public void MissingHiHatSourceFilteringRemainsInForceForStandaloneAndPairedPedals()
        {
            var preset = Production("YARG.Core.Game.EliteDrumsLayoutPreset");
            var presetInstance = preset.GetProperty("Compact", STATIC).GetValue(null);
            var slots = (Array)preset.GetField("Slots").GetValue(presetInstance);
            var slotType = Production("YARG.Core.Game.EliteDrumsLayoutSlot");
            slots.SetValue(Activator.CreateInstance(slotType), 1);
            var resolver = Production("YARG.Core.Game.EliteDrumsLayoutResolver");
            var args = new object[] { presetInstance, null };
            Assert.That((bool)resolver.GetMethod("TryPreviewResolve").Invoke(null, args), Is.True);
            var layout = args[1];
            var represented = Production("YARG.Settings.Preview.FakeTrackPlayer")
                .GetMethod("IsEliteNoteRepresented", STATIC);
            var (_, generator, create, chord) = NewGenerator("Bars");
            int standalonePedals = 0;
            int pairedPedals = 0;
            for (int i = 0; i < 63; i++)
            {
                var note = create.Invoke(generator, new object[] { (double)i, null });
                if (Role(note) is "Stomp" or "Splash")
                {
                    standalonePedals++;
                    Assert.That((bool)represented.Invoke(null, new[] { note, layout }), Is.False);
                }
                foreach (var extra in (IEnumerable)chord.Invoke(generator, new object[] { (double)i, note, null }))
                {
                    if (Role(extra) is "Stomp" or "Splash")
                    {
                        pairedPedals++;
                        Assert.That((bool)represented.Invoke(null, new[] { extra, layout }), Is.False);
                        Assert.That((bool)represented.Invoke(null, new[] { note, layout }), Is.True,
                            "Missing hi-hat filters the companion without dropping its bar kick primary.");
                    }
                }
            }
            Assert.That(standalonePedals, Is.EqualTo(6));
            Assert.That(pairedPedals, Is.EqualTo(4));
        }
    }
}
