// pattern: Imperative Shell
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class PerformanceFixtureLaunchTests
    {
        private static Type Launch => Type.GetType("YARG.PerformanceTesting.PerformanceFixtureLaunch, Assembly-CSharp", true);
        private string _root;
        private string _fallback;

        [SetUp]
        public void SetUp()
        {
            Assert.That(Get("Requested"), Is.False, "Tests must not run inside a live performance fixture");
            _root = Path.Combine(Path.GetTempPath(), "yarg-fixture-launch-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            ((IDisposable) Launch.GetField("_reservation", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))?.Dispose();
            foreach (var field in Launch.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
                if (!field.IsInitOnly) field.SetValue(null, field.FieldType == typeof(bool) ? false : null);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            if (_fallback != null && Directory.Exists(_fallback)) Directory.Delete(_fallback, true);
        }

        [Test]
        public void FreshRunSeedsIsolatedSourcesAndNoAutoConnectedProfiles()
        {
            string input = Path.Combine(_root, "input.json");
            File.WriteAllText(input, "{\"mode\":\"discover\",\"songFolders\":[\"" + _root + "\"]}");
            Initialize(input, Path.Combine(_root, "run"));
            Assert.That(Get("Error"), Is.Null);
            var data = (string) Get("DataDirectory");
            Assert.That(data, Is.EqualTo(Path.Combine(_root, "run", "data")));
            var settings = File.ReadAllText(Path.Combine(data, "settings.json"));
            StringAssert.Contains("\"ReconnectProfiles\": false", settings);
            StringAssert.Contains("\"AutoCreateProfiles\": false", settings);
            StringAssert.Contains("\"MaestroEnable\": false", settings);
            StringAssert.Contains(_root, settings);
        }

        [Test]
        public void ReusedOutputPreservesExistingSettingsAndFallsBackToFreshIsolation()
        {
            var run = Path.Combine(_root, "run");
            var data = Path.Combine(run, "data");
            Directory.CreateDirectory(data);
            var settings = Path.Combine(data, "settings.json");
            File.WriteAllText(settings, "sentinel experiment");
            Initialize("missing.json", run);
            _fallback = (string) Get("OutputDirectory");
            Assert.That(Get("Error"), Is.Not.Null);
            Assert.That(Get("DataDirectory"), Is.Not.EqualTo(data));
            Assert.That(File.ReadAllText(settings), Is.EqualTo("sentinel experiment"));
            Assert.That(File.Exists(Path.Combine((string) Get("DataDirectory"), "settings.json")), Is.True);
        }

        [Test]
        public void MalformedInputCannotFallBackToNormalUserData()
        {
            string input = Path.Combine(_root, "input.json");
            File.WriteAllText(input, "{broken");
            Initialize(input, Path.Combine(_root, "run"));
            _fallback = (string) Get("OutputDirectory");
            Assert.That(Get("Requested"), Is.True);
            Assert.That(Get("Error"), Is.Not.Null);
            var settings = File.ReadAllText(Path.Combine((string) Get("DataDirectory"), "settings.json"));
            StringAssert.Contains("\"SongFolders\": []", settings);
        }

        private static object Get(string name) => Launch.GetProperty(name).GetValue(null);
        private static void Initialize(string input, string output) => Launch.GetMethod("Initialize").Invoke(null, new object[] { input, output });
    }
}
