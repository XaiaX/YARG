using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.PlayMode
{
    // Uses production prefabs and callbacks, but deliberately bypasses OnEnable's
    // global song-session startup. No player is registered and no profile is saved.
    public sealed partial class DifficultySelectDonePlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<UnityEngine.Object> _owned = new();
        private readonly List<IDisposable> _players = new();
        private Component _menu;
        private Component _navigation;
        private ScrollRect _scroll;
        private PropertyInfo _statsInstance;
        private object _previousStats;
        private IDictionary<string, string> _dictionary;
        private KeyValuePair<string, string>[] _previousDictionary;
        private PropertyInfo _culture;
        private object _previousCulture;
        private YargProfile _profile;

        [UnityTest]
        public IEnumerator DoneTransitionsAndRetainsValuesAt720p()
        {
            yield return Exercise(new Vector2Int(1280, 720));
        }

        [UnityTest]
        public IEnumerator DoneTransitionsAndRetainsValuesAt1080p()
        {
            yield return Exercise(new Vector2Int(1920, 1080));
        }

        private IEnumerator Exercise(Vector2Int resolution)
        {
#if UNITY_EDITOR
            Bootstrap(resolution);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(Mathf.Abs(_menu.transform.lossyScale.y), Is.GreaterThan(0f),
                "The production root Canvas must acquire a nonzero driven transform before any row geometry is tested.");
            foreach (bool overflow in new[] { false, true })
            {
                // A fresh private profile per case; not PlayerContainer or a loaded profile.
                _profile = new YargProfile
                {
                    Name = "Difficulty Done fixture",
                    GameMode = GameMode.FiveFretGuitar,
                    CurrentInstrument = Instrument.FiveFretGuitar,
                    PreferredInstrument = Instrument.FiveFretGuitar,
                    CurrentDifficulty = Difficulty.Expert,
                    IsBot = true,
                };
                var playerType = Production("YARG.Player.YargPlayer");
                // The warning properties inspect Bindings before checking IsBot.
                // Empty local bindings avoid hardware, InputManager registration, and profile I/O.
                var bindings = (IDisposable)Activator.CreateInstance(Production("YARG.Input.ProfileBindings"),
                    new object[] { _profile });
                object player;
                try
                {
                    player = Activator.CreateInstance(playerType, new object[] { _profile, bindings });
                }
                catch
                {
                    bindings.Dispose();
                    throw;
                }
                _players.Add((IDisposable)player);
                Assert.That(Property(player, "Bindings"), Is.SameAs(bindings));
                Assert.That(Property(player, "InputsEnabled"), Is.False);
                Assert.That(Property(bindings, "HasDeviceAssigned"), Is.False);
                Assert.That(Property(player, "IsMissingInputDevice"), Is.False);
                Assert.That(Property(player, "IsMissingMicrophone"), Is.False);
                var players = Array.CreateInstance(playerType, 1);
                players.SetValue(player, 0);
                Set(_menu, "_explicitPlayers", players);
                Set(_menu, "_menuState", Enum.Parse(Field(_menu, "_menuState").FieldType, "Main"));
                Set(_menu, "_lastMenuState", Enum.Parse(Field(_menu, "_lastMenuState").FieldType, "Main"));
                var instruments = (IList)Get(_menu, "_possibleInstruments");
                instruments.Clear();
                instruments.Add(Instrument.FiveFretGuitar);
                var modifiers = (IList)Get(_menu, "_possibleModifiers");
                modifiers.Clear();
                modifiers.Add(Modifier.AllStrums);
                modifiers.Add(Modifier.OpensToGreens);
                // Exercise different row counts without copying the production menu builders.
                if (overflow)
                {
                    foreach (var value in new[] { Modifier.AllHopos, Modifier.AllTaps, Modifier.HoposToTaps })
                        modifiers.Add(value);
                }
                Call(_menu, "UpdateForPlayer");
                yield return Settle();
                AssertState("Main");
                ActivateLabel(Header("Adjustments"));
                yield return Settle();
                AssertState("Adjustments");
                AssertOneDone();

                ActivateLabel(Header("Modifiers"));
                yield return Settle();
                AssertState("Modifiers");
                var modifierRows = Rows();
                Call(modifierRows[0], "Confirm");
                var expectedModifiers = _profile.CurrentModifiers;
                Assert.That(expectedModifiers, Is.Not.EqualTo(Modifier.None), "The production toggle callback must mutate the private profile.");
                yield return CheckAndActivateDone(overflow, resolution, "Modifiers");
                AssertState("Adjustments");
                Assert.That(_profile.CurrentModifiers, Is.EqualTo(expectedModifiers));
                ActivateLabel(Header("Modifiers"));
                yield return Settle();
                Assert.That(_profile.CurrentModifiers, Is.EqualTo(expectedModifiers), "Reopening must preserve the modifier callback value.");
                yield return CheckAndActivateDone(false, resolution, "Modifiers reopened");

                ActivateLabel(Header("Accessibility"));
                yield return Settle();
                AssertState("Accessibility");
                Call(Rows()[0], "Confirm");
                Assert.That(_profile.LeftyFlip, Is.True, "The first guitar accessibility row must invoke its existing profile callback.");
                yield return CheckAndActivateDone(overflow, resolution, "Accessibility");
                AssertState("Adjustments");
                Assert.That(_profile.LeftyFlip, Is.True);
                ActivateLabel(Header("Accessibility"));
                yield return Settle();
                Assert.That(_profile.LeftyFlip, Is.True, "Reopening must preserve the accessibility callback value.");
                yield return CheckAndActivateDone(false, resolution, "Accessibility reopened");
                yield return CheckAndActivateDone(overflow, resolution, "Adjustments");
                AssertState("Main");
                Assert.That(_profile.LeftyFlip, Is.True);
                Assert.That(_profile.CurrentModifiers, Is.EqualTo(expectedModifiers));
            }
#else
            Assert.Ignore("Production-prefab fixture requires Unity Editor AssetDatabase.");
            yield break;
#endif
        }

        private IEnumerator CheckAndActivateDone(bool overflow, Vector2Int resolution, string state)
        {
            var done = AssertOneDone();
            var rows = Rows();
            var content = _scroll.content;
            var originalHeights = new List<(LayoutElement element, float min, float preferred, bool enabled)>();
            float originalViewportHeight = _scroll.viewport.rect.height;
            if (overflow)
            {
                // Controlled stress geometry on the real instances: unequal rows and
                // a short viewport, so both render resolutions actually test scrolling.
                _scroll.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 180f);
                for (int i = 0; i < content.childCount; i++)
                {
                    var row = content.GetChild(i);
                    var element = row.GetComponent<LayoutElement>();
                    if (element == null) element = row.gameObject.AddComponent<LayoutElement>();
                    originalHeights.Add((element, element.minHeight, element.preferredHeight, element.enabled));
                    element.enabled = true;
                    element.minHeight = element.preferredHeight = i % 2 == 0 ? 110f : 65f;
                }
            }
            yield return Settle();
            Call(_menu, "RefreshScrollbar");
            yield return Settle();
            float hidden = content.rect.height - _scroll.viewport.rect.height;
            Debug.Log($"[DifficultyDone] {resolution} {state} overflow={overflow} rows={rows.Length} nav={Property(_navigation, "Count")} content={content.rect} viewport={_scroll.viewport.rect} hidden={hidden} scrollbar={_scroll.verticalScrollbar?.value}/{_scroll.verticalScrollbar?.size}");
            if (overflow) Assert.That(hidden, Is.GreaterThan(0f), "Stress case must really overflow.");
            else Assert.That(hidden, Is.LessThanOrEqualTo(2f), "The authored short menu must fit without scrolling.");
            Call(_navigation, "SelectFirst", EnumValue("YARG.Menu.Navigation.SelectionOrigin", "Navigation"));
            for (int i = 1; i < rows.Length; i++)
            {
                Call(_navigation, "SelectNext", true);
                yield return null;
            }
            yield return Settle();
            Assert.That(Property(_navigation, "SelectedBehaviour"), Is.SameAs(done), "Done must be the final reachable navigation row.");
            AssertVisible(done, state + " last");
            Call(_navigation, "SelectPrevious", true);
            Call(_navigation, "SelectNext", true);
            yield return Settle();
            Assert.That(Property(_navigation, "SelectedBehaviour"), Is.SameAs(done));
            AssertVisible(done, state + " returned");
            // Restore geometry before invoking a callback that destroys the old rows.
            foreach (var saved in originalHeights)
            {
                saved.element.minHeight = saved.min;
                saved.element.preferredHeight = saved.preferred;
                saved.element.enabled = saved.enabled;
            }
            _scroll.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, originalViewportHeight);
            Call(done, "Confirm");
            yield return Settle();
        }

        private void AssertVisible(Component row, string context)
        {
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_scroll.viewport, (RectTransform)row.transform);
            var viewport = _scroll.viewport.rect;
            var rowRect = (RectTransform)row.transform;
            Debug.Log($"[DifficultyDone] {context} rowRect={rowRect.rect} scale={rowRect.lossyScale} position={rowRect.position} contentPosition={_scroll.content.anchoredPosition}");
            Debug.Log($"[DifficultyDone] {context} selected={Property(_navigation, "SelectedIndex")} bounds={bounds} viewport={viewport} normalized={_scroll.verticalNormalizedPosition} active={row.gameObject.activeInHierarchy}");
            Assert.That(row.gameObject.activeInHierarchy, Is.True);
            Assert.That(rowRect.rect.width, Is.GreaterThan(1f), context + ": Done must have a visible width, not only a point inside the viewport.");
            Assert.That(rowRect.rect.height, Is.GreaterThan(1f), context + ": Done must have a visible height.");
            Assert.That(bounds.size.y, Is.GreaterThan(1f), context + ": Done must retain nonzero rendered bounds.");
            Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(viewport.yMin - 2f), context + ": Done bottom is clipped.");
            Assert.That(bounds.max.y, Is.LessThanOrEqualTo(viewport.yMax + 2f), context + ": Done top is clipped.");
        }

        private Component AssertOneDone()
        {
            var matches = Rows().Where(row => Caption(row).Contains(Header("Done"))).ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), "Exactly one existing localized Done must be registered; do not add a duplicate.");
            Assert.That(Rows().Last(), Is.SameAs(matches[0]));
            var physicalRows = _scroll.content.GetComponentsInChildren(Production("YARG.Menu.DifficultySelect.DifficultyItem"), true)
                .Cast<Component>().Where(item => Caption(item).Contains(Header("Done"))).ToArray();
            Assert.That(physicalRows.Length, Is.EqualTo(1), "The content must contain exactly one physical Done row as well as one navigation entry.");
            return matches[0];
        }

        private void ActivateLabel(string label)
        {
            var matches = Rows().Where(row => Caption(row).Contains(label)).ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), "Expected a unique production row: " + label);
            Call(matches[0], "Confirm");
        }

        private Component[] Rows() => ((IEnumerable)Get(_navigation, "_navigatables")).Cast<Component>().ToArray();
        private static string Caption(Component row) => string.Join("\n", row.GetComponentsInChildren(Production("TMPro.TMP_Text"), false)
            .Cast<Component>().Select(text => (string)Property(text, "text")));
        private void AssertState(string state) => Assert.That(Get(_menu, "_menuState").ToString(), Is.EqualTo(state));
        private string Header(string key) => (string)Call(_menu, "LocalizeHeader", key);

        private IEnumerator Settle()
        {
            // DestroyChildren defers destruction until end of frame; never inspect old and new rows together.
            yield return null;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scroll.content);
            _scroll.Rebuild(CanvasUpdate.PostLayout);
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

