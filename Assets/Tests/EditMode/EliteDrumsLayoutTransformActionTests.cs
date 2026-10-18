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
        private static float NavigationX(Component target)
        {
            var rect = target.transform as RectTransform;
            return rect != null ? rect.TransformPoint(rect.rect.center).x : target.transform.position.x;
        }

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

        [TestCase(false, true, "drum")]
        [TestCase(false, true, "cymbal")]
        [TestCase(false, true, "hi-hat")]
        [TestCase(false, true, "kick1")]
        [TestCase(false, true, "kick2")]
        [TestCase(false, true, "split-kicks")]
        [TestCase(false, false, "drum")]
        [TestCase(false, false, "cymbal")]
        [TestCase(false, false, "hi-hat")]
        [TestCase(false, false, "kick1")]
        [TestCase(false, false, "kick2")]
        [TestCase(false, false, "split-kicks")]
        [TestCase(true, true, "drum")]
        [TestCase(true, true, "cymbal")]
        [TestCase(true, true, "hi-hat")]
        [TestCase(true, true, "kick1")]
        [TestCase(true, true, "kick2")]
        [TestCase(true, true, "split-kicks")]
        [TestCase(true, false, "drum")]
        [TestCase(true, false, "cymbal")]
        [TestCase(true, false, "hi-hat")]
        [TestCase(true, false, "kick1")]
        [TestCase(true, false, "kick2")]
        [TestCase(true, false, "split-kicks")]
        public void ScreenRelativeShiftDirectionAndBoundaries(bool lefty, bool screenLeft, string sourceKind)
        {
            var original = new EliteDrumsLayoutPreset("Screen-relative boundary");
            int edgeColumn = screenLeft == lefty ? EliteDrumsLayoutPreset.SLOT_COUNT - 1 : 0;
            original.Slots[edgeColumn] = sourceKind switch
            {
                "drum" => EliteDrumsLayoutSlot.Ordinary("snare", null),
                "cymbal" => EliteDrumsLayoutSlot.Ordinary(null, "left-crash"),
                "hi-hat" => EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat),
                "kick1" or "split-kicks" => EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1),
                "kick2" => EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2),
                _ => throw new ArgumentOutOfRangeException(nameof(sourceKind))
            };
            if (sourceKind is "kick1" or "kick2" or "split-kicks")
            {
                int companionColumn = edgeColumn == 0 ? 1 : edgeColumn - 1;
                var companionKind = sourceKind == "kick2"
                    ? EliteDrumsLayoutSlotKind.Kick1 : EliteDrumsLayoutSlotKind.Kick2;
                original.Slots[companionColumn] = EliteDrumsLayoutSlot.Exclusive(companionKind);
            }
            Initialize(original);
            _menuType.GetMethod("OnLeftyFlipToggleChanged", PRIVATE_INSTANCE)
                .Invoke(_menu, new object[] { lefty });
            var before = Slots(Draft);
            string blockedDirection = screenLeft ? "Left" : "Right";
            string availableDirection = screenLeft ? "Right" : "Left";

            Assert.That(Property<bool>("CanShift" + blockedDirection), Is.False,
                $"The {sourceKind} at saved column {edgeColumn} must block shifting off screen-{(screenLeft ? "left" : "right")}.");
            Action("Shift" + blockedDirection);
            Assert.That(Slots(Draft), Is.EqualTo(before), "A blocked screen-relative shift must preserve every saved slot.");
            Assert.That(Property<bool>("IsDirty"), Is.False);

            Assert.That(Property<bool>("CanShift" + availableDirection), Is.True,
                "Shifting away from an occupied screen edge must remain available.");
            Action("Shift" + availableDirection);
            var expected = new (EliteDrumsLayoutSlotKind, string, string)[before.Length];
            int availableColumnDelta = (availableDirection == "Left" ? -1 : 1) * (lefty ? -1 : 1);
            for (int column = 0; column < before.Length; column++)
            {
                int destination = column + availableColumnDelta;
                if (destination >= 0 && destination < expected.Length)
                    expected[destination] = before[column];
            }
            Assert.That(Slots(Draft), Is.EqualTo(expected),
                "A screen-relative shift moves whole saved columns and preserves intervening gaps.");
            Assert.That(Slots(original), Is.EqualTo(before), "The input layout remains detached from the draft.");
        }

        [TestCase(false, true)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public void ScreenRelativeShiftMovesAcrossGaps(bool lefty, bool screenLeft)
        {
            var original = new EliteDrumsLayoutPreset("Gaps stay in saved columns");
            original.Slots[4] = EliteDrumsLayoutSlot.Ordinary("snare", "ride");
            Initialize(original);
            _menuType.GetMethod("OnLeftyFlipToggleChanged", PRIVATE_INSTANCE)
                .Invoke(_menu, new object[] { lefty });
            var before = Slots(Draft);
            string direction = screenLeft ? "Left" : "Right";
            int delta = (screenLeft ? -1 : 1) * (lefty ? -1 : 1);

            Assert.That(Property<bool>("CanShift" + direction), Is.True);
            Action("Shift" + direction);

            var expected = new (EliteDrumsLayoutSlotKind, string, string)[before.Length];
            for (int column = 0; column < before.Length; column++)
            {
                int destination = column + delta;
                if (destination >= 0 && destination < expected.Length)
                    expected[destination] = before[column];
            }
            Assert.That(Slots(Draft), Is.EqualTo(expected),
                "Empty columns move with the neighboring assignments rather than collapsing the gap.");
            Assert.That(Draft.Slots[4 + delta].Drum, Is.EqualTo("snare"));
            Assert.That(Draft.Slots[4 + delta].Cymbal, Is.EqualTo("ride"));
        }

        [TestCase(false, true)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public void ScreenRelativeShiftCanMoveIntoEmptyEdgeGaps(bool lefty, bool screenLeft)
        {
            var original = new EliteDrumsLayoutPreset("Empty edge gap");
            int edgeColumn = screenLeft == lefty ? EliteDrumsLayoutPreset.SLOT_COUNT - 1 : 0;
            int towardEdgeDelta = (screenLeft ? -1 : 1) * (lefty ? -1 : 1);
            int sourceColumn = edgeColumn - towardEdgeDelta;
            original.Slots[sourceColumn] = EliteDrumsLayoutSlot.Ordinary("snare", "ride");
            Initialize(original);
            _menuType.GetMethod("OnLeftyFlipToggleChanged", PRIVATE_INSTANCE)
                .Invoke(_menu, new object[] { lefty });

            string direction = screenLeft ? "Left" : "Right";
            Assert.That(Property<bool>("CanShift" + direction), Is.True,
                "An empty outermost column is a valid landing gap, not a blocked edge.");
            Action("Shift" + direction);

            Assert.That(Draft.Slots[edgeColumn].Drum, Is.EqualTo("snare"));
            Assert.That(Draft.Slots[edgeColumn].Cymbal, Is.EqualTo("ride"));
            Assert.That(Property<bool>("CanShift" + direction), Is.False,
                "Once the assignment reaches the visual edge, another outward shift is blocked.");
        }

        [Test]
        public void ReverseAndLeftyHaveDistinctDraftEffects()
        {
            var original = SparseDraft();
            var before = Slots(original);
            Initialize(original);
            _menuType.GetMethod("OnLeftyFlipToggleChanged", PRIVATE_INSTANCE)
                .Invoke(_menu, new object[] { true });
            Assert.That(Slots(Draft), Is.EqualTo(before), "Lefty changes only screen presentation, not assignments.");
            Assert.That(Property<bool>("IsDirty"), Is.False);

            Action("ReverseLayout");
            var reversed = Slots(Draft);
            Assert.That(reversed, Is.EqualTo(before.Reverse().ToArray()));
            Assert.That(Draft.Id, Is.EqualTo(original.Id));
            Assert.That(Draft.Name, Is.EqualTo(original.Name));
            Assert.That(Slots(Draft), Is.Not.EqualTo(before), "Reverse changes saved slot order unlike lefty presentation.");
        }

        [Test]
        public void LeftyChangesDoNotDirtyDraftOrMutateSavedAssignments()
        {
            var original = SparseDraft();
            var before = Slots(original);
            Initialize(original);

            _menuType.GetMethod("OnLeftyFlipToggleChanged", PRIVATE_INSTANCE)
                .Invoke(_menu, new object[] { true });

            Assert.That(Property<bool>("IsDirty"), Is.False);
            Assert.That(Slots(Draft), Is.EqualTo(before));
            Assert.That(Slots(original), Is.EqualTo(before));
        }

        [Test]
        public void StandaloneInitializationResetsLeftyAndPreviewOptionNeverDuplicatesIt()
        {
            SetField("_leftyFlip", true);
            var toggle = (Toggle)Field("_leftyFlipToggle");
            toggle.SetIsOnWithoutNotify(true);
            Initialize(SparseDraft());
            Assert.That(Field("_leftyFlip"), Is.EqualTo(false));
            Assert.That(toggle.isOn, Is.False);

            var setting = (System.Collections.Generic.IEnumerable<string>)Field("_previewOptionSetting");
            Assert.That(setting, Does.Not.Contain("Lefty Flip"));
        }

        [Test]
        public void PreviewOptionsDoNotOfferLeftyWhenDedicatedToggleIsMissing()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/EliteDrumsLayoutMenu.prefab");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var menu = instance.GetComponent(_menuType);
                _menuType.GetField("_leftyFlipToggle", PRIVATE_INSTANCE).SetValue(menu, null);
                _menuType.GetMethod("Awake", PRIVATE_INSTANCE).Invoke(menu, null);
                var setting = (System.Collections.Generic.IEnumerable<string>)
                    _menuType.GetField("_previewOptionSetting", PRIVATE_INSTANCE).GetValue(menu);
                Assert.That(setting, Does.Not.Contain("Lefty Flip"),
                    "The preview options must not add a competing lefty control when the separate toggle is absent.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
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
            var leftShift = ButtonTarget("_shiftLeft");
            var rightShift = ButtonTarget("_shiftRight");
            var reverse = ButtonTarget("_reverseLayout");
            var leftyToggle = ((Toggle)Field("_leftyFlipToggle")).GetComponent(navigatableType);
            var enabledShift = rightBoundary ? leftShift : rightShift;
            var blockedShift = rightBoundary ? rightShift : leftShift;
            Assert.That(blockedShift.GetComponent<Button>().interactable, Is.False);

            Select(ButtonTarget("_importLayout"));
            Move(0, 1);
            AssertGridRow(false);
            Move(0, 1);
            AssertGridRow(true);
            Move(0, 1);
            Assert.That(new[] { enabledShift, reverse }, Does.Contain(Selected()),
                "Down from drums must reach an enabled transform action below the grid.");

            var transformRow = new[] { leftShift, reverse, rightShift, leftyToggle }
                .Where(target => target != blockedShift)
                .OrderBy(target => NavigationX(target))
                .ToArray();
            int reverseIndex = Array.IndexOf(transformRow, reverse);
            var expectedLeft = transformRow[Math.Max(0, reverseIndex - 1)];
            var expectedRight = transformRow[Math.Min(transformRow.Length - 1, reverseIndex + 1)];
            Select(reverse);
            Move(-1, 0);
            Assert.That(Selected(), Is.SameAs(expectedLeft),
                "Left navigation follows screen X order, including the toggle, and clamps at the row edge.");
            Move(-1, 0);
            Assert.That(Selected(), Is.SameAs(transformRow[Math.Max(0, reverseIndex - 2)]),
                "Repeated left navigation follows screen order and clamps only at the row edge.");
            Select(reverse);
            Move(1, 0);
            Assert.That(Selected(), Is.SameAs(expectedRight),
                "Right navigation follows screen X order, including the toggle, and clamps at the row edge.");
            Move(1, 0);
            Assert.That(Selected(), Is.SameAs(transformRow[Math.Min(transformRow.Length - 1, reverseIndex + 2)]),
                "Repeated right navigation follows screen order and clamps only at the row edge.");
            Move(0, -1);
            AssertGridRow(true);
            Move(0, -1);
            AssertGridRow(false);
            Move(0, -1);
            Assert.That(management, Does.Contain(Selected()), "Up from cymbals must return to management.");
        }

        [TestCase(false, false, "Navigation")]
        [TestCase(false, true, "Navigation")]
        [TestCase(true, false, "Navigation")]
        [TestCase(true, true, "Navigation")]
        [TestCase(false, false, "Mouse")]
        [TestCase(false, true, "Mouse")]
        [TestCase(true, false, "Mouse")]
        [TestCase(true, true, "Mouse")]
        public void RepeatedConfirmKeepsBoundaryShiftSelectedAndNavigationUsesItsSpatialRow(
            bool lefty, bool screenRight, string selectionOrigin)
        {
            ((RectTransform)_instance.transform).sizeDelta = new Vector2(1920f, 1080f);
            Canvas.ForceUpdateCanvases();
            var original = new EliteDrumsLayoutPreset("Boundary focus");
            original.Slots[4] = EliteDrumsLayoutSlot.Ordinary("snare", "ride");
            var originalSlots = Slots(original);
            int saves = 0;
            int closes = 0;
            Initialize(original, _ => saves++);
            SetField("_closed", (Action)(() => closes++));
            ((Toggle)Field("_leftyFlipToggle")).isOn = lefty;
            var navigation = Field("_navigation");
            var navigatableType = RuntimeType("YARG.Menu.Navigation.NavigatableBehaviour");
            var origin = Enum.Parse(RuntimeType("YARG.Menu.Navigation.SelectionOrigin"), selectionOrigin);
            var shift = ((Button)Field(screenRight ? "_shiftRight" : "_shiftLeft"))
                .GetComponent(navigatableType);
            var reverse = ((Button)Field("_reverseLayout")).GetComponent(navigatableType);
            var leftyToggle = ((Toggle)Field("_leftyFlipToggle")).GetComponent(navigatableType);
            object Selected() => navigation.GetType().GetProperty("SelectedBehaviour").GetValue(navigation);
            void Select(Component target) => navigatableType.GetMethod("SetSelected")
                .Invoke(target, new[] { (object)true, origin });
            void Move(int horizontal, int vertical) => _menuType.GetMethod("NavigateGrid")
                .Invoke(_menu, new object[] { horizontal, vertical });
            void Confirm() => navigation.GetType().GetMethod("ConfirmSelection").Invoke(navigation, null);
            Component NearestDrum() => ((Array)Field("_cells")).Cast<Component>()
                .Where(cell => (bool)cell.GetType().GetProperty("Drum").GetValue(cell) &&
                    ((Behaviour)cell).isActiveAndEnabled && cell.GetComponent<Selectable>().IsInteractable())
                .OrderBy(cell => Mathf.Abs(NavigationX(cell) - NavigationX(shift))).First();

            Select(shift);
            Assert.That(Selected(), Is.SameAs(shift));
            Assert.That(shift.GetComponent<Button>().interactable, Is.True);
            var expectedUp = NearestDrum();
            Move(0, -1);
            Assert.That(Selected(), Is.SameAs(expectedUp), "Enabled shifts navigate Up to the nearest drum.");
            Select(shift);

            int confirms = 0;
            while (shift.GetComponent<Button>().interactable && confirms < EliteDrumsLayoutPreset.SLOT_COUNT)
            {
                Confirm();
                confirms++;
                Assert.That(Selected(), Is.SameAs(shift), "Confirm must not move focus, including the last valid shift.");
            }
            Assert.That(confirms, Is.GreaterThan(1), "Exercise repeated keyboard/controller confirm before the boundary.");
            Assert.That(shift.GetComponent<Button>().interactable, Is.False);
            Assert.That(shift.gameObject.activeInHierarchy, Is.True);
            var boundaryDraft = Field("_draft");
            var boundarySlots = Slots(Draft);
            var dirty = Property<bool>("IsDirty");
            for (int repeat = 0; repeat < 3; repeat++)
            {
                Confirm();
                Assert.That(Selected(), Is.SameAs(shift));
                Assert.That(Field("_draft"), Is.SameAs(boundaryDraft), "Blocked confirm must not replace the draft.");
                Assert.That(Slots(Draft), Is.EqualTo(boundarySlots));
                Assert.That(Property<bool>("IsDirty"), Is.EqualTo(dirty));
                Assert.That(((Toggle)Field("_leftyFlipToggle")).isOn, Is.EqualTo(lefty));
                Assert.That(saves, Is.Zero);
                Assert.That(closes, Is.Zero);
                Assert.That(((GameObject)Field("_popup")).activeSelf, Is.False);
                Assert.That((bool)Field("_discardConfirmationShowing"), Is.False);
                Assert.That(Field("_pendingDiscard"), Is.Null);
            }
            Assert.That(Slots(original), Is.EqualTo(originalSlots));

            Move(0, 1);
            Assert.That(Selected(), Is.SameAs(shift), "Down clamps within the transform row.");
            if (!screenRight)
            {
                Move(-1, 0);
                Assert.That(Selected(), Is.SameAs(shift), "Left clamps at the disabled left shift.");
            }
            Move(screenRight ? -1 : 1, 0);
            Assert.That(Selected(), Is.SameAs(reverse), "The inward neighbour is Swap, not a management action.");
            Select(shift);
            if (screenRight)
            {
                Move(1, 0);
                Assert.That(Selected(), Is.SameAs(leftyToggle), "Right from the disabled right shift reaches Lefty.");
                Select(shift);
            }
            Assert.That(NearestDrum(), Is.SameAs(expectedUp), "Shifting does not change enabled drum geometry.");
            Move(0, -1);
            Assert.That(Selected(), Is.SameAs(expectedUp), "Disabled shifts use the same nearest-drum Up rule.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SwapSkipsDisabledRightShiftToLefty(bool lefty)
        {
            ((RectTransform)_instance.transform).sizeDelta = new Vector2(1920f, 1080f);
            Canvas.ForceUpdateCanvases();
            var original = new EliteDrumsLayoutPreset("Skip blocked shift");
            original.Slots[lefty ? 0 : EliteDrumsLayoutPreset.SLOT_COUNT - 1] =
                EliteDrumsLayoutSlot.Ordinary("snare", "ride");
            Initialize(original);
            ((Toggle)Field("_leftyFlipToggle")).isOn = lefty;
            var navigation = Field("_navigation");
            var navigatableType = RuntimeType("YARG.Menu.Navigation.NavigatableBehaviour");
            var reverse = ((Button)Field("_reverseLayout")).GetComponent(navigatableType);
            var leftyToggle = ((Toggle)Field("_leftyFlipToggle")).GetComponent(navigatableType);
            Assert.That(((Button)Field("_shiftRight")).interactable, Is.False);
            navigatableType.GetMethod("SetSelected").Invoke(reverse, new[] { (object)true,
                Enum.Parse(RuntimeType("YARG.Menu.Navigation.SelectionOrigin"), "Navigation") });
            _menuType.GetMethod("NavigateGrid").Invoke(_menu, new object[] { 1, 0 });
            Assert.That(navigation.GetType().GetProperty("SelectedBehaviour").GetValue(navigation),
                Is.SameAs(leftyToggle), "An unselected disabled shift must not become a navigation destination.");
        }

        [Test]
        public void LeftyDisplayMirrorsAnchorsInsideGridWithoutChangingSavedColumnOrText()
        {
            var cells = (Array)Field("_cells");
            var cell = (Component)cells.GetValue(10);
            cell.GetType().GetMethod("Awake", PRIVATE_INSTANCE).Invoke(cell, null);
            var rect = (RectTransform)cell.transform;
            var authoredMin = rect.anchorMin;
            var authoredMax = rect.anchorMax;
            var label = (Component)cell.GetType().GetField("_label", PRIVATE_INSTANCE).GetValue(cell);
            var labelRect = (RectTransform)label.GetType().GetProperty("rectTransform").GetValue(label);
            var labelScale = labelRect.localScale;
            cell.GetType().GetMethod("SetLeftyFlip").Invoke(cell, new object[] { true });
            Assert.That(rect.anchorMin.x, Is.GreaterThan(authoredMin.x));
            Assert.That(rect.anchorMax.x, Is.GreaterThan(authoredMax.x));
            Assert.That(rect.anchorMin.x, Is.GreaterThanOrEqualTo(0.0225f));
            Assert.That(rect.anchorMax.x, Is.LessThanOrEqualTo(0.6275f + 0.000001f));
            Assert.That(labelRect.localScale, Is.EqualTo(labelScale));
            var pill = (Image)cell.GetType().GetField("_fretPill", PRIVATE_INSTANCE).GetValue(cell);
            var pillMin = pill.rectTransform.anchorMin;
            cell.GetType().GetMethod("SetLeftyFlip").Invoke(cell, new object[] { true });
            Assert.That(pill.rectTransform.anchorMin.x, Is.EqualTo(pillMin.x));
            Assert.That(cell.GetType().GetProperty("Column").GetValue(cell), Is.EqualTo(0));
            cell.GetType().GetMethod("SetLeftyFlip").Invoke(cell, new object[] { false });
            Assert.That(rect.anchorMin, Is.EqualTo(authoredMin));
            Assert.That(rect.anchorMax, Is.EqualTo(authoredMax));
        }

        [Test]
        public void PreviewOptionsCanExcludeLeftyWhenTheEditorHasSeparateHandednessControl()
        {
            var dropdownType = RuntimeType("YARG.Settings.Metadata.PreviewOptionsDropdown");
            var stateType = dropdownType.GetNestedType("State");
            var stateDelegateType = typeof(Func<>).MakeGenericType(stateType);
            var getState = System.Linq.Expressions.Expression.Lambda(stateDelegateType,
                System.Linq.Expressions.Expression.Default(stateType)).Compile();
            var create = dropdownType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            var setting = create.Invoke(null, new[] { getState, null, (object)false });
            var options = ((System.Collections.Generic.IEnumerable<string>)setting).ToArray();
            Assert.That(options, Does.Not.Contain("Lefty Flip"));
            Assert.That(options, Does.Contain("Groove"));
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
