using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.IO;
using YARG.Core.Song;

namespace YARG.Tests.PlayMode
{
    public sealed partial class DifficultySelectDonePlayModeTests
    {
        [UnityTest]
        public IEnumerator DifficultyDoneProductionPrefabHarness() => ExerciseProductionInputs(new Vector2Int(1280, 720), false);

        [UnityTest]
        public IEnumerator DifficultyDoneProductionPrefabHarness1080p() => ExerciseProductionInputs(new Vector2Int(1920, 1080), false);

        [UnityTest]
        public IEnumerator DifficultyDoneVisibleAndReachable() => ExerciseProductionInputs(new Vector2Int(1280, 720), true);

        [UnityTest]
        public IEnumerator DifficultyDoneVisibleAndReachable1080p() => ExerciseProductionInputs(new Vector2Int(1920, 1080), true);

        [UnityTest]
        public IEnumerator DifficultyDoneReturnsWithoutLosingOptions() => ExerciseProductionInputs(new Vector2Int(1280, 720), false);

        [UnityTest]
        public IEnumerator DifficultyDoneReturnsWithoutLosingOptions1080p() => ExerciseProductionInputs(new Vector2Int(1920, 1080), false);

        private IEnumerator ExerciseProductionInputs(Vector2Int resolution, bool pointerDone)
        {
#if UNITY_EDITOR
            Bootstrap(resolution);
            var eventsObject = Own(new GameObject("Difficulty Done private EventSystem"));
            // An inactive EventSystem can service explicit RaycastAll calls without replacing
            // the runner's EventSystem.current or polling physical devices.
            eventsObject.SetActive(false);
            var events = eventsObject.AddComponent<EventSystem>();
            var navigatorObject = Own(new GameObject("Difficulty Done private Navigator"));
            navigatorObject.SetActive(false);
            var navigator = navigatorObject.AddComponent(Production("YARG.Menu.Navigation.Navigator"));
            InstallDoneInputScheme(navigator);

            var groupType = _navigation.GetType();
            var groupStack = (IList)groupType.GetField("_navGroupsStack", STATIC).GetValue(null);
            var previousGroups = groupStack.Cast<object>().ToArray();
            groupStack.Clear();
            groupStack.Add(_navigation);
            // PersistentState is a value type. Replace the whole value temporarily, never
            // mutate its shared show list or any song belonging to the runner.
            var stateField = Production("YARG.GlobalVariables").GetField("State", STATIC);
            var previousState = stateField.GetValue(null);
            var fixtureState = stateField.GetValue(null);
            var song = new DoneInputSong();
            stateField.FieldType.GetField("CurrentSong").SetValue(fixtureState, song);
            stateField.SetValue(null, fixtureState);
            try
            {
                Set(_menu, "_songList", new List<SongEntry> { song });
                foreach (var mode in new[] { GameMode.FiveFretGuitar, GameMode.EliteDrums, GameMode.FourLaneDrums })
                {
                    InitializeDoneInputPlayer(mode);
                    yield return Settle();
                    NavigateToCaption(navigator, Header("Adjustments"));
                    DispatchDoneInput(navigator, "Green");
                    yield return Settle();
                    AssertState("Adjustments");
                    foreach (var state in new[] { "Modifiers", "Accessibility" })
                    {
                        NavigateToCaption(navigator, Header(state));
                        DispatchDoneInput(navigator, "Green");
                        yield return Settle();
                        AssertState(state);
                        var initialCaptions = Rows().Select(Caption).ToArray();
                        Assert.That(initialCaptions.Length, Is.GreaterThan(1));
                        Call(_navigation, "SelectFirst", EnumValue("YARG.Menu.Navigation.SelectionOrigin", "Navigation"));
                        DispatchDoneInput(navigator, "Green");
                        yield return Settle();
                        var expectedModifiers = _profile.CurrentModifiers;
                        var expectedLefty = _profile.LeftyFlip;
                        if (state == "Modifiers")
                            Assert.That(expectedModifiers, Is.Not.EqualTo(Modifier.None));
                        else
                            Assert.That(expectedLefty, Is.True);

                        // Exercise Elite Drums-specific profile flags as well as its common lefty row.
                        if (mode == GameMode.EliteDrums && state == "Accessibility")
                        {
                            NavigateToCaption(navigator, Header("AutoHiHatPedal"));
                            DispatchDoneInput(navigator, "Green");
                            NavigateToCaption(navigator, Header("NoHiHatPedal"));
                            DispatchDoneInput(navigator, "Green");
                            yield return Settle();
                            Assert.That(_profile.AutoHiHatPedal, Is.True);
                            Assert.That(_profile.NoHiHatPedal, Is.True);
                        }

                        yield return ReachAndUseProductionDone(navigator, events, resolution, mode, state, pointerDone);
                        AssertState("Adjustments");
                        NavigateToCaption(navigator, Header(state));
                        DispatchDoneInput(navigator, "Green");
                        yield return Settle();
                        AssertState(state);
                        Assert.That(_profile.CurrentModifiers, Is.EqualTo(expectedModifiers));
                        Assert.That(_profile.LeftyFlip, Is.EqualTo(expectedLefty));
                        Assert.That(Rows().Length, Is.EqualTo(initialCaptions.Length), "Returning must not lose options or duplicate Done.");
                        if (mode == GameMode.EliteDrums && state == "Accessibility")
                        {
                            Assert.That(_profile.AutoHiHatPedal, Is.True);
                            Assert.That(_profile.NoHiHatPedal, Is.True);
                        }
                        yield return ReachAndUseProductionDone(navigator, events, resolution, mode, state + "-reopened", !pointerDone);
                        AssertState("Adjustments");
                    }
                    var modifiers = _profile.CurrentModifiers;
                    yield return ReachAndUseProductionDone(navigator, events, resolution, mode, "Adjustments", pointerDone);
                    AssertState("Main");
                    Assert.That(_profile.CurrentModifiers, Is.EqualTo(modifiers));
                    Assert.That(_profile.LeftyFlip, Is.True);
                }
            }
            finally
            {
                stateField.SetValue(null, previousState);
                groupStack.Clear();
                foreach (var group in previousGroups) groupStack.Add(group);
            }
#else
            Assert.Ignore("Production-prefab fixture requires Unity Editor AssetDatabase.");
            yield break;
#endif
        }

