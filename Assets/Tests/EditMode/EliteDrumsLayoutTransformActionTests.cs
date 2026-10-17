using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class EliteDrumsLayoutTransformActionTests
    {
        private GameObject _instance;
        private Component _menu;
        private Type _menuType;
        private Type _localizationType;
        private IDictionary<string, string> _localizations;
        private KeyValuePair<string, string>[] _previousLocalizations;
        private string _previousCulture;
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;

        private static Type RuntimeType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private object Field(string name) => _menuType.GetField(name, PRIVATE_INSTANCE).GetValue(_menu);
        private void SetField(string name, object value) => _menuType.GetField(name, PRIVATE_INSTANCE).SetValue(_menu, value);
        private T Property<T>(string name) => (T)_menuType.GetProperty(name).GetValue(_menu);
        private void Action(string name) => _menuType.GetMethod(name).Invoke(_menu, null);
        private EliteDrumsLayoutPreset Draft => Property<EliteDrumsLayoutPreset>("Draft");
        private static (EliteDrumsLayoutSlotKind, string, string)[] Slots(EliteDrumsLayoutPreset preset) =>
            preset.Slots.Select(slot => (slot.Kind, slot.Drum, slot.Cymbal)).ToArray();

        [SetUp]
        public void MountMenuWithoutPreviewRendering()
        {
            _localizationType = RuntimeType("YARG.Localization.LocalizationManager");
            _localizations = (IDictionary<string, string>)_localizationType.GetField("_localizationMap",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            _previousLocalizations = _localizations.ToArray();
            _previousCulture = (string)_localizationType.GetProperty("CultureCode").GetValue(null);
            Assert.That(_localizationType.GetMethod("TryParseAndLoadLanguage", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { "en-US" }), Is.True);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(prefab, Is.Not.Null);
            _instance = UnityEngine.Object.Instantiate(prefab);
            _menuType = RuntimeType("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            _menu = _instance.GetComponent(_menuType);
            SetField("_previewWorld", null);
            SetField("_layoutDropdown", null);
            _menuType.GetMethod("Awake", PRIVATE_INSTANCE).Invoke(_menu, null);
        }

        [TearDown]
        public void Unmount()
        {
            if (_instance != null) UnityEngine.Object.DestroyImmediate(_instance);
            if (_localizations == null) return;
            _localizations.Clear();
            foreach (var pair in _previousLocalizations) _localizations.Add(pair.Key, pair.Value);
            _localizationType.GetProperty("CultureCode").GetSetMethod(true)
                .Invoke(null, new object[] { _previousCulture });
        }

        private void Initialize(EliteDrumsLayoutPreset preset, Action<EliteDrumsLayoutPreset> save = null) =>
            _menuType.GetMethod("Initialize", new[] { typeof(EliteDrumsLayoutPreset),
                typeof(Action<EliteDrumsLayoutPreset>), typeof(Action) })
                .Invoke(_menu, new object[] { preset, save, (Action)(() => { }) });

        private static EliteDrumsLayoutPreset SparseDraft()
        {
            var preset = new EliteDrumsLayoutPreset("Incomplete draft");
            preset.Slots[1] = EliteDrumsLayoutSlot.Ordinary("snare", "ride");
            preset.Slots[3] = EliteDrumsLayoutSlot.Ordinary(null, "left-crash");
            preset.Slots[5] = EliteDrumsLayoutSlot.Ordinary("tom2", null);
            preset.Slots[6] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat);
            preset.Slots[7] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            preset.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            return preset;
        }

        [Test]
        public void ShiftPreservesBothRowsGapsKicksAndOriginalAndIsReversible()
        {
            var original = SparseDraft();
            var before = Slots(original);
            Initialize(original);
            foreach (string field in new[] { "_shiftLeft", "_shiftRight", "_reverseLayout" })
                Assert.That(((Button)Field(field)).gameObject.activeInHierarchy, Is.True);
            int notifications = 0;
            EliteDrumsLayoutPreset notified = null;
            Action<EliteDrumsLayoutPreset> observer = preset => { notifications++; notified = preset; };
            _menuType.GetEvent("DraftChanged").AddEventHandler(_menu, observer);
            Action("ShiftLeft");
            Assert.That(Slots(Draft).Take(9), Is.EqualTo(before.Skip(1)));
            Assert.That(Draft.Slots[9].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Empty));
            Assert.That(Slots(original), Is.EqualTo(before));
            Assert.That(Property<bool>("IsDirty"), Is.True);
            Assert.That(Property<bool>("CanSave"), Is.False, "Incomplete drafts must remain unsaveable.");
            Assert.That(Property<bool>("CanShiftLeft"), Is.False);
            Assert.That(((Button)Field("_shiftLeft")).interactable, Is.False);
            Assert.That(notifications, Is.EqualTo(1));
            notified.Slots[0].Drum = "tom3";
            Assert.That(Draft.Slots[0].Drum, Is.EqualTo("snare"), "Preview observers receive a detached copy.");
            Action("ShiftLeft");
            Assert.That(notifications, Is.EqualTo(1), "Blocked shifts must not notify or displace sources.");
            Action("ShiftRight");
            Assert.That(Slots(Draft), Is.EqualTo(before));
            Assert.That(Property<bool>("IsDirty"), Is.False);
        }

        [Test]
        public void ReverseMirrorsAllTenColumnsAndKeepsIdentityAndPreviewColorsSeparate()
        {
            var original = SparseDraft();
            var before = Slots(original);
            Initialize(original);
            var colors = new ColorProfile("Preview only");
            colors.EliteDrums.Fret1 = System.Drawing.Color.Magenta;
            colors.EliteDrums.Fret10 = System.Drawing.Color.Cyan;
            SetField("_previewColorProfile", colors);
            var slotColors = Enumerable.Range(0, 10).Select(colors.EliteDrums.GetSlotFretColor).ToArray();
            Action("ReverseLayout");
            Assert.That(Slots(Draft), Is.EqualTo(before.Reverse().ToArray()));
            Assert.That(Draft.Id, Is.EqualTo(original.Id));
            Assert.That(Draft.Name, Is.EqualTo(original.Name));
            Assert.That(Field("_previewColorProfile"), Is.SameAs(colors));
            Assert.That(Enumerable.Range(0, 10).Select(colors.EliteDrums.GetSlotFretColor), Is.EqualTo(slotColors),
                "Fret colors belong to saved positions and must not move with assignments.");
            Assert.That(Slots(original), Is.EqualTo(before));
            Assert.That(EliteDrumsLayoutEditor.GetUnassigned(Draft),
                Is.EquivalentTo(EliteDrumsLayoutEditor.GetUnassigned(original)));
            Action("ReverseLayout");
            Assert.That(Slots(Draft), Is.EqualTo(before));
            Assert.That(Property<bool>("IsDirty"), Is.False);
        }

        [TestCase(0, true)]
        [TestCase(0, false)]
        [TestCase(9, true)]
        [TestCase(9, false)]
        public void EitherBoundaryRowBlocksShiftWithoutLosingAssignments(int column, bool drum)
        {
            var original = new EliteDrumsLayoutPreset("Boundary");
            original.Slots[column] = EliteDrumsLayoutSlot.Ordinary(drum ? "snare" : null, drum ? null : "ride");
            Initialize(original);
            string direction = column == 0 ? "Left" : "Right";
            Assert.That(Property<bool>("CanShift" + direction), Is.False);
            Action("Shift" + direction);
            Assert.That(Slots(Draft), Is.EqualTo(Slots(original)));
            Assert.That(Property<bool>("IsDirty"), Is.False);
        }

        [Test]
        public void EmptyDraftDisablesAllActions()
        {
            Initialize(new EliteDrumsLayoutPreset("Empty"));
            foreach (var property in new[] { "CanShiftLeft", "CanShiftRight", "CanReverseLayout" })
                Assert.That(Property<bool>(property), Is.False);
            Action("ShiftLeft");
            Action("ShiftRight");
            Action("ReverseLayout");
            Assert.That(Property<bool>("IsDirty"), Is.False);
            Assert.That(Property<bool>("CanSave"), Is.False);
        }

        [Test]
        public void BuiltinsCannotBeTransformedEvenByDirectCalls()
        {
            var original = EliteDrumsLayoutPreset.Compact;
            Initialize(original);
            Action("ShiftLeft");
            Action("ShiftRight");
            Action("ReverseLayout");
            Assert.That(Slots(Draft), Is.EqualTo(Slots(original)));
            Assert.That(Property<bool>("IsDirty"), Is.False);
            foreach (string field in new[] { "_shiftLeft", "_shiftRight", "_reverseLayout" })
                Assert.That(((Button)Field(field)).gameObject.activeInHierarchy, Is.False);
            Assert.That(((GameObject)Field("_builtInNotice")).activeSelf, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProductionNavigationTraversesGridBeforeActionsAndSkipsBlockedShift(bool rightBoundary)
        {
            var original = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Navigation");
            if (rightBoundary) Array.Reverse(original.Slots);
            Initialize(original);
            var navigation = Field("_navigation");
            var navigatableType = RuntimeType("YARG.Menu.Navigation.NavigatableBehaviour");
            var origin = Enum.Parse(RuntimeType("YARG.Menu.Navigation.SelectionOrigin"), "Programmatically");
            object Selected() => navigation.GetType().GetProperty("SelectedBehaviour").GetValue(navigation);
            Component ButtonTarget(string field) => ((Button)Field(field)).GetComponent(navigatableType);
            void Select(Component target) => navigatableType.GetMethod("SetSelected")
                .Invoke(target, new[] { (object)true, origin });
            void Move(int horizontal, int vertical) => _menuType.GetMethod("NavigateGrid")
                .Invoke(_menu, new object[] { horizontal, vertical });
            void AssertGridRow(bool drum)
            {
                var selected = Selected();
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected.GetType().FullName,
                    Is.EqualTo("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutCell"));
                Assert.That(selected.GetType().GetProperty("Drum").GetValue(selected), Is.EqualTo(drum));
            }

            var management = new[] { "_save", "_renameLayout", "_copyLayout", "_deleteLayout", "_importLayout", "_exportLayout" }
                .Select(ButtonTarget).ToArray();
            var enabledShift = ButtonTarget(rightBoundary ? "_shiftLeft" : "_shiftRight");
            var blockedShift = ButtonTarget(rightBoundary ? "_shiftRight" : "_shiftLeft");
            var reverse = ButtonTarget("_reverseLayout");
            Assert.That(blockedShift.GetComponent<Button>().interactable, Is.False);

            Select(ButtonTarget("_importLayout"));
            Move(0, 1);
            AssertGridRow(false);
            Move(0, 1);
            AssertGridRow(true);
            Move(0, 1);
            Assert.That(new[] { enabledShift, reverse }, Does.Contain(Selected()),
                "Down from drums must reach an enabled transform action below the grid.");

            Select(reverse);
            Move(-1, 0);
            Assert.That(Selected(), Is.SameAs(enabledShift), "Left must skip the disabled boundary action.");
            Move(-1, 0);
            Assert.That(Selected(), Is.SameAs(enabledShift), "The disabled left boundary cannot receive focus.");
            Move(1, 0);
            Assert.That(Selected(), Is.SameAs(reverse), "Right must skip the disabled boundary action.");
            Move(0, -1);
            AssertGridRow(true);
            Move(0, -1);
            AssertGridRow(false);
            Move(0, -1);
            Assert.That(management, Does.Contain(Selected()), "Up from cymbals must return to management.");
        }

        [Test]
        public void CompleteDraftCanSaveDetachedTransformedLayout()
        {
            var original = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Custom");
            var before = Slots(original);
            EliteDrumsLayoutPreset saved = null;
            Initialize(original, preset => saved = preset);
            Action("ShiftRight");
            Assert.That(Property<bool>("CanSave"), Is.True);
            Action("Save");
            Assert.That(saved, Is.Not.Null);
            Assert.That(Slots(saved).Skip(1), Is.EqualTo(before.Take(9)));
            Assert.That(Slots(original), Is.EqualTo(before));
        }
    }
}
