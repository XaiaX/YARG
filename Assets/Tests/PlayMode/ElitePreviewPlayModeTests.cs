using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Game;
using Object = UnityEngine.Object;

namespace YARG.Tests.PlayMode
{
    public sealed class ElitePreviewPlayModeTests
    {
        private const float TEST_GEM_MODEL_WIDTH = 0.35f;

        private readonly List<Object> _owned = new();
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).First(t => t != null);
        private static void Field(object value, string name, object data) => value.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(value, data);
        private static object Property(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
        private static void Set(object value, string name, object data) => value.GetType().GetProperty(name).SetValue(value, data);
        private static object Call(object value, string name, params object[] args) => value.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.Public).Invoke(value, args);
        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var value in _owned) if (value != null) Object.Destroy(value);
            _owned.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ActivePreview_TwiceSpawnsRecolorsReturnsReusesAndDestroys()
        {
            for (int iteration = 0; iteration < 2; iteration++)
            {
                // Frets are instantiated and initialized synchronously. Their authored effect
                // components require Awake before Fret.Initialize colors the theme-bound effects.
                var root = Own(new GameObject("Elite preview lifecycle " + iteration));
                var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
                var player = root.AddComponent(playerType);
                Set(player, "SelectedGameMode", GameMode.EliteDrums);
                if (iteration == 1)
                {
                    var combinedLayout = EliteDrumsLayoutPreset.Full;
                    combinedLayout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
                    Set(player, "EliteLayoutOverride", combinedLayout);
                }
                var poolObject = new GameObject("Real keyed pool");
                poolObject.transform.SetParent(root.transform);
                poolObject.SetActive(false);
                var pool = poolObject.AddComponent(Production("YARG.Gameplay.KeyedPool"));
                var poolBase = pool.GetType().BaseType;
                poolBase.GetField("_prewarmAmount", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pool, 0);
                poolBase.GetField("_objectCap", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pool, 100);
                var fretObject = new GameObject("Real frets");
                fretObject.transform.SetParent(root.transform);
                var fretArray = fretObject.AddComponent(Production("YARG.Gameplay.Visuals.FretArray"));
                Field(player, "_fretArray", fretArray);
                var normal = CreateModels(false);
                var starPower = CreateModels(true);
                var noteType = Production("YARG.Settings.Preview.FakeNote");
                var factory = noteType.GetMethod("CreateFakeNoteFromModels");
                var styleType = Production("YARG.Themes.VisualStyle");
                var themePresetType = Production("YARG.Themes.ThemePreset");
                var themePreset = themePresetType.GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                var factoryDelegateType = typeof(Func<,,>).MakeGenericType(themePresetType, styleType, typeof(GameObject));
                var factoryDelegate = BuildFactory(factoryDelegateType, factory, normal, starPower);
                var infoType = playerType.GetNestedType("Info");
                var initializeType = typeof(Action<,,>).MakeGenericType(infoType, themePresetType, styleType);
                var fretPrefab = CreateFretPrefab();
                var colors = new ColorProfile("Elite preview lifecycle " + iteration);
                var initializer = BuildFretInitializer(initializeType, fretArray, fretPrefab, colors);
                var highway = new HighwayPreset("Elite preview lifecycle " + iteration);
                poolObject.SetActive(true);
                playerType.GetMethod("Initialize").Invoke(player, new object[]
                {
                    themePreset, pool, factoryDelegate, CameraPreset.Default, colors,
                    EnginePreset.Default, highway, false, initializer
                });
                root.SetActive(true);
                yield return null;
                // Disable only automatic stepping; pooled note components and frets remain active.
                ((Behaviour)player).enabled = false;
                var info = Property(player, "CurrentGameModeInfo");
                var eliteLayout = info.GetType().GetField("EliteLayout").GetValue(info);
                int expectedLanes = iteration == 0 ? 5 : 9;
                var resolvedLanes = Property(eliteLayout, "Lanes");
                Assert.That((int)info.GetType().GetField("LaneCount").GetValue(info), Is.EqualTo(expectedLanes));
                Assert.That(expectedLanes, Is.EqualTo((int)resolvedLanes.GetType().GetProperty("Count")
                    .GetValue(resolvedLanes)), "The fake player's Info must retain the resolved Elite lane count.");
                Assert.That(fretObject.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret")).Length,
                    Is.EqualTo(expectedLanes), "Fret geometry must use the resolved Elite lane count.");
                Call(player, "SpotlightEliteRole", EliteDrumsColorRole.DoubleKick, false, null);
                Call(player, "Advance", 0.21d);
                var spawned = (IList)Property(pool, "AllSpawned");
                Assert.That(spawned.Count, Is.GreaterThan(0));
                var note = (Component)spawned[spawned.Count - 1];
                Assert.That(note.gameObject.activeInHierarchy, Is.True);
                float highwayWidth = (float)Production("YARG.Gameplay.Player.TrackPlayer")
                    .GetField("TRACK_WIDTH").GetRawConstantValue();
                string kickMode = Property(eliteLayout, "KickMode").ToString();
                Assert.That(kickMode, Is.EqualTo(iteration == 0 ? "Bars" : "Combined"),
                    "The lifecycle fixtures must retain both the unchanged bar mode and a combined dedicated lane.");
                bool dedicatedKick = kickMode is "Split" or "Combined";
                float resolvedLaneWidth = (float)eliteLayout.GetType().GetMethod("GetLaneWidth")
                    .Invoke(eliteLayout, new object[] { highwayWidth });
                float expectedDoubleKickWidth = dedicatedKick ? resolvedLaneWidth : highwayWidth / 2f;
                Assert.That(RenderedWidth(note), Is.EqualTo(expectedDoubleKickWidth).Within(0.01f),
                    "DoubleKick fills one dedicated lane in Split/Combined mode, but remains a half-highway bar in Bars mode.");
                string doubleKickSource = kickMode == "Combined" ? "kick" : "kick2";
                float expectedDoubleKickCenter = dedicatedKick
                    ? (float)eliteLayout.GetType().GetMethod("GetSourceX")
                        .Invoke(eliteLayout, new object[] { doubleKickSource, highwayWidth, false })
                    : 0.5f;
                Assert.That(RenderedCenter(note), Is.EqualTo(expectedDoubleKickCenter).Within(0.01f),
                    "DoubleKick uses its resolved dedicated lane center without a bar offset; Bars retains its authored offset.");
                if (!dedicatedKick)
                {
                    Assert.That(RenderedWidth(note), Is.EqualTo(highwayWidth / 2f).Within(0.01f));
                    Assert.That(RenderedCenter(note), Is.EqualTo(0.5f).Within(0.01f));
                }
                Assert.That(ActiveModel(note).name, Does.StartWith("Regular"));
                colors.EliteDrums.DoubleKickNote = System.Drawing.Color.Magenta;
                colors.EliteDrums.Fret1 = System.Drawing.Color.Lime;
                Call(player, "ApplySettings", CameraPreset.Default, colors, EnginePreset.Default, highway);
                var recoloredNote = ActiveModel(note).GetComponent<MeshRenderer>().material.color;
                Assert.That(new Color(recoloredNote.r, recoloredNote.g, recoloredNote.b), Is.EqualTo(Color.magenta),
                    "The edited Elite note role must recolor the active regular model.");
                var firstFret = fretObject.transform.GetChild(0);
                var fretMaterials = firstFret.GetComponentsInChildren<MeshRenderer>()
                    .SelectMany(renderer => renderer.materials);
                Assert.That(fretMaterials.Any(material => material.color == Color.green), Is.True,
                    "The first resolved saved column must recolor from the edited Elite Fret1 color.");
                var noteRef = noteType.GetProperty("NoteRef").GetValue(note);
                Field(noteRef, "ForceStarPower", true);
                Call(player, "ApplySettings", CameraPreset.Default, colors, EnginePreset.Default, highway);
                var themeNoteType = Production("YARG.Themes.ThemeNoteType");
                var selectedType = Enum.Parse(themeNoteType, dedicatedKick ? "DedicatedLaneKick" : "Kick");
                var findNoteGroup = noteType.GetMethod("FindNoteGroup", BindingFlags.Instance | BindingFlags.NonPublic);
                var expectedStarPowerGroup = (Component)findNoteGroup.Invoke(note, new[] { selectedType, (object)true });
                Assert.That(ActiveModel(note).transform.IsChildOf(expectedStarPowerGroup.transform), Is.True,
                    "Star Power must select the theme's resolved group, including its missing-model fallback.");
                if (dedicatedKick)
                {
                    var regularNormalGroup = findNoteGroup.Invoke(note,
                        new[] { Enum.Parse(themeNoteType, "Normal"), (object)false });
                    Assert.That(expectedStarPowerGroup, Is.SameAs(regularNormalGroup),
                        "This fixture omits both dedicated variants, so dedicated Star Power retains the regular Normal fallback.");
                    Assert.That(ActiveModel(note).name, Does.StartWith("RegularNormal"));
                }
                else
                {
                    Assert.That(ActiveModel(note).name, Does.StartWith("SPKick"),
                        "Bars must still select the explicitly supplied Star Power Kick model.");
                }
                Assert.That(RenderedWidth(note), Is.EqualTo(expectedDoubleKickWidth).Within(0.01f));
                Assert.That(RenderedCenter(note), Is.EqualTo(expectedDoubleKickCenter).Within(0.01f));
                var selectedRenderer = ActiveModel(note).GetComponent<MeshRenderer>();
                Call(pool, "ReturnAllObjects");
                Assert.That(selectedRenderer.material.color, Is.EqualTo(Color.white),
                    "Returning a pooled note restores its model material defaults.");
                Assert.That(((IList)Property(pool, "AllSpawned")).Count, Is.Zero);
                Assert.That(note.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(note.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(noteType.GetProperty("NoteRef").GetValue(note), Is.Null);
                Call(player, "SpotlightEliteRole", EliteDrumsColorRole.Snare, false, null);
                Call(player, "Advance", 0.21d);
                spawned = (IList)Property(pool, "AllSpawned");
                var reused = (Component)spawned[spawned.Count - 1];
                Assert.That(reused, Is.SameAs(note));
                // Ordinary models are authored for five lanes, as in live Elite gameplay.
                // Keep their authored width at five lanes and scale by the relative lane width.
                float expectedGemWidth = TEST_GEM_MODEL_WIDTH * resolvedLaneWidth / (highwayWidth / 5f);
                Assert.That(RenderedWidth(reused), Is.EqualTo(expectedGemWidth)
                    .Within(0.01f), "A regular Snare gem preserves its authored five-lane width relative to the resolved lane width.");
                Assert.That(RenderedCenter(reused), Is.EqualTo(eliteLayout.GetType().GetMethod("GetSourceX")
                    .Invoke(eliteLayout, new object[] { "snare", highwayWidth, false })).Within(0.01f));
                Assert.That(ActiveModel(reused).name, Does.StartWith("Regular"));
                Call(pool, "ReturnAllObjects");
                Call(player, "SpotlightEliteRole", EliteDrumsColorRole.Kick, false, null);
                Call(player, "Advance", 0.21d);
                spawned = (IList)Property(pool, "AllSpawned");
                var bar = (Component)spawned[spawned.Count - 1];
                Assert.That(RenderedWidth(bar), Is.EqualTo(dedicatedKick ? resolvedLaneWidth : highwayWidth).Within(0.01f),
                    "Kick fills one dedicated lane in Split/Combined mode, but retains full-highway width in Bars mode.");
                string kickSource = kickMode == "Combined" ? "kick" : "kick1";
                float expectedKickCenter = dedicatedKick
                    ? (float)eliteLayout.GetType().GetMethod("GetSourceX")
                        .Invoke(eliteLayout, new object[] { kickSource, highwayWidth, false })
                    : 0f;
                Assert.That(RenderedCenter(bar), Is.EqualTo(expectedKickCenter).Within(0.01f),
                    "Kick follows the resolved dedicated lane, while Bars stays centered on the highway.");
                if (!dedicatedKick)
                {
                    Assert.That(RenderedWidth(bar), Is.EqualTo(highwayWidth).Within(0.01f));
                    Assert.That(RenderedCenter(bar), Is.EqualTo(0f).Within(0.01f));
                }
                foreach (var role in new[] { EliteDrumsColorRole.Stomp, EliteDrumsColorRole.Splash })
                {
                    Call(pool, "ReturnAllObjects");
                    Call(player, "SpotlightEliteRole", role, false, null);
                    Call(player, "Advance", 0.21d);
                    spawned = (IList)Property(pool, "AllSpawned");
                    var pedalBar = (Component)spawned[spawned.Count - 1];
                    float expectedLaneSpan = 3f * resolvedLaneWidth;
                    Assert.That(RenderedWidth(pedalBar), Is.EqualTo(expectedLaneSpan)
                        .Within(0.01f), $"The {role} preview model spans three resolved visible columns.");
                    float expectedPedalCenter = Mathf.Clamp((float)eliteLayout.GetType().GetMethod("GetSourceX")
                            .Invoke(eliteLayout, new object[] { "hi-hat", highwayWidth, false }),
                        -highwayWidth / 2f + expectedLaneSpan / 2f,
                        highwayWidth / 2f - expectedLaneSpan / 2f);
                    Assert.That(RenderedCenter(pedalBar), Is.EqualTo(expectedPedalCenter).Within(0.01f),
                        "The preview bar stays centered on the resolved hi-hat lane within highway bounds.");
                }
                Call(pool, "ReturnAllObjects");
                Assert.Throws<TargetInvocationException>(() => Call(player, "Advance", double.NaN));
                Assert.Throws<TargetInvocationException>(() => Call(player, "Advance", double.PositiveInfinity));
                // The sample sequence keeps cycling while the preview stays open.
                for (int i = 0; i < 100; i++) Call(player, "Advance", 0.21d);
                Assert.That(((IList)Property(pool, "AllSpawned")).Count, Is.GreaterThan(0));
                Call(pool, "ReturnAllObjects");
                Assert.That(((IList)Property(pool, "AllSpawned")).Count, Is.Zero);
                Object.Destroy(root);
                yield return null;
                Assert.That(root == null, Is.True);
            }
        }

        private static GameObject ActiveModel(Component note) => note.GetComponentsInChildren<MeshRenderer>()
            .Single().gameObject;

        private static Renderer[] ActiveRenderers(Component note)
        {
            var renderers = note.GetComponentsInChildren<MeshRenderer>()
                .Where(renderer => renderer.gameObject.activeInHierarchy).ToArray();
            Assert.That(renderers, Is.Not.Empty, "The spawned preview note must have a rendered model.");
            return renderers;
        }

        private static float RenderedWidth(Component note)
        {
            var renderers = ActiveRenderers(note);
            float width = renderers.Max(renderer => renderer.bounds.max.x) -
                renderers.Min(renderer => renderer.bounds.min.x);
            return width / Mathf.Abs(note.transform.root.lossyScale.x);
        }

        private static float RenderedCenter(Component note)
        {
            var renderers = ActiveRenderers(note);
            float center = (renderers.Min(renderer => renderer.bounds.min.x) +
                renderers.Max(renderer => renderer.bounds.max.x)) / 2f;
            return note.transform.root.InverseTransformPoint(new Vector3(center, 0, 0)).x;
        }

        private object CreateModels(bool starPower)
        {
            var enumType = Production("YARG.Themes.ThemeNoteType");
            var dictionaryType = typeof(Dictionary<,>).MakeGenericType(enumType, typeof(GameObject));
            var dictionary = (IDictionary)Activator.CreateInstance(dictionaryType);
            foreach (var name in new[] { "Normal", "Cymbal", "Kick", "Accent", "Ghost", "CymbalAccent", "CymbalGhost", "Wildcard" })
            {
                var model = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
                model.name = (starPower ? "SP" : "Regular") + name;
                // Kick geometry is actually highway-wide, not just a color override.
                model.transform.localScale = new Vector3(name == "Kick" ? 2f : 0.35f, 0.1f, 0.2f);
                model.transform.position = new Vector3(1000f, 0f, 0f);
                var material = Own(new Material(Shader.Find("Standard")));
                material.color = Color.white;
                model.GetComponent<MeshRenderer>().sharedMaterial = material;
                var theme = model.AddComponent(Production("YARG.Themes.ThemeNote"));
                var indexType = Production("YARG.Themes.MeshEmissionMaterialIndex");
                var colored = Array.CreateInstance(indexType, 1);
                var entry = Activator.CreateInstance(indexType);
                Field(entry, "Mesh", model.GetComponent<MeshRenderer>());
                Field(entry, "MaterialIndex", 0);
                Field(entry, "EmissionMultiplier", 1f);
                colored.SetValue(entry, 0);
                Field(theme, "_coloredMaterials", colored);
                foreach (var field in new[] { "_coloredMaterialsNoStarPower", "_coloredMetalMaterials", "_coloredSecondaryMaterials" })
                    Field(theme, field, Array.CreateInstance(indexType, 0));
                dictionary.Add(Enum.Parse(enumType, name), model);
            }
            return dictionary;
        }

        private GameObject CreateFretPrefab()
        {
            // Use the real production theme fret: ThemeManager assembles a Fret whose
            // serialized effect groups (hit/sustain/pressed) exist, unlike a bare
            // authoring prefab. Start() is invoked directly so no frame dependency
            // is introduced.
            var managerObject = Own(new GameObject("Preview theme manager"));
            managerObject.SetActive(false);
            var manager = managerObject.AddComponent(Production("YARG.Themes.ThemeManager"));
            manager.GetType().GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(manager, null);
            var themePresetType = Production("YARG.Themes.ThemePreset");
            var preset = themePresetType.GetField("Default",
                BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var prefab = Own((GameObject) manager.GetType()
                .GetMethod("CreateFretPrefabFromTheme").Invoke(manager, new[]
                {
                    preset, Enum.Parse(Production("YARG.Themes.VisualStyle"), "FiveLaneDrums"), "fret"
                }));
            prefab.SetActive(false);
            Object.Destroy(managerObject);
            return prefab;
        }

        private static Delegate BuildFactory(Type delegateType, MethodInfo factory, object regular, object starPower)
        {
            var invoke = delegateType.GetMethod("Invoke");
            var parameters = invoke.GetParameters().Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType)).ToArray();
            var call = System.Linq.Expressions.Expression.Call(factory,
                System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Constant(regular), factory.GetParameters()[0].ParameterType),
                System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Constant(starPower), factory.GetParameters()[1].ParameterType));
            return System.Linq.Expressions.Expression.Lambda(delegateType, call, parameters).Compile();
        }

        private static Delegate BuildFretInitializer(Type delegateType, Component array, GameObject prefab,
            ColorProfile colors)
        {
            var invoke = delegateType.GetMethod("Invoke");
            var parameters = invoke.GetParameters().Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType)).ToArray();
            var method = typeof(ElitePreviewPlayModeTests).GetMethod(nameof(InitializeFrets), BindingFlags.Static | BindingFlags.NonPublic);
            var call = System.Linq.Expressions.Expression.Call(method, System.Linq.Expressions.Expression.Constant(array),
                System.Linq.Expressions.Expression.Convert(parameters[0], typeof(object)),
                System.Linq.Expressions.Expression.Convert(parameters[1], typeof(object)),
                System.Linq.Expressions.Expression.Convert(parameters[2], typeof(object)),
                System.Linq.Expressions.Expression.Constant(colors),
                System.Linq.Expressions.Expression.Constant(prefab));
            return System.Linq.Expressions.Expression.Lambda(delegateType, call, parameters).Compile();
        }

        private static void InitializeFrets(Component array, object info, object themePreset, object style,
            ColorProfile colors, GameObject prefab)
        {
            var infoType = info.GetType();
            var layout = infoType.GetField("EliteLayout").GetValue(info);
            var lanes = Property(layout, "Lanes");
            int laneCount = (int)infoType.GetField("LaneCount").GetValue(info);
            Assert.That(laneCount, Is.EqualTo((int)lanes.GetType().GetProperty("Count").GetValue(lanes)),
                "The resolved Info lane count must match its layout.");

            var infoTypeForFret = Production("YARG.Gameplay.Visuals.HighwayOrderingInfo");
            var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(int), infoTypeForFret);
            var ordering = (IDictionary)Activator.CreateInstance(dictionaryType);
            var resolvedOrdering = (IDictionary)infoType.GetField("HighwayOrdering").GetValue(info);
            var eliteColorsType = Production("YARG.Core.Game.ColorProfile").GetNestedType("EliteDrumsFretColorAdapter");
            var savedColumnAdapter = eliteColorsType.GetMethod("EncodeSlot", BindingFlags.Public | BindingFlags.Static);
            var indexer = lanes.GetType().GetProperty("Item");
            foreach (DictionaryEntry entry in resolvedOrdering)
            {
                int laneIndex = (int)entry.Value;
                var lane = indexer.GetValue(lanes, new object[] { laneIndex });
                int savedColumn = (int)lane.GetType().GetProperty("SavedColumn").GetValue(lane);
                int encodedColor = (int)savedColumnAdapter.Invoke(null, new object[] { savedColumn });
                ordering.Add(entry.Key, Activator.CreateInstance(infoTypeForFret, laneIndex, encodedColor));
            }

            var fakePlayerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
            var colorProvider = fakePlayerType.GetMethod("CreateEliteDrumsFretColorProvider",
                BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { layout, colors.EliteDrums });
            bool useKickFrets = Property(layout, "KickMode").ToString() == "Bars";
            Field(array, "UseKickFrets", useKickFrets);
            var method = array.GetType().GetMethods().Single(m => m.Name == "Initialize" &&
                m.GetParameters().Length == 6 && m.GetParameters()[4].ParameterType == typeof(GameObject));
            method.Invoke(array, new object[] { ordering, laneCount, infoType.GetField("KickFretPrefab").GetValue(info),
                colorProvider, prefab, false });
        }
    }
}
