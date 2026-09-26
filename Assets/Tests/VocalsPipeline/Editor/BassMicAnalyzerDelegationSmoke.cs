using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Audio.PitchDetection;
using YARG.Core.Audio;
using YARG.Core.Logging;

namespace YARG.Tests.VocalsPipeline
{
    // Tests temporarily replace the global microphone sensitivity setting.
    public sealed class BassMicAnalyzerDelegationSmoke
    {
        private const int SAMPLE_RATE = 48000;
        private const int FRAME_SAMPLES = 1920;
        private const int TIMEOUT_MS = 3000;
        private const float SENSITIVITY = 2f;
        private PropertyInfo _settingsProperty;
        private object _originalSettings;
        private object _slider;
        private float _originalSensitivity;

        [SetUp]
        public void SetUp()
        {
            Type managerType = Type.GetType("YARG.Settings.SettingsManager, Assembly-CSharp");
            Assert.That(managerType, Is.Not.Null);
            _settingsProperty = managerType.GetProperty("Settings", BindingFlags.Static | BindingFlags.Public);
            Assert.That(_settingsProperty, Is.Not.Null);
            _originalSettings = _settingsProperty.GetValue(null);
            if (_originalSettings == null)
            {
                // Normal field initializers require an audio device unavailable in EditMode.
                Type containerType = managerType.GetNestedType("SettingContainer", BindingFlags.Public);
                object settings = FormatterServices.GetUninitializedObject(containerType);
                FieldInfo field = containerType.GetField("<MicrophoneSensitivity>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                Type sliderType = containerType.Assembly.GetType("YARG.Settings.Types.SliderSetting", true);
                _slider = Activator.CreateInstance(sliderType, SENSITIVITY, -50f, 50f, null);
                field.SetValue(settings, _slider);
                _settingsProperty.GetSetMethod(true).Invoke(null, new[] { settings });
            }
            else
            {
                _slider = _originalSettings.GetType().GetProperty("MicrophoneSensitivity").GetValue(_originalSettings);
                _originalSensitivity = (float) _slider.GetType().GetProperty("Value").GetValue(_slider);
                _slider.GetType().GetMethod("SetValueWithoutNotify").Invoke(_slider, new object[] { SENSITIVITY });
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_settingsProperty == null || _slider == null) return;
            if (_originalSettings == null)
                _settingsProperty.GetSetMethod(true).Invoke(null, new object[] { null });
            else
                _slider.GetType().GetMethod("SetValueWithoutNotify").Invoke(_slider,
                    new object[] { _originalSensitivity });
        }

        [Test]
        public void RealWorkerMatchesProcessorAndClearsStateOnPauseAndReset()
        {
            // The game-side adapter implements the worker's internal source. An asmdef cannot
            // reference Assembly-CSharp, so only this test's calls cross via reflection.
            Type adapterType = AdapterType();
            var adapter = (IDisposable) Activator.CreateInstance(adapterType, SAMPLE_RATE);
                try
                {
                    MethodInfo submit = adapterType.GetMethod("SubmitAndWait");
                    MethodInfo dequeue = adapterType.GetMethod("TryDequeue");
                    MethodInfo pause = adapterType.GetMethod("PauseAndWait");
                    MethodInfo resume = adapterType.GetMethod("Resume");
                    MethodInfo reset = adapterType.GetMethod("Reset");
                    MethodInfo boundary = adapterType.GetMethod("WaitForBoundary");
                    Assert.That(boundary, Is.Not.Null);
                    Assert.That(submit, Is.Not.Null);
                    Assert.That(dequeue, Is.Not.Null);
                    Assert.That(pause, Is.Not.Null);
                    Assert.That(resume, Is.Not.Null);
                    Assert.That(reset, Is.Not.Null);
                    var baseline = new VocalsFrameProcessor(SAMPLE_RATE);
                    float[] quiet = new float[FRAME_SAMPLES];
                    float[] loud = new float[FRAME_SAMPLES];
                    for (int i = 0; i < FRAME_SAMPLES; i++) loud[i] = 0.2f;

                    // Reject invalid submissions before publishing anything to the worker.
                    AssertFault<ArgumentNullException>(submit, adapter, null, 9.0, TIMEOUT_MS);
                    AssertFault<ArgumentException>(submit, adapter, new float[FRAME_SAMPLES - 1], 9.0, TIMEOUT_MS);
                    AssertFault<ArgumentOutOfRangeException>(submit, adapter, quiet, 9.0, 0);

                    Compare(quiet, 10.0);
                    Compare(loud, 10.04);
                    // Leave a hit in the queue: pause must discard it, not merely reset the processor.
                    Submit(quiet, 10.08);
                    Submit(loud, 10.12);
                    InvokeBounded(() => boundary.Invoke(adapter, new object[] { TIMEOUT_MS }), "boundary before pause");
                    InvokeBounded(() => pause.Invoke(adapter, new object[] { TIMEOUT_MS }), "pause");
                    AssertEmpty("pause must clear queued output");
                    InvokeBounded(() => resume.Invoke(adapter, null), "resume");
                    baseline.Reset();
                    Compare(loud, 10.16); // Pause cleared prior amplitude; no new hit.
                    Submit(quiet, 10.20);
                    Submit(loud, 10.24);
                    InvokeBounded(() => boundary.Invoke(adapter, new object[] { TIMEOUT_MS }), "boundary before reset");
                    Assert.That((bool) InvokeBounded(() => reset.Invoke(adapter, null), "reset"), Is.True);
                    AssertEmpty("reset must clear queued output");
                    baseline.Reset();
                    Compare(loud, 10.28); // Reset clears the hit edge as well.
                    Compare(quiet, 10.32);
                    Compare(loud, 10.36); // A later rising edge remains functional.

                    void Submit(float[] samples, double readTime)
                    {
                        InvokeBounded(() => submit.Invoke(adapter, new object[] { samples, readTime, TIMEOUT_MS }),
                            $"submit at {readTime}");
                    }

                    void AssertEmpty(string message)
                    {
                        object[] arguments = { null };
                        Assert.That((bool) dequeue.Invoke(adapter, arguments), Is.False, message);
                    }

                    void Compare(float[] samples, double readTime)
                    {
                        var expected = new List<MicOutputFrame>();
                        baseline.Process(samples, FRAME_SAMPLES, FRAME_SAMPLES * sizeof(float), readTime, SENSITIVITY,
                            (frame, _) => expected.Add(frame));
                        Submit(samples, readTime);
                        var actual = new List<MicOutputFrame>();
                        object[] arguments = { null };
                        while ((bool) dequeue.Invoke(adapter, arguments))
                            actual.Add((MicOutputFrame) arguments[0]);
                        Assert.That(actual.Count, Is.EqualTo(expected.Count), $"frame at {readTime}");
                        AssertEmpty($"frame at {readTime} must not emit duplicate output");
                        for (int i = 0; i < expected.Count; i++)
                        {
                            Assert.That(actual[i].IsHit, Is.EqualTo(expected[i].IsHit));
                            Assert.That(actual[i].Time, Is.EqualTo(expected[i].Time).Within(1e-9));
                            Assert.That(actual[i].Pitch, Is.EqualTo(expected[i].Pitch).Within(0.01f));
                            Assert.That(actual[i].Volume, Is.EqualTo(expected[i].Volume).Within(0.01f));
                        }
                    }
                }
                finally
                {
                    InvokeBounded(() => { adapter.Dispose(); return null; }, "dispose");
                }
        }

        [Test]
        public void ConcurrentSubmissionsEachWaitForTheirOwnFrame()
        {
            Type type = AdapterType();
            var adapter = (IDisposable) Activator.CreateInstance(type, SAMPLE_RATE);
            try
            {
                MethodInfo submit = type.GetMethod("SubmitAndWait");
                MethodInfo dequeue = type.GetMethod("TryDequeue");
                Assert.That(submit, Is.Not.Null);
                Assert.That(dequeue, Is.Not.Null);
                float[] quiet = new float[FRAME_SAMPLES];
                float[] loud = new float[FRAME_SAMPLES];
                for (int i = 0; i < loud.Length; i++) loud[i] = 0.2f;
                int firstReturned = 0;
                int secondReturned = 0;
                using (var start = new ManualResetEventSlim(false))
                {
                    Task first = Task.Run(() => { start.Wait(); submit.Invoke(adapter, new object[] { quiet, 30.0, TIMEOUT_MS }); Interlocked.Exchange(ref firstReturned, 1); });
                    Task second = Task.Run(() => { start.Wait(); submit.Invoke(adapter, new object[] { loud, 30.04, TIMEOUT_MS }); Interlocked.Exchange(ref secondReturned, 1); });
                    start.Set();
                    Assert.That(((IAsyncResult) first).AsyncWaitHandle.WaitOne(TIMEOUT_MS + 1000), Is.True);
                    Assert.That(((IAsyncResult) second).AsyncWaitHandle.WaitOne(TIMEOUT_MS + 1000), Is.True);
                    first.GetAwaiter().GetResult();
                    second.GetAwaiter().GetResult();
                }
                // Both submissions returned only after their own processing; the baseline
                // would otherwise see a missing or prematurely published second frame.
                object[] frame = { null };
                var actual = new List<MicOutputFrame>();
                while ((bool) dequeue.Invoke(adapter, frame)) actual.Add((MicOutputFrame) frame[0]);
                Assert.That(Volatile.Read(ref firstReturned), Is.EqualTo(1));
                Assert.That(Volatile.Read(ref secondReturned), Is.EqualTo(1));
                // Order is nondeterministic; determine it from the hit edge when present.
                // A quiet-to-loud order emits the hit, while loud-to-quiet does not.
                var baseline = new VocalsFrameProcessor(SAMPLE_RATE);
                var expected = new List<MicOutputFrame>();
                bool quietFirst = actual.Exists(x => x.IsHit);
                void Feed(float[] samples, double time) => baseline.Process(samples, FRAME_SAMPLES,
                    FRAME_SAMPLES * sizeof(float), time, SENSITIVITY, (frame, _) => expected.Add(frame));
                if (quietFirst) { Feed(quiet, 30.0); Feed(loud, 30.04); }
                else { Feed(loud, 30.04); Feed(quiet, 30.0); }
                Assert.That(actual.Count, Is.EqualTo(expected.Count));
                for (int i = 0; i < actual.Count; i++)
                {
                    Assert.That(actual[i].IsHit, Is.EqualTo(expected[i].IsHit));
                    Assert.That(actual[i].Time, Is.EqualTo(expected[i].Time).Within(1e-9));
                }
            }
            finally
            {
                InvokeBounded(() => { adapter.Dispose(); return null; }, "concurrent dispose");
            }
        }

        [TestCase(false, TestName = "BlockedBoundaryPauseCompletesWithinBound")]
        [TestCase(true, TestName = "BlockedBoundaryResetCompletesWithinBound")]
        public void BlockedBoundaryLifecycleCompletesWithinBound(bool doReset)
        {
            Type type = AdapterType();
            var adapter = (IDisposable) Activator.CreateInstance(type, SAMPLE_RATE);
            try
            {
                MethodInfo boundary = type.GetMethod("WaitForBoundary");
                MethodInfo pause = type.GetMethod("PauseAndWait");
                MethodInfo reset = type.GetMethod("Reset");
                MethodInfo resume = type.GetMethod("Resume");
                MethodInfo submit = type.GetMethod("SubmitAndWait");
                Assert.That(boundary, Is.Not.Null);
                Assert.That(pause, Is.Not.Null);
                Assert.That(reset, Is.Not.Null);
                Assert.That(resume, Is.Not.Null);
                Assert.That(submit, Is.Not.Null);
                InvokeBounded(() => boundary.Invoke(adapter, new object[] { TIMEOUT_MS }), "blocked boundary");
                if (doReset)
                    Assert.That((bool) InvokeBounded(() => reset.Invoke(adapter, null), "blocked reset"), Is.True);
                else
                {
                    InvokeBounded(() => pause.Invoke(adapter, new object[] { TIMEOUT_MS }), "blocked pause");
                    InvokeBounded(() => pause.Invoke(adapter, new object[] { TIMEOUT_MS }), "idempotent pause");
                    InvokeBounded(() => resume.Invoke(adapter, null), "resume after pause");
                }
                InvokeBounded(() => submit.Invoke(adapter, new object[]
                {
                    new float[FRAME_SAMPLES], 20.0, TIMEOUT_MS,
                }), "submit after lifecycle");
            }
            finally
            {
                InvokeBounded(() => { adapter.Dispose(); return null; }, "lifecycle dispose");
            }
        }

        [Test]
        public void IdleWorkerTimesOutWithoutSubmissionAndDisposesWithinBound()
        {
            Type type = AdapterType();
            var adapter = (IDisposable) Activator.CreateInstance(type, SAMPLE_RATE);
            try
            {
                MethodInfo wait = type.GetMethod("WaitForFrameWithoutSubmission");
                Assert.That(wait, Is.Not.Null);
                Exception error = Assert.Throws<TargetInvocationException>(() =>
                    InvokeBounded(() => wait.Invoke(adapter, new object[] { 50 }), "idle timeout"))?.InnerException;
                Assert.That(error, Is.TypeOf<TimeoutException>());
            }
            finally
            {
                InvokeBounded(() => { adapter.Dispose(); return null; }, "idle dispose");
            }
        }

        [Test]
        public void WorkerFaultPropagatesOriginalExceptionAndDisposesWithinBound()
        {
            Type type = AdapterType();
            var adapter = (IDisposable) Activator.CreateInstance(type, SAMPLE_RATE);
            try
            {
                MethodInfo fault = type.GetMethod("ThrowOnNextFrame");
                MethodInfo submit = type.GetMethod("SubmitAndWait");
                MethodInfo boundary = type.GetMethod("WaitForBoundary");
                Assert.That(fault, Is.Not.Null);
                Assert.That(submit, Is.Not.Null);
                Assert.That(boundary, Is.Not.Null);
                InvokeBounded(() => boundary.Invoke(adapter, new object[] { TIMEOUT_MS }), "fault boundary");
                var injected = new InvalidOperationException("smoke-injected-worker-fault");
                var listener = new CapturingLogListener();
                YargLogger.AddLogListener(listener);
                try
                {
                    InvokeBounded(() => fault.Invoke(adapter, new object[] { injected }), "install fault");
                    // Runtime LogHandler is not necessarily initialized in EditMode.
                    Type handler = Type.GetType("YARG.Logging.LogHandler, Assembly-CSharp");
                    FieldInfo initialized = handler?.GetField("_isInitialized",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    if (initialized != null && (bool) initialized.GetValue(null))
                        LogAssert.Expect(LogType.Exception, "smoke-injected-worker-fault");
                    Exception error = Assert.Throws<TargetInvocationException>(() =>
                        InvokeBounded(() => submit.Invoke(adapter, new object[]
                        {
                            new float[FRAME_SAMPLES], 21.0, TIMEOUT_MS,
                        }), "faulted frame"))?.InnerException;
                    Assert.That(error, Is.SameAs(injected), "worker must propagate the original fault, not a timeout");
                    InvokeBounded(() => { YargLogger.FlushLogQueue(); return null; }, "flush worker log");
                    Assert.That(listener.Wait(TIMEOUT_MS), Is.True, "worker failure must be logged");
                }
                finally
                {
                    YargLogger.RemoveLogListener(listener);
                }
            }
            finally
            {
                InvokeBounded(() => { adapter.Dispose(); return null; }, "faulted dispose");
            }
        }

        private sealed class CapturingLogListener : BaseYargLogListener
        {
            private readonly ManualResetEventSlim _received = new(false);

            public CapturingLogListener() : base(new MessageOnlyYargLogFormatter()) { }

            public override void WriteLogItem(ref Utf16ValueStringBuilder output, LogItem item)
            {
                if (item.Level == LogLevel.Exception && output.ToString().Contains("smoke-injected-worker-fault"))
                    _received.Set();
            }

            public bool Wait(int timeoutMilliseconds) => _received.Wait(timeoutMilliseconds);

            protected override void Dispose(bool disposing)
            {
                if (disposing) _received.Dispose();
                base.Dispose(disposing);
            }
        }

        private static Type AdapterType()
        {
            Type type = Type.GetType("YARG.Audio.BASS.BassMicAnalyzerTestAdapter, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, "Editor test adapter must compile into Assembly-CSharp");
            return type;
        }

        private static object InvokeBounded(Func<object> action, string operation)
        {
            // The adapter's own timeout cannot protect against a blocked reflection call or Dispose.
            // The outer wait must finish even if that call never returns.
            Task<object> task = Task.Run(action);
            Assert.That(((IAsyncResult) task).AsyncWaitHandle.WaitOne(TIMEOUT_MS + 1000), Is.True,
                $"{operation} did not return within the external bound");
            return task.GetAwaiter().GetResult();
        }

        private static void AssertFault<T>(MethodInfo method, object adapter, params object[] arguments)
            where T : Exception
        {
            Exception error = Assert.Throws<TargetInvocationException>(() =>
                InvokeBounded(() => method.Invoke(adapter, arguments), "invalid submission"))?.InnerException;
            Assert.That(error, Is.TypeOf<T>());
        }
    }
}
