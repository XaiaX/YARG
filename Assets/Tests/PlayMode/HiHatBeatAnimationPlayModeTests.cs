using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Engine;

// pattern: Imperative Shell
namespace YARG.Tests.PlayMode
{
    public sealed class HiHatBeatAnimationPlayModeTests
    {
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static readonly Vector3 UPPER = new(0f, 0.0022f, -0.0024f);

        private static GameObject OpenModel()
        {
            var theme = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab").GetComponent(Production("YARG.Themes.ThemeComponent"));
            var models = (IDictionary) theme.GetType().GetMethod("GetNoteModelsForVisualStyle").Invoke(theme,
                new[] { Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums"), (object) false });
            return (GameObject) models[Enum.Parse(Production("YARG.Themes.ThemeNoteType"), "OpenHiHat")];
        }

        private static Transform Top(GameObject model) => model.GetComponentInChildren(Production("YARG.Gameplay.Visuals.HiHatBeatAnimation"), true).transform;
        private static void Pose(Transform top, Vector3 expected) =>
            Assert.That(Vector3.Distance(top.localPosition, expected), Is.LessThan(0.0000001f));

        private static FieldInfo Field(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(name);
        }

        private static EliteDrumNote Hat(double time, uint tick) => new(
            (int) EliteDrumNote.EliteDrumPad.HiHat, DrumNoteType.Neutral,
            EliteDrumNote.EliteDrumsHatState.Open, EliteDrumNote.EliteDrumsHatPedalType.Stomp,
            false, DrumNoteFlags.None, NoteFlags.None, EliteDrumNote.EliteDrumsChannelFlag.None,
            time, tick, false);

        [UnityTest]
        public IEnumerator ArrivalAlignedSwayKeepsItsTipFixedAcrossClonesAndPoolReuse() => ExerciseMotion(false);

        [UnityTest]
        public IEnumerator ForwardBackRockDampsToRestAndSurvivesCloningAndReuse() => ExerciseMotion(true);

        private IEnumerator ExerciseMotion(bool rock)
        {
            var managerObject = new GameObject("Hi-hat explicit gameplay clock");
            managerObject.SetActive(false);
            var owner = new GameObject("Real native Elite note owner");
            owner.SetActive(false);
            GameObject second = null;
            try
            {
                var sync = new SyncTrack(480u,
                    new List<TempoChange> { new(60, 0, 0), new(120, 2, 960) },
                    new List<TimeSignatureChange> { new(4, 4, 0, 0, 0, 0, 0, 0) },
                    new List<Beatline> { new(BeatlineType.Measure, 0, 0), new(BeatlineType.Strong, 1, 480),
                        new(BeatlineType.Strong, 2, 960), new(BeatlineType.Measure, 4, 2880) });
                var chart = new SongChart(sync.Resolution) { SyncTrack = sync };
                var handlerType = Production("YARG.Playback.BeatEventHandler");
                var handler = Activator.CreateInstance(handlerType, sync);
                var managerType = Production("YARG.Gameplay.GameManager");
                var manager = (Behaviour) managerObject.AddComponent(managerType);
                var roster = Array.CreateInstance(Production("YARG.Player.YargPlayer"), 0);
                managerType.GetMethod("InitializeRuntime").Invoke(manager,
                    new object[] { null, null, chart, roster, new EngineManager(), handler, null, false, true });
                var runnerType = Production("YARG.Playback.SongRunner");
                var runner = FormatterServices.GetUninitializedObject(runnerType);
                Field(runnerType, "<SongSpeed>k__BackingField").SetValue(runner, 1f);
                Field(managerType, "_songRunner").SetValue(manager, runner);
                manager.enabled = false;
                managerObject.SetActive(true);
                void Clock(double now)
                {
                    Field(runnerType, "<VisualTime>k__BackingField").SetValue(runner, now);
                    handlerType.GetMethod("Update").Invoke(handler, new object[] { 0.1d, now });
                }

                var elementType = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
                var element = (Behaviour) owner.AddComponent(elementType);
                var groupType = Production("YARG.Gameplay.Visuals.NoteGroup");
                Field(elementType, "NoteGroups").SetValue(element, Array.CreateInstance(groupType, 0));
                Field(elementType, "StarPowerNoteGroups").SetValue(element, Array.CreateInstance(groupType, 0));
                elementType.GetProperty("NoteRef").SetValue(element, rock ? Hat(6d, 4800u) : Hat(1d, 480u));
                var group = (Component) groupType.GetMethod("CreateNoteGroupFromTheme").Invoke(null,
                    new object[] { owner.transform, OpenModel() });
                var top = Top(group.gameObject);
                var restRotation = top.localRotation;
                var animation = top.GetComponent(Production("YARG.Gameplay.Visuals.HiHatBeatAnimation"));
                var mode = animation.GetType().GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(mode, Is.Not.Null, "Keep lateral sway selectable alongside the new rocking mode.");
                mode.SetValue(animation, Enum.Parse(mode.FieldType, rock ? "ForwardBackRock" : "LateralSway"));
                Clock(0d);
                owner.SetActive(true);
                element.enabled = false;
                group.gameObject.SetActive(true);
                yield return null;
                if (!rock) Pose(top, UPPER);
                var baseTransform = group.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Base");
                var basePosition = baseTransform.localPosition;
                var baseRotation = baseTransform.localRotation;
                var baseScale = baseTransform.localScale;
                if (rock)
                {
                    var forwardPosition = new Vector3(0f, 0.00222244998f, -0.00112135429f);
                    var forwardRotation = new Quaternion(2.76856529e-08f, 0.773845971f, 0.633373916f, -3.38258843e-08f);
                    var backPosition = new Vector3(0f, 0.00220106752f, -0.00300104637f);
                    var backRotation = new Quaternion(3.03643048e-08f, 0.719343722f, 0.694654346f, -3.14435162e-08f);
                    void RockPose(Transform target, Vector3 position, Quaternion rotation)
                    {
                        Pose(target, position);
                        Assert.That(Quaternion.Angle(target.localRotation, rotation), Is.LessThan(0.05f));
                    }
                    RockPose(top, backPosition, backRotation); // Beat zero now peaks backward.
                    Clock(1d);
                    yield return null;
                    RockPose(top, forwardPosition, forwardRotation); // Next beat: forward.
                    second = UnityEngine.Object.Instantiate(owner); // Clone while rocking.
                    var secondElement = second.GetComponent(elementType);
                    elementType.GetProperty("NoteRef").SetValue(secondElement, Hat(6.25d, 5040u));
                    var secondGroup = (Component) second.GetComponentInChildren(groupType, true);
                    secondGroup.gameObject.SetActive(true);
                    yield return null;
                    RockPose(Top(secondGroup.gameObject), forwardPosition, forwardRotation);
                    Clock(5.125d); // Within the final two beats: reduced backward excursion.
                    yield return null;
                    Assert.That(Vector3.Distance(top.localPosition, UPPER),
                        Is.GreaterThan(0.0001f).And.LessThan(0.0012f));
                    Assert.That(Quaternion.Angle(top.localRotation, restRotation),
                        Is.GreaterThan(1f).And.LessThan(6.4f));
                    Clock(5.5d); // One beat remains; forward swing is still clearly visible.
                    yield return null;
                    Assert.That(Quaternion.Angle(top.localRotation, restRotation), Is.GreaterThan(5f));
                    Assert.That(Vector3.Distance(top.localPosition, UPPER), Is.GreaterThan(0.001f));
                    Clock(6d);
                    yield return null;
                    RockPose(top, UPPER, restRotation);
                    Clock(6.125d); // Remains stationary after crossing.
                    yield return null;
                    RockPose(top, UPPER, restRotation);
                    owner.SetActive(false);
                    RockPose(top, UPPER, restRotation);
                    elementType.GetProperty("NoteRef").SetValue(element, Hat(8d, 6720u));
                    Clock(6d); // A pooled future note returns to the full backward pose.
                    owner.SetActive(true);
                    yield return null;
                    RockPose(top, backPosition, backRotation);
                    var paused = top.localPosition;
                    var pausedRotation = top.localRotation;
                    yield return null;
                    RockPose(top, paused, pausedRotation);
                    Clock(-1d); // Signed count-in beat alternates forward too.
                    yield return null;
                    RockPose(top, forwardPosition, forwardRotation);
                }
                else
                {
                    Clock(0.25d);
                    yield return null;
                    Assert.That(Quaternion.Angle(top.localRotation, restRotation), Is.EqualTo(2f).Within(0.003f));
                    var component = top.GetComponent(Production("YARG.Gameplay.Visuals.HiHatBeatAnimation"));
                    var pivot = (Vector3) Field(component.GetType(), "_pivotLocal").GetValue(component);
                    var fixedTip = UPPER + restRotation * Vector3.Scale(top.localScale, pivot);
                    void Tip() => Assert.That(Vector3.Distance(
                        top.localPosition + top.localRotation * Vector3.Scale(top.localScale, pivot), fixedTip),
                        Is.LessThan(0.0000001f));
                    Tip();
                    var rightRotation = top.localRotation;
                    second = UnityEngine.Object.Instantiate(owner);
                    var secondElement = second.GetComponent(elementType);
                    elementType.GetProperty("NoteRef").SetValue(secondElement, Hat(1.125d, 540u));
                    second.GetComponent<Behaviour>().enabled = false;
                    var secondGroup = (Component) second.GetComponentInChildren(groupType, true);
                    secondGroup.gameObject.SetActive(true);
                    yield return null;
                    var secondTop = Top(secondGroup.gameObject);
                    Assert.That(Quaternion.Angle(secondTop.localRotation, restRotation),
                        Is.GreaterThan(1.74f).And.LessThan(2f),
                        "A clone uses its new note's phase and the authored rest rotation, not the live sway pose.");
                    Clock(1.125d); // This offbeat clone reaches the line centered.
                    yield return null;
                    Pose(secondTop, UPPER);
                    Assert.That(Quaternion.Angle(secondTop.localRotation, restRotation), Is.LessThan(0.001f));
                    Assert.That(Quaternion.Angle(top.localRotation, restRotation), Is.GreaterThan(1.74f),
                        "Notes at different subdivisions should have distinct phases.");
                    Clock(0.75d);
                    yield return null;
                    Assert.That(Quaternion.Angle(top.localRotation, rightRotation), Is.EqualTo(4f).Within(0.003f));
                    Tip();
                    var pausedPosition = top.localPosition;
                    var pausedRotation = top.localRotation;
                    yield return null;
                    Pose(top, pausedPosition);
                    Assert.That(Quaternion.Angle(top.localRotation, pausedRotation), Is.LessThan(0.001f));
                    owner.SetActive(false);
                    Pose(top, UPPER);
                    Assert.That(Quaternion.Angle(top.localRotation, restRotation), Is.LessThan(0.001f));
                    elementType.GetProperty("NoteRef").SetValue(element, Hat(2.3125d, 1260u));
                    Clock(2.4375d); // Tempo doubled: a quarter-cycle past this new note is 0.125 seconds.
                    owner.SetActive(true);
                    yield return null;
                    Assert.That(Quaternion.Angle(top.localRotation, rightRotation), Is.LessThan(0.001f));
                    Tip();
                    Clock(2.3125d); // Pool's new offbeat note reaches the line at its neutral pose.
                    yield return null;
                    Pose(top, UPPER);
                    Assert.That(Quaternion.Angle(top.localRotation, restRotation), Is.LessThan(0.001f));
                    Clock(-0.125d); // Relative phase is -2.75 beats: preroll follows the arrival offset.
                    yield return null;
                    Assert.That(Quaternion.Angle(top.localRotation, rightRotation), Is.LessThan(0.001f));
                    Tip();
                }
                Assert.That(baseTransform.localPosition, Is.EqualTo(basePosition));
                Assert.That(baseTransform.localRotation, Is.EqualTo(baseRotation));
                Assert.That(baseTransform.localScale, Is.EqualTo(baseScale));
            }
            finally
            {
                if (second != null) UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
        }

        [UnityTest]
        public IEnumerator OutsideGameplayTheTopAndItsModelRemainAtTheAuthoredRestPose()
        {
            var animationType = Type.GetType("YARG.Gameplay.Visuals.HiHatBeatAnimation, Assembly-CSharp");
            Assert.That(animationType, Is.Not.Null);
            var model = UnityEngine.Object.Instantiate(OpenModel());
            try
            {
                yield return null;
                Assert.That(model, Is.Not.Null);
                Assert.That(Top(model).GetComponent<MeshFilter>(), Is.Not.Null);
                Pose(Top(model), UPPER);
                var animation = (Behaviour) Top(model).GetComponent(animationType);
                animation.enabled = false;
                Pose(Top(model), UPPER);
                animation.enabled = true;
                yield return null;
                Pose(Top(model), UPPER);
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
        }
    }
}
