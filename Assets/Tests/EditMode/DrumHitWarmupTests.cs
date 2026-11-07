// pattern: Imperative Shell
using System;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Engine.Drums;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class DrumHitWarmupTests
    {
        [TestCase(Instrument.EliteDrums, DrumsEngineParameters.DrumMode.ProFourLane, true, true)]
        [TestCase(Instrument.EliteDrums, DrumsEngineParameters.DrumMode.ProFourLane, false, true)]
        [TestCase(Instrument.ProDrums, DrumsEngineParameters.DrumMode.ProFourLane, true, false)]
        [TestCase(Instrument.ProDrums, DrumsEngineParameters.DrumMode.ProFourLane, false, true)]
        [TestCase(Instrument.FourLaneDrums, DrumsEngineParameters.DrumMode.NonProFourLane, false, false)]
        [TestCase(Instrument.FiveLaneDrums, DrumsEngineParameters.DrumMode.FiveLane, false, false)]
        public void PrivateEngineAdjudicatesAStrikeAndWarmsEachPathOnce(Instrument instrument,
            DrumsEngineParameters.DrumMode mode, bool bot, bool midi)
        {
            var type = Type.GetType("YARG.Gameplay.Player.DrumHitWarmup, Assembly-CSharp", throwOnError: true);
            var prepare = type.GetMethod("PrepareOnce", BindingFlags.Static | BindingFlags.NonPublic);
            var parameters = EnginePreset.Default.Drums.Create(new float[] { 1, 2, 3, 4, 5, 6 },
                new float[] { 1, 2, 3, 4, 5, 6 }, mode);
            var args = new object[] { instrument, bot, midi, parameters };
            var warmed = (HashSet<(Instrument, bool, bool, bool)>) type
                .GetField("_warmed", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var key = (instrument, bot, midi,
                instrument == Instrument.EliteDrums && parameters.EliteHiHatSustains != 0);
            bool alreadyWarm = warmed.Remove(key);
            try
            {
                Assert.That(prepare.Invoke(null, args), Is.True);
                Assert.That(prepare.Invoke(null, args), Is.False);
            }
            finally
            {
                if (!alreadyWarm) warmed.Remove(key);
                else warmed.Add(key);
            }
        }
    }
}
