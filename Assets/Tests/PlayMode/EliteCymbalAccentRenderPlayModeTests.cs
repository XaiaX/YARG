using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.PlayMode
{
    /// <summary>Exercises the authored Elite cymbal-accent model in the production preview renderer.</summary>
    public sealed class EliteCymbalAccentRenderPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;

        private GameObject _world;
        private Component _player;
        private Component _themeManager;
        private bool _ownsThemeManager;
        private RenderTexture _captureTarget;
        private object _originalSettings;
        private bool _originalSettingsInitialized;
        private bool _capturedSettings;
        private bool _ownsAudio;

        private static IEnumerator WaitForRenderedFrame()
        {
            bool rendered = false;
            void OnEndFrame(UnityEngine.Rendering.ScriptableRenderContext context, Camera[] cameras) => rendered = true;
            UnityEngine.Rendering.RenderPipelineManager.endFrameRendering += OnEndFrame;
            try
            {
                for (int frame = 0; frame < 120 && !rendered; frame++) yield return null;
            }
            finally
            {
                UnityEngine.Rendering.RenderPipelineManager.endFrameRendering -= OnEndFrame;
            }
            Assert.That(rendered, Is.True, "A rendered frame must arrive before capture.");
        }

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static object Get(object target, string name)
        {
            var property = target.GetType().GetProperty(name, INSTANCE);
            return property != null ? property.GetValue(target) : target.GetType().GetField(name, INSTANCE).GetValue(target);
        }

        private static void Set(object target, string name, object value) =>
            target.GetType().GetProperty(name, INSTANCE).SetValue(target, value);

        private static object Field(object target, string name) => target.GetType().GetField(name, INSTANCE).GetValue(target);

        private static object Call(object target, string name, params object[] args) => target.GetType()
            .GetMethods(INSTANCE).Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(target, args);

        private static object CallStatic(Type type, string name, params object[] args) => type
            .GetMethods(STATIC).Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(null, args);

        [UnitySetUp]
        public IEnumerator SetUpProductionTheme()
        {
            var settingsManager = Production("YARG.Settings.SettingsManager");
            var settingsContainerType = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
            var settingsInitialized = settingsContainerType.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static);
            _originalSettings = settingsManager.GetProperty("Settings").GetValue(null);
            _originalSettingsInitialized = (bool)settingsInitialized.GetValue(null);
            _capturedSettings = true;

            var audioHandler = Production("YARG.Core.Audio.GlobalAudioHandler");
            if (audioHandler.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null)
            {
                Production("YARG.Helpers.PathHelper").GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                var bassAudioManager = Production("YARG.Audio.BASS.BassAudioManager");
                audioHandler.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)
                    .MakeGenericMethod(bassAudioManager).Invoke(null, null);
                _ownsAudio = true;
            }

            if (_originalSettings == null)
            {
                var temporarySettings = Activator.CreateInstance(settingsContainerType);
                settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null, new[] { temporarySettings });
                settingsInitialized.SetValue(null, true);
            }

            var themeManagerType = Production("YARG.Themes.ThemeManager");
            _themeManager = (Component)themeManagerType.GetProperty("Instance", STATIC).GetValue(null);
            if (_themeManager == null)
            {
                _themeManager = new GameObject("Cymbal accent render test ThemeManager").AddComponent(themeManagerType);
                _ownsThemeManager = true;
            }
            yield return null;
            Assert.That(themeManagerType.GetProperty("Instance", STATIC).GetValue(null), Is.SameAs(_themeManager),
                "The production ThemeManager must load the real default note theme.");
            var presetType = Production("YARG.Themes.ThemePreset");
            var visualStyleType = Production("YARG.Themes.VisualStyle");
            var defaultPreset = presetType.GetField("Default", STATIC).GetValue(null);
            var eliteStyle = Enum.Parse(visualStyleType, "EliteDrums");
            Assert.That(Call(_themeManager, "GetThemeContainer", defaultPreset, eliteStyle), Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_world != null && _captureTarget != null)
            {
                // Detach the owned target from the preview cameras (including HighwayCameraRendering)
                // before either side is destroyed, in the production menu's cleanup order.
                CallStatic(Production("YARG.Helpers.CameraPreviewTexture"), "ClearOwnedPreviewTargets",
                    _world.transform, _captureTarget);
            }
            if (_world != null) UnityEngine.Object.Destroy(_world);
            if (_captureTarget != null)
            {
                CallStatic(Production("YARG.Helpers.CameraPreviewTexture"), "ReleaseOwnedPreviewTexture",
                    _captureTarget);
            }
            _captureTarget = null;
            _world = null;
            yield return null;
            if (_ownsThemeManager && _themeManager != null) UnityEngine.Object.Destroy(_themeManager.gameObject);
            _themeManager = null;
            _ownsThemeManager = false;

            if (_capturedSettings)
            {
                var settingsManager = Production("YARG.Settings.SettingsManager");
                var settingsContainerType = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
                settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null, new[] { _originalSettings });
                settingsContainerType.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static)
                    .SetValue(null, _originalSettingsInitialized);
                _capturedSettings = false;
            }

            if (_ownsAudio)
            {
                Production("YARG.Core.Audio.GlobalAudioHandler").GetMethod("Close", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                _ownsAudio = false;
            }
        }

        [UnityTest]
        public IEnumerator ActualEliteAccentModelsRenderAndCaptureAtTenLanePreviewScale()
        {
            _world = new GameObject("Actual Elite ten-lane track preview");
            var previewBuilderType = Production("YARG.Settings.Metadata.TrackPreviewBuilder");
            Func<string, GameObject> loadAsset = address =>
            {
                Assert.That(address, Is.EqualTo("SettingPreviews/TrackPreview"));
                return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SettingPreviews/Track/TrackPreview.prefab");
            };
            var builder = Activator.CreateInstance(previewBuilderType, new object[] { false, false, false, loadAsset });
            Set(builder, "StartingGameMode", GameMode.EliteDrums);
            Set(builder, "EliteLayoutOverride", TenLaneLayout());
            var buildTask = Call(builder, "BuildPreviewWorld", _world.transform);
            var awaiter = buildTask.GetType().GetMethod("GetAwaiter").Invoke(buildTask, null);
            awaiter.GetType().GetMethod("GetResult", Type.EmptyTypes).Invoke(awaiter, null);

            yield return null;
            var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
            _player = _world.GetComponentInChildren(playerType, true);
            Assert.That(_player, Is.Not.Null, "TrackPreviewBuilder must instantiate its production FakeTrackPlayer.");
            Assert.That(Get(Get(_player, "CurrentGameModeInfo"), "LaneCount"), Is.EqualTo(10),
                "The preview must resolve the owned ten-lane layout.");
            ((Behaviour)_player).enabled = false;

            var themeNoteType = Production("YARG.Themes.ThemeNoteType");
            var noteGroupType = Production("YARG.Gameplay.Visuals.NoteGroup");
            var fakeNoteType = Production("YARG.Settings.Preview.FakeNote");
            var cases = new[]
            {
                (Name: "normal-regular", Type: "Normal", StarPower: false),
                (Name: "normal-starpower", Type: "Normal", StarPower: true),
                (Name: "accent-regular", Type: "CymbalAccent", StarPower: false),
                (Name: "accent-starpower", Type: "CymbalAccent", StarPower: true),
                (Name: "ghost-regular", Type: "CymbalGhost", StarPower: false),
                (Name: "ghost-starpower", Type: "CymbalGhost", StarPower: true),
                (Name: "ClosedHiHatGhost-regular", Type: "ClosedHiHatGhost", StarPower: false),
                (Name: "ClosedHiHatGhost-starpower", Type: "ClosedHiHatGhost", StarPower: true),
                (Name: "ClosedHiHat-regular", Type: "ClosedHiHat", StarPower: false),
                (Name: "ClosedHiHat-starpower", Type: "ClosedHiHat", StarPower: true),
                (Name: "ClosedHiHatAccent-regular", Type: "ClosedHiHatAccent", StarPower: false),
                (Name: "ClosedHiHatAccent-starpower", Type: "ClosedHiHatAccent", StarPower: true),
                (Name: "OpenHiHatGhost-regular", Type: "OpenHiHatGhost", StarPower: false),
                (Name: "OpenHiHatGhost-starpower", Type: "OpenHiHatGhost", StarPower: true),
                (Name: "OpenHiHat-regular", Type: "OpenHiHat", StarPower: false),
                (Name: "OpenHiHat-starpower", Type: "OpenHiHat", StarPower: true),
                (Name: "OpenHiHatAccent-regular", Type: "OpenHiHatAccent", StarPower: false),
                (Name: "OpenHiHatAccent-starpower", Type: "OpenHiHatAccent", StarPower: true),
            };
            foreach (var testCase in cases)
            {
                var noteType = Enum.Parse(themeNoteType, testCase.Type);
                var pool = Field(_player, "_notePool");
                Call(pool, "ReturnAllObjects");
                Call(builder, "SpotlightNoteType", noteType, (object)testCase.StarPower);
                Call(_player, "Advance", 0.35d);

                var spawned = (IList)pool.GetType().GetProperty("AllSpawned").GetValue(pool);
                Assert.That(spawned.Count, Is.GreaterThan(0), $"{testCase.Name}: Advance must spawn a real preview note.");
                var note = (Component)spawned[spawned.Count - 1];
                Assert.That(fakeNoteType.IsInstanceOfType(note), Is.True);
                Assert.That(note.gameObject.activeInHierarchy, Is.True);
                var noteRef = Get(note, "NoteRef");
                noteRef.GetType().GetField("Time").SetValue(noteRef,
                    Convert.ToDouble(Get(_player, "PreviewTime")) + 0.2d);
                Call(note, "RefreshPosition");
                Assert.That(Get(noteRef, "NoteType"), Is.EqualTo(noteType),
                    $"{testCase.Name}: the spotlight request must reach the spawned FakeNote.");
                var activeRenderers = note.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
                Assert.That(activeRenderers, Is.Not.Empty, $"{testCase.Name}: selected authored group must render.");

                var groups = note.GetComponentsInChildren(noteGroupType, true).Cast<Component>().ToArray();
                var activeGroups = groups.Where(group => group.gameObject.activeInHierarchy).ToArray();
                Assert.That(activeGroups.Length, Is.EqualTo(1), $"{testCase.Name}: no legacy fallback group may remain active.");
                var activeGroup = activeGroups.Single();
                var themeNote = (Component)Field(activeGroup, "_themeNote");
                Assert.That(themeNote, Is.Not.Null, $"{testCase.Name}: active group must retain its authored ThemeNote bindings.");
                if (testCase.Type == "CymbalAccent")
                {
                    var forceStarPower = (bool?)Get(noteRef, "ForceStarPower");
                    var useStarPower = forceStarPower ?? (bool)Get(_player, "ForceStarPowerNotes");
                    var expectedGroup = (Component)note.GetType()
                        .GetMethod("FindNoteGroup", INSTANCE)
                        .Invoke(note, new[] { noteType, (object)useStarPower });
                    Assert.That(activeGroup, Is.SameAs(expectedGroup),
                        $"{testCase.Name}: the active group must match production FindNoteGroup resolution, including authored star-power fallback.");
                    Assert.That(Get(themeNote, "NoteType").ToString(), Is.EqualTo("CymbalAccent"),
                        $"{testCase.Name}: the spotlight must select authored CymbalAccent, not legacy Cymbal fallback.");
                    var noteGroups = (System.Collections.IEnumerable)Field(note, "_noteGroups");
                    object activePair = null;
                    foreach (var pair in noteGroups)
                    {
                        if (ReferenceEquals(pair.GetType().GetField("Group", INSTANCE).GetValue(pair), activeGroup))
                        {
                            activePair = pair;
                            break;
                        }
                    }
                    Assert.That(activePair, Is.Not.Null,
                        $"{testCase.Name}: the active group must come from the production group table.");
                    Assert.That((bool)Get(themeNote, "StarPowerVariant"),
                        Is.EqualTo((bool)activePair.GetType().GetField("StarPower", INSTANCE).GetValue(activePair)),
                        $"{testCase.Name}: authored variant metadata must match the selected group entry (fallback to regular is expected when no SP variant is authored).");
                    Assert.That(((IEnumerable)Get(themeNote, "ColoredMaterials")).Cast<object>().Any(), Is.True,
                        "Accent color bindings must exist at runtime.");
                    Assert.That(((IEnumerable)Get(themeNote, "ColoredMetalMaterials")).Cast<object>().Any(), Is.True,
                        "Accent metal-color bindings must exist at runtime.");
                }

                var bounds = activeRenderers.Select(renderer => renderer.bounds).Aggregate(BoundsUnion);
                Assert.That(bounds.size.x, Is.GreaterThan(0.01f), $"{testCase.Name}: model bounds must be non-empty.");
                Assert.That(bounds.size.y, Is.GreaterThan(0.01f), $"{testCase.Name}: model bounds must be non-empty.");
                var camera = _world.GetComponentInChildren<Camera>(true);
                Assert.That(camera, Is.Not.Null, "The actual preview prefab must provide its known camera.");
                if (_captureTarget == null)
                {
                    _captureTarget = (RenderTexture)CallStatic(Production("YARG.Helpers.CameraPreviewTexture"),
                        "CreateOwnedPreviewTexture", 1024, 1024, "Elite cymbal accent runtime capture");
                }
                // Rebind before every case: the owned target must reach HighwayCameraRendering through its
                // preview-target API. Assigning camera.targetTexture directly leaves the highway renderer's
                // render-graph resources screen-sized, so the CalcFadeAlphaMask pass aborts the camera render
                // with a fragment-dimension mismatch and the capture stays fully transparent.
                CallStatic(Production("YARG.Helpers.CameraPreviewTexture"), "SetOwnedPreviewTargets",
                    _world.transform, _captureTarget);
                Assert.That(_captureTarget.IsCreated(), Is.True,
                    "The owned HDR preview texture must be allocated before capture.");
                Assert.That(camera.targetTexture, Is.SameAs(_captureTarget),
                    $"{testCase.Name}: preview camera must render into the owned HDR texture.");
                camera.aspect = 1f;
                camera.pixelRect = new Rect(0, 0, _captureTarget.width, _captureTarget.height);
                var viewport = camera.WorldToViewportPoint(bounds.center);
                Assert.That(viewport.z, Is.GreaterThan(camera.nearClipPlane), $"{testCase.Name}: model center is in front of preview camera.");
                foreach (float x in new[] { bounds.min.x, bounds.max.x })
                foreach (float y in new[] { bounds.min.y, bounds.max.y })
                foreach (float z in new[] { bounds.min.z, bounds.max.z })
                {
                    var corner = camera.WorldToViewportPoint(new Vector3(x, y, z));
                    Assert.That(corner.z, Is.InRange(camera.nearClipPlane, camera.farClipPlane),
                        $"{testCase.Name}: whole model bounds must fit camera depth.");
                    Assert.That(corner.x, Is.InRange(0f, 1f), $"{testCase.Name}: whole model bounds must fit camera width.");
                    Assert.That(corner.y, Is.InRange(0f, 1f), $"{testCase.Name}: whole model bounds must fit camera height.");
                }

                foreach (var renderer in activeRenderers)
                foreach (var material in renderer.sharedMaterials)
                {
                    Assert.That(material, Is.Not.Null, $"{testCase.Name}: {renderer.name} has a null runtime material slot.");
                    Assert.That(material.shader == null || !material.shader.name.Contains("LegacyLit"), Is.True,
                        $"{testCase.Name}: {renderer.name} slot {material.name} unexpectedly uses LegacyLit.");
                }

                yield return WaitForRenderedFrame();
                var pixels = ReadFrame(_captureTarget);
                AssertNonEmptyCapture(pixels, testCase.Name);
                var path = $"/tmp/yarg-elite-cymbal-accent-{testCase.Name}.png";
                WriteFramePng(pixels, _captureTarget.width, _captureTarget.height, path);
                Debug.Log($"[Elite cymbal accent runtime capture] case={testCase.Name}; image={path}; " +
                    $"camera={camera.name}; viewportCenter={viewport}; worldBoundsCenter={bounds.center}; " +
                    $"worldBoundsSize={bounds.size}; activeGroup={activeGroup.name}; themeNote={Get(themeNote, "NoteType")}; " +
                    $"starPower={Get(themeNote, "StarPowerVariant")}; colorBindings={DescribeBindings(Get(themeNote, "ColoredMaterials"))}; " +
                    $"metalBindings={DescribeBindings(Get(themeNote, "ColoredMetalMaterials"))}; materials={DescribeRuntimeMaterials(activeRenderers)}");
            }
            yield return null;
        }

        private static string DescribeBindings(object bindings)
        {
            return string.Join(" | ", ((IEnumerable)bindings).Cast<object>().Select(binding =>
            {
                var renderer = (Renderer)Field(binding, "Mesh");
                int index = (int)Field(binding, "MaterialIndex");
                var material = renderer.materials[index];
                return $"{renderer.name}[{index}]={material.name}:{material.shader?.name}:color={material.color}:" +
                    $"emissionMultiplier={Field(binding, "EmissionMultiplier")}:emissionAddition={Field(binding, "EmissionAddition")}";
            }));
        }

        private static string DescribeRuntimeMaterials(System.Collections.Generic.IEnumerable<Renderer> renderers)
        {
            return string.Join(" | ", renderers.SelectMany(renderer => renderer.materials).Select(material =>
                $"{material.name}:{material.shader?.name}:color={material.color}:metallic=" +
                (material.HasProperty("_Metallic") ? material.GetFloat("_Metallic").ToString("R") : "<not-supported>")));
        }

        private static Bounds BoundsUnion(Bounds left, Bounds right)
        {
            left.Encapsulate(right);
            return left;
        }

        private static Color32[] ReadFrame(RenderTexture source)
        {
            var previous = RenderTexture.active;
            var displayTarget = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var staging = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try
            {
                // Sample the HDR source through a normal GPU blit into an sRGB display target.
                // This follows the same linear-to-display transfer used by the RawImage shader,
                // instead of quantizing half-float linear values directly into 8-bit channels.
                Graphics.Blit(source, displayTarget);
                RenderTexture.active = displayTarget;
                staging.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                staging.Apply(false, false);
                return staging.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(displayTarget);
                UnityEngine.Object.Destroy(staging);
            }
        }

        private static void AssertNonEmptyCapture(Color32[] pixels, string label)
        {
            int opaque = pixels.Count(pixel => pixel.a > 8);
            Assert.That(opaque, Is.GreaterThan(0),
                $"{label}: capture must contain non-transparent pixels; a fully transparent image means " +
                "the camera render never reached the owned preview texture.");
            int rendered = pixels.Count(pixel => pixel.a > 8 && (pixel.r > 10 || pixel.g > 10 || pixel.b > 10));
            Assert.That(rendered, Is.GreaterThan(60),
                $"{label}: preview image must contain rendered pixels (count={rendered}).");
        }

        private static void WriteFramePng(Color32[] pixels, int width, int height, string path)
        {
            var capture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                capture.SetPixels32(pixels);
                capture.Apply(false, false);
                File.WriteAllBytes(path, capture.EncodeToPNG());
                Debug.Log($"[Elite cymbal accent runtime capture] wrote {path} ({width}x{height}, sRGB display values)");
            }
            finally
            {
                UnityEngine.Object.Destroy(capture);
            }
        }

        private static EliteDrumsLayoutPreset TenLaneLayout()
        {
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Cymbal accent render ten lane");
            layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            return layout;
        }
    }
}
