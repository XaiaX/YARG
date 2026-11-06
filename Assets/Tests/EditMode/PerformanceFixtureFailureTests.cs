// pattern: Imperative Shell
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class PerformanceFixtureFailureTests
    {
        [Test]
        public void GameplayExceptionFailsTheRunnerEvenIfSongClockStillAdvances()
        {
            var type = Type.GetType("YARG.PerformanceTesting.PerformanceFixtureRunner, Assembly-CSharp", true);
            var go = new GameObject("Fixture failure test");
            try
            {
                var runner = go.AddComponent(type);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var observe = type.GetMethod("ObserveLog", flags);
                var errors = type.GetField("_runtimeErrors", flags);
                observe.Invoke(runner, new object[] { "before launch", "", LogType.Exception });
                Assert.That(errors.GetValue(runner), Is.EqualTo(0));
                type.GetField("_launched", flags).SetValue(runner, true);
                observe.Invoke(runner, new object[] { "ordinary warning", "", LogType.Warning });
                Assert.That(errors.GetValue(runner), Is.EqualTo(0));
                observe.Invoke(runner, new object[] { "instrument update failed", "", LogType.Exception });
                var error = Assert.Throws<TargetInvocationException>(() => type.GetMethod("Tick", flags).Invoke(runner, null));
                Assert.That(error.InnerException, Is.InstanceOf<InvalidOperationException>());
                StringAssert.Contains("instrument update failed", error.InnerException.Message);
                type.GetField("_finishing", flags).SetValue(runner, true);
                observe.Invoke(runner, new object[] { "shutdown", "", LogType.Error });
                Assert.That(errors.GetValue(runner), Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
