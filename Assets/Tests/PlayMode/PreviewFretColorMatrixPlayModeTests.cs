using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Game;
using Object = UnityEngine.Object;

namespace YARG.Tests.PlayMode
{
    public sealed class PreviewFretColorMatrixPlayModeTests
    {
        private readonly List<Object> _owned = new();
        private Component _themeManager;
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private static void Field(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
        private static object GetField(object target, string name) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        private static void SetProperty(object target, string name, object value) => target.GetType()
            .GetProperty(name).SetValue(target, value);
        private static Color ToUnityColor(System.Drawing.Color color) => new(
            color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        private static void AssertColor(Color actual, Color expected, string message)
        {
            const float TOLERANCE = 1f / 255f;
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(TOLERANCE), message + " red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(TOLERANCE), message + " green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(TOLERANCE), message + " blue");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(TOLERANCE), message + " alpha");
        }
        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var value in _owned) if (value != null) Object.Destroy(value);
            _owned.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator PhysicalFrets_UseSelectedProvidersKeepAliasesAndRefreshBothSixFretHalves()
        {
            var modes = new[]
            {
                ("FiveFretGuitar", "FiveFretGuitar", 5, false),
                ("SixFretGuitar", "SixFretGuitar", 3, true),
                ("FourLaneDrums", "FourLaneDrums", 4, false),
                ("FiveLaneDrums", "FiveLaneDrums", 5, false),
                ("ProKeysCompressed", "FiveFretGuitar", 5, false),
            };

            foreach (var (mode, providerName, physicalCount, dual) in modes)
            {
                var root = Own(new GameObject("Fret matrix " + mode));
                var array = root.AddComponent(Production("YARG.Gameplay.Visuals.FretArray"));
                var profileA = new ColorProfile("selected " + mode);
                var profileB = new ColorProfile("replacement " + mode);
                ConfigureDistinctColors(profileA, providerName, System.Drawing.Color.Magenta);
                ConfigureDistinctColors(profileB, providerName, System.Drawing.Color.Lime);
                bool compressedKeys = mode == "ProKeysCompressed";
                var provider = ResolveProvider(compressedKeys ? "ProKeys" : providerName, profileA);
                if (compressedKeys)
                    provider = ResolveProvider("FiveFretGuitar", profileA);
                var ordering = Ordering(mode);
                SetKickPositions(array);
                var initialize = array.GetType().GetMethods().Single(method => method.Name == "Initialize"
                    && method.GetParameters().Length == 6 && method.GetParameters()[4].ParameterType == typeof(GameObject));
                var styleName = mode switch
                {
                    "FiveFretGuitar" or "ProKeysCompressed" => "FiveFretGuitar",
                    "SixFretGuitar" => "SixFretGuitar",
                    "FourLaneDrums" => "FourLaneDrums",
                    _ => "FiveLaneDrums"
                };
                var theme = CreateThemeFretPrefab(styleName);
                initialize.Invoke(array, new[] { ordering, (object)(mode == "SixFretGuitar" ? 3 : mode == "FourLaneDrums" ? 4 : 5), null, provider, theme, dual });

                var frets = root.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret"));
                Assert.That(frets.Length, Is.EqualTo(physicalCount), mode + " creates unique physical frets");
                int expectedAliases = mode == "SixFretGuitar" ? 6 : mode == "FourLaneDrums" ? 7 : physicalCount;
                Assert.That(((IDictionary)GetField(array, "_frets")).Count, Is.EqualTo(expectedAliases), mode + " retains input aliases");
                var first = (Component)((IDictionary)GetField(array, "_frets"))[1];
                var bind = first.GetType().GetProperty("ThemeBind").GetValue(first);
                var top = ((IEnumerable)bind.GetType().GetMethod("GetColoredMaterials").Invoke(bind, null)).Cast<Material>().ToArray();
                Assert.That(top.Length, Is.EqualTo(1));
                var firstBody = top[0];
                var expectedA = (ColorProfile.IFretColorProvider)provider;
                var infoType = Production("YARG.Gameplay.Visuals.HighwayOrderingInfo");
                int selectedColorIndex = 1;
                int secondaryColorIndex = 4;
                if (dual)
                {
                    foreach (DictionaryEntry entry in (IDictionary)ordering)
                    {
                        var info = entry.Value;
                        if ((int)infoType.GetProperty("Position").GetValue(info) == 0)
                        {
                            selectedColorIndex = (int)infoType.GetProperty("ColorIndex").GetValue(info);
                            break;
                        }
                    }
                    secondaryColorIndex = selectedColorIndex == 1 ? 4 : 1;
                }
                AssertColor(firstBody.color, ToUnityColor(expectedA.GetFretColor(selectedColorIndex)), mode + " initializes from selected profile");
                var inner = ((IEnumerable)bind.GetType().GetMethod("GetInnerColoredMaterials").Invoke(bind, null)).Cast<Material>().Single();
                AssertColor(inner.color, ToUnityColor(expectedA.GetFretInnerColor(selectedColorIndex)), mode + " initializes inner from selected profile");

                var initialPosition = first.transform.localPosition;
                var initialScale = first.transform.localScale;
                var cacheField = first.GetType().GetField("_topMaterials", BindingFlags.Instance | BindingFlags.NonPublic);
                var secondaryCache = first.GetType().GetField("_secondaryTopMaterials", BindingFlags.Instance | BindingFlags.NonPublic);
                int primaryCount = ((ICollection)cacheField.GetValue(first)).Count;
                int secondaryCount = ((ICollection)secondaryCache.GetValue(first)).Count;
                var recolor = array.GetType().GetMethod("RecolorFrets");
                var secondProvider = ResolveProvider(compressedKeys ? "FiveFretGuitar" : providerName, profileB);
                recolor.Invoke(array, new object[] { secondProvider, null });
                var expectedB = (ColorProfile.IFretColorProvider)secondProvider;
                AssertColor(firstBody.color, ToUnityColor(expectedB.GetFretColor(selectedColorIndex)), mode + " applies replacement profile");
                AssertColor(inner.color, ToUnityColor(expectedB.GetFretInnerColor(selectedColorIndex)), mode + " replaces inner profile color");
                for (int iteration = 0; iteration < 4; iteration++)
                {
                    recolor.Invoke(array, new object[] { provider, null });
                    recolor.Invoke(array, new object[] { secondProvider, null });
                }
                Assert.That(first.transform.localPosition, Is.EqualTo(initialPosition), "color changes preserve geometry");
                Assert.That(first.transform.localScale, Is.EqualTo(initialScale), "color changes preserve geometry");
                Assert.That(((ICollection)cacheField.GetValue(first)).Count, Is.EqualTo(primaryCount));
                Assert.That(((ICollection)secondaryCache.GetValue(first)).Count, Is.EqualTo(secondaryCount));
                if (dual)
                {
                    var secondary = ((IEnumerable)bind.GetType().GetMethod("GetSecondaryColoredMaterials").Invoke(bind, null)).Cast<Material>().Single();
                    AssertColor(secondary.color, ToUnityColor(profileB.SixFretGuitar.GetFretColor(secondaryColorIndex)), "Six-Fret replacement applies to secondary half");
                    Assert.That(secondaryCount, Is.EqualTo(1));
                    var allPositions = frets.Cast<Component>().Select(fret => fret.transform.localPosition.x).ToArray();
                    Assert.That(allPositions.Distinct().Count(), Is.EqualTo(3));
                    Assert.That(allPositions[0], Is.EqualTo(-2f / 3f).Within(0.001f));
                    Assert.That(allPositions[1], Is.EqualTo(0f).Within(0.001f));
                    Assert.That(allPositions[2], Is.EqualTo(2f / 3f).Within(0.001f));
                }
                Assert.That(ColorProfile.Default.FiveFretGuitar.GetFretColor(1), Is.Not.EqualTo(System.Drawing.Color.Magenta));
                Assert.That(ColorProfile.Default.SixFretGuitar.BlackFret, Is.Not.EqualTo(System.Drawing.Color.Magenta));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator FakeTrackPlayerInitializeAndApplySettings_UseRegisteredProvidersWithoutRebuilding()
        {
            var cases = new[]
            {
                (GameMode.FiveFretGuitar, false, "FiveFretGuitar", 5, false),
                (GameMode.SixFretGuitar, false, "SixFretGuitar", 3, true),
                (GameMode.FourLaneDrums, false, "FourLaneDrums", 4, false),
                (GameMode.FiveLaneDrums, false, "FiveLaneDrums", 5, false),
                (GameMode.ProKeys, true, "FiveFretGuitar", 5, false),
                (GameMode.ProKeys, false, "ProKeys", 0, false),
            };

            foreach (var (mode, fiveLaneKeys, providerName, expectedPhysical, dual) in cases)
            {
                var root = Own(new GameObject("Registered preview " + mode + " " + fiveLaneKeys));
                var player = root.AddComponent(Production("YARG.Settings.Preview.FakeTrackPlayer"));
                ((Behaviour)player).enabled = false;
                SetProperty(player, "SelectedGameMode", mode);
                SetProperty(player, "UseFiveLaneKeys", fiveLaneKeys);
                var fretRoot = new GameObject("Registered frets");
                fretRoot.transform.SetParent(root.transform);
                var fretArray = fretRoot.AddComponent(Production("YARG.Gameplay.Visuals.FretArray"));
                Field(player, "_fretArray", fretArray);
                SetKickPositions(fretArray);

                var poolObject = new GameObject("Registered zero-prewarm pool");
                poolObject.transform.SetParent(root.transform);
                poolObject.SetActive(false);
                var pool = poolObject.AddComponent(Production("YARG.Gameplay.KeyedPool"));
                var poolBase = pool.GetType().BaseType;
                poolBase.GetField("_prewarmAmount", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pool, 0);
                poolBase.GetField("_objectCap", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pool, 100);
                poolObject.SetActive(true);

                var profile = new ColorProfile("registered initial " + mode);
                var replacement = new ColorProfile("registered replacement " + mode);
                ConfigureDistinctColors(profile, providerName, System.Drawing.Color.Magenta);
                ConfigureDistinctColors(replacement, providerName, System.Drawing.Color.Lime);
                if (providerName == "ProKeys")
                {
                    profile.ProKeys.RedOverlay = System.Drawing.Color.Magenta;
                    replacement.ProKeys.RedOverlay = System.Drawing.Color.Lime;
                }
                var registeredInfo = ResolveRegisteredInfo(player, fiveLaneKeys ? GameMode.FiveFretGuitar : mode, fiveLaneKeys);
                var providerField = registeredInfo.GetType().GetField("FretColorProvider");
                var registeredProvider = providerField.GetValue(registeredInfo) as Delegate;
                if (expectedPhysical > 0)
                    Assert.That(registeredProvider.DynamicInvoke(profile), Is.SameAs(ResolveProvider(providerName, profile)), "registered provider routing");

                var styleName = mode == GameMode.SixFretGuitar ? "SixFretGuitar"
                    : mode == GameMode.FourLaneDrums ? "FourLaneDrums"
                    : mode == GameMode.FiveLaneDrums ? "FiveLaneDrums" : "FiveFretGuitar";
                var theme = CreateThemeFretPrefab(styleName);
                var factoryType = typeof(Func<,,>).MakeGenericType(Production("YARG.Themes.ThemePreset"), Production("YARG.Themes.VisualStyle"), typeof(GameObject));
                var fakeNotePrefab = Own(new GameObject("Injected note model"));
                var factory = BuildConstantFactory(factoryType, fakeNotePrefab);
                var initializerType = typeof(Action<,,>).MakeGenericType(registeredInfo.GetType(), Production("YARG.Themes.ThemePreset"), Production("YARG.Themes.VisualStyle"));
                var kickPrefab = mode == GameMode.FourLaneDrums || mode == GameMode.FiveLaneDrums ? CreateKickFretPrefab() : null;
                var initializeFrets = BuildRegisteredFretInitializer(initializerType, fretArray, theme, profile, dual, kickPrefab);
                var themePreset = Production("YARG.Themes.ThemePreset").GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null);

                var initializeArguments = new object[]
                {
                    themePreset, pool, factory, CameraPreset.Default, profile, EnginePreset.Default,
                    new HighwayPreset("registered preview"), false, mode == GameMode.ProKeys && !fiveLaneKeys ? null : initializeFrets
                };
                if (mode == GameMode.ProKeys && !fiveLaneKeys)
                    Field(player, "_proKeysEdgeTexture", Own(new Texture2D(1, 1)));
                player.GetType().GetMethod("Initialize").Invoke(player, initializeArguments);

                var info = player.GetType().GetProperty("CurrentGameModeInfo").GetValue(player);
                Assert.That(info.GetType(), Is.EqualTo(registeredInfo.GetType()), "Initialize selects compatible mode registry info");
                if (fiveLaneKeys)
                    Assert.That(info.GetType().GetField("FretColorProvider").GetValue(info), Is.Not.Null, "compressed keys uses resolved guitar provider");
                var generator = info.GetType().GetField("Generator").GetValue(info);
                var timeBefore = (double)player.GetType().GetProperty("PreviewTime").GetValue(player);
                var poolBefore = GetField(player, "_notePool");
                var frets = fretRoot.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret"));
                Assert.That(frets.Length, Is.EqualTo(expectedPhysical));
                var aliases = (IDictionary)GetField(fretArray, "_frets");
                if (expectedPhysical > 0)
                    Assert.That(aliases.Count, Is.EqualTo(mode == GameMode.SixFretGuitar ? 6 : mode == GameMode.FourLaneDrums ? 7 : 5));
                var initialIds = frets.Select(fret => fret.GetInstanceID()).ToArray();
                var initialPositions = frets.Select(fret => fret.transform.localPosition).ToArray();

                if (expectedPhysical > 0)
                {
                    int noteKey = mode == GameMode.SixFretGuitar ? 1 : 1;
                    var fret = (Component)aliases[noteKey];
                    var bind = fret.GetType().GetProperty("ThemeBind").GetValue(fret);
                    var body = ((IEnumerable)bind.GetType().GetMethod("GetColoredMaterials").Invoke(bind, null)).Cast<Material>().Single();
                    var inner = ((IEnumerable)bind.GetType().GetMethod("GetInnerColoredMaterials").Invoke(bind, null)).Cast<Material>().Single();
                    int expectedIndex = noteKey;
                    AssertColor(body.color, ToUnityColor(((ColorProfile.IFretColorProvider)ResolveProvider(providerName, profile)).GetFretColor(expectedIndex)), "Initialize selected profile body");
                    AssertColor(inner.color, ToUnityColor(((ColorProfile.IFretColorProvider)ResolveProvider(providerName, profile)).GetFretInnerColor(expectedIndex)), "Initialize selected profile inner");
                    if (dual)
                    {
                        var secondary = ((IEnumerable)bind.GetType().GetMethod("GetSecondaryColoredMaterials").Invoke(bind, null)).Cast<Material>().Single();
                        Assert.That(((ICollection)fret.GetType().GetField("_secondaryTopMaterials", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fret)).Count, Is.EqualTo(1));
                        AssertColor(secondary.color, ToUnityColor(profile.SixFretGuitar.GetFretColor(3)), "Initialize selected white half");
                    }
                    profile.FiveFretGuitar.GreenFret = System.Drawing.Color.Cyan;
                    profile.SixFretGuitar.BlackFret = System.Drawing.Color.Cyan;
                    profile.SixFretGuitar.WhiteFret = System.Drawing.Color.Cyan;
                    profile.FourLaneDrums.RedFret = System.Drawing.Color.Cyan;
                    profile.FiveLaneDrums.RedFret = System.Drawing.Color.Cyan;
                    player.GetType().GetMethod("ApplySettings").Invoke(player, new object[] { CameraPreset.Default, profile, EnginePreset.Default, new HighwayPreset("in-place edit") });
                    AssertColor(body.color, ToUnityColor(((ColorProfile.IFretColorProvider)ResolveProvider(providerName, profile)).GetFretColor(expectedIndex)), "ApplySettings in-place edit");
                    player.GetType().GetMethod("ApplySettings").Invoke(player, new object[] { CameraPreset.Default, replacement, EnginePreset.Default, new HighwayPreset("replacement") });
                    AssertColor(body.color, ToUnityColor(((ColorProfile.IFretColorProvider)ResolveProvider(providerName, replacement)).GetFretColor(expectedIndex)), "ApplySettings replacement provider");
                    if (dual)
                    {
                        var secondary = ((IEnumerable)bind.GetType().GetMethod("GetSecondaryColoredMaterials").Invoke(bind, null)).Cast<Material>().Single();
                        AssertColor(secondary.color, ToUnityColor(replacement.SixFretGuitar.GetFretColor(3)), "ApplySettings replacement white half");
                    }
                }
                else
                {
                    var renderers = root.GetComponentsInChildren<SpriteRenderer>();
                    Assert.That(renderers.Length, Is.GreaterThan(0), "registry-selected piano overlay exists");
                    var before = renderers.Select(renderer => renderer.color).ToArray();
                    Assert.That(before.Any(color => color.r > color.b), Is.True, "initial profile overlay color applied");
                    profile.ProKeys.RedOverlay = System.Drawing.Color.Cyan;
                    player.GetType().GetMethod("ApplySettings").Invoke(player, new object[] { CameraPreset.Default, profile, EnginePreset.Default, new HighwayPreset("piano edit") });
                    Assert.That(renderers.Any(renderer => renderer.color.g > renderer.color.r), Is.True, "ApplySettings recolors piano overlay");
                }

                Assert.That(frets.Select(fret => fret.GetInstanceID()), Is.EqualTo(initialIds), "no fret reconstruction on profile changes");
                Assert.That(frets.Select(fret => fret.transform.localPosition), Is.EqualTo(initialPositions), "no geometry changes on profile updates");
                Assert.That(player.GetType().GetProperty("PreviewTime").GetValue(player), Is.EqualTo(timeBefore), "ApplySettings does not reset preview time");
                Assert.That(GetField(player, "_notePool"), Is.SameAs(poolBefore), "ApplySettings preserves pool instance");
                Assert.That(info.GetType().GetField("Generator").GetValue(info), Is.SameAs(generator), "ApplySettings preserves generator");
                Assert.That(ColorProfile.Default.FiveFretGuitar.GetFretColor(1), Is.Not.EqualTo(System.Drawing.Color.Cyan));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator FiveFretLeftyRemap_AppliesColorProviderWithoutMovingPhysicalFret()
        {
            var theme = CreateThemeFretPrefab();
            var root = Own(new GameObject("Lefty guitar fret"));
            var array = root.AddComponent(Production("YARG.Gameplay.Visuals.FretArray"));
            var ordering = Ordering("FiveFretGuitar");
            var profile = new ColorProfile("lefty policy");
            profile.FiveFretGuitar.GreenFret = System.Drawing.Color.Magenta;
            profile.FiveFretGuitar.OrangeFret = System.Drawing.Color.Cyan;
            var initialize = array.GetType().GetMethods().Single(method => method.Name == "Initialize"
                && method.GetParameters().Length == 6 && method.GetParameters()[4].ParameterType == typeof(GameObject));
            initialize.Invoke(array, new object[] { ordering, 5, null, profile.FiveFretGuitar, theme, false });
            var frets = root.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret")).Cast<Component>().ToArray();
            var byX = frets.OrderBy(fret => fret.transform.localPosition.x).ToArray();
            var left = byX[0];
            var originalPosition = left.transform.localPosition;
            var body = ((IEnumerable)left.GetType().GetProperty("ThemeBind").GetValue(left)
                .GetType().GetMethod("GetColoredMaterials").Invoke(left.GetType().GetProperty("ThemeBind").GetValue(left), null)).Cast<Material>().Single();
            array.GetType().GetMethod("RecolorFrets").Invoke(array, new object[] { profile.FiveFretGuitar, new Func<int, int>(index => 6 - index) });
            Assert.That(body.color, Is.EqualTo(ToUnityColor(profile.FiveFretGuitar.GetFretColor(5))));
            Assert.That(left.transform.localPosition, Is.EqualTo(originalPosition));
            Assert.That(frets.Select(fret => fret.GetInstanceID()).Distinct().Count(), Is.EqualTo(5));
            yield return null;
        }

        [UnityTest]
        public IEnumerator IndependentArrays_AndPreviewColorEdit_PreserveDefaultsTimeGeneratorAndPool()
        {
            var theme = CreateThemeFretPrefab();
            var firstRoot = Own(new GameObject("Independent preview A"));
            var secondRoot = Own(new GameObject("Independent preview B"));
            var firstArray = firstRoot.AddComponent(Production("YARG.Gameplay.Visuals.FretArray"));
            var secondArray = secondRoot.AddComponent(Production("YARG.Gameplay.Visuals.FretArray"));
            var ordering = Ordering("FiveFretGuitar");
            var profileA = new ColorProfile("independent A");
            var profileB = new ColorProfile("independent B");
            profileA.FiveFretGuitar.GreenFret = System.Drawing.Color.Magenta;
            profileB.FiveFretGuitar.GreenFret = System.Drawing.Color.Cyan;
            var initialize = firstArray.GetType().GetMethods().Single(method => method.Name == "Initialize"
                && method.GetParameters().Length == 6 && method.GetParameters()[4].ParameterType == typeof(GameObject));
            initialize.Invoke(firstArray, new object[] { ordering, 5, null, profileA.FiveFretGuitar, theme, false });
            initialize.Invoke(secondArray, new object[] { ordering, 5, null, profileB.FiveFretGuitar, theme, false });
            var firstBody = ((IEnumerable)firstArray.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret"))[0]
                .GetType().GetProperty("ThemeBind").GetValue(firstArray.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret"))[0])
                .GetType().GetMethod("GetColoredMaterials").Invoke(firstArray.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret"))[0]
                    .GetType().GetProperty("ThemeBind").GetValue(firstArray.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret"))[0]), null)).Cast<Material>().Single();
            var secondBody = ((IEnumerable)secondArray.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret"))[0]
                .GetType().GetProperty("ThemeBind").GetValue(secondArray.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret"))[0])
                .GetType().GetMethod("GetColoredMaterials").Invoke(secondArray.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret"))[0]
                    .GetType().GetProperty("ThemeBind").GetValue(secondArray.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret"))[0]), null)).Cast<Material>().Single();
            Assert.That(firstBody.color, Is.Not.EqualTo(secondBody.color));
            Assert.That(ColorProfile.Default.FiveFretGuitar.GetFretColor(1), Is.Not.EqualTo(System.Drawing.Color.Magenta));

            var playerRoot = Own(new GameObject("Player preview state"));
            var player = playerRoot.AddComponent(Production("YARG.Settings.Preview.FakeTrackPlayer"));
            ((Behaviour)player).enabled = false;
            Field(player, "_fretArray", firstArray);
            SetProperty(player, "SelectedGameMode", GameMode.FiveFretGuitar);
            var poolObject = new GameObject("Zero prewarm keyed pool");
            poolObject.transform.SetParent(playerRoot.transform);
            poolObject.SetActive(false);
            var pool = poolObject.AddComponent(Production("YARG.Gameplay.KeyedPool"));
            var poolBase = pool.GetType().BaseType;
            poolBase.GetField("_prewarmAmount", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pool, 0);
            poolObject.SetActive(true);
            Field(player, "_notePool", pool);
            var modes = (IDictionary)player.GetType().GetField("_gameModeInfos", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var info = modes[GameMode.FiveFretGuitar];
            SetProperty(player, "CurrentGameModeInfo", info);
            var generator = info.GetType().GetField("Generator").GetValue(info);
            var timeBefore = (double)player.GetType().GetProperty("PreviewTime").GetValue(player);
            var poolBefore = GetField(player, "_notePool");
            var apply = player.GetType().GetMethod("ApplySettings");
            apply.Invoke(player, new object[] { CameraPreset.Default, profileA, EnginePreset.Default, new HighwayPreset("test") });
            Assert.That(player.GetType().GetProperty("PreviewTime").GetValue(player), Is.EqualTo(timeBefore));
            Assert.That(GetField(player, "_notePool"), Is.SameAs(poolBefore));
            var infoAfter = player.GetType().GetProperty("CurrentGameModeInfo").GetValue(player);
            Assert.That(infoAfter.GetType().GetField("Generator").GetValue(infoAfter), Is.SameAs(generator));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PianoProKeysOverlay_UsesSelectedInitialAndReplacementColors()
        {
            var root = Own(new GameObject("Piano overlay preview"));
            var player = root.AddComponent(Production("YARG.Settings.Preview.FakeTrackPlayer"));
            ((Behaviour)player).enabled = false;
            var create = player.GetType().GetMethod("CreateProKeysOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
            var recolor = player.GetType().GetMethod("RecolorProKeysOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
            var profile = new ColorProfile("piano selected overlay");
            profile.ProKeys.RedOverlay = System.Drawing.Color.Magenta;
            create.Invoke(player, new object[] { profile.ProKeys });
            var renderers = root.GetComponentsInChildren<SpriteRenderer>();
            Assert.That(renderers.Length, Is.GreaterThan(0));
            Assert.That(renderers.Any(renderer => renderer.color.r > renderer.color.b), Is.True);
            profile.ProKeys.RedOverlay = System.Drawing.Color.Cyan;
            recolor.Invoke(player, new object[] { profile.ProKeys });
            Assert.That(renderers.Any(renderer => renderer.color.g > renderer.color.r), Is.True);
            Assert.That(ColorProfile.Default.ProKeys.RedOverlay, Is.Not.EqualTo(System.Drawing.Color.Cyan));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SuppliedKickFrets_RecolorFromReplacementProvider()
        {
            var theme = CreateThemeFretPrefab();
            var kickPrefab = CreateKickFretPrefab();
            var root = Own(new GameObject("Kick fret matrix"));
            var array = root.AddComponent(Production("YARG.Gameplay.Visuals.FretArray"));
            SetKickPositions(array);
            Field(array, "UseKickFrets", true);
            var profile = new ColorProfile("kick selected");
            profile.FourLaneDrums.KickFret = System.Drawing.Color.Cyan;
            var ordering = Ordering("FourLaneDrums");
            var initialize = array.GetType().GetMethods().Single(method => method.Name == "Initialize"
                && method.GetParameters().Length == 6 && method.GetParameters()[4].ParameterType == typeof(GameObject));
            initialize.Invoke(array, new object[] { ordering, 4, kickPrefab, profile.FourLaneDrums, theme, false });
            var kickFrets = ((IEnumerable)GetField(array, "_kickFrets")).Cast<Component>().ToArray();
            Assert.That(kickFrets.Length, Is.EqualTo(2));
            var kickBind = kickFrets[0].GetType().GetProperty("ThemeBind").GetValue(kickFrets[0]);
            var material = ((IEnumerable)kickBind.GetType().GetMethod("GetColoredMaterials").Invoke(kickBind, null)).Cast<Material>().Single();
            Assert.That(material.color, Is.EqualTo(Color.cyan));
            profile.FourLaneDrums.KickFret = System.Drawing.Color.Orange;
            array.GetType().GetMethod("RecolorFrets").Invoke(array, new object[] { profile.FourLaneDrums, null });
            AssertColor(material.color, ToUnityColor(System.Drawing.Color.Orange), "kick uses selected profile orange");
            yield return null;
        }

        private GameObject CreateThemeFretPrefab(string styleName = "FiveLaneDrums")
        {
            if (_themeManager == null)
            {
                var managerObject = Own(new GameObject("Production theme manager"));
                managerObject.SetActive(false);
                _themeManager = managerObject.AddComponent(Production("YARG.Themes.ThemeManager"));
                managerObject.SetActive(true);
                ((Behaviour)_themeManager).enabled = false;
                _themeManager.GetType().GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_themeManager, null);
            }
            var themePreset = Production("YARG.Themes.ThemePreset").GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var prefab = Own((GameObject)_themeManager.GetType().GetMethod("CreateFretPrefabFromTheme").Invoke(_themeManager,
                new[] { themePreset, Enum.Parse(Production("YARG.Themes.VisualStyle"), styleName), "fret" }));
            prefab.SetActive(false);
            return prefab;
        }

        private GameObject CreateKickFretPrefab()
        {
            var kick = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var renderer = kick.GetComponent<MeshRenderer>();
            var material = Own(new Material(Shader.Find("Standard")));
            renderer.sharedMaterial = material;
            var bind = kick.AddComponent(Production("YARG.Themes.ThemeKickFret"));
            var indexType = Production("YARG.Themes.MeshMaterialIndex");
            var index = Activator.CreateInstance(indexType);
            Field(index, "Mesh", renderer);
            Field(index, "MaterialIndex", 0);
            var indexes = Array.CreateInstance(indexType, 1);
            indexes.SetValue(index, 0);
            Field(bind, "_coloredMaterials", indexes);
            var kickFret = kick.AddComponent(Production("YARG.Gameplay.Visuals.KickFret"));
            kickFret.GetType().GetProperty("ThemeBind").SetValue(kickFret, bind);
            return kick;
        }

        private static object ResolveRegisteredInfo(Component player, GameMode mode, bool fiveLaneKeys)
        {
            var infos = (IDictionary)player.GetType().GetField("_gameModeInfos", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            return infos[mode];
        }

        private static Delegate BuildConstantFactory(Type delegateType, GameObject prefab)
        {
            var parameters = delegateType.GetMethod("Invoke").GetParameters()
                .Select(parameter => System.Linq.Expressions.Expression.Parameter(parameter.ParameterType)).ToArray();
            var body = System.Linq.Expressions.Expression.Constant(prefab);
            return System.Linq.Expressions.Expression.Lambda(delegateType, body, parameters).Compile();
        }

        private static Delegate BuildRegisteredFretInitializer(Type delegateType, Component array, GameObject prefab,
            ColorProfile profile, bool dualHalf, GameObject kickPrefab)
        {
            var parameters = delegateType.GetMethod("Invoke").GetParameters()
                .Select(parameter => System.Linq.Expressions.Expression.Parameter(parameter.ParameterType)).ToArray();
            var method = typeof(PreviewFretColorMatrixPlayModeTests).GetMethod(nameof(InitializeRegisteredFrets), BindingFlags.Static | BindingFlags.NonPublic);
            var body = System.Linq.Expressions.Expression.Call(method,
                System.Linq.Expressions.Expression.Constant(array), System.Linq.Expressions.Expression.Convert(parameters[0], typeof(object)),
                System.Linq.Expressions.Expression.Constant(prefab), System.Linq.Expressions.Expression.Constant(profile),
                System.Linq.Expressions.Expression.Constant(dualHalf), System.Linq.Expressions.Expression.Constant(kickPrefab, typeof(GameObject)));
            return System.Linq.Expressions.Expression.Lambda(delegateType, body, parameters).Compile();
        }

        private static void InitializeRegisteredFrets(Component array, object info, GameObject prefab,
            ColorProfile profile, bool dualHalf, GameObject kickPrefab)
        {
            var infoType = info.GetType();
            bool overlay = (bool)infoType.GetField("UseHighwayOverlay").GetValue(info);
            var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(int), typeof(int));
            var registryOrdering = overlay ? Activator.CreateInstance(dictionaryType) : infoType.GetField("HighwayOrdering").GetValue(info);
            object ordering = registryOrdering;
            if (!overlay)
            {
                var orderingInfoType = Production("YARG.Gameplay.Visuals.HighwayOrderingInfo");
                var typedDictionary = typeof(Dictionary<,>).MakeGenericType(typeof(int), orderingInfoType);
                var converted = (IDictionary)Activator.CreateInstance(typedDictionary);
                foreach (DictionaryEntry entry in (IDictionary)registryOrdering)
                    converted.Add(entry.Key, Activator.CreateInstance(orderingInfoType, entry.Value, entry.Key));
                ordering = converted;
            }
            int laneCount = overlay ? 1 : (int)infoType.GetField("LaneCount").GetValue(info);
            bool useKickFrets = !overlay && (bool)infoType.GetField("UseKickFrets").GetValue(info);
            Field(array, "UseKickFrets", useKickFrets);
            var providerFunc = infoType.GetField("FretColorProvider").GetValue(info) as Delegate;
            var provider = providerFunc?.DynamicInvoke(profile) as ColorProfile.IFretColorProvider;
            var initialize = array.GetType().GetMethods().Single(method => method.Name == "Initialize"
                && method.GetParameters().Length == 6 && method.GetParameters()[4].ParameterType == typeof(GameObject));
            initialize.Invoke(array, new object[] { ordering, laneCount, useKickFrets ? kickPrefab : null, provider, prefab, dualHalf });
        }

        private static object ResolveProvider(string providerName, ColorProfile profile)
        {
            return providerName switch
            {
                "FiveFretGuitar" => profile.FiveFretGuitar,
                "SixFretGuitar" => profile.SixFretGuitar,
                "FourLaneDrums" => profile.FourLaneDrums,
                "FiveLaneDrums" => profile.FiveLaneDrums,
                "ProKeys" => profile.ProKeys,
                _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
            };
        }

        private static void ConfigureDistinctColors(ColorProfile profile, string providerName, System.Drawing.Color color)
        {
            if (providerName == "FiveFretGuitar")
            {
                profile.FiveFretGuitar.GreenFret = color;
                profile.FiveFretGuitar.GreenFretInner = System.Drawing.Color.Coral;
                profile.FiveFretGuitar.GreenParticles = System.Drawing.Color.Yellow;
            }
            else if (providerName == "SixFretGuitar")
            {
                profile.SixFretGuitar.BlackFret = color;
                profile.SixFretGuitar.WhiteFret = color;
                profile.SixFretGuitar.BlackFretInner = System.Drawing.Color.Coral;
                profile.SixFretGuitar.WhiteFretInner = System.Drawing.Color.Coral;
            }
            else if (providerName == "FourLaneDrums") profile.FourLaneDrums.RedFret = color;
            else profile.FiveLaneDrums.RedFret = color;
        }

        private static object Ordering(string mode)
        {
            var infoType = Production("YARG.Gameplay.Visuals.HighwayOrderingInfo");
            var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(int), infoType);
            var result = (IDictionary)Activator.CreateInstance(dictionaryType);
            if (mode == "FiveFretGuitar" || mode == "ProKeysCompressed")
            {
                for (int i = 1; i <= 5; i++) result.Add(i, Activator.CreateInstance(infoType, i - 1, i));
                return result;
            }

            if (mode == "SixFretGuitar")
            {
                result.Add(1, Activator.CreateInstance(infoType, 0, 1)); // Black 1
                result.Add(4, Activator.CreateInstance(infoType, 0, 4)); // White 1
                result.Add(2, Activator.CreateInstance(infoType, 1, 2)); // Black 2
                result.Add(5, Activator.CreateInstance(infoType, 1, 5)); // White 2
                result.Add(3, Activator.CreateInstance(infoType, 2, 3)); // Black 3
                result.Add(6, Activator.CreateInstance(infoType, 2, 6)); // White 3
                return result;
            }

            bool fourLane = mode == "FourLaneDrums";
            if (fourLane)
            {
                result.Add(1, Activator.CreateInstance(infoType, 0, 1)); // Red drum
                result.Add(2, Activator.CreateInstance(infoType, 1, 2)); // Yellow drum
                result.Add(5, Activator.CreateInstance(infoType, 1, 5)); // Yellow cymbal
                result.Add(3, Activator.CreateInstance(infoType, 2, 3)); // Blue drum
                result.Add(6, Activator.CreateInstance(infoType, 2, 6)); // Blue cymbal
                result.Add(4, Activator.CreateInstance(infoType, 3, 4)); // Green drum
                result.Add(7, Activator.CreateInstance(infoType, 3, 7)); // Green cymbal
                return result;
            }

            for (int lane = 0; lane < 5; lane++)
            {
                int note = lane + 1;
                result.Add(note, Activator.CreateInstance(infoType, lane, note));
            }
            return result;
        }

        private static void SetKickPositions(Component array)
        {
            var left = new GameObject("Left kick position");
            var right = new GameObject("Right kick position");
            left.transform.SetParent(array.transform);
            right.transform.SetParent(array.transform);
            Field(array, "_leftKickFretPosition", left.transform);
            Field(array, "_rightKickFretPosition", right.transform);
        }
    }
}
