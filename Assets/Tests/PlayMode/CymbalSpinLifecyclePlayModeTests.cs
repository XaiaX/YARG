// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Game;

namespace YARG.Tests.PlayMode
{
    public sealed class CymbalSpinLifecyclePlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static FieldInfo Field(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, INSTANCE | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(name);
        }
        private static EliteDrumNote Hat(double time) => new(
            (int)EliteDrumNote.EliteDrumPad.HiHat, DrumNoteType.Accent,
            EliteDrumNote.EliteDrumsHatState.Open, EliteDrumNote.EliteDrumsHatPedalType.Stomp,
            false, DrumNoteFlags.None, NoteFlags.None, EliteDrumNote.EliteDrumsChannelFlag.None,
            time, (uint)(time * 480), false);

        [UnityTest] public IEnumerator ClosedAccentRetainsRotationAcrossPoolReuse() => Exercise("ClosedHiHatAccent");
        [UnityTest] public IEnumerator OpenAccentRetainsRotationAcrossPoolReuse() => Exercise("OpenHiHatAccent");
        [UnityTest] public IEnumerator IndifferentAndRegularAccentRetainRotationAcrossPoolReuse() => Exercise("CymbalAccent");

        private IEnumerator Exercise(string modelName)
        {
            var managerObject = new GameObject("Explicit animation clock"); managerObject.SetActive(false);
            var owner = new GameObject("Note owner"); owner.SetActive(false);
            var playerObject = new GameObject("Inactive profile holder"); playerObject.SetActive(false);
            GameObject clone = null;
            try
            {
                var sync = new SyncTrack(480u,
                    new List<TempoChange> { new(60, 0, 0), new(120, 2, 960) },
                    new List<TimeSignatureChange> { new(4, 4, 0, 0, 0, 0, 0, 0) },
                    new List<Beatline> { new(BeatlineType.Measure, 0, 0), new(BeatlineType.Strong, 1, 480),
                        new(BeatlineType.Strong, 2, 960), new(BeatlineType.Measure, 4, 2880) });
                var chart = new SongChart(sync.Resolution) { SyncTrack = sync };
                var handler = Activator.CreateInstance(Production("YARG.Playback.BeatEventHandler"), sync);
                var managerType = Production("YARG.Gameplay.GameManager");
                var manager = (Behaviour)managerObject.AddComponent(managerType);
                managerType.GetMethod("InitializeRuntime").Invoke(manager,
                    new object[] { null, null, chart, Array.CreateInstance(Production("YARG.Player.YargPlayer"), 0),
                        new EngineManager(), handler, null, false, true });
                var runnerType = Production("YARG.Playback.SongRunner");
                var runner = FormatterServices.GetUninitializedObject(runnerType);
                Field(runnerType, "<SongSpeed>k__BackingField").SetValue(runner, 1f);
                Field(managerType, "_songRunner").SetValue(manager, runner);
                manager.enabled = false; managerObject.SetActive(true);
                void Clock(double now) => Field(runnerType, "<VisualTime>k__BackingField").SetValue(runner, now);

                var elementType = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
                var element = (Behaviour)owner.AddComponent(elementType);
                var groupType = Production("YARG.Gameplay.Visuals.NoteGroup");
                Field(elementType, "NoteGroups").SetValue(element, Array.CreateInstance(groupType, 0));
                Field(elementType, "StarPowerNoteGroups").SetValue(element, Array.CreateInstance(groupType, 0));
                elementType.GetProperty("NoteRef").SetValue(element, Hat(6));
                var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/" + modelName + ".prefab"), owner.transform);
                var spinType = Production("YARG.Gameplay.Visuals.CymbalSpinAnimation");
                var spin = model.GetComponentInChildren(spinType, true);
                spinType.GetMethod("BindOwner", INSTANCE).Invoke(spin, new object[] { element });
                var rotor = (Transform)spinType.GetProperty("Rotor").GetValue(spin);
                var stationary = model.GetComponentsInChildren<Transform>(true).Where(t => !t.IsChildOf(rotor))
                    .Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToArray();
                owner.SetActive(true); element.enabled = false; model.SetActive(true);
                var player = playerObject.AddComponent(Production("YARG.Gameplay.Player.EliteDrumsPlayer"));
                var profile = new YargProfile();
                var yargPlayer = Activator.CreateInstance(Production("YARG.Player.YargPlayer"), new object[] { profile, null });
                Production("YARG.Gameplay.Player.BasePlayer").GetProperty("Player").SetValue(player, yargPlayer);
                Field(elementType, "<Player>k__BackingField").SetValue(element, player);
                void Angle(Transform target, float degrees) => Assert.That(Quaternion.Angle(target.localRotation,
                    Quaternion.AngleAxis(degrees, Vector3.forward)), Is.LessThan(.05f));

                Clock(1); yield return null; yield return null; Angle(rotor, -90);
                yield return null; Angle(rotor, -90); // Pause.
                Clock(2.25); yield return null; yield return null; Angle(rotor, -225);
                Clock(-.5); yield return null; yield return null; Angle(rotor, -315);
                profile.LeftyFlip = true;
                Clock(1); yield return null; yield return null; Angle(rotor, 90);

                clone = UnityEngine.Object.Instantiate(owner); // Copy while actively rotated.
                var cloneElement = (Behaviour)clone.GetComponent(elementType); cloneElement.enabled = false;
                var cloneSpin = clone.GetComponentInChildren(spinType, true);
                var cloneRotor = (Transform)spinType.GetProperty("Rotor").GetValue(cloneSpin);
                Assert.That(new SerializedObject(cloneSpin).FindProperty("_noteElement").objectReferenceValue, Is.SameAs(cloneElement));
                Field(elementType, "<Player>k__BackingField").SetValue(cloneElement, player);
                elementType.GetProperty("NoteRef").SetValue(cloneElement, Hat(8));
                yield return null; yield return null; Angle(cloneRotor, 90);

                owner.SetActive(false); Angle(rotor, 0);
                elementType.GetProperty("NoteRef").SetValue(element, Hat(9));
                owner.SetActive(true);
                yield return null; yield return null; Angle(rotor, 90);
                elementType.GetProperty("NoteRef").SetValue(element, null);
                yield return null; yield return null; Angle(rotor, 0);
                foreach (var (t, position, rotation, scale) in stationary)
                {
                    Assert.That(Vector3.Distance(t.localPosition, position), Is.LessThan(1e-7f), t.name + " position");
                    Assert.That(Quaternion.Angle(t.localRotation, rotation), Is.LessThan(.001f), t.name + " rotation");
                    Assert.That(Vector3.Distance(t.localScale, scale), Is.LessThan(1e-7f), t.name + " scale");
                }
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(playerObject);
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
        }

        [UnityTest]
        public IEnumerator UnboundThemePreviewsStayStillAndContainNoRocking()
        {
            var root = new GameObject("Unbound cymbal previews");
            foreach (string family in new[] { "ClosedHiHat", "OpenHiHat", "Cymbal" })
            foreach (string variant in new[] { "", "Accent", "Ghost" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/" + family + variant + ".prefab");
                Assert.That(prefab, Is.Not.Null);
                UnityEngine.Object.Instantiate(prefab, root.transform);
            }
            try
            {
                var poses = root.GetComponentsInChildren<Transform>(true)
                    .Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToArray();
                Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true)
                    .Any(c => c == null || c.GetType().Name == "HiHatBeatAnimation"), Is.False);
                yield return null; yield return null;
                foreach (var (t, position, rotation, scale) in poses)
                {
                    Assert.That(Vector3.Distance(t.localPosition, position), Is.LessThan(1e-7f), t.name + " position");
                    Assert.That(Quaternion.Angle(t.localRotation, rotation), Is.LessThan(.001f), t.name + " rotation");
                    Assert.That(Vector3.Distance(t.localScale, scale), Is.LessThan(1e-7f), t.name + " scale");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}