// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YARG.Core.Chart;

namespace YARG.Tests.EditMode
{
    public sealed class CymbalAnimationSetupTests
    {
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static Type Spin => Production("YARG.Gameplay.Visuals.CymbalSpinAnimation");
        private const string THEME = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";

        [Test]
        public void CurrentThemeHasAccentSpinButNoObsoleteRockingComponents()
        {
            var theme = AssetDatabase.LoadAssetAtPath<GameObject>(THEME);
            var scripts = theme.GetComponentsInChildren<MonoBehaviour>(true);
            Assert.That(scripts.All(s => s != null), Is.True, "Removing rocking must not leave missing scripts.");
            Assert.That(scripts.Count(s => s.GetType().Name == "HiHatBeatAnimation"), Is.Zero);
            Assert.That(theme.GetComponentsInChildren(Spin, true).Length, Is.EqualTo(3));
        }

        [Test]
        public void ThemeSetupBindsNormalStarAndSplitModelsAndPoolClonesRemapTheirOwner()
        {
            var root = new GameObject("inactive note template");
            root.SetActive(false);
            GameObject clone = null;
            try
            {
                var noteType = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
                var note = root.AddComponent(noteType);
                var theme = AssetDatabase.LoadAssetAtPath<GameObject>(THEME)
                    .GetComponent(Production("YARG.Themes.ThemeComponent"));
                var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
                object Models(bool star) => theme.GetType().GetMethod("GetNoteModelsForVisualStyle")
                    .Invoke(theme, new[] { style, (object)star });
                noteType.GetMethod("SetThemeModels").Invoke(note, new[] { Models(false), Models(true) });
                void Check(GameObject model)
                {
                    var owner = model.GetComponent(noteType);
                    var groupType = Production("YARG.Gameplay.Visuals.NoteGroup");
                    var groups = model.GetComponentsInChildren(groupType, true);
                    Assert.That(groups.Length, Is.GreaterThan(0));
                    foreach (var group in groups)
                        Assert.That(group.gameObject.activeSelf, Is.False,
                            "Unused model branches must stay inactive when a pooled note first wakes.");
                    var spins = model.GetComponentsInChildren(Spin, true);
                    Assert.That(spins.Length, Is.GreaterThanOrEqualTo(15), "Normal, SP and both split halves need binding.");
                    foreach (var spin in spins)
                    {
                        var binding = new SerializedObject(spin).FindProperty("_noteElement");
                        Assert.That(binding, Is.Not.Null, "Owner must survive Unity prefab/pool cloning.");
                        Assert.That(binding.objectReferenceValue, Is.SameAs(owner));
                    }
                }
                Check(root);
                clone = UnityEngine.Object.Instantiate(root);
                Check(clone);
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SharedClockHandlesTempoSeekPausePeriodLeftyAndSongReplacement()
        {
            var clockType = Type.GetType("YARG.Gameplay.Visuals.CymbalSpinClock, Assembly-CSharp");
            Assert.That(clockType, Is.Not.Null);
            var clock = Activator.CreateInstance(clockType);
            var sync = new SyncTrack(480u,
                new List<TempoChange> { new(60, 0, 0), new(120, 2, 960) },
                new List<TimeSignatureChange>(), new List<Beatline>());
            Quaternion At(double time, float period = 4f, bool lefty = false) =>
                (Quaternion)clockType.GetMethod("Evaluate").Invoke(clock, new object[] { sync, time, period, lefty });
            void Check(double time, float angle) => Assert.That(Quaternion.Angle(At(time),
                Quaternion.AngleAxis(angle, Vector3.forward)), Is.LessThan(.001f));
            Check(1d, -90f);
            Check(2.25d, -225f);
            Check(2.25d, -225f); // Pause, no integrated delta-time drift.
            Check(-.5d, -315f); // Rewind into preroll.
            Assert.That(Quaternion.Angle(At(1, 8), Quaternion.AngleAxis(-45, Vector3.forward)), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(At(1, 4, true), Quaternion.AngleAxis(90, Vector3.forward)), Is.LessThan(.001f));
            sync = new SyncTrack(480u, new List<TempoChange> { new(120, 0, 0) },
                new List<TimeSignatureChange>(), new List<Beatline>());
            Check(1d, -180f); // Same timestamp, different chart.
        }
    }
}