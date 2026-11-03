// pattern: Functional Core
using System;
using System.Reflection;
using NUnit.Framework;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class ClassicDrumReplayPolicyTests
    {
        [TestCase(false, null, 2)]
        [TestCase(false, (byte) 0, 2)]
        [TestCase(false, (byte) 1, 2)]
        [TestCase(false, (byte) 2, 2)]
        [TestCase(true, null, 0)]
        [TestCase(true, (byte) 0, 0)]
        [TestCase(true, (byte) 1, 1)]
        [TestCase(true, (byte) 2, 2)]
        public void LiveAndRecordedSessionsSelectTheirOwnArticulationRules(bool replay, byte? recorded, byte expected)
        {
            BaseEngineParameters parameters = null;
            if (recorded is { } ruleset)
            {
                var defaults = EnginePreset.Default.Drums.Create(Array.Empty<float>(), Array.Empty<float>(),
                    DrumsEngineParameters.DrumMode.ProFourLane);
                parameters = new DrumsEngineParameters(defaults.HitWindow, defaults.MaxMultiplier,
                    defaults.StarMultiplierThresholds, defaults.SoloBonusStarMultiplierThresholds,
                    defaults.Mode, defaults.NoStarPowerOverlap, defaults.EnableLanes,
                    classicArticulationRuleset: ruleset);
            }

            var type = Type.GetType("YARG.Gameplay.Player.DrumsPlayer, Assembly-CSharp", true);
            var resolve = type.GetMethod("ResolveClassicArticulationRuleset",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(resolve, Is.Not.Null,
                "Gameplay preparation must use recorded classic rules rather than interpreting old replays as new charts.");
            Assert.That(resolve.Invoke(null, new object[] { replay, parameters }), Is.EqualTo(expected));
        }
    }
}
