using System;
using System.Collections;
using System.Collections.Generic;
using CoreColor = System.Drawing.Color;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;
using Object = UnityEngine.Object;

namespace YARG.Tests.PresetPreview
{
    internal static class TestColorExtensions
    {
        public static UnityEngine.Color ToUnityColor(this CoreColor color)
        {
            return new UnityEngine.Color32(color.R, color.G, color.B, color.A);
        }
    }

    public sealed class PresetPreviewFretRecoloringTests
    {
        private const BindingFlags INSTANCE_FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags STATIC_FLAGS = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string THEME_PATH = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";

        private Assembly _gameAssembly;
        private GameObject _themeRoot;
        private GameObject _managerObject;
        private object _themeManager;
        private object _themePreset;
        private Type _themePresetType;
        private Type _visualStyleType;
        private readonly List<GameObject> _ownedObjects = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            yield return null;
            _gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .Single(assembly => assembly.GetName().Name == "Assembly-CSharp");
            _themePresetType = _gameAssembly.GetType("YARG.Themes.ThemePreset", true);
            _visualStyleType = _gameAssembly.GetType("YARG.Themes.VisualStyle", true);
            _themePreset = _themePresetType.GetField("Default", STATIC_FLAGS).GetValue(null);
            _themeRoot = AssetDatabase.LoadAssetAtPath<GameObject>(THEME_PATH);
            Assert.That(_themeRoot, Is.Not.Null, $"Required production theme prefab not found: {THEME_PATH}");

            var managerType = _gameAssembly.GetType("YARG.Themes.ThemeManager", true);
            var existingManager = managerType.GetProperty("Instance", STATIC_FLAGS)?.GetValue(null) as Component;
            if (existingManager != null)
            {
                _themeManager = existingManager;
                _managerObject = existingManager.gameObject;
            }
            else
            {
                _managerObject = new GameObject("PresetPreviewFretTestThemeManager");
                _ownedObjects.Add(_managerObject);
                _themeManager = _managerObject.AddComponent(managerType);
            }

            var themeContainerType = _gameAssembly.GetType("YARG.Themes.ThemeContainer", true);
            var themeContainerConstructor = themeContainerType.GetConstructor(new[] { typeof(GameObject), typeof(bool), typeof(AssetBundle) });
            var themeContainer = themeContainerConstructor.Invoke(new object[] { _themeRoot, true, null });
            SetField(_themeManager, "_defaultTheme", themeContainer);
            var containersField = managerType.GetField("_themeContainers", INSTANCE_FLAGS);
            var themeContainers = (IDictionary)containersField.GetValue(_themeManager);
            themeContainers.Clear();
            themeContainers.Add(_themePreset, themeContainer);
            ((Behaviour)_managerObject.GetComponent(managerType)).enabled = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in _ownedObjects)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator PreviewControlsFollowProductionOwnershipAcrossRefreshSwitchAndReopen()
        {
            var previewContainer = new GameObject("PreviewContainer").transform;
            _ownedObjects.Add(previewContainer.gameObject);

            var controls = new GameObject("PreviewControls");
            controls.transform.SetParent(previewContainer, false);
            var presetSubTabType = _gameAssembly.GetType("YARG.Settings.Metadata.PresetSubTab", true);
            presetSubTabType.GetMethod("SetPreviewControlsContainer", STATIC_FLAGS).Invoke(null,
                new object[] { controls.transform });
            var preview = new GameObject("TrackPreviewUI");
            preview.transform.SetParent(previewContainer, false);

            var settingsMenuType = _gameAssembly.GetType("YARG.Menu.Settings.SettingsMenu", true);
            var destroyUI = settingsMenuType.GetMethod("DestroyPreviewUI", STATIC_FLAGS);
            Assert.That(destroyUI, Is.Not.Null, "Production preview UI teardown helper must exist");
            destroyUI.Invoke(null, new object[] { previewContainer, true });
            yield return null;
            Assert.That(preview == null, Is.True, "Refresh removes previous preview children");
            Assert.That(previewContainer.childCount, Is.EqualTo(1));
            Assert.That(previewContainer.GetChild(0), Is.SameAs(controls.transform));

            // Close/tab exit destroys the registered control and clears its owner reference.
            destroyUI.Invoke(null, new object[] { previewContainer, false });
            yield return null;
            Assert.That(controls == null, Is.True);
            Assert.That(previewContainer.childCount, Is.Zero);
            Assert.That(presetSubTabType.GetProperty("PreviewControlsContainer", STATIC_FLAGS).GetValue(null), Is.Null);

            // Reopening registers a new exact Transform; refresh preserves it without
            // depending on a GameObject name or retaining the destroyed previous control.
            controls = new GameObject("ControlsWithDifferentName");
            controls.transform.SetParent(previewContainer, false);
            presetSubTabType.GetMethod("SetPreviewControlsContainer", STATIC_FLAGS).Invoke(null,
                new object[] { controls.transform });
            destroyUI.Invoke(null, new object[] { previewContainer, true });
            Assert.That(previewContainer.childCount, Is.EqualTo(1));
            Assert.That(previewContainer.GetChild(0), Is.SameAs(controls.transform));
        }

