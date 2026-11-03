// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteCymbalPlayerInputPlayModeTests : EliteFlamRuntimeTestBase
    {
        private Gamepad _device;

        [UnityTest, Timeout(90000)]
        public IEnumerator PhysicalAliasesRecordOneInputAndExpandedNotesRequireTheirActualDestination() => VerifyPhysicalInputs(false);

        [UnityTest, Timeout(90000)]
        public IEnumerator AllOrderedPairsDeduplicatePrimaryAndSecondaryAliasesAndJudgeTheExpandedDestination() => VerifyPhysicalInputs(true);

        private IEnumerator VerifyPhysicalInputs(bool ordered)
        {
            foreach (int pair in Enumerable.Range(0, ordered ? 6 : 3))
            {
                var definition = ordered ? EliteCymbalMergePreviewPlayModeTests.OrderedPairs[pair]
                    : (Primary: pair == 0 ? "ride" : "right-crash",
                        Secondary: pair == 2 ? "ride" : "left-crash",
                        Destination: pair == 2 ? "left-crash" : "hi-hat", Sample: pair);
                var displaced = EliteCymbalMergePreviewPlayModeTests.SourcePad(definition.Secondary);
                var keeper = EliteCymbalMergePreviewPlayModeTests.SourcePad(definition.Primary);
                var destinationAction = definition.Destination == "hi-hat" ? EliteDrumsAction.EliteClosedHiHat
                    : Action(EliteCymbalMergePreviewPlayModeTests.SourcePad(definition.Destination));
                var single = Note(displaced, 1);
                var primarySingle = Note(keeper, 2);
                var chord = Note(keeper, 3);
                chord.AddChildNote(Note(displaced, 3));
                var profile = Profile();
                profile.AutoHiHatPedal = false;
                profile.LeftyFlip = pair % 2 == 1;
                Fixture = new InitializedElitePlayerFixture();
                var layout = ordered
                    ? EliteCymbalMergePreviewPlayModeTests.OrderedLayout(definition.Primary, definition.Secondary)
                    : EliteCymbalMergePreviewPlayModeTests.MergedLayout(pair);
                yield return Fixture.Initialize(PreparedElitePlayerFixture.CreateChart(single, primarySingle, chord), profile,
                    layout, noFail: true);
                _device = InputSystem.AddDevice<Gamepad>();
                var bindings = Fixture.Session.GetType().GetProperty("Bindings").GetValue(Fixture.Session);
                var collection = bindings.GetType().GetProperty("Item").GetValue(bindings,
                    new object[] { profile.GameMode });
                var getBinding = collection.GetType().GetMethod("TryGetBindingByAction", new[] { typeof(int) });
                foreach (var action in new[] { Action(displaced), Action(keeper), destinationAction })
                {
                    var binding = getBinding.Invoke(collection, new object[] { (int)action });
                    binding.GetType().GetProperty("DebounceThreshold").SetValue(binding, 0L);
                    binding.GetType().GetMethod("AddControl", new[] { typeof(InputControl) }).Invoke(binding,
                        new object[] { action == destinationAction ? _device.rightTrigger : _device.leftTrigger });
                    foreach (var control in (IEnumerable)binding.GetType().GetProperty("Bindings").GetValue(binding))
                        control.GetType().GetProperty("DebounceThreshold").SetValue(control, 0L);
                }
                collection.GetType().GetMethod("EnableInputs").Invoke(collection, null);
                // Use the public calibration setting to align the real device clock with
                // this tiny chart; all subscription/recording/judgment code stays real.
                double start = InputState.currentTime;
                profile.InputCalibrationSeconds = start - 1d;
                Send(.8f, 0);
                Assert.That(PositiveReplayInputs(), Has.Length.EqualTo(1));
                Assert.That(Playable.Notes[0].WasHit, Is.True);
                Assert.That(Convert.ToInt32(Stat(Engine(Fixture.Player), "NotesHit")), Is.EqualTo(1));
                Send(0, 0);
                while (InputState.currentTime - start < 1d) yield return null;
                Send(.8f, 0);
                Assert.That(PositiveReplayInputs(), Has.Length.EqualTo(2));
                Assert.That(Playable.Notes[1].WasHit, Is.True, "Both source singles share the primary target.");
                Assert.That(Convert.ToInt32(Stat(Engine(Fixture.Player), "NotesHit")), Is.EqualTo(2));
                Send(0, 0);
                while (InputState.currentTime - start < 2d) yield return null;
                Send(.8f, 0);
                Assert.That(PositiveReplayInputs(), Has.Length.EqualTo(3));
                Assert.That(Playable.Notes[2].ChildNotes.Prepend(Playable.Notes[2]).Count(note => note.WasHit), Is.EqualTo(1),
                    "A merged-target strike cannot also judge the expanded destination.");
                Send(.8f, .8f);
                Assert.That(PositiveReplayInputs(), Has.Length.EqualTo(4));
                Assert.That(Playable.Notes[2].ChildNotes.Prepend(Playable.Notes[2]).All(note => note.WasHit), Is.True);
                Assert.That(Convert.ToInt32(Stat(Engine(Fixture.Player), "NotesHit")), Is.EqualTo(4));
                Assert.That(Convert.ToInt32(Stat(Engine(Fixture.Player), "Overhits")), Is.Zero);
                yield return Fixture.Dispose();
                Fixture = null;
                InputSystem.RemoveDevice(_device);
                _device = null;
            }
        }

        private GameInput[] PositiveReplayInputs() => ((IEnumerable<GameInput>)Fixture.Player.GetType()
            .GetProperty("ReplayInputs").GetValue(Fixture.Player)).Where(input => input.Axis > 0).ToArray();

        private void Send(float shared, float destination)
        {
            double time = InputState.currentTime;
            InputSystem.QueueStateEvent(_device, new GamepadState
                { leftTrigger = shared, rightTrigger = destination }, time);
            typeof(InputSystem).GetMethod("Update", System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(InputUpdateType) }, null)
                .Invoke(null, new object[] { InputUpdateType.Dynamic });
            Production("YARG.Gameplay.Player.BasePlayer").GetMethod("UpdateInputs", INSTANCE)
                .Invoke(Fixture.Player, new object[] { time });
        }

        private static EliteDrumsAction Action(EliteDrumPad pad) => pad switch
        {
            EliteDrumPad.LeftCrash => EliteDrumsAction.EliteLeftCrash,
            EliteDrumPad.Ride => EliteDrumsAction.EliteRide,
            _ => EliteDrumsAction.EliteRightCrash
        };

        [TearDown]
        public void RemoveVirtualDevice()
        {
            if (_device != null) InputSystem.RemoveDevice(_device);
            _device = null;
        }
    }
}
