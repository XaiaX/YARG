using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class AntialiasingSettingsTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).First(t => t != null);

        private static object Method(string name, params object[] args)
        {
            var method = Production("YARG.GraphicsManager").GetMethod(name,
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, $"Missing antialiasing behavior: {name}");
            return method.Invoke(null, args);
        }

        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(2, 4)]
        [TestCase(3, 1)]
        [TestCase(4, 1)]
        [TestCase(5, 2)]
        [TestCase(6, 8)]
        public void LegacyAndExpandedMethodsRequestTheExpectedSamples(int method, int samples)
        {
            var value = Enum.ToObject(Production("YARG.VenueAntiAliasingMethod"), method);
            Assert.That(Method("GetRequestedMsaaSamples", value), Is.EqualTo(samples));
        }

        [Test]
        public void DescriptorAppliesSupportedSamplesWithoutMutatingTheSource()
        {
            var descriptor = new RenderTextureDescriptor(320, 240, RenderTextureFormat.DefaultHDR, 32);
            var method = Enum.ToObject(Production("YARG.VenueAntiAliasingMethod"), 6);
            var result = (RenderTextureDescriptor) Method("ConfigureAntialiasingDescriptor", descriptor, method);
            var requested = descriptor;
            requested.msaaSamples = 8;
            int supported = SystemInfo.supportsStoreAndResolveAction
                ? SystemInfo.GetRenderTextureSupportedMSAASampleCount(requested) : 1;
            Assert.That(result.msaaSamples, Is.EqualTo(supported));
            Assert.That(descriptor.msaaSamples, Is.EqualTo(1));
            Assert.That(result.graphicsFormat, Is.EqualTo(descriptor.graphicsFormat));
            Assert.That(result.width, Is.EqualTo(320));
            Assert.That(result.height, Is.EqualTo(240));
        }

        [Test]
        public void TemporalAntialiasingNeverAllocatesAMultisampledTarget()
        {
            var descriptor = new RenderTextureDescriptor(320, 240, RenderTextureFormat.DefaultHDR, 32)
            {
                msaaSamples = 4
            };
            var taa = Enum.ToObject(Production("YARG.VenueAntiAliasingMethod"), 3);
            var result = (RenderTextureDescriptor) Method("ConfigureAntialiasingDescriptor", descriptor, taa);
            Assert.That(result.msaaSamples, Is.EqualTo(1));
        }

        [TestCase(2, true, true, "None", true)]
        [TestCase(4, true, true, "SubpixelMorphologicalAntiAliasing", false)]
        [TestCase(3, false, true, "SubpixelMorphologicalAntiAliasing", false)]
        [TestCase(3, true, false, "None", false)]
        public void CameraUsesTheRequestedMethodWithoutHiddenCombinations(int method, bool temporalAllowed,
            bool postProcessing, string expectedMode, bool expectedMsaa)
        {
            var host = new GameObject("AA policy test");
            try
            {
                var camera = host.AddComponent<Camera>();
                var dataType = Production("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
                var data = host.AddComponent(dataType);
                dataType.GetProperty("renderPostProcessing").SetValue(data, postProcessing);
                var methodValue = Enum.ToObject(Production("YARG.VenueAntiAliasingMethod"), method);
                var quality = Enum.Parse(Production("UnityEngine.Rendering.Universal.AntialiasingQuality"), "High");
                var temporalQuality = Enum.Parse(Production("UnityEngine.Rendering.Universal.TemporalAAQuality"), "VeryHigh");
                Method("ConfigureCameraAntialiasing", camera, methodValue, quality, temporalQuality, temporalAllowed);
                Assert.That(dataType.GetProperty("antialiasing").GetValue(data).ToString(), Is.EqualTo(expectedMode));
                Assert.That(dataType.GetProperty("antialiasingQuality").GetValue(data).ToString(), Is.EqualTo("High"));
                Assert.That(camera.allowMSAA, Is.EqualTo(expectedMsaa));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void UnsupportedSavedMsaaRemainsSavedButDisplaysASupportedChoice()
        {
            var enumType = Production("YARG.VenueAntiAliasingMethod");
            var settingType = Production("YARG.Settings.Types.AntialiasingSetting");
            var choice = Enum.ToObject(enumType, 6);
            var setting = Activator.CreateInstance(settingType, new object[] { choice, false, null });
            var values = (System.Collections.IList) settingType.GetProperty("PossibleValues").GetValue(setting);
            int index = (int) settingType.GetProperty("CurrentIndex").GetValue(setting);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That(settingType.GetProperty("Value").GetValue(setting), Is.EqualTo(choice));
            Assert.That(Method("IsAntialiasingSupported", values[index], false), Is.True);
            foreach (var value in values)
            {
                Assert.That(Method("IsAntialiasingSupported", value, false), Is.True);
                Assert.That(Convert.ToInt32(value), Is.Not.EqualTo(3), "Highways must not advertise TAA.");
            }
        }

        [Test]
        public void UnknownSavedMethodFallsBackToNoAntialiasing()
        {
            var value = Enum.ToObject(Production("YARG.VenueAntiAliasingMethod"), 999);
            Assert.That(Method("GetRequestedMsaaSamples", value), Is.EqualTo(1));
        }
    }
}