        [UnityTest]
        public IEnumerator ProductionPreviewBuildersRespectInactiveContainerAndBlankFallback()
        {
            var cameraBuilderType = _gameAssembly.GetType("YARG.Settings.Metadata.DMXInformationPanelBuilder", true);
            var builderInterface = _gameAssembly.GetType("YARG.Settings.Metadata.IPreviewBuilder", true);
            var metadataTabType = _gameAssembly.GetType("YARG.Settings.Metadata.MetadataTab", true);
            var cameraBuilder = Activator.CreateInstance(cameraBuilderType);
            var metadataTab = metadataTabType.GetConstructor(new[] { typeof(string), typeof(string), builderInterface })
                .Invoke(new[] { "Test", "Generic", cameraBuilder });
            var buildPreviewUI = _gameAssembly.GetType("YARG.Settings.Metadata.Tab", true)
                .GetMethod("BuildPreviewUI", INSTANCE_FLAGS);

            var inactiveContainer = new GameObject("PrefabEquivalentPreviewContainer").transform;
            _ownedObjects.Add(inactiveContainer.gameObject);
            inactiveContainer.gameObject.SetActive(false);
            // UpdatePreview activates the serialized parent before invoking builders.
            inactiveContainer.gameObject.SetActive(true);
            buildPreviewUI.Invoke(metadataTab, new object[] { inactiveContainer });
            yield return null;

            Assert.That(inactiveContainer.gameObject.activeSelf, Is.True);
            Assert.That(inactiveContainer.gameObject.activeInHierarchy, Is.True);
            Assert.That(inactiveContainer.childCount, Is.EqualTo(1), "The production DMX preview builder must instantiate UI directly under its parent");
            Assert.That(inactiveContainer.GetChild(0).gameObject.activeInHierarchy, Is.True);

            // A tab with no preview builder uses Tab.BuildPreviewUI's real blank fallback.
            var blankTab = Activator.CreateInstance(_gameAssembly.GetType("YARG.Settings.Metadata.AllSettingsTab", true));
            buildPreviewUI.Invoke(blankTab, new object[] { inactiveContainer });
            yield return null;
            Assert.That(inactiveContainer.gameObject.activeSelf, Is.False,
                "A blank preview hides the original serialized preview parent");

        }