        // Metadata only: no file, audio, chart loading, or song-library registration.
        private sealed class DoneInputSong : SongEntry
        {
            public DoneInputSong()
            {
                var facts = new[]
                {
                    new DrumSourceTierFacts(DrumSourceFormat.Elite, Difficulty.Expert, true, false, true, false, true),
                    new DrumSourceTierFacts(DrumSourceFormat.FourLane, Difficulty.Expert, true, false, true, false, true),
                };
                typeof(SongEntry).GetField("_authoredDrumSourceFacts", INSTANCE)
                    .SetValue(this, Array.AsReadOnly(facts));
                Assert.That(DrumOutputResolver.Resolve(AuthoredDrumSourceFacts, Instrument.EliteDrums,
                    Difficulty.Expert, false, false, false), Is.Not.Null, "Fixture must provide playable native Elite drums.");
            }

            public override EntryType SubType => EntryType.Ini;
            public override string SortBasedLocation => "difficulty-done-input-fixture";
            public override string ActualLocation => "difficulty-done-input-fixture";
            public override DateTime GetLastWriteTime() => DateTime.UnixEpoch;
            public override SongChart LoadChart(IReadOnlyCollection<Instrument> eliteDrumsDownchartOutputs = null) => throw new InvalidOperationException("No chart loading in the Done fixture.");
            public override StemMixer LoadAudio(float speed, double volume, bool enableCensoring, params SongStem[] ignoreStems) => throw new InvalidOperationException("No audio loading in the Done fixture.");
            public override StemMixer LoadPreviewAudio(float speed, bool enableCensoring) => throw new InvalidOperationException("No preview loading in the Done fixture.");
            public override YARGImage LoadAlbumData() => null;
            public override BackgroundResult LoadBackground(bool censoringEnabled, bool excludeYarground = false) => null;
            public override FixedArray<byte> LoadMiloData() => null;
            public override FixedArray<byte> LoadVocData() => null;
        }

        private void InitializeDoneInputPlayer(GameMode mode)
        {
            var instrument = mode switch
            {
                GameMode.EliteDrums => Instrument.EliteDrums,
                GameMode.FourLaneDrums => Instrument.FourLaneDrums,
                _ => Instrument.FiveFretGuitar,
            };
            _profile = new YargProfile
            {
                Name = "Difficulty Done input fixture",
                GameMode = mode,
                CurrentInstrument = instrument,
                PreferredInstrument = instrument,
                CurrentDifficulty = Difficulty.Expert,
                LeftyFlip = false,
                AutoHiHatPedal = false,
                NoHiHatPedal = false,
                IsBot = true,
            };
            var bindingsType = Production("YARG.Input.ProfileBindings");
            var bindings = (IDisposable)bindingsType.GetConstructor(new[] { typeof(YargProfile) })
                .Invoke(new object[] { _profile });
            var playerType = Production("YARG.Player.YargPlayer");
            object player;
            try
            {
                player = playerType.GetConstructor(new[] { typeof(YargProfile), bindingsType })
                    .Invoke(new object[] { _profile, bindings });
            }
            catch { bindings.Dispose(); throw; }
            _players.Add((IDisposable)player);
            var players = Array.CreateInstance(playerType, 1);
            players.SetValue(player, 0);
            Set(_menu, "_explicitPlayers", players);
            Set(_menu, "_menuState", Enum.Parse(Field(_menu, "_menuState").FieldType, "Main"));
            Set(_menu, "_lastMenuState", Enum.Parse(Field(_menu, "_lastMenuState").FieldType, "Main"));
            var instruments = (IList)Get(_menu, "_possibleInstruments");
            instruments.Clear();
            instruments.Add(instrument);
            var modifiers = (IList)Get(_menu, "_possibleModifiers");
            modifiers.Clear();
            // Valid non-MIDI-preference modifiers avoid song-dependent output recomputation.
            modifiers.Add(mode == GameMode.FiveFretGuitar ? Modifier.AllStrums : Modifier.NoDynamics);
            modifiers.Add(mode == GameMode.FiveFretGuitar ? Modifier.OpensToGreens : Modifier.NoKicks);
            Call(_menu, "UpdateForPlayer");
        }

