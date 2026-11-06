// pattern: Imperative Shell
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace YARG.Tests.PlayMode
{
    public sealed partial class EliteDrumsLayoutMenuPlayModeTests
    {
        [UnityTest]
        public IEnumerator FramePacingMenuDimsCapAndShowsSeparateCaptureControl()
        {
            using var state = new SongManagerInteractionState(this);
            var menu = PositionMenu();
            var tabs = ((IEnumerable)Production("YARG.Settings.SettingsManager")
                .GetField("AllSettingsTabs").GetValue(null)).Cast<object>().ToArray();
            object Setting(string name) => state.Settings.GetType().GetProperty(name).GetValue(state.Settings);
            void Set(string name, bool value) => Setting(name).GetType().GetMethod("SetValueWithoutNotify")
                .Invoke(Setting(name), new object[] { value });
            bool savedVsync = (bool)Setting("VSync").GetType().GetProperty("Value").GetValue(Setting("VSync"));
            try
            {
                Set("VSync", true);
                var graphics = tabs.Single(t => (string)t.GetType().GetProperty("Name").GetValue(t) == "Graphics");
                menu.GetType().GetProperty("CurrentTab").GetSetMethod(true).Invoke(menu, new[] { graphics });
                menu.GetType().GetMethod("UpdateSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(menu, new object[] { false });
                yield return null;
                var rows = state.Container.GetComponentsInChildren(Production("YARG.Menu.Settings.Visuals.BaseSettingVisual"));
                var cap = rows.Single(r => (string)r.GetType().GetProperty("UnlocalizedName").GetValue(r) == "FpsCap");
                Assert.That(cap.GetType().GetProperty("IsEditable").GetValue(cap), Is.False);
                Assert.That(cap.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0.5f));
                Set("VSync", false);
                graphics.GetType().GetMethod("OnSettingChanged").Invoke(graphics, null);
                Assert.That(cap.GetType().GetProperty("IsEditable").GetValue(cap), Is.True);
                var experimental = tabs.Single(t => (string)t.GetType().GetProperty("Name").GetValue(t) == "Experimental");
                menu.GetType().GetProperty("CurrentTab").GetSetMethod(true).Invoke(menu, new[] { experimental });
                menu.GetType().GetMethod("UpdateSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(menu, new object[] { false });
                yield return null;
                rows = state.Container.GetComponentsInChildren(Production("YARG.Menu.Settings.Visuals.BaseSettingVisual"));
                var capture = rows.Single(r => (string)r.GetType().GetProperty("UnlocalizedName").GetValue(r) == "TimingDiagnostics");
                var label = capture.GetType().GetProperty("SettingLabel").GetValue(capture);
                Assert.That((string)label.GetType().GetProperty("text").GetValue(label), Is.EqualTo("Save Frame Timing Log"));
                Assert.That(rows.Any(r => (string)r.GetType().GetProperty("UnlocalizedName").GetValue(r) == "FrameGraph"), Is.True);
                var targeted = rows.Single(r => (string)r.GetType().GetProperty("UnlocalizedName").GetValue(r) == "TargetedPerformanceChecks");
                var targetedLabel = targeted.GetType().GetProperty("SettingLabel").GetValue(targeted);
                Assert.That((string)targetedLabel.GetType().GetProperty("text").GetValue(targetedLabel), Is.EqualTo("Targeted Performance Checks"));
            }
            finally { Set("VSync", savedVsync); }
        }

        [UnityTest]
        public IEnumerator FrameDiagnosticsCaptureMenusAndFinishAfterLoggingIsDisabled()
        {
            using var state = new SongManagerInteractionState(this);
            object Setting(string name) => state.Settings.GetType().GetProperty(name).GetValue(state.Settings);
            void Set(string name, bool value) => Setting(name).GetType().GetMethod("SetValueWithoutNotify")
                .Invoke(Setting(name), new object[] { value });
            bool oldGraph = (bool)Setting("FrameGraph").GetType().GetProperty("Value").GetValue(Setting("FrameGraph"));
            bool oldCapture = (bool)Setting("TimingDiagnostics").GetType().GetProperty("Value").GetValue(Setting("TimingDiagnostics"));
            var initialized = state.Settings.GetType().GetField("IsInitialized");
            var probeType = Production("YARG.Persistent.StutterProbe");
            var probe = (Component)probeType.GetProperty("Instance").GetValue(null);
            Assert.That(probe, Is.Not.Null);
            string path = null;
            try
            {
                initialized.SetValue(null, true);
                Set("TimingDiagnostics", false); Set("FrameGraph", true);
                double deadline = Time.realtimeSinceStartupAsDouble + 5;
                while ((int)probeType.GetProperty("SampleCount").GetValue(probe) < 5 && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                Assert.That((int)probeType.GetProperty("SampleCount").GetValue(probe), Is.GreaterThanOrEqualTo(5));
                Assert.That(probeType.GetProperty("CapturePath").GetValue(probe), Is.Null, "Graph must not start disk logging.");
                var latest = probeType.GetProperty("Latest").GetValue(probe);
                Assert.That(latest.GetType().GetField("HasGameplay").GetValue(latest), Is.False);
                var graph = UnityEngine.Object.FindFirstObjectByType(Production("YARG.Persistent.FrameGraph"));
                Assert.That(graph, Is.Not.Null);
                for (int i = 0; i < 20; i++) yield return null;
                var text = (string)graph.GetType().GetField("_readout", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(graph);
                Assert.That(text, Does.Contain("UPDATE CADENCE"));
                Assert.That(text, Does.Contain("Effective:"));
                ScreenCapture.CaptureScreenshot("/tmp/yarg-frame-diagnostics-menu.png");
                Set("TimingDiagnostics", true);
                yield return null;
                path = (string)probeType.GetProperty("CapturePath").GetValue(probe);
                Assert.That(path, Is.Not.Null);
                for (int i = 0; i < 10; i++) yield return null;
                Set("TimingDiagnostics", false);
                yield return null;
                probeType.GetMethod("OnApplicationQuit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(probe, null);
                var rows = File.ReadAllLines(path);
                Assert.That(rows.Length, Is.GreaterThan(4));
                Assert.That(rows.Last(), Does.Contain("capture complete"));
                Assert.That(rows[0], Does.Contain("effectiveVsync"));
                Assert.That(probeType.GetProperty("CaptureError").GetValue(probe), Is.Null);
            }
            finally
            {
                Set("FrameGraph", oldGraph); Set("TimingDiagnostics", oldCapture);
                initialized.SetValue(null, false);
                if (path != null && File.Exists(path)) File.Delete(path);
            }
        }
    }
}
