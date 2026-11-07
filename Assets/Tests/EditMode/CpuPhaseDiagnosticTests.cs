// pattern: Imperative Shell
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class CpuPhaseDiagnosticTests
    {
        [Test]
        public void CpuPhasesDescribeNativeAndAllocationWindowsSeparately()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("YARG.Persistent.CpuPhaseDiagnostics"))
                .FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, "Unattributed CPU stalls need opt-in phase diagnostics.");
            using var instance = (IDisposable)Activator.CreateInstance(type);
            var capabilities = (string)type.GetProperty("Capabilities").GetValue(instance);
            Assert.That(capabilities, Does.Contain("previous completed native recording interval"));
            Assert.That(capabilities, Does.Contain("probe-to-probe"));
            Assert.That(capabilities, Does.Contain("do not sum"));
            foreach (string marker in new[] { "InputUpdate", "PreUpdate.NewInputUpdate",
                "EarlyUpdate.UpdateInputManager", "PlayerConnection.Poll", "EarlyUpdate.PollPlayerConnection",
                "Profiler.FlushMemoryCounters", "Update.ScriptRunBehaviourUpdate",
                "PreLateUpdate.ScriptRunBehaviourLateUpdate", "Update.ScriptRunDelayedTasks",
                "PostLateUpdate.FinishFrameRendering", "PostLateUpdate.UpdateAllRenderers" })
                Assert.That(capabilities, Does.Contain(marker));
            Assert.That(capabilities, Does.Contain("relatedAvailableMarkers"));

            object Read() => type.GetMethod("Read").Invoke(instance, null);
            var first = Read();
            Assert.That(first.GetType().GetField("Frame").GetValue(first), Is.EqualTo(Time.frameCount - 1));
            Assert.That(first.GetType().GetField("SampledMask").GetValue(first), Is.EqualTo(0));
            Assert.That(first.GetType().GetField("MainAllocatedBytesSampled").GetValue(first), Is.False);
            type.GetMethod("Reset").Invoke(instance, null);
            var reset = Read();
            Assert.That(reset.GetType().GetField("SampledMask").GetValue(reset), Is.EqualTo(0));
            Assert.That(reset.GetType().GetField("MainAllocatedBytesSampled").GetValue(reset), Is.False);
            instance.Dispose();
            Assert.That(Read().GetType().GetField("Enabled").GetValue(Read()), Is.False);
            instance.Dispose();
        }
    }
}