#if UNITY_EDITOR
        private void Bootstrap(Vector2Int resolution)
        {
            Production("YARG.Helpers.PathHelper").GetMethod("Init", STATIC).Invoke(null, null);
            var localization = Production("YARG.Localization.LocalizationManager");
            _dictionary = (IDictionary<string, string>)localization.GetField("_localizationMap", STATIC).GetValue(null);
            _previousDictionary = _dictionary.ToArray();
            _culture = localization.GetProperty("CultureCode", STATIC);
            _previousCulture = _culture.GetValue(null);
            Assert.That((bool)localization.GetMethod("TryParseAndLoadLanguage", STATIC).Invoke(null, new object[] { "en-US" }), Is.True);

            // Replace only the singleton reference, never an existing instance's fields.
            var statsType = Production("YARG.Menu.Persistent.StatsManager");
            _statsInstance = statsType.BaseType.GetProperty("Instance", STATIC);
            _previousStats = _statsInstance.GetValue(null);
            var statsObject = Own(new GameObject("Difficulty Done private stats"));
            statsObject.SetActive(false);
            var stats = (Behaviour)statsObject.AddComponent(statsType);
            stats.enabled = false;
            var inactive = Own(new GameObject("Inactive fixture stats"));
            inactive.SetActive(false);
            var activePlayersType = statsType.GetField("_activePlayerList", INSTANCE).FieldType;
            Set(stats, "_activePlayerList", inactive.AddComponent(activePlayersType));
            Set(stats, "_activeBots", inactive);
            _statsInstance.GetSetMethod(true).Invoke(null, new object[] { stats });
            Assert.That(_statsInstance.GetValue(null), Is.SameAs(stats));
            var statType = statsType.GetNestedType("Stat");
            Assert.That(Call(stats, "IsShowing", Enum.Parse(statType, "ActivePlayers")), Is.False);
            Assert.That(Call(stats, "IsShowing", Enum.Parse(statType, "ActiveBots")), Is.False);
            Call(stats, "UpdateActivePlayers"); // Must early-return without real players, settings, or active-player widgets.

            var target = Own(new RenderTexture(resolution.x, resolution.y, 24));
            Assert.That(target.Create(), Is.True);
            var cameraObject = Own(new GameObject("Difficulty Done camera", typeof(Camera)));
            var camera = cameraObject.GetComponent<Camera>();
            camera.targetTexture = target;
            camera.orthographic = true;
            camera.pixelRect = new Rect(0, 0, resolution.x, resolution.y);
            Assert.That(camera.pixelWidth, Is.EqualTo(resolution.x));
            Assert.That(camera.pixelHeight, Is.EqualTo(resolution.y));
            var staging = Own(new GameObject("Inactive prefab staging"));
            staging.SetActive(false);
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Menu/DifficultySelect/DifficultySelectMenu.prefab");
            Assert.That(prefab, Is.Not.Null);
            var root = Own(UnityEngine.Object.Instantiate(prefab, staging.transform));
            _menu = root.GetComponent(Production("YARG.Menu.DifficultySelect.DifficultySelectMenu"));
            ((Behaviour)_menu).enabled = false;
            var assetMenu = prefab.GetComponent(Production("YARG.Menu.DifficultySelect.DifficultySelectMenu"));
            var assetColored = (GameObject)Get(assetMenu, "_coloredItemPrefab");
            var instanceColored = (GameObject)Get(_menu, "_coloredItemPrefab");
            var container = (Transform)Get(_menu, "_container");
            Debug.Log($"[DifficultyDone] Before first content clear: assetColoredValid={assetColored != null} instanceColoredValid={instanceColored != null} assetColoredManagedNull={ReferenceEquals(assetColored, null)} instanceColoredManagedNull={ReferenceEquals(instanceColored, null)} coloredAssetPath={(assetColored != null ? UnityEditor.AssetDatabase.GetAssetPath(assetColored) : "<missing>")} instanceTemplateInContent={(instanceColored != null && instanceColored.transform.IsChildOf(container))}");
            Assert.That(assetColored != null, Is.True,
                "The production menu asset must reference a valid colored row before any lifecycle/content destruction. Reassign _coloredItemPrefab to the actual DifficultyItemColored root in Unity; a missing serialized fileID is not a scrolling failure.");
            Assert.That(instanceColored != null, Is.True, "Instantiating the menu must retain its persistent colored row asset.");
            Assert.That(instanceColored.transform.IsChildOf(container), Is.False,
                "The colored row template must not be a child that UpdateForPlayer destroys.");
            // Disable only unrelated header ring data lookup (requires a globally selected song).
            Set(_menu, "_difficultyRing", null);
            // The authored screen-space Canvas must be a root Canvas. Nesting it
            // under another Canvas preserves its serialized zero scale instead of
            // letting Unity drive the screen-space transform as it does in-game.
            var canvas = root.GetComponent<Canvas>();
            Assert.That(canvas, Is.Not.Null);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            root.transform.SetParent(null, false);
            Assert.That(canvas.isRootCanvas, Is.True);
            Canvas.ForceUpdateCanvases();
            Debug.Log($"[DifficultyDone] Capture rootCanvas={canvas.isRootCanvas} localScale={root.transform.localScale} lossyScale={root.transform.lossyScale} pixelRect={canvas.pixelRect} referenceResolution={root.GetComponent<CanvasScaler>().referenceResolution}");
            Assert.That(((Behaviour)_menu).enabled, Is.False, "Global song-session startup must remain bypassed.");
            Assert.That(Get(_menu, "_navigationSchemePushed"), Is.False, "The fixture must not push or pop a real Navigator scheme.");
            _navigation = (Component)Get(_menu, "_navGroup");
            _scroll = root.GetComponentInChildren<ScrollRect>(true);
            Assert.That(_scroll, Is.Not.Null);
            Set(_menu, "_scrollRect", _scroll);
            Set(_menu, "_scrollbar", _scroll.verticalScrollbar);
            // Match the production subscription without running global OnEnable.
            var changed = _navigation.GetType().GetEvent("SelectionChanged");
            changed.AddEventHandler(_navigation, Delegate.CreateDelegate(changed.EventHandlerType, _menu,
                _menu.GetType().GetMethod("UpdateForSelectionChanged", INSTANCE)));
        }
