// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class YoloMotdEditModeTests
    {
        [TestCase("en-US")]
        [TestCase("fr-FR")]
        public void MainMenuKeepsBundledYoloWarningWhenRemoteMessagesArrive(string culture)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Menu/MainMenu/MainMenu.prefab");
            var text = prefab.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(component => component != null && component.GetType().FullName == "YARG.Localization.LocalizeText")
                .Select(component => new SerializedObject(component).FindProperty("_localizationKey").stringValue)
                .Single(key => key.StartsWith("Menu.Main.", StringComparison.Ordinal) &&
                    key.EndsWith("MessageOfTheDay", StringComparison.Ordinal));

            var runtime = AppDomain.CurrentDomain.GetAssemblies().First(assembly => assembly.GetName().Name == "Assembly-CSharp");
            var localization = runtime.GetType("YARG.Localization.LocalizationManager", true);
            var path = runtime.GetType("YARG.Helpers.PathHelper", true).GetProperty("StreamingAssetsPath");
            var cultureCode = localization.GetProperty("CultureCode");
            var map = (Dictionary<string, string>) localization.GetField("_localizationMap",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var originalMap = new Dictionary<string, string>(map);
            var originalPath = path.GetValue(null);
            var originalCulture = cultureCode.GetValue(null);

            try
            {
                path.SetValue(null, Application.streamingAssetsPath);
                cultureCode.SetValue(null, culture);
                var load = localization.GetMethod("TryParseAndLoadLanguage", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(load.Invoke(null, new object[] { culture }), Is.True);
                var bundled = map[text];
                Assert.That(bundled, Does.Contain("Use it at your own risk"));
                Assert.That(bundled, Does.Contain("official YARG Discord or GitHub issue tracker"));

                var remote = "{\"messages\":{\"dev\":{" +
                    $"\"{text}\":{{\"en-US\":\"Remote welcome\"}}," +
                    "\"Test.RemoteMessage\":{\"en-US\":\"Delivered\"}}}}";
                localization.GetMethod("UpdateLocalizations").Invoke(null, new object[] { remote });

                Assert.That(map[text], Is.EqualTo(bundled));
                Assert.That(map["Test.RemoteMessage"], Is.EqualTo("Delivered"));
            }
            finally
            {
                map.Clear();
                foreach (var pair in originalMap) map.Add(pair.Key, pair.Value);
                path.SetValue(null, originalPath);
                cultureCode.SetValue(null, originalCulture);
            }
        }
    }
}
