using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;
using Object = UnityEngine.Object;

namespace YARG.Tests.PlayMode
{
    /// <summary>Shared production session and prefab construction; each owner controls its own lifetime.</summary>
    internal static class PreparedElitePlayerFixture
    {
        internal const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        internal static Type ProductionType(string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name))
                .FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, name);
            return type;
        }

        internal static SongChart CreateChart(params EliteDrumNote[] notes)
        {
            var chart = new SongChart(480);
            chart.SyncTrack.Tempos.Add(new TempoChange(120, 0, 0));
            chart.SyncTrack.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0, 0, 0, 0, 16, 4));
            var track = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Hard);
            track.Notes.AddRange(notes);
            var sources = new AuthoredDrumSourceCollection();
            var ctor = typeof(AuthoredDrumSourceTier).GetConstructor(INSTANCE, null, new[]
            {
                typeof(DrumSourceTierFacts), typeof(InstrumentDifficulty<DrumNote>),
                typeof(InstrumentDifficulty<EliteDrumNote>), typeof(IEnumerable<uint>)
            }, null);
            var tier = ctor.Invoke(new object[]
            {
                new DrumSourceTierFacts(DrumSourceFormat.Elite, Difficulty.Hard, true, true, true, false, true),
                null, track, Array.Empty<uint>()
            });
            typeof(AuthoredDrumSourceCollection).GetMethod("Add", INSTANCE).Invoke(sources, new[] { tier });
            typeof(SongChart).GetProperty(nameof(SongChart.AuthoredDrumSources), INSTANCE)
                .GetSetMethod(true).Invoke(chart, new object[] { sources });
            return chart;
        }

        internal static object PrepareSession(YargProfile profile, SongChart chart, Instrument output)
        {
            var bindings = Activator.CreateInstance(ProductionType("YARG.Input.ProfileBindings"), profile);
            var session = Activator.CreateInstance(ProductionType("YARG.Player.YargPlayer"), profile, bindings);
            session.GetType().GetMethod("RefreshPresets").Invoke(session, null);
            var facts = chart.AuthoredDrumSources.Tiers.Values.Select(s => s.Facts).ToArray();
            var resolved = DrumOutputResolver.Resolve(facts, output, profile.CurrentDifficulty,
                profile.IsModifierActive(Modifier.EnableEliteUpconversion),
                profile.IsModifierActive(Modifier.Enable2xKicks),
                profile.IsModifierActive(Modifier.PreferEliteDowncharts));
            Assert.That(resolved, Is.Not.Null);
            var prepared = DrumPlaybackPreparer.Prepare(chart, resolved,
                notes => profile.ApplyModifiers(notes, chart.SyncTrack),
                notes => profile.ApplyModifiers(notes, chart.SyncTrack));
            session.GetType().GetMethod("SetDrumPlayback").Invoke(session,
                new object[] { resolved, prepared.Classic, prepared.Elite });
            return session;
        }

        internal static GameObject ClonePlayer(Func<GameObject, GameObject> own, out Component player)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/FiveLaneDrumsVisual.prefab");
            Assert.That(prefab, Is.Not.Null);
            bool active = prefab.activeSelf;
            GameObject clone;
            prefab.SetActive(false);
            try { clone = own(Object.Instantiate(prefab)); }
            finally { prefab.SetActive(active); }
            clone.SetActive(false);
            var stock = clone.GetComponent(ProductionType("YARG.Gameplay.Player.DrumsPlayer"));
            player = clone.AddComponent(ProductionType("YARG.Gameplay.Player.EliteDrumsPlayer"));
            var copied = new HashSet<string>();
            for (var level = stock.GetType().BaseType; level != null && level != typeof(MonoBehaviour);
                level = level.BaseType)
            {
                foreach (var field in level.GetFields(INSTANCE))
                {
                    if (field.IsDefined(typeof(SerializeField), true) && copied.Add(field.Name))
                        field.SetValue(player, field.GetValue(stock));
                }
            }
            player.GetType().GetMethod("SetTrackCameraForNativeElite").Invoke(player,
                new[] { ProductionType("YARG.Gameplay.Player.TrackPlayer").GetProperty("TrackCamera").GetValue(stock) });
            player.GetType().GetMethod("SetDrumComponentsForNativeElite").Invoke(player, new[]
            {
                clone.GetComponentInChildren(ProductionType("YARG.Gameplay.Visuals.FretArray"), true),
                clone.GetComponentInChildren(ProductionType("YARG.Gameplay.Visuals.KickFretFlash"), true)
            });
            Object.DestroyImmediate(stock);
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != player && behaviour.GetType().Name != "FretArray" &&
                    behaviour.GetType().Name != "KickFretFlash") behaviour.enabled = false;
            return clone;
        }
    }
}
