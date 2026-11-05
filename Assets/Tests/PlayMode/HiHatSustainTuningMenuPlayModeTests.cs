// pattern: Imperative Shell
using System;
using System.Collections;
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
        public IEnumerator ExperimentalHiHatControlsBuildWithLocalizedLabelsAndEditRealValues()
        {
            using var state = new SongManagerInteractionState(this);
            var manager = Production("YARG.Settings.SettingsManager");
            var experimental = ((IEnumerable)manager.GetField("AllSettingsTabs").GetValue(null)).Cast<object>()
                .Single(tab => (string)tab.GetType().GetProperty("Name").GetValue(tab) == "Experimental");
            var menu = PositionMenu();
            menu.GetType().GetProperty("CurrentTab").GetSetMethod(true).Invoke(menu, new[] { experimental });
            menu.GetType().GetMethod("UpdateSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(menu, new object[] { false });
            yield return null;
            var rows = state.Container.GetComponentsInChildren(Production("YARG.Menu.Settings.Visuals.SliderSettingVisual"));
            string[] names = { "EliteHiHatWaitingOpacity", "EliteHiHatHeldOpacity", "EliteHiHatWaitingGlow", "EliteHiHatHeldGlow" };
            float[] saved = new float[names.Length];
            object[] settings = new object[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                settings[i] = state.Settings.GetType().GetProperty(names[i]).GetValue(state.Settings);
                saved[i] = (float)settings[i].GetType().GetProperty("Value").GetValue(settings[i]);
            }
            try
            {
                for (int i = 0; i < names.Length; i++)
                {
                    var row = rows.Single(candidate => (string)candidate.GetType().GetProperty("UnlocalizedName").GetValue(candidate) == names[i]);
                    var label = row.GetType().GetProperty("SettingLabel").GetValue(row);
                    string text = (string)label.GetType().GetProperty("text").GetValue(label);
                    Assert.That(text, Does.Contain("Hi-Hat Sustain"));
                    Assert.That(text, Does.Contain("%"));
                    Assert.That((bool)row.GetType().GetProperty("IsEditable").GetValue(row), Is.True);
                    var slider = row.GetType().GetField("_slider", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(row);
                    slider.GetType().GetProperty("Value").SetValue(slider, 37f + i);
                    Assert.That(settings[i].GetType().GetProperty("Value").GetValue(settings[i]), Is.EqualTo(37f + i));
                }
            }
            finally
            {
                for (int i = 0; i < names.Length; i++)
                    settings[i].GetType().GetMethod("SetValueWithoutNotify").Invoke(settings[i], new object[] { saved[i] });
            }
        }
    }
}
