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
                var initializer = BuildFretInitializer(initializeType, fretArray, fretPrefab);
                var colors = new ColorProfile("Elite preview lifecycle " + iteration);
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
                Assert.That(fretObject.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret")).Length, Is.EqualTo(5));
                var info = Property(player, "CurrentGameModeInfo");
                Assert.That((int)info.GetType().GetField("LaneCount").GetValue(info), Is.EqualTo(5));
                Call(player, "SpotlightEliteRole", EliteDrumsColorRole.DoubleKick, false, null);
                Call(player, "Advance", 0.21d);
                var spawned = (IList)Property(pool, "AllSpawned");
                Assert.That(spawned.Count, Is.GreaterThan(0));
                var note = (Component)spawned[spawned.Count - 1];
                Assert.That(note.gameObject.activeInHierarchy, Is.True);
                Assert.That(note.transform.localScale.x, Is.EqualTo(0.5f));
                Assert.That(note.transform.localPosition.x, Is.EqualTo(0.5f));
                Assert.That(ActiveModel(note).name, Does.StartWith("Regular"));
                colors.EliteDrums.DoubleKickNote = System.Drawing.Color.Magenta;
                colors.EliteDrums.SnareFret = System.Drawing.Color.Lime;
                Call(player, "ApplySettings", CameraPreset.Default, colors, EnginePreset.Default, highway);
                var recoloredNote = ActiveModel(note).GetComponent<MeshRenderer>().material.color;
                Assert.That(new Color(recoloredNote.r, recoloredNote.g, recoloredNote.b), Is.EqualTo(Color.magenta),
                    "The edited Elite note role must recolor the active regular model.");
                var firstFret = fretObject.transform.GetChild(0);
                var fretMaterials = firstFret.GetComponentsInChildren<MeshRenderer>()
                    .SelectMany(renderer => renderer.materials);
                Assert.That(fretMaterials.Any(material => material.color == Color.green), Is.True,
                    "The real fret model must recolor from the edited Elite profile.");
                var noteRef = noteType.GetProperty("NoteRef").GetValue(note);
                Field(noteRef, "ForceStarPower", true);
                Call(player, "ApplySettings", CameraPreset.Default, colors, EnginePreset.Default, highway);
                Assert.That(ActiveModel(note).name, Does.StartWith("SP"));
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
                Assert.That(reused.transform.localScale.x, Is.EqualTo(1f));
                Assert.That(ActiveModel(reused).name, Does.StartWith("Regular"));
                Call(pool, "ReturnAllObjects");
                Call(player, "SpotlightEliteRole", EliteDrumsColorRole.Kick, false, null);
                Call(player, "Advance", 0.21d);
                spawned = (IList)Property(pool, "AllSpawned");
                var bar = (Component)spawned[spawned.Count - 1];
                Assert.That(bar.transform.localScale.x, Is.EqualTo(1f));
                Assert.That(ActiveModel(bar).transform.localScale.x, Is.EqualTo(2f));
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

        private static Delegate BuildFretInitializer(Type delegateType, Component array, GameObject prefab)
        {
            var invoke = delegateType.GetMethod("Invoke");
            var parameters = invoke.GetParameters().Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType)).ToArray();
            var method = typeof(ElitePreviewPlayModeTests).GetMethod(nameof(InitializeFrets), BindingFlags.Static | BindingFlags.NonPublic);
            var call = System.Linq.Expressions.Expression.Call(method, System.Linq.Expressions.Expression.Constant(array),
                System.Linq.Expressions.Expression.Constant(prefab));
            return System.Linq.Expressions.Expression.Lambda(delegateType, call, parameters).Compile();
        }

        private static void InitializeFrets(Component array, GameObject prefab)
        {
            var infoType = Production("YARG.Gameplay.Visuals.HighwayOrderingInfo");
            var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(int), infoType);
            var ordering = (IDictionary)Activator.CreateInstance(dictionaryType);
            for (int i = 1; i <= 5; i++) ordering.Add(i, Activator.CreateInstance(infoType, i - 1, i));
            var method = array.GetType().GetMethods().Single(m => m.Name == "Initialize" &&
                m.GetParameters().Length == 6 && m.GetParameters()[4].ParameterType == typeof(GameObject));
            method.Invoke(array, new object[] { ordering, 5, null, ColorProfile.Default.EliteDrums, prefab, false });
        }
    }
}
