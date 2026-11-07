// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.IO;
using YARG.Core.Song;

namespace YARG.Tests.PlayMode
{
    public sealed class MenuMusicLifetimePlayModeTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PRIVATE_STATIC = BindingFlags.Static | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator HidingParentCancelsPendingMusicWithoutStartingAnotherLoad()
        {
            var playerType = Type.GetType("YARG.Menu.Persistent.MusicPlayer, Assembly-CSharp", true);
            var songContainer = Type.GetType("YARG.Song.SongContainer, Assembly-CSharp", true);
            var settingsType = Type.GetType("YARG.Settings.SettingsManager, Assembly-CSharp", true);
            var songs = songContainer.GetField("_songs", PRIVATE_STATIC);
            var settings = settingsType.GetProperty("Settings");
            var screenHelper = Type.GetType("YARG.Helpers.ScreenHelper, Assembly-CSharp", true);
            var resolution = screenHelper.GetField("_lastScreenResolution", PRIVATE_STATIC);
            var priorResolution = resolution.GetValue(null);
            var nowPlaying = playerType.GetField("_nowPlaying", PRIVATE_STATIC);
            var priorSongs = songs.GetValue(null);
            var priorSettings = settings.GetValue(null);
            var priorSong = nowPlaying.GetValue(null);
            bool priorApply = StemSettings.ApplySettings;
            var load = new PendingLoad();
            var parent = new GameObject("Help bar");
            parent.SetActive(false);
            var child = new GameObject("Menu music");
            child.transform.SetParent(parent.transform);
            try
            {
                if (priorSettings == null)
                {
                    resolution.SetValue(null, screenHelper.GetMethod("GetScreenResolution").Invoke(null, null));
                    settings.GetSetMethod(true).Invoke(null, new[] { CreateSettings(settings.PropertyType) });
                }
                songs.SetValue(null, new SongEntry[] { new TestSong(load), new TestSong(load) });
                nowPlaying.SetValue(null, null);
                var player = child.AddComponent(playerType);
                foreach (string fieldName in new[] { "_songText", "_artistText" })
                {
                    var field = playerType.GetField(fieldName, PRIVATE_INSTANCE);
                    var text = new GameObject(fieldName, typeof(RectTransform));
                    text.transform.SetParent(child.transform);
                    field.SetValue(player, text.AddComponent(field.FieldType));
                }

                parent.SetActive(true);
                float deadline = Time.realtimeSinceStartup + 5;
                while (load.Entered == 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(load.Entered, Is.EqualTo(1), "The initial background load must be in flight.");
                parent.SetActive(false);
                Assert.That(child.activeSelf, Is.True, "Only the parent is hidden.");

                // The scene transition uses the same drain after explicitly stopping the player.
                var stop = playerType.GetMethod("StopAndDrainAsync");
                Task drain = stop == null ? null : (Task) stop.Invoke(player, null);
                if (drain != null) Assert.That(drain.IsCompleted, Is.False, "Gameplay must wait for the in-flight decoder.");
                load.Release = true;
                deadline = Time.realtimeSinceStartup + 1;
                while ((drain == null || !drain.IsCompleted) && load.Entered < 2 &&
                    Time.realtimeSinceStartup < deadline) yield return null;

                Assert.That(load.Entered, Is.EqualTo(1), "A disabled menu must not try another track.");
                Assert.That(drain?.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
            finally
            {
                load.Release = true;
                UnityEngine.Object.DestroyImmediate(parent);
                songs.SetValue(null, priorSongs);
                settings.GetSetMethod(true).Invoke(null, new[] { priorSettings });
                resolution.SetValue(null, priorResolution);
                nowPlaying.SetValue(null, priorSong);
                StemSettings.ApplySettings = priorApply;
            }
        }

        private static object CreateSettings(Type type)
        {
            // Only the menu settings are needed; avoid starting the real audio backend.
            var settings = FormatterServices.GetUninitializedObject(type);
            foreach (string name in new[] { "ApplyVolumesInMusicLibrary", "ApplyVolumesInMusicPlayer", "CensorMatureContent" })
            {
                var field = type.GetField($"<{name}>k__BackingField", PRIVATE_INSTANCE);
                field.SetValue(settings, Activator.CreateInstance(field.FieldType, false, null));
            }
            var volume = type.GetField("<MusicPlayerVolume>k__BackingField", PRIVATE_INSTANCE);
            volume.SetValue(settings, Activator.CreateInstance(volume.FieldType, 0.15f, null));
            return settings;
        }

        private sealed class PendingLoad
        {
            public volatile bool Release;
            public volatile int Entered;

            public StemMixer Run()
            {
                Interlocked.Increment(ref Entered);
                if (!SpinWait.SpinUntil(() => Release, TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The test did not release its simulated audio load.");
                return null;
            }
        }

        private sealed class TestSong : SongEntry
        {
            private readonly PendingLoad _load;
            public TestSong(PendingLoad load) => _load = load;
            public override EntryType SubType => EntryType.Ini;
            public override string SortBasedLocation => "menu-music-lifetime";
            public override string ActualLocation => SortBasedLocation;
            public override DateTime GetLastWriteTime() => DateTime.UnixEpoch;
            public override SongChart LoadChart(IReadOnlyCollection<Instrument> eliteDrumsDownchartOutputs = null) => throw new NotSupportedException();
            public override StemMixer LoadAudio(float speed, double volume, bool enableCensoring, params SongStem[] ignoreStems) => _load.Run();
            public override StemMixer LoadPreviewAudio(float speed, bool enableCensoring) => throw new NotSupportedException();
            public override YARGImage LoadAlbumData() => null;
            public override BackgroundResult LoadBackground(bool censoringEnabled, bool excludeYarground = false) => null;
            public override FixedArray<byte> LoadMiloData() => null;
            public override FixedArray<byte> LoadVocData() => null;
        }
    }
}