#endif

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Restore references before destroying owned components, even after assertion failures.
            if (_statsInstance != null) _statsInstance.GetSetMethod(true).Invoke(null, new[] { _previousStats });
            if (_dictionary != null && _previousDictionary != null)
            {
                _dictionary.Clear();
                foreach (var entry in _previousDictionary) _dictionary.Add(entry);
                _culture.GetSetMethod(true).Invoke(null, new[] { _previousCulture });
            }
            foreach (var value in _owned.AsEnumerable().Reverse())
                if (value != null) UnityEngine.Object.Destroy(value);
            _owned.Clear();
            foreach (var player in _players) player.Dispose();
            _players.Clear();
            yield return null;
        }

        private T Own<T>(T value) where T : UnityEngine.Object { _owned.Add(value); return value; }
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private static object EnumValue(string type, string value) => Enum.Parse(Production(type), value);
        private static FieldInfo Field(object value, string name) => value.GetType().GetField(name, INSTANCE)
            ?? throw new MissingFieldException(value.GetType().FullName, name);
        private static object Get(object value, string name) => Field(value, name).GetValue(value);
        private static void Set(object value, string name, object data) => Field(value, name).SetValue(value, data);
        private static object Property(object value, string name) => value.GetType().GetProperty(name, INSTANCE).GetValue(value);
        private static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, INSTANCE).Invoke(value, args);
    }
}
