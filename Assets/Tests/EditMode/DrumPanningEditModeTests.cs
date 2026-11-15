using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using YARG.Core.Audio;

namespace YARG.Tests.EditMode
{
    public sealed class DrumPanningEditModeTests
    {
        [Test]
        public void ExperimentalDropdownSelectsPlaybackPreference()
        {
            const string SETTING_NAME = "PreferredDrumPanning";
            var manager = FindType("YARG.Settings.SettingsManager");
            var tabs = (IEnumerable) manager.GetField("AllSettingsTabs").GetValue(null);
            var experimentalTab = tabs.Cast<object>().Single(tab =>
                (string) tab.GetType().GetProperty("Name").GetValue(tab) == "Experimental");
            var fields = ((IEnumerable) experimentalTab.GetType().GetProperty("Settings").GetValue(experimentalTab))
                .Cast<object>().Select(field => field.GetType().GetProperty("FieldName")?.GetValue(field));
            Assert.That(fields, Does.Contain(SETTING_NAME));

            var audioType = typeof(GlobalAudioHandler);
            bool ownsAudio = audioType.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null) == null;
            if (ownsAudio)
            {
                FindType("YARG.Helpers.PathHelper").GetMethod("Init").Invoke(null, null);
                audioType.GetMethod("Initialize").MakeGenericMethod(FindType("YARG.Audio.BASS.BassAudioManager"))
                    .Invoke(null, null);
            }

            var previous = GlobalAudioHandler.PreferredDrumPanning;
            try
            {
                VerifyDropdown(manager, SETTING_NAME);
            }
            finally
            {
                GlobalAudioHandler.PreferredDrumPanning = previous;
                if (ownsAudio)
                {
                    GlobalAudioHandler.Close();
                }
            }
        }

        private static void VerifyDropdown(Type manager, string settingName)
        {
            var container = Activator.CreateInstance(manager.GetNestedType("SettingContainer"));
            var dropdown = container.GetType().GetProperty(settingName).GetValue(container);
            var options = (IEnumerable<DrumPanningPreference>) dropdown.GetType()
                .GetProperty("PossibleValues").GetValue(dropdown);
            Assert.That(options, Is.EqualTo(new[]
            {
                DrumPanningPreference.DontChange,
                DrumPanningPreference.ForceAudiencePanning,
                DrumPanningPreference.ForceDrummerPanning
            }));

            var value = dropdown.GetType().GetProperty("Value");
            Assert.That(value.GetValue(dropdown), Is.EqualTo(DrumPanningPreference.DontChange));
            foreach (var preference in options)
            {
                value.SetValue(dropdown, preference);
                Assert.That(GlobalAudioHandler.PreferredDrumPanning, Is.EqualTo(preference));
            }
        }

        [Test]
        public void PlaybackOnlyReversesLeftAndRightForFlaggedStem()
        {
            var helpers = FindType("YARG.Audio.BASS.BassHelpers");
            const BindingFlags FLAGS = BindingFlags.Static | BindingFlags.NonPublic;
            var buildMatrix = helpers.GetMethod("BuildVolumeMatrix", FLAGS, null,
                new[] { typeof(IEnumerable<StemMixer.StemInfo>), typeof(int), typeof(bool) }, null);
            var buildAuthoredMatrix = helpers.GetMethod("BuildVolumeMatrix", FLAGS, null,
                new[] { typeof(StemMixer.StemInfo) }, null);

            var kit = new StemMixer.StemInfo(SongStem.Drums2, new[] { 1, 2 }, new[] { 1f, 2f, 3f, 4f })
            {
                SwapStereo = true
            };
            var kick = new StemMixer.StemInfo(SongStem.Drums1, new[] { 0 }, new[] { 5f, 6f });
            var guitar = new StemMixer.StemInfo(SongStem.Guitar, new[] { 3, 4 }, new[] { 7f, 8f, 9f, 10f });
            var playback = (float[,]) buildMatrix.Invoke(null, new object[] { new[] { kick, kit, guitar }, 5, true });
            var authored = (float[,]) buildAuthoredMatrix.Invoke(null, new object[] { kit });

            Assert.That(playback, Is.EqualTo(new float[,] { { 5, 2, 4, 7, 9 }, { 6, 1, 3, 8, 10 } }));
            Assert.That(authored, Is.EqualTo(new float[,] { { 1, 3 }, { 2, 4 } }));
        }

        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
    }
}
