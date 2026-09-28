using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class NativeElitePedalAccessibilityEditModeTests
    {
        [Test]
        public void NoHiHatIsEliteAccessibilityOnly()
        {
            var rules = Type.GetType("YARG.Menu.Maestro.MaestroSelectionRules, Assembly-CSharp");
            Assert.That(rules, Is.Not.Null);
            var classify = rules.GetMethod("IsAccessibilityModifier");
            Assert.That(classify.Invoke(null, new object[] { Modifier.NoHiHat }), Is.True);
            var (elite, _) = GameMode.EliteDrums.PossibleModifiers(Instrument.EliteDrums);
            var (drums, _) = GameMode.FourLaneDrums.PossibleModifiers(Instrument.FourLaneDrums);
            Assert.That(elite & Modifier.NoHiHat, Is.Not.EqualTo(Modifier.None));
            Assert.That(drums & Modifier.NoHiHat, Is.EqualTo(Modifier.None));
        }

        [Test]
        public void NativeEngineAndMenusWirePedalSettings()
        {
            var engine = typeof(YARG.Core.Engine.Drums.Engines.EliteDrumsEngine);
            Assert.That(engine.GetField("OnPedalAssisted")?.FieldType,
                Is.EqualTo(typeof(Action<YARG.Core.Chart.EliteDrumNote>)));
            Assert.That(engine.GetConstructors().Any(ctor => ctor.GetParameters().Length == 6), Is.True);

            string root = Application.dataPath;
            string player = File.ReadAllText(Path.Combine(root, "Script/Gameplay/Player/EliteDrumsPlayer.cs"));
            Assert.That(player, Does.Contain("Player.Profile.EffectiveAutoHiHatPedal"));
            Assert.That(player, Does.Contain("engine.OnPedalAssisted += OnPedalAssisted"));

            string difficulty = File.ReadAllText(Path.Combine(root, "Script/Menu/DifficultySelect/DifficultySelectMenu.cs"));
            Assert.That(difficulty, Does.Contain("Modifier.NoHiHat"));
            Assert.That(difficulty, Does.Contain("profile.NoHiHatPedal = on"));
            string maestro = File.ReadAllText(Path.Combine(root, "Script/Menu/Maestro/MaestroSetupSession.cs"));
            Assert.That(maestro, Does.Contain("profile.NoHiHatPedal = staged.NoHiHatPedal"));
            Assert.That(maestro, Does.Contain("Profile.NoHiHatPedal = NoHiHatPedal"));
            Assert.That(maestro, Does.Contain("profile.AutoHiHatPedal = staged.AutoHiHatPedal"));
        }
    }
}
