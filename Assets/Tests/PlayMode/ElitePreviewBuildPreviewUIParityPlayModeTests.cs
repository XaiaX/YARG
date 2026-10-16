using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YARG.Core.Game;

namespace YARG.Tests.PlayMode
{
    /// <summary>Rendered display comparison between Configure and the production preset preview UI path.</summary>
    public sealed class ElitePreviewBuildPreviewUIParityPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly System.Collections.Generic.List<UnityEngine.Object> _owned = new();
        private GameObject _host;
        private Component _menu;
        private object _originalPreviewTexture;
        private object _originalSettings;
        private bool _originalSettingsInitialized;
        private bool _capturedSettings;
        private bool _ownsAudio;
        private Component _themeManager;
        private Type _localizationType;
        private System.Collections.Generic.IDictionary<string, string> _localizationMap;
        private System.Collections.Generic.KeyValuePair<string, string>[] _previousLocalizations;
        private string _previousCulture;

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static object Field(object target, string name) => target.GetType()
            .GetField(name, INSTANCE).GetValue(target);

        private static void Call(object target, string name, params object[] args) => target.GetType()
            .GetMethods(INSTANCE).Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(target, args);

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            _owned.Add(value);
            return value;
        }

        [UnitySetUp]
        public IEnumerator SetUpProductionTheme()
        {
            var settingsManager = Production("YARG.Settings.SettingsManager");
            var settingContainer = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
            var initialized = settingContainer.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static);
            _originalSettings = settingsManager.GetProperty("Settings").GetValue(null);
            _originalSettingsInitialized = (bool)initialized.GetValue(null);
            _capturedSettings = true;
            var audioHandler = Production("YARG.Core.Audio.GlobalAudioHandler");
            if (audioHandler.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null)
            {
                Production("YARG.Helpers.PathHelper").GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                audioHandler.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)
                    .MakeGenericMethod(Production("YARG.Audio.BASS.BassAudioManager")).Invoke(null, null);
                _ownsAudio = true;
            }
            if (_originalSettings == null)
            {
                settingsManager.GetProperty("Settings").GetSetMethod(true)
                    .Invoke(null, new[] { Activator.CreateInstance(settingContainer) });
                initialized.SetValue(null, true);
            }

            var themeType = Production("YARG.Themes.ThemeManager");
            const BindingFlags STATIC = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            if (themeType.GetProperty("Instance", STATIC).GetValue(null) == null)
            {
                _themeManager = Own(new GameObject("Preview parity production ThemeManager").AddComponent(themeType));
                yield return null;
            }
            Assert.That(themeType.GetProperty("Instance", STATIC).GetValue(null), Is.Not.Null);
            LoadProductionEnglishLocalization();
        }

        private void LoadProductionEnglishLocalization()
        {
            _localizationType = Production("YARG.Localization.LocalizationManager");
            _localizationMap = (System.Collections.Generic.IDictionary<string, string>)_localizationType
                .GetField("_localizationMap", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            _previousLocalizations = _localizationMap.ToArray();
            _previousCulture = (string)_localizationType.GetProperty("CultureCode").GetValue(null);
            var load = _localizationType.GetMethod("TryParseAndLoadLanguage", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That((bool)load.Invoke(null, new object[] { "en-US" }), Is.True);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_originalPreviewTexture != null)
                Production("YARG.Helpers.CameraPreviewTexture").GetProperty("PreviewTexture")
                    .GetSetMethod(true).Invoke(null, new[] { _originalPreviewTexture });
            foreach (var item in _owned)
                if (item != null) UnityEngine.Object.Destroy(item);
            _owned.Clear();
            yield return null;
            if (_capturedSettings)
            {
                var settingsManager = Production("YARG.Settings.SettingsManager");
                var settingContainer = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
                settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null, new[] { _originalSettings });
                settingContainer.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static)
                    .SetValue(null, _originalSettingsInitialized);
                _capturedSettings = false;
            }
            if (_ownsAudio)
            {
                Production("YARG.Core.Audio.GlobalAudioHandler").GetMethod("Close", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                _ownsAudio = false;
            }
            if (_localizationMap != null)
            {
                _localizationMap.Clear();
                foreach (var entry in _previousLocalizations) _localizationMap.Add(entry.Key, entry.Value);
                _localizationType.GetProperty("CultureCode").GetSetMethod(true)
                    .Invoke(null, new object[] { _previousCulture });
                _localizationMap = null;
            }
        }

        [UnityTest]
        public IEnumerator SameEliteRenderSourceUsesConfigureAndActualBuildPreviewUIWithVerticalScalePreserved()
        {
#if !UNITY_EDITOR
            Assert.Ignore("The production menu and preview UI prefabs require the Editor asset database.");
            yield break;
#else
            var menuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(menuPrefab, Is.Not.Null);
            _host = Own(new GameObject("Configure and preset preview parity Canvas"));
            var cameraObject = Own(new GameObject("Preview parity output camera"));
            var outputCamera = cameraObject.AddComponent<Camera>();
            outputCamera.clearFlags = CameraClearFlags.SolidColor;
            outputCamera.backgroundColor = Color.black;
            outputCamera.orthographic = true;
            outputCamera.targetTexture = Own(new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB) { name = "Preview parity labeled canvas" });
            outputCamera.targetTexture.Create();
            outputCamera.aspect = 1280f / 720f;
            outputCamera.pixelRect = new Rect(0, 0, 1280, 720);
            var canvas = _host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = outputCamera;
            canvas.planeDistance = 1f;
            var scaler = _host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var manager = _host.AddComponent(Production("YARG.Menu.MenuManager"));
            ((Behaviour)manager).enabled = false;
            var menuEnum = manager.GetType().GetNestedType("Menu");
            var menuObject = (Component)manager.GetType().GetMethod("ShowMenu", new[] { menuEnum })
                .Invoke(manager, new[] { Enum.Parse(menuEnum, "EliteDrumsLayout") });
            _menu = menuObject.GetComponent(Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu"));
            Assert.That(_menu, Is.Not.Null, "The shown menu must contain the actual Elite layout component.");
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Rendered parity layout");
            var profile = new ColorProfile("Rendered parity colors");
            for (int slot = 1; slot <= ColorProfile.EliteDrumsColors.SAVED_SLOT_COUNT; slot++)
            {
                profile.EliteDrums.GetType().GetField($"Fret{slot}")
                    .SetValue(profile.EliteDrums, System.Drawing.Color.Lime);
                profile.EliteDrums.GetType().GetField($"Fret{slot}Inner")
                    .SetValue(profile.EliteDrums, System.Drawing.Color.Lime);
            }
            Call(_menu, "Initialize", layout, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }), profile);
            yield return null;
            yield return null;

            var configureImage = (RawImage)Field(_menu, "_previewImage");
            var configureTexture = (RenderTexture)configureImage.texture;
            var configureWorld = (Transform)Field(_menu, "_previewWorld");
            var previewPlayer = configureWorld.GetComponentInChildren(Production("YARG.Settings.Preview.FakeTrackPlayer"), true);
            Assert.That(previewPlayer, Is.Not.Null, "The actual preview builder must create the production player.");
            Assert.That(previewPlayer.GetType().GetProperty("ColorProfileOverride").GetValue(previewPlayer),
                Is.SameAs(profile), "The real player must use the explicitly controlled marker profile.");
            var fretArray = (Component)Field(previewPlayer, "_fretArray");
            var frets = fretArray.GetComponentsInChildren<Component>(true)
                .Where(component => component.GetType().Name == "Fret").ToArray();
            Assert.That(frets, Is.Not.Empty, "The actual theme must create receptor geometry.");
            foreach (var fret in frets)
            {
                var materials = ((IEnumerable)Field(fret, "_topMaterials")).Cast<Material>().ToArray();
                Assert.That(materials, Is.Not.Empty, "Every receptor must expose its actual theme materials.");
                foreach (var material in materials)
                    Assert.That((Color32)material.color, Is.EqualTo(new Color32(0, 255, 0, 255)),
                        "The live receptor materials must use the controlled lime marker color.");
            }
            ((Behaviour)previewPlayer).enabled = false;
            Call(previewPlayer, "Advance", 0.25d);
            var configureCamera = configureWorld.GetComponentInChildren<Camera>(true);
            Assert.That(configureCamera, Is.Not.Null);
            Assert.That(configureTexture.width, Is.EqualTo(outputCamera.pixelWidth));
            Assert.That(configureTexture.height, Is.EqualTo(outputCamera.pixelHeight));
            Assert.That(configureCamera.targetTexture, Is.SameAs(configureTexture));
            Assert.That(configureImage.uvRect.width, Is.GreaterThan(0.1f));
            Assert.That(configureImage.uvRect.height, Is.GreaterThan(0.1f));

            // The production builder creates its real TrackPreviewUI prefab. Point that image at
            // the Configure world's already-rendering target so both views show the same exact frame.
            var previewTextureType = Production("YARG.Helpers.CameraPreviewTexture");
            var previewTextureProperty = previewTextureType.GetProperty("PreviewTexture");
            _originalPreviewTexture = previewTextureProperty.GetValue(null);
            previewTextureProperty.GetSetMethod(true).Invoke(null, new object[] { configureTexture });
            var settingsObject = Own(new GameObject("SettingsMenu singleton for BuildPreviewUI"));
            var settingsMenuType = Production("YARG.Menu.Settings.SettingsMenu");
            var settingsMenu = settingsObject.AddComponent(settingsMenuType);
            ((Behaviour)settingsMenu).enabled = false;
            settingsObject.SetActive(true);
            Assert.That(settingsMenuType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .GetValue(null), Is.SameAs(settingsMenu));

            var presetContainer = Own(new GameObject("Preset preview actual BuildPreviewUI container", typeof(RectTransform)));
            var presetRect = (RectTransform)presetContainer.transform;
            presetRect.SetParent(_host.transform, false);
            presetRect.anchorMin = new Vector2(0.04f, 0.08f);
            presetRect.anchorMax = new Vector2(0.48f, 0.92f);
            presetRect.offsetMin = presetRect.offsetMax = Vector2.zero;
            presetContainer.transform.localScale = Vector3.one;
            var builderType = Production("YARG.Settings.Metadata.TrackPreviewBuilder");
            var builder = Activator.CreateInstance(builderType, new object[] { false, false, false, null });
            builderType.GetProperty("StartingGameMode").SetValue(builder, YARG.Core.GameMode.EliteDrums);
            builderType.GetProperty("EliteLayoutOverride").SetValue(builder, layout.Copy());
            builderType.GetProperty("ColorProfileOverride").SetValue(builder, profile);
            foreach (var property in new[] { "ForceStarPowerNotes", "ForceStarPower", "ForceGroove", "LeftyFlip" })
                builderType.GetProperty(property).SetValue(builder, false);
            yield return BuildPresetPreviewUI(builder, presetContainer.transform);
            Canvas.ForceUpdateCanvases();
            yield return null;

            var presetImage = presetContainer.GetComponentInChildren<RawImage>(true);
            Assert.That(presetImage, Is.Not.Null, "BuildPreviewUI must instantiate the real TrackPreviewUI RawImage.");
            Assert.That(presetImage.texture, Is.SameAs(configureTexture),
                "The production preset builder and Configure image must use the same captured Elite frame.");
            yield return null;
            yield return null;
            var outputTarget = (RenderTexture)outputCamera.targetTexture;
            Assert.That(configureImage.texture, Is.SameAs(configureTexture),
                "The shared source must remain alive and bound throughout both preview builds.");
            var sourcePixels = ReadPixels(configureTexture);
            var sourceSnapshot = new Texture2D(configureTexture.width, configureTexture.height, TextureFormat.RGBA32, false, true);
            sourceSnapshot.SetPixels32(sourcePixels);
            sourceSnapshot.Apply(false, false);
            File.WriteAllBytes("/tmp/yarg-elite-parity-source.png", sourceSnapshot.EncodeToPNG());
            UnityEngine.Object.Destroy(sourceSnapshot);
            Debug.Log($"PARITY SOURCE target={configureTexture.GetInstanceID()} format={configureTexture.format} " +
                $"size={configureTexture.width}x{configureTexture.height} " +
                $"opaque={sourcePixels.Count(pixel => pixel.a > 200)} " +
                $"green={sourcePixels.Count(pixel => pixel.g > 80 && pixel.g > pixel.r * 1.6f && pixel.g > pixel.b * 1.4f)} " +
                "snapshot=/tmp/yarg-elite-parity-source.png");
            float authoredPresetScale = VerticalPixelsPerSourceUnit(presetImage, outputCamera);
            Assert.That(VerticalPixelsPerSourceUnit(configureImage, outputCamera),
                Is.EqualTo(authoredPresetScale).Within(authoredPresetScale * 0.03f),
                "The original production images must agree in vertical scale before capture relocation.");
            // Keep the production UVs, but remove menu graphics and authored offscreen anchors
            // from the capture. These are the real instantiated images, not replacement images.
            // The menu owns the live world and texture: disabling it calls ClearPreview.
            // Hide only unrelated graphics, retaining the original Canvas, camera and owner.
            foreach (var graphic in _host.GetComponentsInChildren<Graphic>(true))
                if (graphic != configureImage && graphic != presetImage)
                    graphic.enabled = false;
            configureImage.transform.SetParent(_host.transform, false);
            presetImage.transform.SetParent(_host.transform, false);
            Canvas.ForceUpdateCanvases();
            PlaceComparisonImage(presetImage, outputCamera, new Vector2(320, 360), authoredPresetScale, configureTexture);
            PlaceComparisonImage(configureImage, outputCamera, new Vector2(960, 360), authoredPresetScale, configureTexture);
            AddCaptureLabel(presetImage.rectTransform, "PRESET / REAL BuildPreviewUI");
            AddCaptureLabel(configureImage.rectTransform, "CONFIGURE / same Elite frame");
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
            Assert.That(configureImage.texture, Is.SameAs(configureTexture),
                "The live menu must retain its owned target through the comparison capture.");
            Assert.That(configureTexture.IsCreated(), Is.True);
            Assert.That(presetImage.texture, Is.SameAs(configureTexture));
            Color32[] displayedPixels = null;
            int configureRed = 0;
            int presetRed = 0;
            for (int step = 0; step < 100; step++)
            {
                displayedPixels = ReadPixels(outputTarget);
                configureRed = CountRenderedNotes(configureImage, outputCamera, displayedPixels, outputTarget.width, outputTarget.height);
                presetRed = CountRenderedNotes(presetImage, outputCamera, displayedPixels, outputTarget.width, outputTarget.height);
                if (configureRed > 4 && presetRed > 4) break;
                Call(previewPlayer, "Advance", 0.05d);
                yield return null;
                yield return null;
                yield return null;
            }
            // Preserve the exact populated source while the output camera completes its capture.
            // Child FakeNote.Update reads PreviewTime independently of the disabled player Update.
            foreach (var behaviour in configureWorld.GetComponentsInChildren<Behaviour>(true))
                if (behaviour.GetType().Name.Contains("FakeNote")) behaviour.enabled = false;
            foreach (var camera in configureWorld.GetComponentsInChildren<Camera>(true))
                camera.enabled = false;
            yield return null;
            yield return null;
            Assert.That(configureTexture.IsCreated(), Is.True, "Freezing render cameras must preserve the owned source allocation.");
            Assert.That(configureImage.texture, Is.SameAs(configureTexture));
            Assert.That(presetImage.texture, Is.SameAs(configureTexture));
            displayedPixels = ReadPixels(outputTarget);
            configureRed = CountRenderedNotes(configureImage, outputCamera, displayedPixels, outputTarget.width, outputTarget.height);
            presetRed = CountRenderedNotes(presetImage, outputCamera, displayedPixels, outputTarget.width, outputTarget.height);
            sourcePixels = ReadPixels(configureTexture);
            var matchedSource = new Texture2D(configureTexture.width, configureTexture.height, TextureFormat.RGBA32, false, true);
            matchedSource.SetPixels32(sourcePixels);
            matchedSource.Apply(false, false);
            File.WriteAllBytes("/tmp/yarg-elite-parity-source.png", matchedSource.EncodeToPNG());
            UnityEngine.Object.Destroy(matchedSource);
            WriteLabeledCapture(outputTarget, "/tmp/yarg-elite-buildpreviewui-configure-parity.png");
            Debug.Log($"PARITY MATCHED FRAME time={previewPlayer.GetType().GetProperty("PreviewTime").GetValue(previewPlayer)} " +
                $"configureRed={configureRed} presetRed={presetRed} configureUV={configureImage.uvRect} presetUV={presetImage.uvRect}");
            Assert.That(configureRed, Is.GreaterThan(4), "The final Configure capture must contain real rendered notes.");
            Assert.That(presetRed, Is.GreaterThan(4), "The final BuildPreviewUI capture must contain the same rendered notes.");
            var configureMarkers = MeasureRenderedMarkers(configureImage, outputCamera, displayedPixels, outputTarget.width, outputTarget.height);
            var presetMarkers = MeasureRenderedMarkers(presetImage, outputCamera, displayedPixels, outputTarget.width, outputTarget.height);
            AssertRenderedRegionsEqual(configureImage, presetImage, outputCamera, displayedPixels, outputTarget.width, outputTarget.height);
            Assert.That(sourcePixels.Count(pixel => pixel.r > 80 && pixel.r > pixel.g * 1.5f && pixel.r > pixel.b * 1.5f),
                Is.GreaterThan(4), "The shared source must contain actual colored note geometry, not just receptors or multiplier text.");
            var configurePixelsPerSourceUnit = VerticalPixelsPerSourceUnit(configureImage, outputCamera);
            var presetPixelsPerSourceUnit = VerticalPixelsPerSourceUnit(presetImage, outputCamera);
            Assert.That(configurePixelsPerSourceUnit,
                Is.EqualTo(presetPixelsPerSourceUnit).Within(presetPixelsPerSourceUnit * 0.03f),
                "Different authored pane heights must still map the shared source geometry to the same vertical scale.");
            Assert.That(configureImage.uvRect.width, Is.LessThan(presetImage.uvRect.width),
                "Configure's narrower pane may trim horizontal empty space relative to the preset editor.");
            Assert.That(configureMarkers.ReceptorBelowPixels,
                Is.EqualTo(presetMarkers.ReceptorBelowPixels).Within(2f),
                "The receptor-to-track-below distance must match for one shared rendered frame.");
            Assert.That(configureMarkers.DetailToReceptorPixels,
                Is.EqualTo(presetMarkers.DetailToReceptorPixels).Within(2f),
                "The actual rendered below-receptor detail distance must match in both panes.");

            Canvas.ForceUpdateCanvases();
            yield return null;
            WriteLabeledCapture(outputTarget, "/tmp/yarg-elite-buildpreviewui-configure-parity.png");
            Debug.Log($"ACTUAL PREVIEW PARITY configureUV={configureImage.uvRect} presetBuildPreviewUIUV={presetImage.uvRect} " +
                $"configureTarget={configureTexture.width}x{configureTexture.height} output={outputTarget.width}x{outputTarget.height} " +
                $"configurePane={configureImage.rectTransform.rect} presetPane={presetImage.rectTransform.rect} " +
                $"receptorBelowPx={configureMarkers.ReceptorBelowPixels}/{presetMarkers.ReceptorBelowPixels} " +
                $"belowReceptorDetailPx={configureMarkers.DetailToReceptorPixels}/{presetMarkers.DetailToReceptorPixels} " +
                "capture=/tmp/yarg-elite-buildpreviewui-configure-parity.png");
