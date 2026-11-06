// pattern: Functional Core
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class PerformanceFixtureSettingsTests
    {
        private static Type Adapter => RuntimeType("YARG.PerformanceTesting.PerformanceFixtureSettings");
        private static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static object Toggle(bool value, Action<bool> callback = null) => Activator.CreateInstance(
            RuntimeType("YARG.Settings.Types.ToggleSetting"), value, callback);
        private static object Integer(int value, int min, int max, Action<int> callback = null) => Activator.CreateInstance(
            RuntimeType("YARG.Settings.Types.IntSetting"), value, min, max, callback);
        private static object Slider(float value, float min, float max) => Activator.CreateInstance(
            RuntimeType("YARG.Settings.Types.SliderSetting"), value, min, max, null);

        private static IDictionary Settings(params (string Name, object Setting)[] entries)
        {
            var type = typeof(Dictionary<,>).MakeGenericType(typeof(string), RuntimeType("YARG.Settings.Types.ISettingType"));
            var settings = (IDictionary) Activator.CreateInstance(type);
            foreach (var entry in entries) settings.Add(entry.Name, entry.Setting);
            return settings;
        }

        private static object ParseJson(string json)
        {
            var jsonType = Adapter.GetMethod("Apply").GetParameters()[0].ParameterType;
            return jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
        }

        private static object Invoke(string method, object values, IDictionary settings) =>
            Adapter.GetMethod(method).Invoke(null, new object[] { values, settings });

        [TestCase("{\"VSync\":\"false\"}")]
        [TestCase("{\"VSync\":0}")]
        [TestCase("{\"VSync\":null}")]
        [TestCase("{\"FpsCap\":60.0}")]
        [TestCase("{\"FpsCap\":\"60\"}")]
        [TestCase("{\"FpsCap\":-1}")]
        [TestCase("{\"FpsCap\":241}")]
        [TestCase("{\"FpsCap\":999999999999999999999999999999}")]
        [TestCase("{\"SongBackgroundOpacity\":1.1}")]
        [TestCase("{\"SongBackgroundOpacity\":\"0.5\"}")]
        [TestCase("{\"SongBackgroundOpacity\":NaN}")]
        [TestCase("{\"SongBackgroundOpacity\":Infinity}")]
        [TestCase("{\"SongBackgroundOpacity\":1e100}")]
        [TestCase("{\"vsync\":false}")]
        [TestCase("{\"OutputDevice\":\"Default\"}")]
        public void RejectsInvalidValuesWithoutCoercion(string json)
        {
            var settings = Settings(("VSync", Toggle(true)), ("FpsCap", Integer(60, 0, 240)),
                ("SongBackgroundOpacity", Slider(1, 0, 1)));
            var error = Assert.Throws<TargetInvocationException>(() => Invoke("Validate", ParseJson(json), settings));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        [Test]
        public void InvalidAggregateDoesNotChangeEarlierSettingsOrInvokeCallbacks()
        {
            var callbacks = 0;
            var toggle = Toggle(true, _ => callbacks++);
            var settings = Settings(("VSync", toggle), ("FpsCap", Integer(60, 0, 240, _ => callbacks++)));
            Assert.Throws<TargetInvocationException>(() => Invoke("ApplyTo",
                ParseJson("{\"VSync\":false,\"FpsCap\":-1}"), settings));
            Assert.That(toggle.GetType().GetProperty("Value").GetValue(toggle), Is.True);
            Assert.That(callbacks, Is.Zero);
        }

        [Test]
        public void ValidApplicationUsesNormalSettersAndInvokesEachCallbackExactlyOnce()
        {
            var callbacks = 0;
            var toggle = Toggle(true, _ => callbacks++);
            var cap = Integer(60, 0, 240, _ => callbacks++);
            var settings = Settings(("VSync", toggle), ("FpsCap", cap),
                ("SongBackgroundOpacity", Slider(1, 0, 1)));
            // A UI-disabled preference must still retain the fixture's requested value.
            toggle.GetType().GetProperty("EditableWhen").SetValue(toggle, (Func<bool>) (() => false));
            var values = ParseJson("{\"VSync\":false,\"FpsCap\":120,\"SongBackgroundOpacity\":0.5}");
            Invoke("Validate", values, settings);
            Assert.That(callbacks, Is.Zero);
            Invoke("ApplyTo", values, settings);
            Assert.That(toggle.GetType().GetProperty("Value").GetValue(toggle), Is.False);
            Assert.That(cap.GetType().GetProperty("Value").GetValue(cap), Is.EqualTo(120));
            Assert.That(callbacks, Is.EqualTo(2));
        }

        [TestCase("\"Friday\"")]
        [TestCase("\"UnknownOption\"")]
        [TestCase("\"Monday\"")]
        [TestCase("\"Sunday, Tuesday\"")]
        [TestCase("\"0\"")]
        [TestCase("0")]
        [TestCase("\"sunday\"")]
        public void DropdownRejectsUndefinedOrUnavailableOptions(string jsonValue)
        {
            var dropdown = Dropdown();
            var error = Assert.Throws<TargetInvocationException>(() => Invoke("Validate",
                ParseJson("{\"VenueAntiAliasing\":" + jsonValue + "}"), Settings(("VenueAntiAliasing", dropdown))));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        [Test]
        public void AvailableEnumOptionIsAppliedAsItsTypedValue()
        {
            var dropdown = Dropdown();
            Invoke("ApplyTo", ParseJson("{\"VenueAntiAliasing\":\"Tuesday\"}"), Settings(("VenueAntiAliasing", dropdown)));
            Assert.That(dropdown.GetType().GetProperty("Value").GetValue(dropdown), Is.EqualTo(DayOfWeek.Tuesday));
        }

        [Test]
        public void WhitelistCannotBeExpandedByTheAvailableSettingsDictionary()
        {
            Assert.Throws<TargetInvocationException>(() => Invoke("Validate", ParseJson("{\"Offline\":true}"),
                Settings(("Offline", Toggle(false)))));
        }

        [Test]
        public void MissingWhitelistedSettingFailsClearly()
        {
            var error = Assert.Throws<TargetInvocationException>(() => Invoke("Validate",
                ParseJson("{\"FrameGraph\":true}"), Settings()));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        private static object Dropdown()
        {
            var type = RuntimeType("YARG.Settings.Types.DropdownSetting`1").MakeGenericType(typeof(DayOfWeek));
            var dropdown = Activator.CreateInstance(type, DayOfWeek.Sunday, null, true);
            type.GetMethod("Add").Invoke(dropdown, new object[] { DayOfWeek.Sunday });
            type.GetMethod("Add").Invoke(dropdown, new object[] { DayOfWeek.Tuesday });
            return dropdown;
        }
    }
}