        [UnityTest]
        public IEnumerator SetAllPreviewsEagerlyAssignsTheSharedTextureToRegisteredCameras()
        {
            var cameraTextureType = _gameAssembly.GetType("YARG.Helpers.CameraPreviewTexture", true);
            var previewTextureProperty = cameraTextureType.GetProperty("PreviewTexture", STATIC_FLAGS);
            var world = new GameObject("SettingsPreviewWorld");
            var cameraObject = new GameObject("SettingsPreviewCamera");
            cameraObject.transform.SetParent(world.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent(cameraTextureType);
            _ownedObjects.Add(world);

            cameraTextureType.GetMethod("SetAllPreviews", STATIC_FLAGS).Invoke(null, null);
            yield return null;

            var sharedTexture = previewTextureProperty.GetValue(null);
            Assert.That(sharedTexture, Is.Not.Null, "The original eager lifecycle allocates a shared preview texture");
            Assert.That(camera.targetTexture, Is.SameAs(sharedTexture),
                "SetAllPreviews assigns the shared texture directly to every registered preview camera");
        }

        [Test]
        public void FiveFretRecolorUsesBoundColorIndexOnceAndPreservesPhysicalAlias()
        {
            var profile = NewProfile();
            profile.FiveFretGuitar.GreenFret = CoreColor.FromArgb(255, 20, 190, 30);
            profile.FiveFretGuitar.RedFret = CoreColor.FromArgb(255, 210, 40, 50);
            var array = CreateFretArray();
            var ordering = new Dictionary<int, object>
            {
                [(int)FiveFretGuitarFret.Green] = CreateOrderingInfo(0, (int)FiveFretGuitarFret.Green),
                [(int)FiveFretGuitarFret.Red] = CreateOrderingInfo(0, (int)FiveFretGuitarFret.Red)
            };

            Initialize(array, ordering, 5, null, profile.FiveFretGuitar, ParseVisualStyle("FiveFretGuitar"), false);
            var fretMap = GetField<IDictionary>(array, "_frets");
            Assert.That(fretMap[(int)FiveFretGuitarFret.Green], Is.SameAs(fretMap[(int)FiveFretGuitarFret.Red]));
            var fret = (Component)fretMap[(int)FiveFretGuitarFret.Green];
            AssertMaterialColors(fret, profile.FiveFretGuitar.GreenFret.ToUnityColor(), profile.FiveFretGuitar.GreenFretInner.ToUnityColor());

            profile.FiveFretGuitar.GreenFret = CoreColor.FromArgb(255, 90, 80, 70);
            Recolor(array, profile.FiveFretGuitar);
            AssertMaterialColors(fret, profile.FiveFretGuitar.GreenFret.ToUnityColor(), profile.FiveFretGuitar.GreenFretInner.ToUnityColor());
            int topCacheCount = GetListCount(fret, "_topMaterials");
            int innerCacheCount = GetListCount(fret, "_innerMaterials");

            Recolor(array, profile.FiveFretGuitar);
            Assert.That(GetListCount(fret, "_topMaterials"), Is.EqualTo(topCacheCount));
            Assert.That(GetListCount(fret, "_innerMaterials"), Is.EqualTo(innerCacheCount));
        }

        [Test]
        public void SixFretRecolorUpdatesBothHalvesAndKeepsCachesStable()
        {
            var profile = NewProfile();
            profile.SixFretGuitar.BlackFretInner = CoreColor.FromArgb(255, 30, 50, 70);
            profile.SixFretGuitar.WhiteFretInner = CoreColor.FromArgb(255, 180, 190, 200);
            var array = CreateFretArray();
            var ordering = new Dictionary<int, object>
            {
                [0] = CreateOrderingInfo(0, (int)SixFretGuitarFret.Black1),
                [1] = CreateOrderingInfo(0, (int)SixFretGuitarFret.White1)
            };

            Initialize(array, ordering, 3, null, profile.SixFretGuitar, ParseVisualStyle("SixFretGuitar"), true);
            var fretMap = GetField<IDictionary>(array, "_frets");
            var fret = (Component)fretMap[0];
            Assert.That(fretMap[1], Is.SameAs(fret));
            AssertSecondaryInnerColor(fret, profile.SixFretGuitar.WhiteFretInner.ToUnityColor());

            profile.SixFretGuitar.WhiteFretInner = CoreColor.FromArgb(255, 15, 25, 35);
            Recolor(array, profile.SixFretGuitar);
            AssertSecondaryInnerColor(fret, profile.SixFretGuitar.WhiteFretInner.ToUnityColor());
            int secondaryTopCount = GetListCount(fret, "_secondaryTopMaterials");
            int secondaryInnerCount = GetListCount(fret, "_secondaryInnerMaterials");

            Recolor(array, profile.SixFretGuitar);
            Assert.That(GetListCount(fret, "_secondaryTopMaterials"), Is.EqualTo(secondaryTopCount));
            Assert.That(GetListCount(fret, "_secondaryInnerMaterials"), Is.EqualTo(secondaryInnerCount));
        }

        [Test]
        public void SuppliedKickFretsAreRecoloredAlongsideHands()
        {
            var profile = NewProfile();
            profile.FourLaneDrums.KickFret = CoreColor.FromArgb(255, 25, 45, 225);
            var array = CreateFretArray();
            SetField(array, "UseKickFrets", true);
            var kickPrefab = CreateKickPrefab();

            Initialize(array, new Dictionary<int, object>
            {
                [0] = CreateOrderingInfo(0, 0)
            }, 4, kickPrefab, profile.FourLaneDrums, ParseVisualStyle("FourLaneDrums"), false);
            var kicks = GetField<IList>(array, "_kickFrets");
            Assert.That(kicks.Count, Is.EqualTo(2));
            var expected = profile.FourLaneDrums.KickFret.ToUnityColor();
            foreach (Component kick in kicks)
            {
                AssertColorApproximately(GetColoredMaterials(kick).Single().color, expected);
            }

            profile.FourLaneDrums.KickFret = CoreColor.FromArgb(255, 100, 120, 140);
            Recolor(array, profile.FourLaneDrums);
            foreach (Component kick in kicks)
            {
                AssertColorApproximately(GetColoredMaterials(kick).Single().color, profile.FourLaneDrums.KickFret.ToUnityColor());
            }
        }

        [Test]
        public void LeftyMappingChangesColorWithoutMovingTheReceptor()
        {
            var profile = NewProfile();
            profile.FiveFretGuitar.GreenFret = CoreColor.FromArgb(255, 20, 30, 40);
            profile.FiveFretGuitar.OrangeFret = CoreColor.FromArgb(255, 210, 220, 230);
            var array = CreateFretArray();
            Initialize(array, new Dictionary<int, int>
            {
                [(int)FiveFretGuitarFret.Green] = 0
            }, 5, null, profile.FiveFretGuitar, ParseVisualStyle("FiveFretGuitar"), false);
            var fret = (Component)GetField<IDictionary>(array, "_frets")[(int)FiveFretGuitarFret.Green];
            var position = ((Component)array).transform.GetChild(0).localPosition;

            Recolor(array, profile.FiveFretGuitar, index => 6 - index);
            AssertMaterialColors(fret, profile.FiveFretGuitar.OrangeFret.ToUnityColor(), profile.FiveFretGuitar.OrangeFretInner.ToUnityColor());
            Assert.That(((Component)array).transform.GetChild(0).localPosition, Is.EqualTo(position));
        }

        [Test]
        public void PianoProKeysInitializesAndRefreshesOverlayFromSelectedProfileReplacement()
        {
            var playerObject = new GameObject("Piano Pro Keys Preview Under Test");
            _ownedObjects.Add(playerObject);
            var playerType = _gameAssembly.GetType("YARG.Settings.Preview.FakeTrackPlayer", true);
            var player = playerObject.AddComponent(playerType);
            SetField(player, "_fretArray", CreateFretArray());
            SetField(player, "_proKeysEdgeTexture", null);
            SetAutoProperty(player, "SelectedGameMode", GameMode.ProKeys);
            SetAutoProperty(player, "UseFiveLaneKeys", false);

            var firstProfile = NewProfile();
            var expectedInitial = CoreColor.FromArgb(255, 34, 56, 78);
            firstProfile.ProKeys.RedOverlay = expectedInitial;
            Invoke(player, "ConfigureCurrentGameModeInfo");
            var modeInfo = GetField<object>(player, "CurrentGameModeInfo");
            Assert.That(GetField<Delegate>(modeInfo, "FretColorProvider"), Is.Null);
            Assert.That(GetField<bool>(modeInfo, "UseHighwayOverlay"), Is.True);
            Invoke(player, "InitializeReceptors", firstProfile, _themePreset, ParseVisualStyle("ProKeys"));
            var overlayRenderers = playerObject.GetComponentsInChildren<SpriteRenderer>(true);
            var overlay = overlayRenderers.First(renderer => renderer.gameObject.name == "ProKeysOverlayFill");
            AssertOverlayShader(overlayRenderers);
            var initialGeometry = overlayRenderers.Select(CaptureOverlayGeometry).ToArray();
            Assert.That(overlay.color.r, Is.EqualTo(expectedInitial.R / 255f).Within(0.01f));
            Assert.That(overlay.color.g, Is.EqualTo(expectedInitial.G / 255f).Within(0.01f));

            firstProfile.ProKeys.RedOverlay = CoreColor.FromArgb(255, 90, 100, 110);
            Invoke(player, "RefreshReceptorColors", firstProfile);
            Assert.That(overlay.color.r, Is.EqualTo(90f / 255f).Within(0.01f));
            AssertOverlayGeometryUnchanged(overlayRenderers, initialGeometry);

            var replacementProfile = NewProfile();
            replacementProfile.ProKeys.RedOverlay = CoreColor.FromArgb(255, 150, 160, 170);
            Invoke(player, "RefreshReceptorColors", replacementProfile);
            Assert.That(overlay.color.r, Is.EqualTo(150f / 255f).Within(0.01f));
            var refreshedRenderers = playerObject.GetComponentsInChildren<SpriteRenderer>(true);
            AssertOverlayShader(refreshedRenderers);
            AssertOverlayGeometryUnchanged(refreshedRenderers, initialGeometry);
            Assert.That(refreshedRenderers.Count(renderer => renderer.name == "ProKeysOverlayFill"), Is.EqualTo(10));
        }

        [Test]
        public void FiveLaneKeysPresentationUsesGuitarReceptorProvider()
        {
            var playerObject = new GameObject("Five Lane Keys Preview Under Test");
            _ownedObjects.Add(playerObject);
            var playerType = _gameAssembly.GetType("YARG.Settings.Preview.FakeTrackPlayer", true);
            var player = playerObject.AddComponent(playerType);
            SetField(player, "_fretArray", CreateFretArray());
            SetAutoProperty(player, "SelectedGameMode", GameMode.ProKeys);
            SetAutoProperty(player, "UseFiveLaneKeys", true);
            Invoke(player, "ConfigureCurrentGameModeInfo");
            var modeInfo = GetField<object>(player, "CurrentGameModeInfo");
            var providerFunc = GetField<Delegate>(modeInfo, "FretColorProvider");
            var profile = NewProfile();
            Assert.That(providerFunc, Is.Not.Null);
            Assert.That(providerFunc.DynamicInvoke(profile), Is.SameAs(profile.FiveFretGuitar));
            Assert.That(GetField<bool>(modeInfo, "UseHighwayOverlay"), Is.False);

            Invoke(player, "InitializeReceptors", profile, _themePreset, ParseVisualStyle("FiveLaneKeys"));
            profile.FiveFretGuitar.GreenFret = CoreColor.FromArgb(255, 11, 22, 33);
            Invoke(player, "RefreshReceptorColors", profile);
            var fret = (Component)GetField<IDictionary>(GetField<Component>(player, "_fretArray"), "_frets")
                [(int)FiveFretGuitarFret.Green];
            var originalPosition = fret.transform.localPosition;
            AssertMaterialColors(fret, profile.FiveFretGuitar.GreenFret.ToUnityColor(), profile.FiveFretGuitar.GreenFretInner.ToUnityColor());

            var replacementProfile = NewProfile();
            replacementProfile.FiveFretGuitar.GreenFret = CoreColor.FromArgb(255, 101, 102, 103);
            Invoke(player, "RefreshReceptorColors", replacementProfile);
            AssertMaterialColors(fret, replacementProfile.FiveFretGuitar.GreenFret.ToUnityColor(), replacementProfile.FiveFretGuitar.GreenFretInner.ToUnityColor());
            Assert.That(fret.transform.localPosition, Is.EqualTo(originalPosition));
            Assert.That(GetField<IDictionary>(GetField<Component>(player, "_fretArray"), "_frets")
                [(int)FiveFretGuitarFret.Green], Is.SameAs(fret));
        }

        [Test]
        public void PreviewRegistryResolvesInstrumentProvidersFromSuppliedProfile()
        {
            var profile = NewProfile();
            profile.FiveFretGuitar.GreenFret = CoreColor.FromArgb(255, 4, 5, 6);
            profile.FourLaneDrums.KickFret = CoreColor.FromArgb(255, 7, 8, 9);
            var infoField = _gameAssembly.GetType("YARG.Settings.Preview.FakeTrackPlayer", true)
                .GetField("_gameModeInfos", STATIC_FLAGS);
            var registry = (IDictionary)infoField.GetValue(null);

            (GameMode Mode, ColorProfile.IFretColorProvider Expected)[] providers =
            {
                (GameMode.FiveFretGuitar, profile.FiveFretGuitar),
                (GameMode.SixFretGuitar, profile.SixFretGuitar),
                (GameMode.FourLaneDrums, profile.FourLaneDrums),
                (GameMode.FiveLaneDrums, profile.FiveLaneDrums)
            };
            foreach (var (mode, expected) in providers)
            {
                object info = registry[mode];
                var providerFunc = info.GetType().GetField("FretColorProvider").GetValue(info) as Delegate;
                Assert.That(providerFunc, Is.Not.Null);
                Assert.That(providerFunc.DynamicInvoke(profile), Is.SameAs(expected), mode.ToString());
            }

            Assert.That(profile.FiveFretGuitar.GreenFret, Is.Not.EqualTo(ColorProfile.Default.FiveFretGuitar.GreenFret));
        }

        private ColorProfile NewProfile() => new("Preview Test Profile");

        private object ParseVisualStyle(string name) => Enum.Parse(_visualStyleType, name);

        private Component CreateFretArray()
        {
            var gameObject = new GameObject("Test Fret Array");
            _ownedObjects.Add(gameObject);
            var type = _gameAssembly.GetType("YARG.Gameplay.Visuals.FretArray", true);
            var array = gameObject.AddComponent(type);
            SetField(array, "_leftKickFretPosition", CreateTransform("Left Kick Position"));
            SetField(array, "_rightKickFretPosition", CreateTransform("Right Kick Position"));
            return array;
        }

        private Transform CreateTransform(string name)
        {
            var gameObject = new GameObject(name);
            _ownedObjects.Add(gameObject);
            return gameObject.transform;
        }

        private GameObject CreateKickPrefab()
        {
            var createKick = _gameAssembly.GetType("YARG.Themes.ThemeManager", true)
                .GetMethod("CreateKickFretPrefabFromTheme", INSTANCE_FLAGS);
            return (GameObject)createKick.Invoke(_themeManager, new object[] { _themePreset, ParseVisualStyle("FourLaneDrums") });
        }

        private void Initialize(Component array, IDictionary ordering, int lanes, GameObject kickPrefab,
            ColorProfile.IFretColorProvider colors, object style, bool dualHalf)
        {
            var infoType = _gameAssembly.GetType("YARG.Gameplay.Visuals.HighwayOrderingInfo", true);
            var signatureType = typeof(Dictionary<,>).MakeGenericType(typeof(int), infoType);
            var convertedOrdering = Activator.CreateInstance(signatureType);
            var add = signatureType.GetMethod("Add");
            foreach (DictionaryEntry entry in ordering)
            {
                int position;
                int colorIndex;
                if (entry.Value is int lanePosition)
                {
                    position = lanePosition;
                    colorIndex = (int)entry.Key;
                }
                else
                {
                    position = (int)entry.Value.GetType().GetProperty("Position").GetValue(entry.Value);
                    colorIndex = (int)entry.Value.GetType().GetProperty("ColorIndex").GetValue(entry.Value);
                }

                add.Invoke(convertedOrdering, new[]
                {
                    entry.Key,
                    Activator.CreateInstance(infoType, position, colorIndex)
                });
            }

            var method = array.GetType().GetMethods(INSTANCE_FLAGS)
                .Single(candidate => candidate.Name == "Initialize"
                    && candidate.GetParameters().Length == 7
                    && candidate.GetParameters()[0].ParameterType == signatureType);
            method.Invoke(array, new object[]
            {
                convertedOrdering, lanes, kickPrefab, colors, _themePreset, style, dualHalf
            });
        }

        private object CreateOrderingInfo(int position, int colorIndex)
        {
            var infoType = _gameAssembly.GetType("YARG.Gameplay.Visuals.HighwayOrderingInfo", true);
            return Activator.CreateInstance(infoType, position, colorIndex);
        }

        private static void Recolor(Component array, ColorProfile.IFretColorProvider provider, Func<int, int> remap = null)
        {
            array.GetType().GetMethod("RecolorFrets", INSTANCE_FLAGS).Invoke(array, new object[] { provider, remap });
        }

        private static void AssertOverlayShader(SpriteRenderer[] renderers)
        {
            var shader = Shader.Find("Sprites-Default-Overlay");
            Assert.That(shader, Is.Not.Null, "The highway-aware sprite shader must be included in the project.");
            Assert.That(renderers, Is.Not.Empty);
            foreach (var renderer in renderers)
            {
                Assert.That(renderer.sharedMaterial, Is.Not.Null, renderer.name);
                Assert.That(renderer.sharedMaterial.shader, Is.SameAs(shader), renderer.name);
            }
        }

        private static OverlayGeometry CaptureOverlayGeometry(SpriteRenderer renderer)
        {
            return new OverlayGeometry(
                renderer.gameObject.name,
                renderer.transform.localPosition,
                renderer.transform.localRotation,
                renderer.transform.localScale,
                renderer.sprite,
                renderer.sprite.textureRect);
        }

        private static void AssertOverlayGeometryUnchanged(SpriteRenderer[] renderers, OverlayGeometry[] expected)
        {
            Assert.That(renderers.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < renderers.Length; i++)
            {
                Assert.That(CaptureOverlayGeometry(renderers[i]), Is.EqualTo(expected[i]), renderers[i].name);
            }
        }

        private readonly struct OverlayGeometry : IEquatable<OverlayGeometry>
        {
            public OverlayGeometry(string name, Vector3 position, Quaternion rotation, Vector3 scale, Sprite sprite, Rect textureRect)
            {
                Name = name;
                Position = position;
                Rotation = rotation;
                Scale = scale;
                Sprite = sprite;
                TextureRect = textureRect;
            }

            private string Name { get; }
            private Vector3 Position { get; }
            private Quaternion Rotation { get; }
            private Vector3 Scale { get; }
            private Sprite Sprite { get; }
            private Rect TextureRect { get; }

            public bool Equals(OverlayGeometry other) => Name == other.Name
                && Position == other.Position
                && Rotation == other.Rotation
                && Scale == other.Scale
                && Sprite == other.Sprite
                && TextureRect == other.TextureRect;
        }

        private static IEnumerable<Material> GetColoredMaterials(Component bind)
        {
            var themeBind = bind.GetType().GetProperty("ThemeBind", INSTANCE_FLAGS).GetValue(bind);
            return (IEnumerable<Material>)themeBind.GetType()
                .GetMethod("GetColoredMaterials", INSTANCE_FLAGS).Invoke(themeBind, null);
        }

        private static void AssertColorApproximately(UnityEngine.Color actual, UnityEngine.Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.01f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.01f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.01f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.01f));
        }

        private static void AssertMaterialColors(Component fret, UnityEngine.Color expectedTop, UnityEngine.Color expectedInner)
        {
            var top = GetList(GetField<object>(fret, "_topMaterials")).Cast<Material>().Single();
            var inner = GetList(GetField<object>(fret, "_innerMaterials")).Cast<Material>().Single();
            AssertColorApproximately(top.color, expectedTop);
            AssertColorApproximately(inner.color, expectedInner);
        }

        private static void AssertSecondaryInnerColor(Component fret, UnityEngine.Color expected)
        {
            var materials = GetList(GetField<object>(fret, "_secondaryInnerMaterials")).Cast<Material>().ToArray();
            Assert.That(materials, Is.Not.Empty);
            AssertColorApproximately(materials[0].GetColor("_SecondaryColor"), expected);
        }

        private static int GetListCount(Component component, string name) => ((IList)GetField<object>(component, name)).Count;

        private static void Invoke(object target, string methodName, params object[] arguments)
        {
            var method = target.GetType().GetMethod(methodName, INSTANCE_FLAGS);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(target, arguments);
        }

        private static void SetAutoProperty(object target, string propertyName, object value)
        {
            var backingField = target.GetType().GetField($"<{propertyName}>k__BackingField", INSTANCE_FLAGS);
            Assert.That(backingField, Is.Not.Null, propertyName);
            backingField.SetValue(target, value);
        }

        private static IList GetList(object value) => (IList)value;

        private static T GetField<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, INSTANCE_FLAGS);
            if (field != null)
            {
                return (T)field.GetValue(target);
            }

            return (T)target.GetType().GetProperty(name, INSTANCE_FLAGS).GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, INSTANCE_FLAGS | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }

            throw new MissingFieldException(target.GetType().FullName, name);
        }
    }
}
