// pattern: Imperative Shell
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using YARG.Core.Diagnostics;

namespace YARG.Tests.EditMode
{
    public sealed class TargetedPerformanceIntegrationTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

        [TearDown]
        public void Reset() => TargetedPerformance.SetEnabled(false);

        [Test]
        public void ExperimentalSettingDefaultsOffAndAppliesImmediately()
        {
            Production("YARG.Helpers.PathHelper").GetMethod("Init").Invoke(null, null);
            var audio = Production("YARG.Core.Audio.GlobalAudioHandler");
            bool ownsAudio = audio.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null;
            if (ownsAudio)
                audio.GetMethod("Initialize").MakeGenericMethod(Production("YARG.Audio.BASS.BassAudioManager")).Invoke(null, null);
            try
            {
                var settingsType = Production("YARG.Settings.SettingsManager+SettingContainer");
                var settings = Activator.CreateInstance(settingsType);
                var property = settingsType.GetProperty("TargetedPerformanceChecks");
                Assert.That(property, Is.Not.Null);
                var setting = property.GetValue(settings);
                var value = setting.GetType().GetProperty("Value");
                Assert.That(value.GetValue(setting), Is.False);
                value.SetValue(setting, true);
                Assert.That(TargetedPerformance.Enabled, Is.True);
                value.SetValue(setting, false);
                Assert.That(TargetedPerformance.Enabled, Is.False);
            }
            finally
            {
                if (ownsAudio) audio.GetMethod("Close").Invoke(null, null);
            }
        }

        [Test]
        public void ProbeRecordsIntervalAndToggleTransitionsWithoutLeakingOldSamples()
        {
            var probeType = Production("YARG.Persistent.StutterProbe");
            var host = new GameObject("targeted-probe-test"); host.SetActive(false);
            try
            {
                var probe = host.AddComponent(probeType);
                var sample = probeType.GetMethod("Sample");
                sample.Invoke(probe, new object[] { 1.0 });
                TargetedPerformance.SetEnabled(true);
                using (TargetedPerformance.Measure(TargetedOperation.HiHatActivation)) { }
                sample.Invoke(probe, new object[] { 1.02 });
                object latest = probeType.GetProperty("Latest").GetValue(probe);
                TargetedPerformanceSnapshot Snapshot() => (TargetedPerformanceSnapshot)latest.GetType().GetField("Targeted").GetValue(latest);
                Assert.That(Snapshot().HiHatActivation.Calls, Is.EqualTo(1));
                Assert.That((bool)latest.GetType().GetField("TargetedChanged").GetValue(latest), Is.True);
                sample.Invoke(probe, new object[] { 1.04 });
                latest = probeType.GetProperty("Latest").GetValue(probe);
                Assert.That(Snapshot().HiHatActivation.Calls, Is.Zero);
                Assert.That((bool)latest.GetType().GetField("TargetedChanged").GetValue(latest), Is.False);
                TargetedPerformance.SetEnabled(false);
                sample.Invoke(probe, new object[] { 1.06 });
                latest = probeType.GetProperty("Latest").GetValue(probe);
                Assert.That(Snapshot().Enabled, Is.False);
                Assert.That((bool)latest.GetType().GetField("TargetedChanged").GetValue(latest), Is.True);
                Assert.That((double)latest.GetType().GetField("WallMs").GetValue(latest), Is.EqualTo(20).Within(0.001));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealNoteGroupReportsMaterialAccessAndFirstUseWithoutChangingMaterials(bool enabled)
        {
            var host = new GameObject("note-timing-test");
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            Material instance = null;
            try
            {
                var renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var themeType = Production("YARG.Themes.ThemeNote");
                var theme = host.AddComponent(themeType);
                foreach (var field in themeType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(f => f.FieldType.IsArray))
                    field.SetValue(theme, Array.CreateInstance(field.FieldType.GetElementType(), 0));
                var colored = themeType.GetField("_coloredMaterials", BindingFlags.NonPublic | BindingFlags.Instance);
                var itemType = colored.FieldType.GetElementType();
                var item = Activator.CreateInstance(itemType);
                itemType.GetField("Mesh").SetValue(item, renderer);
                var items = Array.CreateInstance(itemType, 1); items.SetValue(item, 0);
                colored.SetValue(theme, items);
                var groupType = Production("YARG.Gameplay.Visuals.NoteGroup");
                var group = host.AddComponent(groupType);
                groupType.GetField("_themeNote", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(group, theme);
                TargetedPerformance.SetEnabled(enabled);
                // This is the real play-mode path, exercised in EditMode with explicit cleanup below.
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                    "Instantiating material due to calling renderer.material during edit mode. This will leak materials into the scene. You most likely want to use renderer.sharedMaterial instead.");
                groupType.GetMethod("Initialize").Invoke(group, null);
                instance = renderer.sharedMaterial;
                var first = TargetedPerformance.TakeSnapshot();
                groupType.GetMethod("Initialize").Invoke(group, null);
                groupType.GetMethod("SetActive").Invoke(group, new object[] { false });
                groupType.GetMethod("SetActive").Invoke(group, new object[] { true });
                var reused = TargetedPerformance.TakeSnapshot();
                Assert.That(renderer.sharedMaterial, Is.SameAs(instance));
                Assert.That(host.activeSelf, Is.True);
                Assert.That(first.NoteGroupInitialize.Calls, Is.EqualTo(enabled ? 1 : 0));
                Assert.That(first.MaterialAccess.Calls, Is.EqualTo(enabled ? 1 : 0));
                Assert.That(first.FirstNoteGroupInitializations, Is.EqualTo(enabled ? 1 : 0));
                Assert.That(reused.FirstNoteGroupInitializations, Is.Zero);
                Assert.That(reused.NoteGroupInitialize.Calls, Is.EqualTo(enabled ? 1 : 0));
                Assert.That(reused.MaterialAccess.Calls, Is.EqualTo(enabled ? 1 : 0));
                Assert.That(reused.NoteGroupSetActive.Calls, Is.EqualTo(enabled ? 2 : 0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                if (instance != null && instance != material) UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void CurrentThemeDoesNotExecuteTheObsoleteHiHatDiscoveryPath()
        {
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab");
            Assert.That(theme.GetComponentsInChildren<MonoBehaviour>(true)
                .Any(c => c != null && c.GetType().Name == "HiHatBeatAnimation"), Is.False);
        }
    }
}
