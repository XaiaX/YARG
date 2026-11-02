// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Input.Diagnostics;

namespace YARG.Tests.EditMode
{
    public sealed class MidiTestCaptureEditModeTests
    {
        private readonly List<object> _devices = new();
        private object _bindings, _capture;
        private string _directory, _oldDataPath;
        private const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static Type Type(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private static object Get(object value, string name) => value.GetType().GetProperty(name, ALL).GetValue(value);
        private static object Call(object value, string name, params object[] args) => value.GetType()
            .GetMethods(ALL).Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(value, args);

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "yarg-midi-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var path = Type("YARG.Helpers.PathHelper").GetProperty("PersistentDataPath");
            _oldDataPath = (string)path.GetValue(null);
            path.GetSetMethod(true).Invoke(null, new object[] { _directory });
            var profile = new YargProfile { Name = "MIDI diagnostic test", GameMode = GameMode.EliteDrums };
            _bindings = Activator.CreateInstance(Type("YARG.Input.ProfileBindings"), profile);
        }

        [TearDown]
        public void TearDown()
        {
            (_capture as IDisposable)?.Dispose();
            (_bindings as IDisposable)?.Dispose();
            foreach (var device in _devices)
                Type("UnityEngine.InputSystem.InputSystem").GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "RemoveDevice" && method.GetParameters().Length == 1)
                    .Invoke(null, new[] { device });
            _devices.Clear();
            Type("YARG.Helpers.PathHelper").GetProperty("PersistentDataPath").GetSetMethod(true)
                .Invoke(null, new object[] { _oldDataPath });
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            _capture = null;
        }

