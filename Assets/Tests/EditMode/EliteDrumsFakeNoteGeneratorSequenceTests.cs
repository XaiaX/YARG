// pattern: Imperative Shell
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
        private const int CYCLE_LENGTH = 135;

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

        [Test]
        public void OpensWithEverySnareFormInDynamicOrder()
        {
            var samples = Sequence("Bars").Take(9).ToArray();
            Assert.That(samples.Select(s => Role(s.Primary)), Is.All.EqualTo("Snare"));
            Assert.That(samples.Select(s => Dynamics(s.Primary)),
                Is.EqualTo(new[] { "Ghost", "Normal", "Accent", "Ghost", "Normal", "Accent", "Ghost", "Normal", "Accent" }));
            for (int i = 0; i < samples.Length; i++)
            {
                Assert.That(Read(samples[i].Primary, "EliteHandFlam"), Is.EqualTo(i >= 3));
                Assert.That(Read(samples[i].Primary, "EliteFlatFlam"), Is.EqualTo(i >= 6));
                Assert.That(samples[i].Companions, Is.Empty);
            }
        }

        [TestCase("Bars")]
        [TestCase("Combined")]
        [TestCase("Split")]
        public void KitTourAndCollisionChordsRepeatInOrder(string mode)
        {
            var (type, generator, create, chord) = NewGenerator(mode);
            Assert.That(type.GetProperty("CycleLength").GetValue(null), Is.EqualTo(CYCLE_LENGTH));
            string kickPrefix = mode == "Bars" ? "Kick" : "DedicatedLaneKick";
            string[] strengths = { "Ghost", "", "Accent" };
            string[] drums = { "Snare", "Tom1", "Tom2", "Tom3" };
            string[] hats = { "HatClosed", "HatIndifferent", "HatOpen" };
            string[] cymbals = { "LeftCrash", "Ride", "RightCrash" };
            int beat = 0;

            string Signature(object note)
            {
                string form = (bool)Read(note, "EliteFlatFlam") ? "/flat" :
                    (bool)Read(note, "EliteHandFlam") ? "/flam" : "";
                return Role(note) + "/" + Dynamics(note) + form;
            }

            void Expect(params string[] gems)
            {
                double time = .2d * beat++;
                var note = create.Invoke(generator, new object[] { time, null });
                var extras = ((IEnumerable)chord.Invoke(generator, new[] { (object)time, note, null })).Cast<object>();
                var notes = new[] { note }.Concat(extras).ToArray();
                Assert.That(notes.Select(Signature), Is.EqualTo(gems), $"Beat {beat}");
                foreach (var data in notes)
                {
                    Assert.That(Read(data, "Time"), Is.EqualTo(time));
                    if (Role(data) is "Kick" or "DoubleKick" or "KickFlam")
                    {
                        var descriptor = Read(data, "EliteDescriptor");
                        Assert.That(Property(descriptor, "Width"), Is.EqualTo(1f));
                        Assert.That(Property(descriptor, "Offset"), Is.EqualTo(0f));
                        Assert.That(Property(descriptor, "DedicatedLaneSource"), Is.EqualTo(mode switch
                        {
                            "Bars" => null, "Combined" => "kick", _ => Role(data) == "DoubleKick" ? "kick2" : "kick1"
                        }));
                    }
                }
            }

            string Model(string role, string strength) => role switch
            {
                "HatClosed" => "ClosedHiHat" + strength, "HatOpen" => "OpenHiHat" + strength,
                "HatIndifferent" or "LeftCrash" or "Ride" or "RightCrash" => "Cymbal" + strength,
                _ => strength == "" ? "Normal" : strength
            };

            void Hand(string role)
            {
                foreach (string form in new[] { "", "/flam", "/flat" })
                foreach (string strength in strengths)
                    Expect(role + "/" + Model(role, strength) + form);
            }

            for (int lap = 0; lap < 2; lap++)
            {
                Hand("Snare");
                foreach (string hat in hats) Hand(hat);
                Expect("Stomp/HatStomp");
                Expect("Splash/HatSplash");
                foreach (string kick in new[] { "Kick", "DoubleKick", "KickFlam" })
                foreach (string strength in strengths)
                    Expect(kick + "/" + kickPrefix + (kick == "KickFlam" ? "Flam" : "") + strength);
                foreach (string strength in strengths)
                {
                    Expect("Kick/" + kickPrefix + strength, "Stomp/HatStomp");
                    Expect("Kick/" + kickPrefix + strength, "Splash/HatSplash");
                }
                foreach (string tom in drums.Skip(1)) Hand(tom);
                foreach (string cymbal in cymbals) Hand(cymbal);
                Expect("Wildcard/Wildcard");

                int pair = 0;
                foreach (string cymbal in hats.Concat(cymbals))
                foreach (string drum in drums)
                {
                    pair++;
                    var expected = new List<string> { cymbal + "/" + Model(cymbal, ""), drum + "/Normal" };
                    if (pair % 2 == 0)
                        expected.Add("Kick/" + kickPrefix + strengths[(pair / 2 - 1) % 3]);
                    if (pair is 15 or 21) expected.Add("Stomp/HatStomp");
                    if (pair is 18 or 24) expected.Add("Splash/HatSplash");
                    Expect(expected.ToArray());
                }
                Expect("LeftCrash/Cymbal", "Ride/Cymbal");
                Expect("LeftCrash/Cymbal", "RightCrash/Cymbal");
                Expect("Ride/Cymbal", "RightCrash/Cymbal");
                Assert.That(beat, Is.EqualTo(CYCLE_LENGTH * (lap + 1)));
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
                            "A missing pedal lane still leaves the accompanying kick or cymbal visible.");
                    }
                }
            }
            Assert.That(standalonePedals, Is.EqualTo(2));
            Assert.That(pairedPedals, Is.EqualTo(10));
        }
    }
}
