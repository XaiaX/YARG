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
        private const int CYCLE_LENGTH = 71;

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
            for (int i = 0; i < CYCLE_LENGTH; i++)
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
            foreach (int index in new[] { 12, 13, 17, 19, 20 })
            {
                var (note, companions) = samples[index];
                string role = Role(note);
                Assert.That(role, Is.EqualTo(pedalRoles[Array.IndexOf(new[] { 12, 13, 17, 19, 20 }, index)]));
                Assert.That(companions, Is.Empty, "A stacked kick flam is one preview note, matching gameplay.");
                actual.Add(role);
            }
            foreach (int index in new[] { 67, 68, 69, 70 })
            {
                var (note, companions) = samples[index];
                string role = Role(note);
                Assert.That(companions, Has.Length.EqualTo(1));
                Assert.That(Role(companions[0]), Is.EqualTo(index % 2 == 1 ? "Stomp" : "Splash"));
                actual.Add($"{role}+{Role(companions[0])}");
            }
            Assert.That(actual, Is.EqualTo(expected), "Pedal examples must occur once in the exact requested order.");

        }

        [TestCase("Bars")]
        [TestCase("Combined")]
        [TestCase("Split")]
        // Retain this historical test identity while the complete cycle grows to 71 samples.
        public void TwoCompleteCyclesPreserveOriginalRolesDynamicsAndWrapAfter63Samples(string mode)
        {
            var (type, generator, create, chord) = NewGenerator(mode);
            Assert.That(type.GetField("NOTE_COUNT").GetRawConstantValue(), Is.EqualTo(CYCLE_LENGTH * 2));
            string[] roles = { "Snare", "HatClosed", "HatIndifferent", "HatOpen", "LeftCrash", "Ride",
                "RightCrash", "Tom1", "Tom2", "Tom3", "Kick", "DoubleKick", "KickFlam", "Stomp",
                "Splash", "Snare", "Wildcard" };
            string[] flams = { "Snare", "Tom1", "Tom2", "Tom3", "HatIndifferent", "LeftCrash", "Ride", "RightCrash" };
            var signatures = new List<string>();
            string Signature(object note)
            {
                var descriptor = Read(note, "EliteDescriptor");
                return string.Join("/", Role(note), Dynamics(note), Property(descriptor, "Width"),
                    Property(descriptor, "Offset"), Property(descriptor, "DedicatedLaneSource"));
            }
            for (int i = 0; i < CYCLE_LENGTH * 2; i++)
            {
                Assert.That(type.GetProperty("HasNext").GetValue(generator), Is.True, $"Before sample {i}");
                var note = create.Invoke(generator, new object[] { (double)i, null });
                var companions = ((IEnumerable)chord.Invoke(generator, new object[] { (double)i, note, null }))
                    .Cast<object>().ToArray();
                int index = i % CYCLE_LENGTH;
                if (index < 51)
                {
                    if (index >= 10 && index <= 18)
                    {
                        int sample = index - 10;
                        string role = sample >= 6 ? "KickFlam" : sample % 2 == 0 ? "Kick" : "DoubleKick";
                        string suffix = sample < 2 || sample == 6 ? "Ghost" : sample is 4 or 5 or 8 ? "Accent" : "";
                        string expected = (mode == "Bars" ? "" : "DedicatedLane") +
                            (sample >= 6 ? "KickFlam" : "Kick") + suffix;
                        Assert.That(Role(note), Is.EqualTo(role));
                        Assert.That(Dynamics(note), Is.EqualTo(expected));
                        Assert.That(companions, Is.Empty);
                    }
                    else
                    {
                        int nonKickIndex = index < 10 ? index : index - 9;
                        int roleIndex = nonKickIndex % 14;
                        if (roleIndex >= 10) roleIndex += 3;
                        Assert.That(Role(note), Is.EqualTo(roles[roleIndex]), $"Retained role {i}");
                        string dynamics = nonKickIndex / 14 == 1 ? "Accent" : nonKickIndex / 14 == 2 ? "Ghost" : "Normal";
                        string expectedModel = roleIndex == 2 || (roleIndex >= 4 && roleIndex <= 6)
                            ? dynamics == "Normal" ? "Cymbal" : "Cymbal" + dynamics
                            : roleIndex == 3 ? "OpenHiHat" + (dynamics == "Normal" ? "" : dynamics)
                            : roleIndex == 1 ? "ClosedHiHat" + (dynamics == "Normal" ? "" : dynamics)
                            : roleIndex == 13 ? "HatStomp" : roleIndex == 14 ? "HatSplash"
                            : roleIndex == 16 ? "Wildcard" : dynamics;
                        Assert.That(Dynamics(note), Is.EqualTo(expectedModel), $"Retained dynamics {i}");
                    }
                }
                else if (index < 67)
                {
                    Assert.That(Role(note), Is.EqualTo(flams[(index - 51) / 2]));
                    Assert.That(Property(Read(note, "EliteDescriptor"), "Width"), Is.EqualTo(1f));
                    Assert.That(Read(note, "EliteHandFlam"), Is.True);
                    Assert.That(Read(note, "EliteFlatFlam"), Is.EqualTo((index - 51) % 2 == 1),
                        "Every role must show a regular hand flam followed by a flat hand flam.");
                    Assert.That(companions, Is.Empty, "One source descriptor owns two physical gems without a delayed source.");
                }
                string signature = Signature(note) + "/" + Read(note, "EliteHandFlam") + "/" +
                    Read(note, "EliteFlatFlam") + ":" + string.Join(",", companions.Select(Signature));
                if (i < CYCLE_LENGTH) signatures.Add(signature);
                else Assert.That(signature, Is.EqualTo(signatures[index]), $"Full cycle wrap {i}");
            }
            Assert.That(type.GetProperty("HasNext").GetValue(generator), Is.True,
                "The preview generator repeats indefinitely; NOTE_COUNT is two cycles, not a stop condition.");
            var thirdCycle = create.Invoke(generator, new object[] { (double)(CYCLE_LENGTH * 2), null });
            Assert.That(Signature(thirdCycle) + "/" + Read(thirdCycle, "EliteHandFlam") + "/" +
                Read(thirdCycle, "EliteFlatFlam"), Is.EqualTo(signatures[0].Split(':')[0]),
                "The third cycle must restart at Snare.");
        }

        [TestCase("Bars")]
        [TestCase("Combined")]
        [TestCase("Split")]
        public void StandaloneAndSimultaneousPedalKickDescriptorsStayFullWidth(string mode)
        {
            var samples = Sequence(mode);
            foreach (int index in new[] { 10, 11, 12, 13, 14, 15, 16, 17, 18 })
            {
                var (note, companions) = samples[index];
                Assert.That(companions, Is.Empty);
                var descriptor = Read(note, "EliteDescriptor");
                Assert.That(Property(descriptor, "Width"), Is.EqualTo(1f), $"Standalone {Role(note)} at {index}");
                Assert.That(Property(descriptor, "Offset"), Is.EqualTo(0f));
            }
            for (int index = 67; index < CYCLE_LENGTH; index++)
            {
                var (note, companions) = samples[index];
                Assert.That(companions, Has.Length.EqualTo(1));
                Assert.That(Read(companions[0], "Time"), Is.EqualTo(Read(note, "Time")),
                    "The paired example must retain its simultaneous pedal companion.");
                Assert.That(Role(companions[0]), Is.EqualTo(index % 2 == 1 ? "Stomp" : "Splash"));
                var descriptor = Read(note, "EliteDescriptor");
                Assert.That(Property(descriptor, "Width"), Is.EqualTo(1f));
                Assert.That(Property(descriptor, "Offset"), Is.EqualTo(0f));
            }
        }

        [Test]
        public void SplitKickFlamUsesOneStackAndSpotlightsRemainPrimaryOnly()
        {
            var (type, generator, create, chord) = NewGenerator("Split");
            object kickFlam = null;
            for (int i = 0; i < CYCLE_LENGTH; i++)
            {
                var note = create.Invoke(generator, new object[] { i, null });
                if (Role(note) == "KickFlam") { kickFlam = note; break; }
            }
            Assert.That(kickFlam, Is.Not.Null);
            var splitExtras = ((IEnumerable)chord.Invoke(generator,
                new[] { Read(kickFlam, "Time"), kickFlam, null })).Cast<object>().ToArray();
            Assert.That(splitExtras, Is.Empty);
            Assert.That(Property(Read(kickFlam, "EliteDescriptor"), "DedicatedLaneSource"), Is.EqualTo("kick1"));

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
            for (int i = 0; i < CYCLE_LENGTH; i++)
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
