using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Game;
using Object = UnityEngine.Object;

namespace YARG.Tests.PlayMode
{
    /// <summary>Owns a real initialized native player and its minimal gameplay environment.</summary>
    internal sealed class InitializedElitePlayerFixture
    {
        private const BindingFlags FLAGS = PreparedElitePlayerFixture.INSTANCE;
        private readonly List<GameObject> _objects = new();
        private object _session;
        private EngineManager _engines;
        private int _highwayCount;
        internal Component Player { get; private set; }
        internal Component Manager { get; private set; }
        internal SongChart Chart { get; private set; }
        internal object Session => _session;
        internal EngineManager EngineManager => _engines;
        internal bool NoFailEnabled { get; private set; }
        internal IReadOnlyList<GameObject> Objects => _objects;
        private static Type Type(string name) => PreparedElitePlayerFixture.ProductionType(name);
        private GameObject Own(GameObject value) { _objects.Add(value); return value; }

        internal IEnumerator Initialize(SongChart chart, YargProfile profile, EliteDrumsLayoutPreset layout,
            bool legacyTheme = false, bool noFail = false)
        {
            _highwayCount = (int) Type("YARG.Gameplay.Player.TrackPlayer").GetField("HighwayCount").GetValue(null);
            Chart = chart;
            Assert.That(EliteDrumsLayoutValidator.Validate(layout).IsRuntimeSupported, Is.True,
                "The fixture must test its requested layout rather than the runtime fallback.");
            _session = PreparedElitePlayerFixture.PrepareSession(profile, chart, Instrument.EliteDrums);
            _session.GetType().GetProperty("EliteDrumsLayout").SetValue(_session, layout);
            profile.ReplayEliteDrumsLayout = layout.Copy();
            _engines = new EngineManager();
            var managerObject = Own(new GameObject("Elite fixture runtime"));
            managerObject.SetActive(false);
            Manager = managerObject.AddComponent(Type("YARG.Gameplay.GameManager"));
            var manager = Manager;
            var roster = (IList) Activator.CreateInstance(typeof(List<>).MakeGenericType(_session.GetType()));
            roster.Add(_session);
            manager.GetType().GetMethod("InitializeRuntime").Invoke(manager, new object[]
            {
                null, null, chart, roster, _engines,
                Activator.CreateInstance(Type("YARG.Playback.BeatEventHandler"), chart.SyncTrack),
                null, false, true
            });
            managerObject.SetActive(true);
            var themeManager = Own(new GameObject("ThemeManager")).AddComponent(Type("YARG.Themes.ThemeManager"));
            Own(new GameObject("Visuals"));
            var viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gameplay/HUD/TrackView.prefab");
            bool active = viewPrefab.activeSelf;
            viewPrefab.SetActive(false);
            Component view;
            try { view = Own(Object.Instantiate(viewPrefab)).GetComponentInChildren(Type("YARG.Gameplay.HUD.TrackView"), true); }
            finally { viewPrefab.SetActive(active); }
            yield return null;
            if (legacyTheme)
            {
                var legacy = Own(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab")));
                legacy.SetActive(false);
                var themeComponent = legacy.GetComponent(Type("YARG.Themes.ThemeComponent"));
                themeComponent.GetType().GetField("_eliteLaneNotes", FLAGS).SetValue(themeComponent, null);
                var presetType = Type("YARG.Themes.ThemePreset");
                var preset = Activator.CreateInstance(presetType, "Legacy five-lane fallback", false);
                var styles = (IList) presetType.GetField("SupportedStyles").GetValue(preset);
                styles.Add(Enum.Parse(Type("YARG.Themes.VisualStyle"), "FiveLaneDrums"));
                var container = Activator.CreateInstance(Type("YARG.Themes.ThemeContainer"), legacy, true, null);
                var containers = (IDictionary) themeManager.GetType().GetField("_themeContainers", FLAGS)
                    .GetValue(themeManager);
                containers.Add(preset, container);
                _session.GetType().GetProperty("ThemePreset").SetValue(_session, preset);
            }
            var playerObject = PreparedElitePlayerFixture.ClonePlayer(Own, out var player);
            Player = player;
            chart.SyncTrack.GenerateBeatlines(10d, true);
            playerObject.SetActive(true);
            player.GetType().GetMethod("Initialize", FLAGS, null, new[]
            {
                typeof(int), Type("YARG.Player.YargPlayer"), typeof(SongChart),
                Type("YARG.Gameplay.HUD.TrackView"), typeof(StemMixer), typeof(int?)
            }, null).Invoke(player, new object[] { 0, _session, chart, view, null, null });
            if (noFail)
            {
                _engines.InitializeHappiness(true);
                NoFailEnabled = true;
            }
            yield return null;
        }

        internal IEnumerator Dispose()
        {
            if (Player != null)
            {
                var field = Type("YARG.Gameplay.Player.BasePlayer").GetField("EngineContainer", FLAGS);
                var container = field.GetValue(Player);
                if (container != null)
                {
                    foreach (var method in _engines.GetType().GetMethods())
                        if (method.Name == "Unregister" && method.GetParameters().Length == 1)
                        { method.Invoke(_engines, new[] { container }); break; }
                    field.SetValue(Player, null);
                }
            }
            _engines?.Reset();
            foreach (var item in _objects)
                if (item != null) { item.SetActive(false); Object.Destroy(item); }
            _objects.Clear();
            yield return null;
            (_session as IDisposable)?.Dispose();
            _session = null;
            Player = null;
            Type("YARG.Gameplay.Player.TrackPlayer").GetField("HighwayCount").SetValue(null, _highwayCount);
        }
    }
}
