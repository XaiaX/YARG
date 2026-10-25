using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using YARG.Core.Song.Cache;

namespace YARG.Tests.EditMode
{
    public sealed class SongSourceSettingsTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static object Parse(string json) => Production("Newtonsoft.Json.Linq.JObject")
            .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });

        private static object Deserialize(object json) => Production("YARG.Settings.SettingsManager")
            .GetMethod("DeserializeSongSourceSettings").Invoke(null, new[] { json });

        private static object ToJson(object value)
        {
            var settingsType = Production("Newtonsoft.Json.JsonSerializerSettings");
            var serializerType = Production("Newtonsoft.Json.JsonSerializer");
            var settings = Production("YARG.Settings.SettingsManager")
                .GetField("JsonSettings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var serializer = serializerType.GetMethod("Create", new[] { settingsType })
                .Invoke(null, new[] { settings });
            return Production("Newtonsoft.Json.Linq.JObject")
                .GetMethod("FromObject", new[] { typeof(object), serializerType })
                .Invoke(null, new[] { value, serializer });
        }

        private static object Read(object settings, string name) => settings.GetType().GetField(name).GetValue(settings);

        [OneTimeSetUp]
        public void InitializeAudio()
        {
            // SettingContainer callbacks require the same audio initialization as startup.
            Production("YARG.Helpers.PathHelper").GetMethod("Init").Invoke(null, null);
            var audio = Production("YARG.Core.Audio.GlobalAudioHandler");
            if (audio.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == null)
                audio.GetMethod("Initialize").MakeGenericMethod(Production("YARG.Audio.BASS.BassAudioManager"))
                    .Invoke(null, null);
        }

        [Test]
        public void LegacySongSourcesRequireExplicitSelection()
        {
            var settings = Deserialize(Parse("{ 'SongFolders':['one','two'], 'UseCumulativeSongUpdates':true }"));
            Assert.That(Read(settings, "SongUpdatesDtaPath"), Is.EqualTo(string.Empty));
            Assert.That(Read(settings, "SongUpgradesDtaPath"), Is.EqualTo(string.Empty));
            Assert.That(Read(settings, "SongFolders"), Has.Count.EqualTo(2));
            Assert.That(Read(settings, "ShowExplicitSongSourcesWarning"), Is.True);
        }

        [Test]
        public void SongSourceSettingsRoundTrip()
        {
            var original = Parse("{ 'ExplicitSongSourcesConfigured':true, 'SongFolders':['one','two'], " +
                "'SongUpdatesDtaPath':'missing/community.dta', 'SongUpgradesDtaPath':'missing/elite.dta', " +
                "'ShowExplicitSongSourcesWarning':false }");
            var settings = Deserialize(ToJson(Deserialize(original)));
            Assert.That(Read(settings, "SongUpdatesDtaPath"), Is.EqualTo("missing/community.dta"));
            Assert.That(Read(settings, "SongUpgradesDtaPath"), Is.EqualTo("missing/elite.dta"));
            Assert.That(Read(settings, "SongFolders"), Has.Count.EqualTo(2));
            Assert.That(Read(settings, "ShowExplicitSongSourcesWarning"), Is.False);
        }

        [TestCase("DTA")]
        [TestCase("yargup")]
        public void SourcePickerAcceptsLooseAndPackagedNamesAndRejectsMissingPaths(string extension)
        {
            var actions = Production("YARG.Menu.Settings.SongSourceActions");
            var validate = actions.GetMethod("ValidateSelection");
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "community-elite." + extension);
            try
            {
                File.WriteAllText(path, "()");
                Assert.That(validate.Invoke(null, new object[] { path }), Is.EqualTo(Path.GetFullPath(path)));
                File.Delete(path);
                Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, new object[] { path }));
                Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, new object[] { path + ".txt" }));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void LibraryRefreshActionScansAll()
        {
            // Source-contract guard: exercising the complete library menu requires the runtime scene.
            // Limit checks to the production popup callback and RefreshSongs method body.
            string popup = File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath,
                "Script/Menu/MusicLibrary/PopupMenu.cs"));
            int action = popup.IndexOf("CreateItem(\"ScanAll\"", StringComparison.Ordinal);
            Assert.That(action, Is.GreaterThanOrEqualTo(0));
            int end = popup.IndexOf("});", action, StringComparison.Ordinal);
            Assert.That(popup.Substring(action, end - action), Does.Contain("_musicLibrary.RefreshSongs()"));
            string library = File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath,
                "Script/Menu/MusicLibrary/MusicLibraryMenu.cs"));
            int refresh = library.IndexOf("public async void RefreshSongs()", StringComparison.Ordinal);
            Assert.That(refresh, Is.GreaterThanOrEqualTo(0));
            string method = library.Substring(refresh);
            int next = method.IndexOf("\n        public ", 1, StringComparison.Ordinal);
            if (next >= 0) method = method.Substring(0, next);
            const string scopeCall = @"\bSongContainer\s*\.\s*RunRefresh\s*\(\s*(?:global::)?(?:[A-Za-z_]\w*\s*\.\s*)*SongScanScope\s*\.\s*";
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(method, scopeCall + @"All\b"), Is.True,
                "The library refresh must explicitly request All, whether the enum name is qualified or imported.");
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(method, scopeCall + @"Songs\b"), Is.False);
        }

        [Test]
        public async Task SongManagerBusyAndScopeDispatch()
        {
            var actions = Production("YARG.Menu.Settings.SongSourceActions");
            var refresh = actions.GetField("Refresh");
            var isRefreshing = actions.GetField("IsRefreshing");
            var oldRefresh = refresh.GetValue(null);
            var oldBusy = isRefreshing.GetValue(null);
            var oldFeedback = actions.GetProperty("Feedback").GetValue(null);
            var completion = new TaskCompletionSource<bool>();
            SongScanScope? dispatched = null;
            int calls = 0;
            try
            {
                refresh.SetValue(null, new Func<SongScanScope, Task>(scope =>
                { calls++; dispatched = scope; return completion.Task; }));
                isRefreshing.SetValue(null, new Func<bool>(() => false));
                var scan = actions.GetMethod("Scan");
                var first = (Task)scan.Invoke(null, new object[] { SongScanScope.Updates });
                Assert.That(actions.GetProperty("Busy").GetValue(null), Is.True);
                await (Task)scan.Invoke(null, new object[] { SongScanScope.Updates });
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(dispatched, Is.EqualTo(SongScanScope.Updates));
                completion.SetResult(true);
                await first;
                Assert.That(actions.GetProperty("Busy").GetValue(null), Is.False);
            }
            finally
            {
                completion.TrySetResult(true);
                refresh.SetValue(null, oldRefresh);
                isRefreshing.SetValue(null, oldBusy);
                actions.GetProperty("Feedback").GetSetMethod(true).Invoke(null, new[] { oldFeedback });
            }
        }
    }
}
