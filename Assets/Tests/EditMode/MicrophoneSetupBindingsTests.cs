// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class MicrophoneSetupBindingsTests
    {
        private const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private readonly List<IDisposable> _bindings = new();
        private string _directory;

        private static Type Production(string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, "Missing microphone setup type: " + name);
            return type;
        }

        private static object Get(object owner, string name)
        {
            var property = owner.GetType().GetProperty(name, FLAGS);
            Assert.That(property, Is.Not.Null, "Missing microphone setup property: " + name);
            return property.GetValue(owner);
        }

        private static object Call(object owner, string name, params object[] arguments)
        {
            var method = owner.GetType().GetMethods(FLAGS).SingleOrDefault(m => m.Name == name && m.GetParameters().Length == arguments.Length);
            Assert.That(method, Is.Not.Null, "Missing microphone setup method: " + name);
            try { return method.Invoke(owner, arguments); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        private static object[] Rows(object bindings) => ((IEnumerable)Get(bindings, "MicrophoneBindings")).Cast<object>().ToArray();
        private static SerializedMic Identity(object row) => (SerializedMic)Get(row, "Identity");
        private static List<MicDevice> Connected(object bindings) => (List<MicDevice>)Get(bindings, "Microphones");

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "yarg-microphone-bindings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var bindings in _bindings) bindings.Dispose();
            _bindings.Clear();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private object Bindings(GameMode mode = GameMode.PartyVocals, object serialized = null)
        {
            var profile = new YargProfile { Name = "Microphone setup test", GameMode = mode };
            var type = Production("YARG.Input.ProfileBindings");
            var bindings = serialized == null ? Activator.CreateInstance(type, profile) : Activator.CreateInstance(type, profile, serialized);
            _bindings.Add((IDisposable)bindings);
            return bindings;
        }

        private object Load(string json, GameMode mode = GameMode.PartyVocals)
        {
            string path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, json);
            var serialization = Production("YARG.Input.Serialization.BindingSerialization");
            var document = serialization.GetMethod("DeserializeBindings", FLAGS).Invoke(null, new object[] { path });
            Assert.That(document, Is.Not.Null, "Existing binding formats must continue loading.");
            var profiles = (IDictionary)document.GetType().GetField("Profiles").GetValue(document);
            return Bindings(mode, profiles.Values.Cast<object>().Single());
        }

        private string Save(object bindings, string path = null)
        {
            var document = Activator.CreateInstance(Production("YARG.Input.Serialization.SerializedBindings"));
            var profiles = (IDictionary)document.GetType().GetField("Profiles").GetValue(document);
            var profile = (YargProfile)Get(bindings, "Profile");
            profiles.Add(profile.Id, Call(bindings, "Serialize"));
            path ??= Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json");
            Production("YARG.Input.Serialization.BindingSerialization").GetMethod("SerializeBindings", FLAGS)
                .Invoke(null, new[] { document, path });
            return File.ReadAllText(path);
        }

        private static string V3(params SerializedMic[] identities)
        {
            string[] microphones = identities.Select(m => "{\"BaseName\":\"" + m.BaseName + "\",\"Channel\":" + m.Channel +
                ",\"StableId\":\"" + m.StableId + "\"}").ToArray();
            return "{\"Version\":3,\"Profiles\":{\"11111111-1111-1111-1111-111111111111\":{\"Devices\":[],\"Microphones\":[" +
                string.Join(",", microphones) + "],\"ModeMappings\":{}}}}";
        }

        [Test]
        public void SavedMissingRowsKeepTheirOrderWhenOnlySecondSameNameMicrophoneConnects()
        {
            var first = new FakeMic(10);
            var second = new FakeMic(11);
            var bindings = Load(V3(first.Serialize(), second.Serialize()));
            Assert.That(Call(bindings, "AddMicrophone", second), Is.True);
            var rows = Rows(bindings);
            Assert.That(rows.Select(Identity).Select(i => i.StableId), Is.EqualTo(new[] { first.StableId, second.StableId }));
            Assert.That(Get(rows[0], "Device"), Is.Null);
            Assert.That(Get(rows[1], "Device"), Is.SameAs(second));
            Assert.That(Connected(bindings), Is.EqualTo(new[] { second }), "Keep compact connected-device gameplay routing.");
            var restored = Load(Save(bindings));
            Assert.That(Rows(restored).Select(Identity).Select(i => i.StableId), Is.EqualTo(new[] { first.StableId, second.StableId }));
            first.Dispose();
        }

        [Test]
        public void ReorderAndReconnectRetainEachSameNameMicrophonesOwnSettings()
        {
            var first = new FakeMic(20); var second = new FakeMic(21);
            var bindings = Bindings();
            Assert.That(Call(bindings, "AddMicrophone", first), Is.True);
            Assert.That(Call(bindings, "AddMicrophone", second), Is.True);
            Call(bindings, "SetMicrophoneNeedle", 0, 5);
            Call(bindings, "SetMicrophoneSensitivity", 0, -7.5f);
            Call(bindings, "SetMicrophoneNeedle", 1, 3);
            Call(bindings, "SetMicrophoneSensitivity", 1, 9f);
            Call(bindings, "MoveMicrophone", 0, 1);
            Assert.That(Connected(bindings), Is.EqualTo(new[] { second, first }));
            Call(bindings, "ReleaseMicrophones");
            Assert.That(Connected(bindings), Is.Empty);
            Assert.That(first.Disposed && second.Disposed, Is.True);
            var replacement = new FakeMic(20);
            Assert.That(Call(bindings, "AddMicrophone", replacement), Is.True);
            var rows = Rows(bindings);
            Assert.That(rows.Select(Identity).Select(i => i.StableId), Is.EqualTo(new[] { second.StableId, first.StableId }));
            Assert.That(Get(rows[0], "NeedleId"), Is.EqualTo(3));
            Assert.That(Get(rows[0], "SensitivityOffsetDb"), Is.EqualTo(9f));
            Assert.That(Get(rows[1], "NeedleId"), Is.EqualTo(5));
            Assert.That(Get(rows[1], "SensitivityOffsetDb"), Is.EqualTo(-7.5f));
            var restored = Load(Save(bindings));
            Assert.That(Rows(restored).Select(r => Get(r, "SensitivityOffsetDb")), Is.EqualTo(new object[] { 9f, -7.5f }));
            Assert.That(Rows(restored).Select(r => Get(r, "NeedleId")), Is.EqualTo(new object[] { 3, 5 }));
        }

        [Test]
        public void OneLiveDeviceReferenceCannotBelongToTwoProfiles()
        {
            var first = Bindings(); var second = Bindings(); var mic = new FakeMic(30);
            Assert.That(Call(first, "AddMicrophone", mic), Is.True);
            Assert.That(Call(second, "AddMicrophone", mic), Is.False, "A second profile must not change another profile's capture settings.");
            ((IDisposable)second).Dispose();
            Assert.That(mic.Disposed, Is.False, "Rejected capture references are still owned by the first profile.");
            Assert.That(Connected(first), Has.Count.EqualTo(1));
        }

        [TestCase(GameMode.Vocals, 1)]
        [TestCase(GameMode.PartyVocals, 7)]
        public void MicrophoneLimitsArePreserved(GameMode mode, int cap)
        {
            var bindings = Bindings(mode);
            for (int i = 0; i < cap; i++) Assert.That(Call(bindings, "AddMicrophone", new FakeMic(100 + i)), Is.True);
            var rejected = new FakeMic(199);
            Assert.That(Call(bindings, "AddMicrophone", rejected), Is.False);
            Assert.That(Rows(bindings), Has.Length.EqualTo(cap));
            rejected.Dispose();
        }

        [Test]
        public void RemovingAMissingMicrophoneOnlyRemovesItsOwnSavedEntry()
        {
            var first = new FakeMic(40); var second = new FakeMic(41);
            var bindings = Load(V3(first.Serialize(), second.Serialize()));
            Call(bindings, "AddMicrophone", second);
            Call(bindings, "RemoveMicrophoneAt", 0);
            Assert.That(Rows(bindings).Select(Identity).Select(i => i.StableId), Is.EqualTo(new[] { second.StableId }));
            Assert.That(second.Disposed, Is.False);
            first.Dispose();
        }

        [TestCase(999f, 50f)]
        [TestCase(-999f, -50f)]
        [TestCase(float.NaN, 0f)]
        [TestCase(float.PositiveInfinity, 0f)]
        [TestCase(float.NegativeInfinity, 0f)]
        public void SensitivityOffsetsAreFiniteAndBounded(float input, float expected)
        {
            var bindings = Bindings(); Call(bindings, "AddMicrophone", new FakeMic(50));
            Call(bindings, "SetMicrophoneSensitivity", 0, input);
            Assert.That(Get(Rows(bindings)[0], "SensitivityOffsetDb"), Is.EqualTo(expected));
        }

        [Test]
        public void AutomaticNeedlesReserveExplicitDesignsWithoutChangingDefaultAppearance()
        {
            var bindings = Bindings();
            for (int i = 0; i < 3; i++) Call(bindings, "AddMicrophone", new FakeMic(60 + i));
            Assert.That(Enumerable.Range(0, 3).Select(i => Call(bindings, "GetMicrophoneNeedleId", i)), Is.EqualTo(new object[] { 1, 2, 3 }));
            Call(bindings, "SetMicrophoneNeedle", 2, 1);
            Assert.That(Enumerable.Range(0, 3).Select(i => Call(bindings, "GetMicrophoneNeedleId", i)), Is.EqualTo(new object[] { 2, 3, 1 }));
            Call(bindings, "SetMicrophoneNeedle", 2, -1);
            Assert.That(Get(Rows(bindings)[2], "NeedleId"), Is.EqualTo(0));
        }

        [Test]
        public void VersionThreeBindingsMigrateWithoutDiscardingMissingMicrophones()
        {
            var mic = new FakeMic(70);
            var bindings = Load(V3(mic.Serialize()));
            Assert.That(Get(Rows(bindings)[0], "NeedleId"), Is.EqualTo(0));
            Assert.That(Get(Rows(bindings)[0], "SensitivityOffsetDb"), Is.EqualTo(0f));
            Assert.That(Get(Rows(bindings)[0], "Device"), Is.Null);
            string json = Save(bindings);
            Assert.That(json, Does.Contain("\"Version\": 4"));
            var restored = Load(json);
            Assert.That(Identity(Rows(restored)[0]).StableId, Is.EqualTo(mic.StableId));
            mic.Dispose();
        }

        [Test]
        public void MigrationRetainsTheOriginalVersionThreeFileAcrossLaterSaves()
        {
            var mic = new FakeMic(71);
            string original = V3(mic.Serialize());
            var bindings = Load(original);
            string path = Path.Combine(_directory, "bindings.json");
            File.WriteAllText(path, original);
            Save(bindings, path);
            string backup = path + ".pre-microphone-setup-v3.bak";
            Assert.That(File.Exists(backup), Is.True, "Keep a legacy-readable checkpoint before writing v4.");
            Assert.That(File.ReadAllText(backup), Is.EqualTo(original));
            Call(bindings, "SetMicrophoneSensitivity", 0, 4f);
            Save(bindings, path);
            Assert.That(File.ReadAllText(backup), Is.EqualTo(original), "Ordinary later saves must not replace the migration checkpoint.");
            Assert.That(Get(Rows(Load(File.ReadAllText(path)))[0], "SensitivityOffsetDb"), Is.EqualTo(4f));
            mic.Dispose();
        }

        [Test]
        public void RecoveringFromValidBindingsCanOverwriteAMalformedMainFile()
        {
            var mic = new FakeMic(73);
            var recovered = Load(V3(mic.Serialize()));
            string path = Path.Combine(_directory, "bindings.json");
            File.WriteAllText(path, "{ malformed main file");
            bool oldIgnore = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
            string saved;
            try
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                saved = Save(recovered, path);
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = oldIgnore;
            }
            Assert.That(saved, Does.Contain("\"Version\": 4"), "A malformed version probe must not block the established backup-recovery write.");
            var restored = Load(saved);
            Assert.That(Identity(Rows(restored).Single()).StableId, Is.EqualTo(mic.StableId));
            mic.Dispose();
        }

        [Test]
        public void ResolvingAnUnavailableCapturePreservesItsIdentityAndSettings()
        {
            var old = new FakeMic(72);
            var replacement = new FakeMic(72);
            var bindings = Bindings();
            Call(bindings, "AddMicrophone", old);
            Call(bindings, "SetMicrophoneNeedle", 0, 6);
            Call(bindings, "SetMicrophoneSensitivity", 0, -3f);
            Func<SerializedMic, MicDevice> resolve = identity => identity.StableId == replacement.StableId ? replacement : null;
            Func<MicDevice, bool> alive = device => !ReferenceEquals(device, old);
            Assert.That(Call(bindings, "ResolveMicrophones", resolve, alive), Is.True);
            Assert.That(old.Disposed, Is.True);
            var row = Rows(bindings).Single();
            Assert.That(Get(row, "Device"), Is.SameAs(replacement));
            Assert.That(Get(row, "NeedleId"), Is.EqualTo(6));
            Assert.That(Get(row, "SensitivityOffsetDb"), Is.EqualTo(-3f));
            Assert.That(Connected(bindings), Is.EqualTo(new[] { replacement }));
        }

        [Test]
        public void MissingSameNameBindingCannotClaimAnotherBindingsExactDevice()
        {
            var first = new FakeMic(80); var second = new FakeMic(81);
            var selector = Production("YARG.Input.MicrophoneSetupValues").GetMethod("SelectInput", FLAGS);
            Assert.That(selector, Is.Not.Null, "Resolve saved exact identities before allowing same-name fallback.");
            var saved = new[] { first.Serialize(), second.Serialize() };
            var available = new[] { new InputDeviceInfo(81, "Identical USB microphone", 0, 1) };
            Assert.That(selector.Invoke(null, new object[] { saved[0], saved, available }), Is.Null,
                "Missing microphone A must not steal B merely because their display names match.");
            var candidate = selector.Invoke(null, new object[] { saved[1], saved, available });
            Assert.That(candidate, Is.Not.Null);
            Assert.That(((InputDeviceInfo)candidate).DeviceId, Is.EqualTo(81));
            var replacement = new[] { new InputDeviceInfo(82, "Identical USB microphone", 0, 1) };
            Assert.That(((InputDeviceInfo)selector.Invoke(null, new object[] { saved[0], saved, replacement })).DeviceId,
                Is.EqualTo(82), "An unreserved candidate remains eligible for legacy fallback.");
            first.Dispose(); second.Dispose();
        }

        private sealed class FakeMic : MicDevice
        {
            private readonly int _id;
            public bool Disposed { get; private set; }
            public FakeMic(int id) : base("Identical USB microphone") { _id = id; }
            public override string StableId => ComputeStableId(_id, DisplayName);
            public override int Reset() => 0;
            public override bool DequeueOutputFrame(out MicOutputFrame frame) { frame = default; return false; }
            public override void ClearOutputQueue() { }
            public override void SetMonitoringLevel(float volume) { }
            public override void SetReverbLevel(float wet) { }
            public override MicBufferInfo? GetBufferInfo() => null;
            public override SerializedMic Serialize() => new(DisplayName, 0, StableId);
            protected override void DisposeUnmanagedResources() => Disposed = true;
        }
    }
}
