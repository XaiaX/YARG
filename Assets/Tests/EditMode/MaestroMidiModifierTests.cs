using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Song;

namespace YARG.Tests.EditMode
{
    public sealed class MaestroMidiModifierTests
    {
        private static Type RuntimeType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static object Call(object target, string method, params object[] arguments) =>
            target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(info => info.Name == method && info.GetParameters().Length == arguments.Length)
                .Invoke(target, arguments);

        private static object Player(YargProfile profile) => Activator.CreateInstance(
            RuntimeType("YARG.Player.YargPlayer"), new object[] { profile, null });

        private static object Session(object player)
        {
            var roster = Array.CreateInstance(player.GetType(), 1);
            roster.SetValue(player, 0);
            return RuntimeType("YARG.Menu.Maestro.MaestroSetupSession").GetMethod("Create")
                .Invoke(null, new object[] { roster, Array.Empty<SongEntry>(), 0, Guid.Empty });
        }

        private static object Staged(object session) =>
            ((IEnumerable)session.GetType().GetProperty("Players").GetValue(session)).Cast<object>().Single();

        private static Modifier Flags(object session) =>
            (Modifier)Staged(session).GetType().GetProperty("Modifiers").GetValue(Staged(session));

        private static YargProfile Profile() => new YargProfile(Guid.NewGuid())
        {
            GameMode = GameMode.EliteDrums,
            CurrentInstrument = Instrument.EliteDrums,
            PreferredInstrument = Instrument.EliteDrums,
            CurrentDifficulty = Difficulty.Expert,
        };

        [Test]
        public void IndependentSession_InitializesOnlyStagedCopy_AndSeparatesAccessibility()
        {
            var profile = Profile();
            var type = RuntimeType("YARG.Menu.Maestro.MaestroSetupSession");
            var active = type.GetProperty("Active").GetValue(null);
            var session = Session(Player(profile));
            Assert.That(type.GetProperty("Active").GetValue(null), Is.SameAs(active));
            Assert.That(profile.CurrentModifiers, Is.EqualTo(Modifier.None));
            Assert.That(Flags(session), Is.EqualTo(Modifier.EnableEliteUpconversion));
            var modifiers = (IEnumerable)Call(session, "GetAvailableModifiers", profile.Id);
            Assert.That(modifiers.Cast<Modifier>(), Does.Contain(Modifier.EnableEliteUpconversion));
            Assert.That(modifiers.Cast<Modifier>(), Does.Contain(Modifier.Enable2xKicks));
            Assert.That(modifiers.Cast<Modifier>(), Does.Contain(Modifier.PreferEliteDowncharts));
            var accessibility = (IEnumerable)Call(session, "GetAvailableAccessibilityModifiers", profile.Id);
            Assert.That(accessibility.Cast<Modifier>().Any(flag =>
                (flag & YargProfile.MIDI_DRUM_PREFERENCES) != 0), Is.False);
        }

        [Test]
        public void BulkAdjustment_AndModeNormalization_PreserveDormantPreferences()
        {
            var profile = Profile();
            var session = Session(Player(profile));
            Call(session, "StageModifiers", profile.Id, Modifier.Enable2xKicks | Modifier.PreferEliteDowncharts);
            Assert.That(Flags(session), Is.EqualTo(Modifier.Enable2xKicks | Modifier.PreferEliteDowncharts));
            var staged = Staged(session);
            staged.GetType().GetProperty("GameMode").SetValue(staged, GameMode.FourLaneDrums);
            var normalize = session.GetType().GetMethod("NormalizeModifiers", BindingFlags.NonPublic | BindingFlags.Static);
            normalize.Invoke(null, new[] { staged });
            Assert.That(Flags(session) & YargProfile.MIDI_DRUM_PREFERENCES, Is.EqualTo(Modifier.None));
            staged.GetType().GetProperty("GameMode").SetValue(staged, GameMode.EliteDrums);
            normalize.Invoke(null, new[] { staged });
            Assert.That(Flags(session), Is.EqualTo(Modifier.Enable2xKicks | Modifier.PreferEliteDowncharts));
            Assert.That(profile.CurrentModifiers, Is.EqualTo(Modifier.None));
        }

        [Test]
        public void RejectedCommit_DoesNotPersistStagedPreferencesOrPreferredInstrument()
        {
            var profile = Profile();
            var session = Session(Player(profile));
            Call(session, "StageModifier", profile.Id, Modifier.Enable2xKicks, true);
            var result = Call(session, "TryCommit");
            Assert.That(result.GetType().GetProperty("Success").GetValue(result), Is.False);
            Assert.That(profile.CurrentModifiers, Is.EqualTo(Modifier.None));
            Assert.That(profile.PreferredInstrument, Is.EqualTo(Instrument.EliteDrums));
            profile.InitializeLiveMidiDrumModifiers();
            Assert.That(profile.CurrentModifiers, Is.EqualTo(Modifier.EnableEliteUpconversion));
        }

        [Test]
        public void SnapshotRestore_RestoresSavedAndEffectiveFlagsAndInitializationState()
        {
            var profile = Profile();
            profile.AddSingleModifier(Modifier.NoKicks);
            profile.RestoreSessionModifiers(Modifier.NoDynamics);
            var player = Player(profile);
            var snapshotType = RuntimeType("YARG.Menu.Maestro.MaestroSetupSession")
                .GetNestedType("ProfileSnapshot", BindingFlags.NonPublic);
            var snapshot = Activator.CreateInstance(snapshotType, new[] { player });
            profile.InitializeLiveMidiDrumModifiers();
            profile.AddSingleModifier(Modifier.Enable2xKicks);
            profile.PreferredInstrument = Instrument.ProDrums;
            Call(snapshot, "Restore");
            Assert.That(profile.CurrentModifiers, Is.EqualTo(Modifier.NoDynamics));
            Assert.That(profile.PreferredInstrument, Is.EqualTo(Instrument.EliteDrums));
            profile.RestoreSavedModifiers();
            Assert.That(profile.CurrentModifiers, Is.EqualTo(Modifier.NoKicks));
            profile.InitializeLiveMidiDrumModifiers();
            Assert.That(profile.CurrentModifiers, Is.EqualTo(Modifier.NoKicks | Modifier.EnableEliteUpconversion));
        }
    }
}
