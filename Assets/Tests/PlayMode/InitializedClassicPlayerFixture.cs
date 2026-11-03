// pattern: Imperative Shell
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
    internal sealed class InitializedClassicPlayerFixture
    {
        private const BindingFlags FLAGS = PreparedElitePlayerFixture.INSTANCE;
        private readonly List<GameObject> _objects = new();
        private readonly List<object> _additionalSessions = new();
        private readonly List<Component> _additionalPlayers = new();
        private object _session;
        private EngineManager _engines;
        private int _highwayCount;
        internal double PlayerInitializationMilliseconds { get; private set; }
        internal Component Player { get; private set; }
        internal Component Manager { get; private set; }
        internal SongChart Chart { get; private set; }
        internal object Session => _session;
        internal Component AdditionalPlayer { get; private set; }
        internal EngineManager EngineManager => _engines;
        internal bool NoFailEnabled { get; private set; }
        internal IReadOnlyList<GameObject> Objects => _objects;
        private static Type Type(string name) => PreparedElitePlayerFixture.ProductionType(name);
        private GameObject Own(GameObject value) { _objects.Add(value); return value; }

        internal IEnumerator Initialize(SongChart chart, YargProfile profile)
        {
            _highwayCount = (int) Type("YARG.Gameplay.Player.TrackPlayer").GetField("HighwayCount").GetValue(null);
            Chart = chart;
            var bindings = Activator.CreateInstance(Type("YARG.Input.ProfileBindings"), profile);
            _session = Activator.CreateInstance(Type("YARG.Player.YargPlayer"), profile, bindings);
            _session.GetType().GetMethod("RefreshPresets").Invoke(_session, null);
            _engines = new EngineManager();
            var managerObject = Own(new GameObject("Classic fixture runtime"));
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
            var prefabPath = profile.CurrentInstrument == Instrument.FiveLaneDrums
                ? "Assets/Prefabs/Gameplay/Visual/FiveLaneDrumsVisual.prefab"
                : "Assets/Prefabs/Gameplay/Visual/FourLaneDrumsVisual.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            bool playerWasActive = prefab.activeSelf;
            GameObject playerObject;
            prefab.SetActive(false);
            try { playerObject = Own(Object.Instantiate(prefab)); }
            finally { prefab.SetActive(playerWasActive); }
            playerObject.SetActive(false);
            var player = playerObject.GetComponent(Type("YARG.Gameplay.Player.DrumsPlayer"));
            foreach (var behaviour in playerObject.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != player && behaviour.GetType().Name != "FretArray" &&
                    behaviour.GetType().Name != "KickFretFlash") behaviour.enabled = false;
            Player = player;
            chart.SyncTrack.GenerateBeatlines(10d, true);
            playerObject.SetActive(true);
            var initializationClock = System.Diagnostics.Stopwatch.StartNew();
            player.GetType().GetMethod("Initialize", FLAGS, null, new[]
            {
                typeof(int), Type("YARG.Player.YargPlayer"), typeof(SongChart),
                Type("YARG.Gameplay.HUD.TrackView"), typeof(StemMixer), typeof(int?)
            }, null).Invoke(player, new object[] { 0, _session, chart, view, null, null });
            PlayerInitializationMilliseconds = initializationClock.Elapsed.TotalMilliseconds;
            yield return null;
        }

        internal IEnumerator AddPlayer(YargProfile profile)
        {
            var chart = Chart;
            var bindings = Activator.CreateInstance(Type("YARG.Input.ProfileBindings"), profile);
            var session = Activator.CreateInstance(Type("YARG.Player.YargPlayer"), profile, bindings);
            session.GetType().GetMethod("RefreshPresets").Invoke(session, null);
            _additionalSessions.Add(session);
            ((IList)Manager.GetType().GetProperty("YargPlayers").GetValue(Manager)).Add(session);
            var viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gameplay/HUD/TrackView.prefab");
            bool viewActive = viewPrefab.activeSelf;
            Component view;
            viewPrefab.SetActive(false);
            try { view = Own(Object.Instantiate(viewPrefab)).GetComponentInChildren(Type("YARG.Gameplay.HUD.TrackView"), true); }
            finally { viewPrefab.SetActive(viewActive); }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(profile.CurrentInstrument == Instrument.FiveLaneDrums
                ? "Assets/Prefabs/Gameplay/Visual/FiveLaneDrumsVisual.prefab"
                : "Assets/Prefabs/Gameplay/Visual/FourLaneDrumsVisual.prefab");
            bool active = prefab.activeSelf;
            GameObject instance;
            prefab.SetActive(false);
            try { instance = Own(Object.Instantiate(prefab)); }
            finally { prefab.SetActive(active); }
            instance.SetActive(false);
            var player = instance.GetComponent(Type("YARG.Gameplay.Player.DrumsPlayer"));
            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != player && behaviour.GetType().Name != "FretArray" &&
                    behaviour.GetType().Name != "KickFretFlash") behaviour.enabled = false;
            _additionalPlayers.Add(player);
            AdditionalPlayer = player;
            chart.SyncTrack.GenerateBeatlines(10d, true);
            instance.SetActive(true);
            player.GetType().GetMethod("Initialize", FLAGS, null, new[]
            {
                typeof(int), Type("YARG.Player.YargPlayer"), typeof(SongChart),
                Type("YARG.Gameplay.HUD.TrackView"), typeof(StemMixer), typeof(int?)
            }, null).Invoke(player, new object[] { _additionalPlayers.Count, session, chart, view, null, null });
            yield return null;
        }

        internal IEnumerator Dispose()
        {
            foreach (var player in _additionalPlayers.ToArray())
                Unregister(player);
            _additionalPlayers.Clear();
            Unregister(Player);
            _engines?.Reset();
            foreach (var item in _objects)
                if (item != null) { item.SetActive(false); Object.Destroy(item); }
            _objects.Clear();
            yield return null;
            foreach (var session in _additionalSessions) (session as IDisposable)?.Dispose();
            _additionalSessions.Clear();
            (_session as IDisposable)?.Dispose();
            _session = null;
            Player = null;
            AdditionalPlayer = null;
            Type("YARG.Gameplay.Player.TrackPlayer").GetField("HighwayCount").SetValue(null, _highwayCount);
        }

        private void Unregister(Component player)
        {
            if (player != null)
            {
                var field = Type("YARG.Gameplay.Player.BasePlayer").GetField("EngineContainer", FLAGS);
                var container = field.GetValue(player);
                if (container != null)
                {
                    foreach (var method in _engines.GetType().GetMethods())
                        if (method.Name == "Unregister" && method.GetParameters().Length == 1)
                        { method.Invoke(_engines, new[] { container }); break; }
                    field.SetValue(player, null);
                }
            }
        }
    }
}