#endif
        }

        private static IEnumerator BuildPresetPreviewUI(object builder, Transform container)
        {
            var uniTask = builder.GetType().GetMethod("BuildPreviewUI", new[] { typeof(Transform) })
                .Invoke(builder, new object[] { container });
            var asTask = Production("Cysharp.Threading.Tasks.UniTaskExtensions")
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "AsTask" && !method.IsGenericMethod &&
                    method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == uniTask.GetType());
            var task = (System.Threading.Tasks.Task)asTask.Invoke(null, new[] { uniTask });
            const int MAX_BUILD_FRAMES = 120;
            for (int frame = 0; frame < MAX_BUILD_FRAMES && !task.IsCompleted; frame++)
                yield return null;
            Assert.That(task.IsCompleted, Is.True,
                $"The actual TrackPreviewBuilder.BuildPreviewUI must complete layout and image binding within {MAX_BUILD_FRAMES} player-loop frames; task status={task.Status}.");
            task.GetAwaiter().GetResult();
        }

        private static void PlaceComparisonImage(RawImage image, Camera camera, Vector2 center, float scale, RenderTexture source)
        {
            var rect = image.rectTransform;
            var layout = image.GetComponent<LayoutElement>() ?? image.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            var parent = (RectTransform)rect.parent;
            Vector2 local;
            center.y = 100f + image.uvRect.height * scale * 0.5f;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, center, camera, out local);
            rect.anchoredPosition3D = new Vector3(local.x, local.y, 0f);
            float unitsPerPixel = parent.rect.height / camera.pixelHeight;
            rect.sizeDelta = new Vector2(image.uvRect.width * source.width / source.height, image.uvRect.height) * scale * unitsPerPixel;
        }

        private static RectInt ImagePixelRect(RawImage image, Camera camera, int width, int height)
        {
            var corners = new Vector3[4];
            image.rectTransform.GetWorldCorners(corners);
            var bottom = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var top = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            Assert.That(bottom.x, Is.GreaterThanOrEqualTo(0));
            Assert.That(bottom.y, Is.GreaterThanOrEqualTo(0));
            Assert.That(top.x, Is.LessThanOrEqualTo(width));
            Assert.That(top.y, Is.LessThanOrEqualTo(height));
            return new RectInt(Mathf.RoundToInt(bottom.x), Mathf.RoundToInt(bottom.y),
                Mathf.RoundToInt(top.x - bottom.x), Mathf.RoundToInt(top.y - bottom.y));
        }

        private static int CountRenderedNotes(RawImage image, Camera camera, Color32[] pixels, int width, int height)
        {
            var rect = ImagePixelRect(image, camera, width, height);
            int receptor = -1;
            int maximumGreen = 0;
            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                int green = 0;
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    var p = pixels[y * width + x];
                    if (p.g > 80 && p.g > p.r * 1.6f && p.g > p.b * 1.4f) green++;
                }
                if (green > maximumGreen) { maximumGreen = green; receptor = y; }
            }
            if (maximumGreen <= 8) return 0;
            int red = 0;
            for (int y = receptor + 5; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                var p = pixels[y * width + x];
                if (p.r > 80 && p.r > p.g * 1.5f && p.r > p.b * 1.5f) red++;
            }
            return red;
        }

        private static (float ReceptorBelowPixels, float DetailToReceptorPixels) MeasureRenderedMarkers(
            RawImage image, Camera camera, Color32[] pixels, int width, int height)
        {
            var rect = ImagePixelRect(image, camera, width, height);
            var rows = Enumerable.Range(rect.yMin, rect.height).Select(y => new
            {
                Y = y,
                Count = Enumerable.Range(rect.xMin, rect.width).Count(x =>
                    pixels[y * width + x].g > 80 && pixels[y * width + x].g > pixels[y * width + x].r * 1.6f &&
                    pixels[y * width + x].g > pixels[y * width + x].b * 1.4f)
            }).OrderByDescending(row => row.Count).ToArray();
            Assert.That(rows[0].Count, Is.GreaterThan(8), "Each actual onscreen image must render receptor pixels.");
            int receptor = rows[0].Y;
            int redNotes = Enumerable.Range(receptor + 5, Mathf.Max(0, rect.yMax - receptor - 5)).Sum(y =>
                Enumerable.Range(rect.xMin, rect.width).Count(x =>
                {
                    var p = pixels[y * width + x];
                    return p.r > 80 && p.r > p.g * 1.5f && p.r > p.b * 1.5f;
                }));
            Assert.That(redNotes, Is.GreaterThan(4),
                "Each real rendered pane must show red note geometry above the receptors inside its crop.");
            var detail = Enumerable.Range(rect.yMin, Mathf.Max(0, receptor - rect.yMin - 4))
                .Where(y => Enumerable.Range(rect.xMin + rect.width / 3, rect.width / 3).Any(x =>
                {
                    var p = pixels[y * width + x];
                    return p.r > 160 && p.g > 160 && p.b > 160;
                })).ToArray();
            Assert.That(detail, Is.Not.Empty, "The rendered below-receptor region must contain actual multiplier detail, not only green receptors.");
            return (receptor - rect.yMin, receptor - detail.Max());
        }

        private static void AssertRenderedRegionsEqual(RawImage configure, RawImage preset, Camera camera,
            Color32[] pixels, int width, int height)
        {
            var a = ImagePixelRect(configure, camera, width, height);
            var b = ImagePixelRect(preset, camera, width, height);
            var uvA = configure.uvRect;
            var uvB = preset.uvRect;
            var shared = Rect.MinMaxRect(Mathf.Max(uvA.xMin, uvB.xMin), Mathf.Max(uvA.yMin, uvB.yMin),
                Mathf.Min(uvA.xMax, uvB.xMax), Mathf.Min(uvA.yMax, uvB.yMax));
            Assert.That(shared.width, Is.GreaterThan(0));
            Assert.That(shared.height, Is.GreaterThan(0));
            var cornersA = new Vector3[4];
            var cornersB = new Vector3[4];
            configure.rectTransform.GetWorldCorners(cornersA);
            preset.rectTransform.GetWorldCorners(cornersB);
            Vector2 bottomA = RectTransformUtility.WorldToScreenPoint(camera, cornersA[0]);
            Vector2 topA = RectTransformUtility.WorldToScreenPoint(camera, cornersA[2]);
            Vector2 bottomB = RectTransformUtility.WorldToScreenPoint(camera, cornersB[0]);
            Vector2 topB = RectTransformUtility.WorldToScreenPoint(camera, cornersB[2]);
            int commonWidth = Mathf.FloorToInt(shared.width * Mathf.Min(a.width / uvA.width, b.width / uvB.width));
            int commonHeight = Mathf.FloorToInt(shared.height * Mathf.Min(a.height / uvA.height, b.height / uvB.height));
            int mismatches = 0;
            for (int y = 0; y < commonHeight; y++)
            for (int x = 0; x < commonWidth; x++)
            {
                var uv = new Vector2(shared.xMin + (x + 0.5f) / commonWidth * shared.width,
                    shared.yMin + (y + 0.5f) / commonHeight * shared.height);
                var first = SampleRendered(pixels, width, height, bottomA + Vector2.Scale(topA - bottomA,
                    new Vector2((uv.x - uvA.x) / uvA.width, (uv.y - uvA.y) / uvA.height)));
                var second = SampleRendered(pixels, width, height, bottomB + Vector2.Scale(topB - bottomB,
                    new Vector2((uv.x - uvB.x) / uvB.width, (uv.y - uvB.y) / uvB.height)));
                if (Mathf.Abs(first.r - second.r) + Mathf.Abs(first.g - second.g) + Mathf.Abs(first.b - second.b) > 30f / 255f)
                    mismatches++;
            }
            Debug.Log($"PARITY PIXELS sharedUV={shared} samples={commonWidth}x{commonHeight} mismatches={mismatches}");
            Assert.That(mismatches, Is.LessThan(commonWidth * commonHeight * 0.03f),
                "Actual rendered common regions, including receptors and below-receptor geometry, must agree, not merely their UV-derived dimensions.");
        }

        private static Color SampleRendered(Color32[] pixels, int width, int height, Vector2 point)
        {
            float x = Mathf.Clamp(point.x - 0.5f, 0, width - 1);
            float y = Mathf.Clamp(point.y - 0.5f, 0, height - 1);
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);
            return Color.Lerp(Color.Lerp(pixels[y0 * width + x0], pixels[y0 * width + x1], x - x0),
                Color.Lerp(pixels[y1 * width + x0], pixels[y1 * width + x1], x - x0), y - y0);
        }

        private static float VerticalPixelsPerSourceUnit(RawImage image, Camera camera)
        {
            var corners = new Vector3[4];
            image.rectTransform.GetWorldCorners(corners);
            float height = Mathf.Abs(RectTransformUtility.WorldToScreenPoint(camera, corners[1]).y -
                RectTransformUtility.WorldToScreenPoint(camera, corners[0]).y);
            return height / Mathf.Max(image.uvRect.height, 0.0001f);
        }

        private static void AddCaptureLabel(RectTransform parent, string text)
        {
            var labelType = Production("TMPro.TextMeshProUGUI");
            var labelObject = new GameObject("Parity capture label", typeof(RectTransform), typeof(CanvasRenderer),
                labelType);
            labelObject.transform.SetParent(parent, false);
            var rect = (RectTransform)labelObject.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 12f);
            rect.sizeDelta = new Vector2(0f, 34f);
            var label = labelObject.GetComponent(labelType);
            labelType.GetProperty("text").SetValue(label, text);
            labelType.GetProperty("fontSize").SetValue(label, 20f);
            labelType.GetProperty("color").SetValue(label, Color.white);
            var fontStyle = labelType.GetProperty("fontStyle");
            fontStyle.SetValue(label, Enum.Parse(fontStyle.PropertyType, "Bold"));
            var alignment = labelType.GetProperty("alignment");
            alignment.SetValue(label, Enum.Parse(alignment.PropertyType, "Center"));
            labelType.GetProperty("outlineWidth").SetValue(label, 0.2f);
            labelType.GetProperty("outlineColor").SetValue(label, (Color32)Color.black);
        }

        private static Color32[] ReadPixels(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var displayTarget = RenderTexture.GetTemporary(target.width, target.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(target, displayTarget);
                RenderTexture.active = displayTarget;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply(false, false);
                return image.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(displayTarget);
                UnityEngine.Object.Destroy(image);
            }
        }

        private static void WriteLabeledCapture(RenderTexture target, string path)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.active = previous;
        }
    }
}
