using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YARG.Tests.PlayMode
{
    public sealed class AntialiasingRenderPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly List<UnityEngine.Object> _owned = new();
        private object _savedSettings;
        private object _settings;
        private Component _graphics;
        private bool _ownsGraphics;
        private bool _ownsAudio;
        private int _savedFps;
        private int _savedVsync;

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).First(t => t != null);
        private static object Call(object target, string name, params object[] args) => target.GetType()
            .GetMethod(name, INSTANCE).Invoke(target, args);
        private T Own<T>(T obj) where T : UnityEngine.Object { _owned.Add(obj); return obj; }
        private object Choice(string name) => Enum.Parse(Production("YARG.VenueAntiAliasingMethod"), name);
        private void Set(string name, object value)
        {
            var setting = _settings.GetType().GetProperty(name).GetValue(_settings);
            setting.GetType().GetMethod("SetValueWithoutNotify").Invoke(setting, new[] { value });
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _savedFps = Application.targetFrameRate;
            _savedVsync = QualitySettings.vSyncCount;
            Production("YARG.Helpers.PathHelper").GetMethod("Init").Invoke(null, null);
            var audio = Production("YARG.Core.Audio.GlobalAudioHandler");
            if (audio.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null)
            {
                audio.GetMethod("Initialize").MakeGenericMethod(Production("YARG.Audio.BASS.BassAudioManager"))
                    .Invoke(null, null);
                _ownsAudio = true;
            }
            var manager = Production("YARG.Settings.SettingsManager");
            _savedSettings = manager.GetProperty("Settings").GetValue(null);
            _settings = Activator.CreateInstance(manager.GetNestedType("SettingContainer"));
            manager.GetProperty("Settings").GetSetMethod(true).Invoke(null, new[] { _settings });
            Set("LowQuality", false);
            Set("BackgroundFpsCap", 0);
            Set("VSync", false);
            Set("FpsCap", 120);
            Set("VenueFpsCap", 120);
            Set("VenuePostProcessing", true);

            var graphicsType = Production("YARG.GraphicsManager");
            _graphics = (Component) graphicsType.GetProperty("Instance", BindingFlags.Public |
                BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            if (_graphics == null)
            {
                var host = new GameObject("AA render test graphics");
                host.SetActive(false);
                _graphics = host.AddComponent(graphicsType);
                var profileType = Production("UnityEngine.Rendering.VolumeProfile");
                var profile = AssetDatabase.LoadAssetAtPath("Assets/Settings/PostProcessProfile.asset", profileType);
                graphicsType.GetField("postProcessingProfile", INSTANCE).SetValue(_graphics, profile);
                host.SetActive(true);
                _ownsGraphics = true;
            }
            Call(_graphics, "ApplyGraphicsSettings");
            // An empty test scene has not rendered a camera yet. Let URP initialize its
            // volume stack before the venue's first Update reads custom volume components.
            var warmupTarget = Own(new RenderTexture(32, 32, 16));
            warmupTarget.Create();
            var warmup = CreateCamera("AA pipeline warmup");
            warmup.targetTexture = warmupTarget;
            warmup.gameObject.SetActive(true);
            yield return WaitForRenders(warmup);
            warmup.gameObject.SetActive(false);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                if (_owned[i] != null) UnityEngine.Object.DestroyImmediate(_owned[i]);
            }
            _owned.Clear();
            Production("YARG.Settings.SettingsManager").GetProperty("Settings").GetSetMethod(true)
                .Invoke(null, new[] { _savedSettings });
            if (_ownsGraphics && _graphics != null) UnityEngine.Object.DestroyImmediate(_graphics.gameObject);
            else if (_graphics != null && _savedSettings != null) Call(_graphics, "ApplyGraphicsSettings");
            _ownsGraphics = false;
            if (_ownsAudio) Production("YARG.Core.Audio.GlobalAudioHandler").GetMethod("Close").Invoke(null, null);
            _ownsAudio = false;
            Application.targetFrameRate = _savedFps;
            QualitySettings.vSyncCount = _savedVsync;
            yield return null;
        }

        private GameObject CreateVisibleGeometry()
        {
            var quad = Own(GameObject.CreatePrimitive(PrimitiveType.Quad));
            quad.name = "AA render test surface";
            quad.layer = 30;
            quad.transform.localScale = Vector3.one * 20;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var material = Own(new Material(shader));
            material.SetColor("_BaseColor", Color.red);
            quad.GetComponent<Renderer>().sharedMaterial = material;
            return quad;
        }

        private Camera CreateCamera(string name)
        {
            var host = Own(new GameObject(name));
            host.SetActive(false);
            var camera = host.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 30;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100;
            var data = host.AddComponent(Production("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData"));
            data.GetType().GetProperty("renderPostProcessing").SetValue(data, true);
            return camera;
        }

        private static IEnumerator WaitForRenders(Camera camera)
        {
            int renders = 0;
            void Rendered(ScriptableRenderContext context, Camera rendered)
            {
                if (rendered == camera) renders++;
            }
            RenderPipelineManager.endCameraRendering += Rendered;
            try
            {
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (renders < 2 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.That(renders, Is.GreaterThanOrEqualTo(2), "Camera must actually render through URP.");
            }
            finally { RenderPipelineManager.endCameraRendering -= Rendered; }
        }

        private static void AssertVisible(RenderTexture target, string artifact)
        {
            Assert.That(target, Is.Not.Null);
            Assert.That(target.IsCreated(), Is.True);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                Assert.That(pixels.GetPixels32().Any(p => p.r > 30 && p.r > p.g * 2), Is.True,
                    "The rendered surface must survive the antialiasing and output resolve.");
                File.WriteAllBytes("/tmp/" + artifact + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixels);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HighwayPreviewRendersSupportedAntialiasingWithoutChangingBorrowedTarget()
        {
            CreateVisibleGeometry();
            var target = Own(new RenderTexture(128, 128, 32, RenderTextureFormat.DefaultHDR));
            target.Create();
            var camera = CreateCamera("AA highway render camera");
            camera.orthographic = true;
            camera.orthographicSize = 25;
            var track = CreateCamera("AA track projection");
            track.enabled = false;
            var renderer = camera.gameObject.AddComponent(Production("YARG.Gameplay.Visuals.HighwayCameraRendering"));
            Call(renderer, "SetPreviewRenderTarget", target);
            Call(renderer, "AddPlayerParams", Vector3.zero, track, 0f, -35f, 5f, 0f, false);
            camera.gameObject.SetActive(true);
            foreach (string name in new[] { "SMAA", "MSAA2x", "MSAA", "MSAA8x" })
            {
                var method = Choice(name);
                bool supported = (bool) Production("YARG.GraphicsManager").GetMethod("IsAntialiasingSupported")
                    .Invoke(null, new[] { method, (object) false });
                if (!supported) continue;
                Set("HighwayAntiAliasing", method);
                Call(_graphics, "ApplyGraphicsSettings");
                var actual = camera.targetTexture;
                int expected = (int) Production("YARG.GraphicsManager").GetMethod("GetRequestedMsaaSamples")
                    .Invoke(null, new[] { method });
                Assert.That(actual.antiAliasing, Is.EqualTo(expected));
                Call(_graphics, "ApplyGraphicsSettings");
                Assert.That(camera.targetTexture, Is.SameAs(actual), "Unchanged AA must not reallocate the output.");
                yield return WaitForRenders(camera);
                AssertVisible(target, "yarg-aa-highway-" + name);
                Assert.That(target.antiAliasing, Is.EqualTo(1));
                Assert.That(target.IsCreated(), Is.True, "Borrowed target must stay alive.");
            }
            Call(renderer, "SetPreviewRenderTarget", (object) null);
            Set("HighwayAntiAliasing", Choice("SMAA"));
            Call(_graphics, "ApplyGraphicsSettings");
            yield return WaitForRenders(camera);
            AssertVisible(camera.targetTexture, "yarg-aa-highway-owned-output");
            camera.gameObject.SetActive(false);
            Assert.That(target.IsCreated(), Is.True, "Disabling the renderer must not destroy its borrowed output.");
        }

        [UnityTest]
        public IEnumerator VenueRendersSupportedAntialiasingThroughItsProductionOutput()
        {
            CreateVisibleGeometry();
            var output = Own(new GameObject("Venue Output", typeof(RectTransform), typeof(RawImage)))
                .GetComponent<RawImage>();
            var camera = CreateCamera("AA venue camera");
            camera.gameObject.AddComponent(Production("YARG.Gameplay.VenueCameraRenderer"));
            camera.gameObject.SetActive(true);
            foreach (string name in new[] { "SMAA", "MSAA2x", "MSAA", "MSAA8x", "TAA" })
            {
                var method = Choice(name);
                bool supported = (bool) Production("YARG.GraphicsManager").GetMethod("IsAntialiasingSupported")
                    .Invoke(null, new[] { method, (object) true });
                if (!supported) continue;
                Set("VenueAntiAliasing", method);
                Call(_graphics, "ApplyGraphicsSettings");
                yield return WaitForRenders(camera);
                AssertVisible((RenderTexture) output.texture, "yarg-aa-venue-" + name);
            }
        }
    }
}