        private static void InstallDoneInputScheme(Component navigator)
        {
            var schemeType = Production("YARG.Menu.Navigation.NavigationScheme");
            var entryType = schemeType.GetNestedType("Entry");
            var entries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType));
            foreach (string name in new[] { "NavigateUp", "NavigateDown", "NavigateSelect" })
                entries.Add(entryType.GetField(name, STATIC).GetValue(null));
            // null is ambiguous to Mono's binder between the Action and bool overloads.
            var scheme = schemeType.GetConstructor(new[] { entries.GetType(), typeof(bool?), typeof(Action) })
                .Invoke(new object[] { entries, (bool?)false, null });
            // Use the production entries and dispatch stack, but not PushScheme's global
            // dialog/help-bar lifecycle. This is not a full OnEnable integration test.
            var stack = Get(navigator, "_schemeStack");
            stack.GetType().GetMethod("Push", new[] { schemeType }).Invoke(stack, new[] { scheme });
        }

        private static void DispatchDoneInput(Component navigator, string action)
        {
            var contextType = Production("YARG.Menu.Navigation.NavigationContext");
            var actionType = Production("YARG.Core.Input.MenuAction");
            var context = contextType.GetConstructor(new[] { actionType, Production("YARG.Player.YargPlayer"), typeof(bool) })
                .Invoke(new object[] { Enum.Parse(actionType, action), null, true });
            navigator.GetType().GetMethod("InvokeNavigationEvent", INSTANCE, null, new[] { contextType }, null)
                .Invoke(navigator, new[] { context });
        }

        private void NavigateToCaption(Component navigator, string label)
        {
            var matches = Rows().Where(row => Caption(row).Contains(label)).ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), "Expected one navigation target: " + label);
            Call(_navigation, "SelectFirst", EnumValue("YARG.Menu.Navigation.SelectionOrigin", "Navigation"));
            int index = Array.IndexOf(Rows(), matches[0]);
            for (int i = 0; i < index; i++) DispatchDoneInput(navigator, "Down");
            Assert.That(Property(_navigation, "SelectedBehaviour"), Is.SameAs(matches[0]));
        }

        private IEnumerator ReachAndUseProductionDone(Component navigator, EventSystem events, Vector2Int resolution,
            GameMode mode, string state, bool pointer)
        {
            var done = AssertOneDone();
            NavigateToCaption(navigator, Header("Done"));
            yield return Settle();
            AssertVisible(done, mode + " " + state + " production geometry");
            DispatchDoneInput(navigator, "Up");
            DispatchDoneInput(navigator, "Down");
            yield return Settle();
            Assert.That(Property(_navigation, "SelectedBehaviour"), Is.SameAs(done));
            AssertVisible(done, state + " returned via Navigator");
            CaptureDoneProduction(resolution, mode + "-" + state + (pointer ? "-pointer" : "-navigation"));
            // No row/viewport resizing here: controlled overflow belongs to the separate stress fixture.
            if (pointer) ClickDoneProduction(events, done);
            else DispatchDoneInput(navigator, "Green");
            yield return Settle();
        }

        private void ClickDoneProduction(EventSystem events, Component done)
        {
            var canvas = _scroll.GetComponentInParent<Canvas>();
            var rect = (RectTransform)done.transform;
            var position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(events) { position = position, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            events.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty, "Done must receive an actual GraphicRaycaster hit.");
            var target = hits[0].gameObject;
            Assert.That(target == done.gameObject || target.transform.IsChildOf(done.transform), Is.True,
                "The top raycast hit must belong to Done, not an occluding row or graphic.");
            pointer.pointerCurrentRaycast = hits[0];
            Assert.That(ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler), Is.Not.Null);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerClickHandler);
        }

        private void CaptureDoneProduction(Vector2Int resolution, string context)
        {
            var camera = _scroll.GetComponentInParent<Canvas>().worldCamera;
            var target = camera.targetTexture;
            Assert.That(target.width, Is.EqualTo(resolution.x));
            Assert.That(target.height, Is.EqualTo(resolution.y));
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                string directory = Path.Combine(Application.temporaryCachePath, "DifficultyDoneProduction");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"{resolution.x}x{resolution.y}-{context}.png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log("[DifficultyDone] capture=" + path);
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(image);
            }
        }
    }
}
