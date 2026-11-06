using System;
using System.Collections.Generic;
using NUnit.Framework;
using YARG.Core.Chart;

// pattern: Imperative Shell
namespace YARG.Tests.EditMode
{
    public sealed class CymbalBeatPhaseTests
    {
        private static Type Motion => Type.GetType("YARG.Gameplay.Visuals.HiHatBeatMotion, Assembly-CSharp", true);
        private static object Call(string name, params object[] arguments) => Motion.GetMethod(name).Invoke(null, arguments);

        [Test]
        public void SharedBeatPhaseFollowsTempoChangesAndNegativeTimeWithoutTickRounding()
        {
            var sync = new SyncTrack(480u,
                new List<TempoChange> { new(60, 0, 0), new(120, 2, 960) },
                new List<TimeSignatureChange> { new(4, 4, 0, 0, 0, 0, 0, 0) }, new List<Beatline>());
            Assert.That((double) Call("EvaluateBeatPhase", 0.2501d, sync), Is.EqualTo(0.2501d).Within(1e-10));
            Assert.That((double) Call("EvaluateBeatPhase", 2.125d, sync), Is.EqualTo(2.25d).Within(1e-10));
            Assert.That((double) Call("EvaluateBeatPhase", -0.25d, sync), Is.EqualTo(-0.25d).Within(1e-10));
        }

    }
}