// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Input;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteCymbalPhysicalInputPlayModeTests
    {
        private const BindingFlags PUBLIC = BindingFlags.Instance | BindingFlags.Public;
        private Gamepad _device;
        private object _bindings;
        private Type _type;
        private readonly List<GameInput> _received = new();
        private Delegate _callback;
        private int _primaryAction = (int)EliteDrumsAction.EliteRide;
        private int _secondaryAction = (int)EliteDrumsAction.EliteLeftCrash;

        [SetUp]
        public void SetUp()
        {
            _primaryAction = (int)EliteDrumsAction.EliteRide;
            _secondaryAction = (int)EliteDrumsAction.EliteLeftCrash;
            _device = InputSystem.AddDevice<Gamepad>();
            _type = PreparedElitePlayerFixture.ProductionType("YARG.Input.ProfileBindings");
            _bindings = Activator.CreateInstance(_type, new YargProfile { GameMode = GameMode.EliteDrums });
            var collection = _type.GetProperty("Item").GetValue(_bindings, new object[] { GameMode.EliteDrums });
            var getBinding = collection.GetType().GetMethod("TryGetBindingByAction", new[] { typeof(int) });
            foreach (int action in new[] { (int)EliteDrumsAction.EliteLeftCrash, (int)EliteDrumsAction.EliteRide })
            {
                var binding = getBinding.Invoke(collection, new object[] { action });
                binding.GetType().GetProperty("DebounceThreshold").SetValue(binding, 0L);
                binding.GetType().GetMethod("AddControl", new[] { typeof(InputControl) })
                    .Invoke(binding, new object[] { _device.leftTrigger });
                foreach (var single in (IEnumerable)binding.GetType().GetProperty("Bindings").GetValue(binding))
                    single.GetType().GetProperty("DebounceThreshold").SetValue(single, 0L);
            }
            collection.GetType().GetMethod("EnableInputs").Invoke(collection, null);
        }

        [TearDown]
        public void TearDown()
        {
            (_bindings as IDisposable)?.Dispose();
            if (_device != null) InputSystem.RemoveDevice(_device);
            _received.Clear();
        }

        private void Receive(ref GameInput input)
        {
            if (input.Axis > 0) _received.Add(input);
        }

        private void Subscribe(bool merged)
        {
            var method = _type.GetMethod("SubscribeToGameplayInputsWithPhysicalAliases", PUBLIC);
            Assert.That(method, Is.Not.Null, "Gameplay needs physical event identity before replay recording.");
            _callback = Delegate.CreateDelegate(method.GetParameters()[1].ParameterType, this,
                GetType().GetMethod(nameof(Receive), BindingFlags.Instance | BindingFlags.NonPublic));
            Func<int, int?> group = action => merged && (action == _primaryAction
                || action == _secondaryAction) ? _primaryAction : null;
            method.Invoke(_bindings, new object[] { GameMode.EliteDrums, _callback, group });
        }

        private void Send(float left, float right, double time)
        {
            InputSystem.QueueStateEvent(_device, new GamepadState { leftTrigger = left, rightTrigger = right }, time);
            typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(InputUpdateType) }, null).Invoke(null, new object[] { InputUpdateType.Dynamic });
        }

        [Test]
        public void SamePhysicalControlEventIsRecordedOnceButEqualTimeEventsRemainDistinct()
        {
            Subscribe(true);
            double time = InputState.currentTime;
            Send(1, 0, time);
            Assert.That(_received, Has.Count.EqualTo(1));
            Send(0, 0, time);
            Send(1, 0, time);
            Assert.That(_received, Has.Count.EqualTo(2), "Timestamps cannot identify physical hits.");
        }

        [TestCase("left-crash", "right-crash")]
        [TestCase("left-crash", "ride")]
        [TestCase("ride", "left-crash")]
        [TestCase("ride", "right-crash")]
        [TestCase("right-crash", "left-crash")]
        [TestCase("right-crash", "ride")]
        public void EveryOrderedPrimaryDeduplicatesOnePhysicalEventWithoutCollapsingRepeatedEqualTimeHits(
            string primary, string secondary)
        {
            _primaryAction = SourceAction(primary);
            _secondaryAction = SourceAction(secondary);
            var collection = _type.GetProperty("Item").GetValue(_bindings, new object[] { GameMode.EliteDrums });
            var getBinding = collection.GetType().GetMethod("TryGetBindingByAction", new[] { typeof(int) });
            foreach (int action in new[] { (int)EliteDrumsAction.EliteLeftCrash,
                (int)EliteDrumsAction.EliteRide, (int)EliteDrumsAction.EliteRightCrash })
            {
                var binding = getBinding.Invoke(collection, new object[] { action });
                binding.GetType().GetMethod("ClearAllBindings").Invoke(binding, null);
                if (action != _primaryAction && action != _secondaryAction) continue;
                binding.GetType().GetProperty("DebounceThreshold").SetValue(binding, 0L);
                binding.GetType().GetMethod("AddControl", new[] { typeof(InputControl) })
                    .Invoke(binding, new object[] { _device.leftTrigger });
                foreach (var control in (IEnumerable)binding.GetType().GetProperty("Bindings").GetValue(binding))
                    control.GetType().GetProperty("DebounceThreshold").SetValue(control, 0L);
            }
            collection.GetType().GetMethod("EnableInputs").Invoke(collection, null);
            Subscribe(true);
            double time = InputState.currentTime;
            Send(.8f, 0, time);
            Assert.That(_received, Has.Count.EqualTo(1));
            Send(0, 0, time);
            Send(.8f, 0, time);
            Assert.That(_received, Has.Count.EqualTo(2), "Equal timestamps still represent distinct physical presses.");
        }

        private static int SourceAction(string source) => source switch
        {
            "left-crash" => (int)EliteDrumsAction.EliteLeftCrash,
            "ride" => (int)EliteDrumsAction.EliteRide,
            "right-crash" => (int)EliteDrumsAction.EliteRightCrash,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

        [UnityTest]
        public IEnumerator DeferredDebounceKeepsPhysicalIdentityAcrossTheTwoAliases() => DeferredDebounce(false);

        [UnityTest]
        public IEnumerator DeferredPressIgnoresUnrelatedSubthresholdControlNoise() => DeferredDebounce(true);

        private IEnumerator DeferredDebounce(bool addNoiseControl)
        {
            var collection = _type.GetProperty("Item").GetValue(_bindings, new object[] { GameMode.EliteDrums });
            if (addNoiseControl)
            {
                var left = collection.GetType().GetMethod("TryGetBindingByAction", new[] { typeof(int) })
                    .Invoke(collection, new object[] { (int)EliteDrumsAction.EliteLeftCrash });
                left.GetType().GetMethod("AddControl", new[] { typeof(InputControl) })
                    .Invoke(left, new object[] { _device.rightTrigger });
            }
            foreach (var binding in (IEnumerable)collection)
            foreach (var single in (IEnumerable)binding.GetType().GetProperty("Bindings").GetValue(binding))
            {
                single.GetType().GetProperty("DebounceThreshold").SetValue(single, 50L);
                var mode = single.GetType().GetProperty("DebounceMode");
                mode.SetValue(single, Enum.Parse(mode.PropertyType, "PressAndRelease"));
            }
            Subscribe(true);
            double time = InputState.currentTime;
            Send(1, 0, time);
            yield return new WaitForSecondsRealtime(.1f);
            Send(0, 0, InputState.currentTime);
            Send(1, 0, InputState.currentTime);
            if (addNoiseControl)
            {
                yield return new WaitForSecondsRealtime(.01f);
                Send(1, .1f, InputState.currentTime);
            }
            yield return new WaitForSecondsRealtime(.06f);
            foreach (var binding in (IEnumerable)collection)
                binding.GetType().GetMethod("UpdateForFrame").Invoke(binding, new object[] { InputState.currentTime });
            Assert.That(_received, Has.Count.EqualTo(2), "Delayed aliases still describe one physical hit.");
        }

        [UnityTest]
        public IEnumerator UnequalDebounceKeepsTheOriginalPressThroughAnalogVelocityNoise()
        {
            var collection = _type.GetProperty("Item").GetValue(_bindings, new object[] { GameMode.EliteDrums });
            var ride = collection.GetType().GetMethod("TryGetBindingByAction", new[] { typeof(int) })
                .Invoke(collection, new object[] { (int)EliteDrumsAction.EliteRide });
            foreach (var single in (IEnumerable)ride.GetType().GetProperty("Bindings").GetValue(ride))
            {
                single.GetType().GetProperty("DebounceThreshold").SetValue(single, 50L);
                var mode = single.GetType().GetProperty("DebounceMode");
                mode.SetValue(single, Enum.Parse(mode.PropertyType, "PressAndRelease"));
            }
            Subscribe(true);
            Send(.8f, 0, InputState.currentTime);
            yield return new WaitForSecondsRealtime(.1f);
            Send(0, 0, InputState.currentTime);
            Send(.8f, 0, InputState.currentTime);
            Assert.That(_received, Has.Count.EqualTo(2));
            yield return new WaitForSecondsRealtime(.01f);
            Send(.9f, 0, InputState.currentTime);
            yield return new WaitForSecondsRealtime(.06f);
            foreach (var binding in (IEnumerable)collection)
                binding.GetType().GetMethod("UpdateForFrame").Invoke(binding, new object[] { InputState.currentTime });
            Assert.That(_received, Has.Count.EqualTo(2), "Analog noise cannot give a delayed alias a new press identity.");
        }

        [Test]
        public void DistinctControlsInOneEventRemainTwoHits()
        {
            var collection = _type.GetProperty("Item").GetValue(_bindings, new object[] { GameMode.EliteDrums });
            var binding = collection.GetType().GetMethod("TryGetBindingByAction", new[] { typeof(int) })
                .Invoke(collection, new object[] { (int)EliteDrumsAction.EliteRide });
            binding.GetType().GetMethod("ClearAllBindings").Invoke(binding, null);
            binding.GetType().GetMethod("AddControl", new[] { typeof(InputControl) })
                .Invoke(binding, new object[] { _device.rightTrigger });
            Subscribe(true);
            Send(1, 1, InputState.currentTime);
            Assert.That(_received, Has.Count.EqualTo(2));
        }

        [Test]
        public void UnmergedInputsKeepBothBindingsAndUnsubscribeRemovesTheWrapper()
        {
            Subscribe(false);
            double time = InputState.currentTime;
            Send(1, 0, time);
            Assert.That(_received, Has.Count.EqualTo(2));
            _type.GetMethod("UnsubscribeFromGameplayInputs").Invoke(_bindings,
                new object[] { GameMode.EliteDrums, _callback });
            Send(0, 0, time);
            Send(1, 0, time);
            Assert.That(_received, Has.Count.EqualTo(2));
        }
    }
}