        private object Device(long port, int channel, bool ready = true)
        {
            var descriptionType = Type("UnityEngine.InputSystem.Layouts.InputDeviceDescription");
            object description = Activator.CreateInstance(descriptionType);
            descriptionType.GetProperty("interfaceName").SetValue(description, "Minis");
            descriptionType.GetProperty("product").SetValue(description, "Identical device name " + channel);
            descriptionType.GetProperty("capabilities").SetValue(description, "{\"channel\":" + channel + "}");
            var inputSystem = Type("UnityEngine.InputSystem.InputSystem");
            var device = inputSystem.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "AddDevice" && method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == descriptionType).Invoke(null, new[] { description });
            _devices.Add(device);
            if (ready) Call(device, "SetSource", port, "Identical device name");
            return device;
        }

        private void Assign(object device) => Call(_bindings, "AddDevice", device);
        private void Bind(object device, string key, int number, bool controller = false)
        {
            var collection = _bindings.GetType().GetProperty("Item").GetValue(_bindings, new object[] { GameMode.EliteDrums });
            var binding = Call(collection, "TryGetBindingByKey", key);
            var control = Call(device, controller ? "GetControl" : "GetNote", number);
            binding.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(method => method.Name == "AddControl" && method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType.FullName == "UnityEngine.InputSystem.InputControl")
                .Invoke(binding, new[] { control });
        }
        private void Capture() => _capture = Activator.CreateInstance(Type("YARG.Input.Diagnostics.MidiTestCapture"), _bindings);
        private static void Publish(long port, byte status, byte number, byte value) => Type("Minis.RawMidiEvents")
            .GetMethod("Publish", ALL).Invoke(null, new object[] { port, "Identical device name", .001, status, number, value });
        private MidiTestRecord[] History => (MidiTestRecord[])Get(_capture, "History");

        [TestCase(0xA9, 42, 91, 3, true, 2, 91)]
        [TestCase(0xD9, 91, 255, 2, true, 1, 91)]
        [TestCase(0xD9, 0, 0, 2, true, 1, 0)]
        [TestCase(0xA9, 42, 91, 2, false, 0, 0)]
        [TestCase(0xD9, 91, 0, 1, false, 0, 0)]
        [TestCase(0xA9, 128, 0, 3, false, 0, 0)]
        [TestCase(0xC9, 42, 0, 2, false, 0, 0)]
        [TestCase(0xA9, 42, 128, 3, false, 0, 0)]
        [TestCase(0xD9, 128, 0, 2, false, 0, 0)]
        [TestCase(0x99, 42, 91, 3, true, 2, 91)]
        [TestCase(0xB9, 4, 0, 3, true, 2, 0)]
        [TestCase(0x89, 42, 64, 3, true, 2, 64)]
        public void RawPacketFramingHandlesBothAftertouchTypes(int status, int data1, int data2,
            int length, bool accepted, int dataLength, int value)
        {
            var factory = Type("Minis.RawMidiMessage").GetMethod("TryCreate", ALL);
            Assert.That(factory, Is.Not.Null, "Aftertouch must be framed before physical dispatch");
            object[] args = { 707L, "Kit", .001, (byte)status, (byte)data1, (byte)data2, length, null };
            Assert.That(factory.Invoke(null, args), Is.EqualTo(accepted));
            if (!accepted) return;
            Assert.That(Get(args[7], "DataLength"), Is.EqualTo(dataLength));
            Assert.That(Get(args[7], dataLength == 1 ? "Data1" : "Data2"), Is.EqualTo((byte)value));
            if (dataLength == 1) Assert.That(Get(args[7], "Data2"), Is.EqualTo((byte)0));
        }

        [Test]
        public void AftertouchOnNewChannelStagesAndRecordsPressureWithoutInventingCCState()
        {
            var device = Device(707, 9, false); Assign(device); Capture();
            Call(_capture, "SetRecording", true);
            string path = (string)Get(_capture, "LogPath");
            var factory = Type("Minis.RawMidiMessage").GetMethod("TryCreate", ALL);
            Assert.That(factory, Is.Not.Null);
            object[] args = { 707L, "Kit", .001, (byte)0xA9, (byte)42, (byte)127, 3, null };
            Assert.That(factory.Invoke(null, args), Is.True);
            var delivery = Activator.CreateInstance(Type("Minis.RawMidiDelivery"), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { 707L, 512 }, null);
            Call(delivery, "Enqueue", args[7]);
            Assert.That(History, Is.Empty);
            Call(device, "SetSource", 707L, "Kit");
            Call(delivery, "ChannelReady", -1); Call(delivery, "ChannelReady", 9);
            Publish(707, 0xD9, 61, 0);
            Assert.That(History, Has.Length.EqualTo(2));
            Assert.That(History[0].Kind.ToString(), Is.EqualTo("PolyphonicAftertouch"));
            Assert.That(History[1].Kind.ToString(), Is.EqualTo("ChannelAftertouch"));
            Assert.That(Get(_capture, "LatestNote"), Is.Null);
            Assert.That(Get(_capture, "CurrentPedalValue"), Is.Null);
            Call(_capture, "SetRecording", false);
            string text = File.ReadAllText(path);
            Assert.That(text, Does.Contain("\"format_version\":2").And.Contain("PolyphonicAftertouch").And.Contain("ChannelAftertouch"));
            Assert.That(text, Does.Contain("\"DataLength\":1").And.Contain("\"Value\":61"));
        }

        [Test]
        public void MidiMenuSuppressionPreservesOtherControlsAndRestoresWithoutChangingBindings()
        {
            var device = Device(799, 9);
            var bindingType = Type("YARG.Input.ButtonBinding");
            var binding = Activator.CreateInstance(bindingType, "Menu.Green", 0);
            var control = Call(device, "GetNote", 42);
            var add = bindingType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.Name == "AddControl" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.FullName == "UnityEngine.InputSystem.InputControl");
            add.Invoke(binding, new[] { control });
            var keyboard = Type("UnityEngine.InputSystem.InputSystem").GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "AddDevice" && !m.IsGenericMethod && m.GetParameters().Length == 3 &&
                    m.GetParameters()[0].ParameterType == typeof(string))
                .Invoke(null, new object[] { "Keyboard", "MIDI test keyboard", null });
            _devices.Add(keyboard);
            add.Invoke(binding, new[] { Get(keyboard, "aKey") });
            var singles = ((System.Collections.IEnumerable)Get(binding, "Bindings")).Cast<object>().ToArray();
            var single = singles[0];
            var key = singles[1];
            var state = single.GetType().GetProperty("State", ALL);
            var onChanged = bindingType.GetMethod("OnStateChanged", ALL);
            var suspend = bindingType.GetMethod("SuspendMidiInput");
            Assert.That(suspend, Is.Not.Null, "MIDI-only scoped suppression must leave keyboard/gamepad bindings enabled");
            Call(binding, "Enable");
            var json = Type("Newtonsoft.Json.JsonConvert").GetMethod("SerializeObject", new[] { typeof(object) });
            var before = json.Invoke(null, new[] { Call(binding, "Serialize") });
            using ((IDisposable)suspend.Invoke(binding, null))
            {
                Assert.That(Get(binding, "Enabled"), Is.True);
                state.GetSetMethod(true).Invoke(single, new object[] { 1f });
                onChanged.Invoke(binding, new object[] { single, 100d });
                Assert.That(Get(binding, "State"), Is.False);
                state.GetSetMethod(true).Invoke(key, new object[] { 1f });
                onChanged.Invoke(binding, new object[] { key, 102d });
                onChanged.Invoke(binding, new object[] { key, 103d });
                Assert.That(Get(binding, "State"), Is.True, "Assigned keyboard controls continue to navigate");
            }
            Assert.That(((System.Collections.IEnumerable)Get(binding, "Bindings")).Cast<object>(), Is.EqualTo(singles));
            Assert.That(json.Invoke(null, new[] { Call(binding, "Serialize") }), Is.EqualTo(before));
            state.GetSetMethod(true).Invoke(key, new object[] { 0f });
            onChanged.Invoke(binding, new object[] { key, 150d });
            onChanged.Invoke(binding, new object[] { key, 151d });
            state.GetSetMethod(true).Invoke(single, new object[] { 1f });
            onChanged.Invoke(binding, new object[] { single, 200d });
            onChanged.Invoke(binding, new object[] { single, 201d });
            Assert.That(Get(binding, "State"), Is.True, "Normal MIDI navigation resumes after disposing the scope");
            Call(binding, "ClearAllBindings");
        }

        private sealed class CannotSerialize
        {
            public string Value => throw new InvalidOperationException("test serialization failure");
        }

        [Test]
        public void MidiSuppressionAlsoAppliesToFrameDebounceWithNegativePedalThreshold()
        {
            var device = Device(798, 9);
            var binding = Activator.CreateInstance(Type("YARG.Input.ButtonBinding"), "Menu.Red", 1);
            var control = Call(device, "GetControl", 4);
            binding.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.Name == "AddControl" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.FullName == "UnityEngine.InputSystem.InputControl")
                .Invoke(binding, new[] { control });
            try
            {
                var single = ((System.Collections.IEnumerable)Get(binding, "Bindings")).Cast<object>().Single();
                single.GetType().GetProperty("Inverted").SetValue(single, true);
                single.GetType().GetProperty("PressPoint").SetValue(single, -.5f);
                using ((IDisposable)Call(binding, "SuspendMidiInput"))
                {
                    Call(binding, "UpdateForFrame", 100d);
                    Call(binding, "UpdateForFrame", 101d);
                    Assert.That(Get(binding, "State"), Is.False,
                        "Frame debounce must not resurrect a suppressed pedal as a menu button");
                }
            }
            finally { Call(binding, "ClearAllBindings"); }
        }

        [Test]
        public void FailedInFlightLogRecordIsIncludedInDropCount()
        {
            var writer = Activator.CreateInstance(Type("YARG.Input.Diagnostics.MidiTestLogWriter"),
                Path.Combine(_directory, "failing.jsonl"), new { type = "metadata" }, 8192);
            try
            {
                Assert.That(Call(writer, "TryEnqueue", new CannotSerialize()), Is.True);
                Call(writer, "Stop");
                Assert.That(Get(writer, "Error"), Is.Not.Null);
                Assert.That((long)Get(writer, "DroppedRecords"), Is.GreaterThanOrEqualTo(1));
            }
            finally { ((IDisposable)writer).Dispose(); }
        }

        [Test]
        public void AssignedPortAndChannelFilterPreservesAllMatchesWithoutAliasDuplicates()
        {
            var all = Device(701, -1); var channel = Device(701, 9); var other = Device(702, 9);
            Assign(all); Assign(channel);
            Bind(all, "EliteDrums.ClosedHiHat", 42);
            Bind(channel, "EliteDrums.Stomp", 42);
            Capture();
            Publish(702, 0x99, 42, 91); // Same name, different physical port: never accepted.
            Assert.That(History, Is.Empty);
            Publish(701, 0x99, 42, 91);
            Assert.That(History, Has.Length.EqualTo(1));
            Assert.That(History[0].Matches.Select(m => m.Key), Is.EquivalentTo(new[] {
                "EliteDrums.ClosedHiHat", "EliteDrums.Stomp" }));
            Assert.That(History[0].Message.Data2, Is.EqualTo(91));
            Assert.That(History[0].Message.ReceiptMilliseconds, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void SpecificChannelRejectsOtherChannelsButIncludesUnmappedControllers()
        {
            var channel = Device(703, 9); Assign(channel); Capture();
            Publish(703, 0x98, 42, 80);
            Assert.That(History, Is.Empty);
            Publish(703, 0xB9, 11, 79);
            Assert.That(History, Has.Length.EqualTo(1));
            Assert.That(History[0].Kind, Is.EqualTo(MidiTestMessageKind.ControlChange));
            Assert.That(History[0].Matches, Is.Empty);
        }

        [Test]
        public void BackendSourceReadinessRefreshesIdentityWithoutChangingSavedDescription()
        {
            var device = Device(704, 9, false); Assign(device); Capture();
            Assert.That(Get(_capture, "ConfiguredDeviceCount"), Is.EqualTo(0));
            var before = Get(device, "description").ToString();
            Call(device, "SetSource", 704L, "Identical device name");
            Assert.That(Get(_capture, "ConfiguredDeviceCount"), Is.EqualTo(1));
            Assert.That(Get(device, "description").ToString(), Is.EqualTo(before));
            Publish(704, 0x99, 42, 90);
            Assert.That(History, Has.Length.EqualTo(1));
            Call(_bindings, "OnDeviceRemoved", device);
            Publish(704, 0x99, 42, 90);
            Assert.That(History, Has.Length.EqualTo(1), "Disconnect must close the accepted endpoint set");
        }

        [Test]
        public void FirstPhysicalMessageWaitsForLazyChannelIdentityAndReportsBoundedOverflow()
        {
            var device = Device(706, 9, false); Assign(device); Capture();
            var deliveryType = Type("Minis.RawMidiDelivery");
            var delivery = Activator.CreateInstance(deliveryType, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { 706L, 1 }, null);
            var messageType = Type("Minis.RawMidiMessage");
            var packet = Activator.CreateInstance(messageType, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { 706L, "Identical device name", .001, (byte)0x99, (byte)42, (byte)88 }, null);
            Call(delivery, "Enqueue", packet);
            Call(delivery, "Enqueue", packet);
            Assert.That(History, Is.Empty);
            Call(device, "SetSource", 706L, "Identical device name");
            Call(delivery, "ChannelReady", 9);
            Assert.That(History, Is.Empty, "All-channel device also needs its backend identity");
            Call(delivery, "ChannelReady", -1);
            Assert.That(History, Has.Length.EqualTo(1));
            Assert.That(History[0].Message.Data2, Is.EqualTo(88));
            long origin = (long)_capture.GetType().GetField("_clockOrigin", ALL).GetValue(_capture);
            long receipt = (long)Get(packet, "ReceivedTimestamp");
            double originalElapsed = (receipt - origin) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            Assert.That(History[0].Message.ReceiptMilliseconds, Is.EqualTo(originalElapsed));
            Assert.That(Get(_capture, "DroppedRecords"), Is.EqualTo(1L));
        }

        [Test]
        public void RecordingDrainsReceiptSnapshotAndClearingHistoryDoesNotEraseLog()
        {
            var device = Device(705, 9); Assign(device);
            Bind(device, "EliteDrums.Stomp", 4, true); Capture();
            string path = (string)Call(_capture, "SetRecording", true);
            Assert.That(Get(_capture, "IsRecording"), Is.True);
            Publish(705, 0xB9, 4, 111);
            Publish(705, 0x99, 42, 87);
            Assert.That(History.Last().PedalValue, Is.EqualTo(111));
            Publish(705, 0xB9, 4, 19);
            Assert.That(History[1].PedalValue, Is.EqualTo(111), "History holds receipt-time state");
            Call(_capture, "ClearHistory");
            Assert.That(History, Is.Empty);
            Assert.That(Call(_capture, "SetRecording", false), Is.EqualTo(path));
            Assert.That(Get(_capture, "IsRecording"), Is.False);
            Assert.That(Get(_capture, "Error"), Is.Null);
            var lines = File.ReadAllLines(path);
            Assert.That(lines[0], Does.Contain("format_version"));
            Assert.That(lines.Count(line => line.Contains("\"type\":\"event\"")), Is.EqualTo(3));
            Assert.That(lines.Last(), Does.Contain("recording_end"));
            Assert.That(Get(_capture, "LogPath"), Is.EqualTo(path));
        }
    }
}
