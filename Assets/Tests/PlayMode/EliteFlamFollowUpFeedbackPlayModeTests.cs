using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    // Test-only environment ownership shared by the flam runtime suites.
    public abstract class EliteFlamRuntimeTestBase
    {
        protected const BindingFlags INSTANCE = PreparedElitePlayerFixture.INSTANCE;
        internal InitializedElitePlayerFixture Fixture;
        private object _originalSettings;
        private bool _originalInitialized;
        private bool _capturedSettings;
        private bool _ownsAudio;
        protected static Type Production(string name) => PreparedElitePlayerFixture.ProductionType(name);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var settings = Production("YARG.Settings.SettingsManager");
            var container = settings.GetNestedType("SettingContainer", BindingFlags.Public);
            _originalSettings = settings.GetProperty("Settings").GetValue(null);
            _originalInitialized = (bool) container.GetField("IsInitialized").GetValue(null);
            _capturedSettings = true;
            var audio = Production("YARG.Core.Audio.GlobalAudioHandler");
            if (audio.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == null)
            {
                Production("YARG.Helpers.PathHelper").GetMethod("Init").Invoke(null, null);
                audio.GetMethod("Initialize").MakeGenericMethod(Production("YARG.Audio.BASS.BassAudioManager"))
                    .Invoke(null, null);
                _ownsAudio = true;
            }
            if (_originalSettings == null)
            {
                settings.GetProperty("Settings").GetSetMethod(true).Invoke(null,
                    new[] { Activator.CreateInstance(container) });
                container.GetField("IsInitialized").SetValue(null, true);
            }
            yield return null;
        }

        protected static YargProfile Profile() => new YargProfile
        {
            GameMode = GameMode.EliteDrums, CurrentInstrument = Instrument.EliteDrums,
            PreferredInstrument = Instrument.EliteDrums, CurrentDifficulty = Difficulty.Hard,
            UseCymbalModels = true
        };
        protected static EliteDrumNote Note(EliteDrumPad pad, double time, bool flam = false, bool flat = false) =>
            new EliteDrumNote(pad, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent,
                EliteDrumsHatPedalType.Stomp, flam, DrumNoteFlags.None, NoteFlags.None,
                EliteDrumsChannelFlag.None, time, (uint) (time * 960), false, isFlatFlam: flat);
        protected InstrumentDifficulty<EliteDrumNote> Playable => (InstrumentDifficulty<EliteDrumNote>)
            Fixture.Session.GetType().GetProperty("PlayableEliteDrumDifficulty").GetValue(Fixture.Session);
        protected static object Engine(Component player) => player.GetType().BaseType
            .GetProperty("Engine", INSTANCE).GetValue(player);
        protected static object Stats(object engine) => engine.GetType().GetField("EngineStats").GetValue(engine);
        protected static object Stat(object engine, string name) => Stats(engine).GetType().GetField(name)
            .GetValue(Stats(engine));
        protected static void Press(Component player, EliteDrumsAction action, double time)
        {
            var input = GameInput.Create(time, action, 0.8f);
            var type = Production("YARG.Gameplay.Player.BasePlayer");
            type.GetMethod("OnGameInput", INSTANCE).Invoke(player, new object[] { input });
            type.GetMethod("UpdateInputs", INSTANCE).Invoke(player, new object[] { time });
        }
        protected static Component Receptor(Component player, EliteDrumPad pad)
        {
            var array = player.GetComponentInChildren(Production("YARG.Gameplay.Visuals.FretArray"), true);
            var frets = (IDictionary) array.GetType().GetField("_frets", INSTANCE).GetValue(array);
            return (Component) frets[(int) pad];
        }
        protected static Component Effect(Component receptor) => (Component) receptor.GetType()
            .GetProperty("ThemeBind").GetValue(receptor).GetType().GetProperty("HitEffect")
            .GetValue(receptor.GetType().GetProperty("ThemeBind").GetValue(receptor));
        protected static void StopEffect(Component receptor)
        {
            foreach (var particle in Effect(receptor).GetComponentsInChildren<ParticleSystem>(true))
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        protected static void AssertEffect(Component receptor, System.Drawing.Color expected)
        {
            var particles = Effect(receptor).GetComponentsInChildren(
                Production("YARG.Helpers.Authoring.EffectParticle"), true);
            Assert.That(particles, Is.Not.Empty);
            foreach (var particle in particles)
            {
                var color = (Color) particle.GetType().GetProperty("InitialColor", INSTANCE).GetValue(particle);
                Assert.That(color.r, Is.EqualTo(expected.R / 255f).Within(0.005f));
                Assert.That(color.g, Is.EqualTo(expected.G / 255f).Within(0.005f));
                Assert.That(color.b, Is.EqualTo(expected.B / 255f).Within(0.005f));
            }
            Assert.That(particles[0].GetComponent<ParticleSystem>().isPlaying, Is.True);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try { if (Fixture != null) yield return Fixture.Dispose(); }
            finally
            {
                Fixture = null;
                try
                {
                    if (_capturedSettings)
                    {
                        var settings = Production("YARG.Settings.SettingsManager");
                        settings.GetProperty("Settings").GetSetMethod(true).Invoke(null, new[] { _originalSettings });
                        settings.GetNestedType("SettingContainer", BindingFlags.Public).GetField("IsInitialized")
                            .SetValue(null, _originalInitialized);
                    }
                }
                finally
                {
                    _capturedSettings = false;
                    if (_ownsAudio)
                    {
                        Production("YARG.Core.Audio.GlobalAudioHandler").GetMethod("Close").Invoke(null, null);
                        _ownsAudio = false;
                    }
                }
            }
        }
    }

    public sealed class EliteFlamFollowUpFeedbackPlayModeTests : EliteFlamRuntimeTestBase
    {
        [UnityTest]
        public IEnumerator FlatAndRegularFollowUpsRestartOnlyTheirReceptorWithoutRepeatingNoteLifecycle()
        {
            foreach (bool flat in new[] { false, true })
            {
                Fixture = new InitializedElitePlayerFixture();
                yield return Fixture.Initialize(PreparedElitePlayerFixture.CreateChart(
                    Note(EliteDrumPad.Snare, 1), Note(EliteDrumPad.Snare, 2, !flat, flat),
                    Note(EliteDrumPad.Tom1, 4)), Profile(), EliteDrumsLayoutPreset.Full, noFail: true);
                var player = Fixture.Player;
                var engine = Engine(player);
                var colors = new ColorProfile("Flam follow-up effects");
                colors.EliteDrums.SnareInputEffect = System.Drawing.Color.Magenta;
                colors.EliteDrums.HandFlamInputEffect = System.Drawing.Color.Cyan;
                Fixture.Session.GetType().GetProperty("ColorProfile").SetValue(Fixture.Session, colors);
                var snare = Receptor(player, EliteDrumPad.Snare);
                var tom = Receptor(player, EliteDrumPad.Tom1);
                var array = player.GetComponentInChildren(Production("YARG.Gameplay.Visuals.FretArray"), true);
                int animations = 0;
                Action<int> animationObserver = pad =>
                {
                    Assert.That(pad, Is.EqualTo((int) EliteDrumPad.Snare));
                    animations++;
                };
                array.GetType().GetEvent("OnColoredHitAnimation", INSTANCE)
                    .GetAddMethod(true).Invoke(array, new object[] { animationObserver });
                int followUps = 0;
                var callback = (Action<EliteDrumNote, EliteDrumsAction, float, bool>) ((note, action, velocity, bonus) =>
                {
                    Assert.That(note, Is.SameAs(Playable.Notes[1]));
                    Assert.That(action, Is.EqualTo(EliteDrumsAction.EliteSnare));
                    Assert.That(bonus, Is.EqualTo(!flat));
                    followUps++;
                });
                var field = engine.GetType().GetField("OnFlamFollowUp");
                var handlers = (Delegate) field.GetValue(engine);
                Assert.That(handlers.GetInvocationList().Count(d => ReferenceEquals(d.Target, player)), Is.EqualTo(1));
                field.SetValue(engine, Delegate.Combine(handlers, callback));

                Press(player, EliteDrumsAction.EliteSnare, 1);
                Assert.That(Playable.Notes[0].WasHit, Is.True);
                AssertEffect(snare, System.Drawing.Color.Magenta);
                Assert.That(followUps, Is.Zero, "An ordinary first hit cannot be a flam follow-up.");
                Assert.That(animations, Is.EqualTo(1), "Ordinary first-hit feedback must not be duplicated.");
                Press(player, EliteDrumsAction.EliteSnare, 2);
                Assert.That(Playable.Notes[1].WasHit, Is.True);
                Assert.That(Stat(engine, "NotesHit"), Is.EqualTo(2));
                var combo = Stat(engine, "Combo");
                StopEffect(snare);
                StopEffect(tom);
                Assert.That(Effect(snare).GetComponentsInChildren<ParticleSystem>(true).Any(p => p.isPlaying), Is.False);
                Press(player, EliteDrumsAction.EliteSnare, 2.02);
                Assert.That(followUps, Is.EqualTo(1));
                Assert.That(animations, Is.EqualTo(3), "Each physical strike animates exactly once.");
                AssertEffect(snare, System.Drawing.Color.Magenta);
                Assert.That(Effect(tom).GetComponentsInChildren<ParticleSystem>(true).Any(p => p.isPlaying), Is.False);
                Assert.That(Stat(engine, "NotesHit"), Is.EqualTo(2));
                Assert.That(Stat(engine, "Combo"), Is.EqualTo(combo));
                Assert.That(Playable.Notes[2].WasHit, Is.False);

                yield return Fixture.Dispose();
                Fixture = null;
                var remaining = (Delegate) field.GetValue(engine);
                Assert.That(remaining.GetInvocationList().Any(d => ReferenceEquals(d.Target, player)), Is.False,
                    "Destroyed players must unsubscribe before the next fixture is initialized.");
            }
        }
    }
}
