// pattern: Imperative Shell
using System;
using System.Linq;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class FrameGraphHistoryTests
    {
        private Type _type, _sampleType;
        private object _history;

        [SetUp]
        public void SetUp()
        {
            _type = Production("YARG.Persistent.FrameGraphHistory");
            Assert.That(_type, Is.Not.Null, "Graph needs a native-frame history independent of late-Update wall cadence.");
            _sampleType = Production("YARG.Persistent.FrameDiagnosticSample");
            _history = Activator.CreateInstance(_type);
        }

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        private T Get<T>(string name) => (T)_type.GetProperty(name).GetValue(_history);
        private string Source => _type.GetProperty("Source").GetValue(_history).ToString();
        private void Add(int frame, long nativeNs = 16666667, double unityMs = 17,
            int sampled = 1, int available = 1, int nativeOffset = -1, bool enabled = true, bool pacingChanged = false)
        {
            var sample = Activator.CreateInstance(_sampleType);
            void Set(string name, object value) => _sampleType.GetField(name).SetValue(sample, value);
            Set("Frame", frame); Set("WallMs", frame % 2 == 0 ? 8.0 : 25.0);
            Set("UnityMs", unityMs); Set("PacingChanged", pacingChanged);
            var field = _sampleType.GetField("RenderDiagnostics");
            var native = Activator.CreateInstance(field.FieldType);
            void Native(string name, object value) => field.FieldType.GetField(name).SetValue(native, value);
            Native("Enabled", enabled); Native("Frame", frame + nativeOffset);
            Native("MainThreadNanoseconds", nativeNs); Native("SampledMask", sampled); Native("AvailableMask", available);
            field.SetValue(sample, native);
            _type.GetMethod("Add").Invoke(_history, new[] { sample });
        }

        [Test]
        public void SteadyNativeFramesDoNotInheritAlternatingUpdateIntervals()
        {
            for (int frame = 1; frame <= 480; frame++) Add(frame);
            Assert.That(Source, Is.EqualTo("Native"));
            Assert.That(Get<int>("Count"), Is.EqualTo(480));
            Assert.That(Get<double[]>("History"), Has.All.EqualTo(16.666667).Within(0.000001));
            Assert.That(Get<double>("LatestMs"), Is.EqualTo(16.666667).Within(0.000001));
        }

        [TestCase(0L, 1, 1, -1, true)]
        [TestCase(-100L, 1, 1, -1, true)]
        [TestCase(16666667L, 0, 1, -1, true)]
        [TestCase(16666667L, 1, 0, -1, true)]
        [TestCase(16666667L, 1, 1, -2, true)]
        [TestCase(16666667L, 1, 1, 0, true)]
        [TestCase(16666667L, 1, 1, -1, false)]
        public void UnusableNativeSampleUsesExplicitUnityDelta(long ns, int sampled, int available, int offset, bool enabled)
        {
            Add(10, ns, 18, sampled, available, offset, enabled);
            Assert.That(Source, Is.EqualTo("UnityDelta"));
            Assert.That(Get<double>("LatestMs"), Is.EqualTo(18));
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void MissingBothSourcesClearsOldGraphInsteadOfUsingWallOrStaleNative(double delta)
        {
            Add(1);
            Add(2, 0, delta);
            Assert.That(Get<int>("Count"), Is.Zero);
            Assert.That(Source, Is.EqualTo("None"));
            Assert.That(Get<double>("LatestMs"), Is.Zero);
        }

        [Test]
        public void DuplicateOrOlderFrameDoesNotAddOrChangeSource()
        {
            Add(5);
            int revision = Get<int>("Revision");
            Add(5, 0, 42);
            Add(4, 0, 42);
            Assert.That(Get<int>("Count"), Is.EqualTo(1));
            Assert.That(Get<int>("Revision"), Is.EqualTo(revision));
            Assert.That(Source, Is.EqualTo("Native"));
        }

        [Test]
        public void SourceChangesStartNewStatistics()
        {
            Add(1, 0, 40);
            Add(2);
            Assert.That(Get<int>("Count"), Is.EqualTo(1));
            Assert.That(Get<double[]>("History")[0], Is.EqualTo(16.666667).Within(0.000001));
            Add(3, 0, 18);
            Assert.That(Get<int>("Count"), Is.EqualTo(1));
            Assert.That(Source, Is.EqualTo("UnityDelta"));
            Assert.That(Get<double[]>("History")[0], Is.EqualTo(18));
        }

        [Test]
        public void PacingTransitionClearsHistoryAndOmitsCrossingFrame()
        {
            Add(1);
            int revision = Get<int>("Revision");
            Add(2, pacingChanged: true);
            Assert.That(Get<int>("Count"), Is.Zero);
            Assert.That(Get<int>("Revision"), Is.GreaterThan(revision));
            Add(2);
            Assert.That(Get<int>("Count"), Is.Zero);
            Add(3);
            Assert.That(Get<int>("Count"), Is.EqualTo(1));
        }

        [Test]
        public void WrappedHistoryRetainsLatestFramesAndResetAllowsNewSession()
        {
            for (int frame = 1; frame <= 1000; frame++) Add(frame, frame * 1000000L);
            Assert.That(Get<int>("Count"), Is.EqualTo(480));
            var history = Get<double[]>("History");
            int head = Get<int>("Head");
            for (int i = 0; i < 480; i++)
                Assert.That(history[(head + i) % 480], Is.EqualTo(521 + i));
            int revision = Get<int>("Revision");
            _type.GetMethod("Reset").Invoke(_history, null);
            Assert.That(Get<int>("Count"), Is.Zero);
            Assert.That(Source, Is.EqualTo("None"));
            Assert.That(Get<int>("Revision"), Is.GreaterThan(revision));
            Add(1);
            Assert.That(Get<int>("Count"), Is.EqualTo(1));
        }
    }
}
