using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YARG.Core.Game;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YARG.Tests.PlayMode
{
    public sealed partial class EliteDrumsLayoutMenuPlayModeTests
    {
        private Guid _boundaryProfileLayoutId;
        private Guid _boundaryProfileColorId;
        private bool _boundaryProfileLeftyFlip;
        private bool _boundaryProfileIsCurrent;
        private int _boundaryLayoutWrites;
        private int _boundaryPreferenceWrites;
        private int _boundaryCommitCalls;

        private void IsolateBoundaryLayoutStore(string name)
        {
            _layoutContainerType = Production("YARG.Settings.Customization.EliteDrumsLayoutPresetContainer");
            _layoutContainer = Production("YARG.Settings.Customization.CustomContentManager")
                .GetField("EliteDrumsLayouts", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            _layoutRootProperty = _layoutContainerType.GetProperty("ContentRootOverride", BindingFlags.Public | BindingFlags.Static);
            _previousLayoutRoot = (string)_layoutRootProperty.GetValue(null);
            _layoutContents = (System.Collections.IList)_layoutContainerType
                .GetField("_content", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_layoutContainer);
            _previousLayouts = _layoutContents.Cast<object>().ToArray();
            _layoutContents.Clear();
            _layoutRoot = Path.Combine(Path.GetTempPath(), name + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_layoutRoot);
            _layoutRootProperty.SetValue(null, _layoutRoot);
        }

        private EliteDrumsLayoutPreset PersistBoundaryLayout(EliteDrumsLayoutPreset preset)
        {
            _boundaryCommitCalls++;
            return (EliteDrumsLayoutPreset)_layoutContainerType.GetMethod("CommitPreset")
                .Invoke(_layoutContainer, new object[] { preset });
        }

        private void InitializeBoundaryProfile(Component menu, EliteDrumsLayoutPreset initial)
        {
            _boundaryProfileLayoutId = initial.Id;
            _boundaryProfileColorId = ColorProfile.Default.Id;
            _boundaryProfileLeftyFlip = false;
            _boundaryProfileIsCurrent = true;
            _boundaryLayoutWrites = 0;
            _boundaryPreferenceWrites = 0;
            _boundaryCommitCalls = 0;

            Func<bool> isCurrent = () => _boundaryProfileIsCurrent;
            Func<EliteDrumsLayoutPreset, bool> acceptLayout = preset =>
            {
                if (!_boundaryProfileIsCurrent || preset == null ||
                    !EliteDrumsLayoutValidator.Validate(preset).IsRuntimeSupported ||
                    !((System.Collections.IEnumerable)_layoutContainerType.GetProperty("UsablePresets")
                        .GetValue(_layoutContainer)).Cast<EliteDrumsLayoutPreset>().Any(item => item.Id == preset.Id))
                    return false;
                _boundaryLayoutWrites++;
                _boundaryProfileLayoutId = preset.Id;
                return true;
            };
            Func<ColorProfile, bool> acceptColor = color =>
            {
                if (!_boundaryProfileIsCurrent || color == null) return false;
                _boundaryPreferenceWrites++;
                _boundaryProfileColorId = color.Id;
                return true;
            };
            Func<bool, bool> acceptLeftyFlip = value =>
            {
                if (!_boundaryProfileIsCurrent) return false;
                _boundaryPreferenceWrites++;
                _boundaryProfileLeftyFlip = value;
                return true;
            };
            Func<EliteDrumsLayoutPreset, EliteDrumsLayoutPreset> commit = preset =>
                _boundaryProfileIsCurrent ? PersistBoundaryLayout(preset) : null;
            menu.GetType().GetMethod("InitializeForProfileWithCommit").Invoke(menu, new object[]
            {
                initial, ColorProfile.Default, false, commit, (Action)(() => { }), isCurrent,
                acceptLayout, acceptColor, acceptLeftyFlip
            });
        }

        private Component MountBoundaryMenu()
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/EliteDrumsLayoutMenu.prefab");
#else
            var prefab = Resources.Load<GameObject>("EliteDrumsLayoutMenu");
#endif
            Assert.That(prefab, Is.Not.Null);
            _menuCanvasHost = new GameObject("Layout selection boundary Canvas");
            var canvas = _menuCanvasHost.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _menuCanvasHost.AddComponent<GraphicRaycaster>();
            var scaler = _menuCanvasHost.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _root = UnityEngine.Object.Instantiate(prefab, _menuCanvasHost.transform, false);
            MountMenuServices();
            return _root.GetComponent(Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu"));
        }

        private Component[] BoundaryDialogButtons()
        {
            var message = _dialogRoot.GetComponentInChildren(Production("YARG.Menu.Dialogs.MessageDialog"), true);
            Assert.That(message, Is.Not.Null, "The real compact confirmation dialog must be open.");
            var buttonContainer = (Transform)message.GetType().GetMethod("get_DialogButtonContainer",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(message, null);
            return buttonContainer.GetComponentsInChildren(Production("YARG.Menu.ColoredButton"), true)
                .Cast<Component>().ToArray();
        }

        private static void InvokeBoundaryDialogButton(Component button)
        {
            var click = button.GetType().GetProperty("OnClick").GetValue(button);
            click.GetType().GetMethod("Invoke").Invoke(click, null);
        }

        private static void SelectBoundaryDropdown(Component dropdown, int index) =>
            dropdown.GetType().GetProperty("value").SetValue(dropdown, index);

        private static int BoundaryLayoutIndex(Component menu, Guid id)
        {
            var layouts = (EliteDrumsLayoutPreset[])Field(menu, "_layouts");
            int index = Array.FindIndex(layouts, layout => layout.Id == id);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "The stored layout should be present in the production selector.");
            return index;
        }

        [UnityTest]
        public IEnumerator LayoutSelectionCommitBoundary_KeepEditingThenDiscardUsesRealDialogAndStoredMember()
        {
#if UNITY_EDITOR
            IsolateBoundaryLayoutStore("eliteDrumsSelectionBoundary");
            var persisted = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Stored selection target");
            PersistBoundaryLayout(persisted);
            var initial = PersistBoundaryLayout((EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Editable initial"));
            var menu = MountBoundaryMenu();
            InitializeBoundaryProfile(menu, initial);
            Assert.That(_boundaryLayoutWrites, Is.Zero, "Opening the profile editor must not write a layout preference.");

            Call(menu, "ReverseLayout");
            Assert.That((bool)menu.GetType().GetProperty("IsDirty").GetValue(menu), Is.True,
                "Reverse must change the draft before attempting a management selection.");
            var dropdown = (Component)Field(menu, "_layoutDropdown");
            int targetIndex = BoundaryLayoutIndex(menu, persisted.Id);
            SelectBoundaryDropdown(dropdown, targetIndex);
            yield return null;
            Assert.That((bool)_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.True);
            var keepButtons = BoundaryDialogButtons();
            Assert.That(keepButtons, Has.Length.EqualTo(2));
            InvokeBoundaryDialogButton(keepButtons[0]);
            yield return null;
            Assert.That((bool)_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.False);
            Assert.That((int)dropdown.GetType().GetProperty("value").GetValue(dropdown),
                Is.EqualTo(BoundaryLayoutIndex(menu, initial.Id)),
                "Keep Editing restores the previously accepted selector item.");
            Assert.That((bool)menu.GetType().GetProperty("IsDirty").GetValue(menu), Is.True);
            Assert.That(_boundaryProfileLayoutId, Is.EqualTo(initial.Id));
            Assert.That(_boundaryLayoutWrites, Is.Zero);

            SelectBoundaryDropdown(dropdown, targetIndex);
            yield return null;
            var discardButtons = BoundaryDialogButtons();
            Assert.That(discardButtons[1].GetComponentInChildren<Button>().interactable, Is.False,
                "Discard starts behind the production confirmation delay.");
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.That(discardButtons[1].GetComponentInChildren<Button>().interactable, Is.True);
            InvokeBoundaryDialogButton(discardButtons[1]);
            yield return null;
            Assert.That((bool)_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.False);
            Assert.That(_boundaryProfileLayoutId, Is.EqualTo(persisted.Id));
            Assert.That(_boundaryLayoutWrites, Is.EqualTo(1),
                "Discard commits a single selection only after resolving the dirty draft.");
            Assert.That(((EliteDrumsLayoutPreset)menu.GetType().GetProperty("Draft").GetValue(menu)).Id,
                Is.EqualTo(persisted.Id));
#else
            Assert.Ignore("Requires production prefabs from the Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator UnsupportedManagementSelectionDoesNotAssignProfile_StoredUnsupportedMember()
        {
#if UNITY_EDITOR
            IsolateBoundaryLayoutStore("eliteDrumsUnsupportedSelection");
            var unsupported = CreateUnsupportedBoundaryLayout();
            PersistBoundaryLayout(unsupported);
            var menu = MountBoundaryMenu();
            InitializeBoundaryProfile(menu, EliteDrumsLayoutPreset.Compact);
            SelectBoundaryDropdown((Component)Field(menu, "_layoutDropdown"), BoundaryLayoutIndex(menu, unsupported.Id));
            yield return null;

            Assert.That(_boundaryProfileLayoutId, Is.EqualTo(EliteDrumsLayoutPreset.CompactId));
            Assert.That(_boundaryLayoutWrites, Is.Zero);
            Assert.That(menu.GetType().GetProperty("Draft").GetValue(menu), Is.Null,
                "A stored but unsupported layout remains management-only.");
#else
            Assert.Ignore("Requires production prefabs from the Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator ProfilePreferencesSurviveDraftDiscard_ActualLeftyColorAndDiscardActions()
        {
#if UNITY_EDITOR
            IsolateBoundaryLayoutStore("eliteDrumsPreferenceDiscard");
            var initial = PersistBoundaryLayout((EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Editable preferences draft"));
            var menu = MountBoundaryMenu();
            InitializeBoundaryProfile(menu, initial);
            Assert.That(_boundaryPreferenceWrites, Is.Zero, "Initial profile preferences load without write callbacks.");
            yield return null; // Allow the production preview player to complete Start before preference interaction.

            var toggle = (Toggle)Field(menu, "_leftyFlipToggle");
            toggle.isOn = true;
            var colorDropdown = (Component)Field(menu, "_previewColor");
            var colors = ((Array)Field(menu, "_previewColors")).Cast<ColorProfile>().ToArray();
            int currentColorIndex = Array.FindIndex(colors, color => color.Id == _boundaryProfileColorId);
            int nextColorIndex = (currentColorIndex + 1) % colors.Length;
            SelectBoundaryDropdown(colorDropdown, nextColorIndex);
            Assert.That(_boundaryProfileLeftyFlip, Is.True);
            Assert.That(_boundaryProfileColorId, Is.EqualTo(colors[nextColorIndex].Id));
            Assert.That(_boundaryPreferenceWrites, Is.EqualTo(2),
                "Lefty Flip and color each write immediately, exactly once.");

            Call(menu, "ReverseLayout");
            Assert.That((bool)menu.GetType().GetProperty("IsDirty").GetValue(menu), Is.True,
                "Reverse must change the layout draft before requesting discard.");
            Call(menu, "Back");
            yield return null;
            var buttons = BoundaryDialogButtons();
            Assert.That(buttons[1].GetComponentInChildren<Button>().interactable, Is.False);
            yield return new WaitForSecondsRealtime(1.1f);
            InvokeBoundaryDialogButton(buttons[1]);
            yield return null;

            Assert.That(_root.activeSelf, Is.False, "Discard closes the editor through its real close path.");
            Assert.That(_boundaryProfileLayoutId, Is.EqualTo(initial.Id),
                "Discarded assignment edits do not alter the accepted profile layout.");
            Assert.That(_boundaryProfileLeftyFlip, Is.True);
            Assert.That(_boundaryProfileColorId, Is.EqualTo(colors[nextColorIndex].Id));
            Assert.That(_boundaryPreferenceWrites, Is.EqualTo(2),
                "Discarding layout edits does not roll back or re-save already committed preferences.");
#else
            Assert.Ignore("Requires production prefabs from the Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator SavedCopyAssignsPersistedId_ProductionCopyDialogAndContainer()
        {
#if UNITY_EDITOR
            IsolateBoundaryLayoutStore("eliteDrumsSavedCopyBoundary");
            var menu = MountBoundaryMenu();
            InitializeBoundaryProfile(menu, EliteDrumsLayoutPreset.Compact);
            Assert.That(_boundaryLayoutWrites, Is.Zero);
            ((Button)Field(menu, "_copyLayout")).onClick.Invoke();
            yield return null;
            var rename = _dialogRoot.GetComponentInChildren(Production("YARG.Menu.Dialogs.RenameDialog"), true);
            Assert.That(rename, Is.Not.Null, "Copy must use the production name dialog.");
            var input = rename.GetType().GetField("_inputField", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rename);
            input.GetType().GetProperty("text").SetValue(input, "Persisted profile copy");
            var buttonContainer = (Transform)rename.GetType().GetMethod("get_DialogButtonContainer",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rename, null);
            var buttons = buttonContainer.GetComponentsInChildren(Production("YARG.Menu.ColoredButton"), true)
                .Cast<Component>().ToArray();
            Assert.That(buttons, Has.Length.EqualTo(2));
            InvokeBoundaryDialogButton(buttons[1]);
            yield return null;

            var custom = ((System.Collections.IEnumerable)_layoutContainerType.GetProperty("CustomPresets")
                .GetValue(_layoutContainer)).Cast<EliteDrumsLayoutPreset>().Single();
            Assert.That(custom.Name, Is.EqualTo("Persisted profile copy"));
            Assert.That(File.Exists(Directory.GetFiles(_layoutRoot, "*.json", SearchOption.AllDirectories).Single()), Is.True);
            Assert.That(_boundaryProfileLayoutId, Is.EqualTo(custom.Id),
                "The real Copy flow must assign the ID returned by the persisted custom container entry.");
            Assert.That(_boundaryLayoutWrites, Is.EqualTo(1), "Copy selection must assign exactly once.");
            Assert.That(Directory.GetFiles(_layoutRoot, "*.json", SearchOption.AllDirectories), Has.Length.EqualTo(1),
                "Production Copy must persist exactly one layout file.");
            Assert.That(((EliteDrumsLayoutPreset)menu.GetType().GetProperty("Draft").GetValue(menu)).Id,
                Is.EqualTo(custom.Id));
#else
            Assert.Ignore("Requires production prefabs from the Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator SavedEditedStoredLayoutRetainsAssignedId_ProductionSaveAndContainer()
        {
#if UNITY_EDITOR
            IsolateBoundaryLayoutStore("eliteDrumsSavedCustomBoundary");
            var stored = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Stored profile layout");
            PersistBoundaryLayout(stored);
            var menu = MountBoundaryMenu();
            InitializeBoundaryProfile(menu, stored);
            Call(menu, "ShiftRight");
            Assert.That((bool)menu.GetType().GetProperty("IsDirty").GetValue(menu), Is.True);
            ((Button)Field(menu, "_save")).onClick.Invoke();
            yield return null;

            var committed = ((System.Collections.IEnumerable)_layoutContainerType.GetProperty("CustomPresets")
                .GetValue(_layoutContainer)).Cast<EliteDrumsLayoutPreset>().Single();
            Assert.That(committed.Id, Is.EqualTo(stored.Id));
            Assert.That(_boundaryProfileLayoutId, Is.EqualTo(committed.Id),
                "The Save path must retain the persisted ID and current profile assignment.");
            Assert.That(_boundaryCommitCalls, Is.EqualTo(1));
            Assert.That(_boundaryLayoutWrites, Is.Zero,
                "Saving an already assigned layout must not redundantly write its profile ID.");
            Assert.That(_root.activeSelf, Is.True, "A successful save must keep the profile editor open.");
            Assert.That(menu.GetType().GetProperty("IsDirty").GetValue(menu), Is.False);
            Call(menu, "ShiftLeft");
            Assert.That(menu.GetType().GetProperty("IsDirty").GetValue(menu), Is.True,
                "Further changes compare against the newly saved layout.");
            Call(menu, "ShiftRight");
            Assert.That(menu.GetType().GetProperty("IsDirty").GetValue(menu), Is.False,
                "Returning to the saved layout must clear the unsaved-change state.");
            ((Button)Field(menu, "_save")).onClick.Invoke();
            Assert.That(_boundaryCommitCalls, Is.EqualTo(2), "Save remains usable without reopening the editor.");
            Assert.That(_boundaryProfileLayoutId, Is.EqualTo(stored.Id));
            Assert.That(_root.activeSelf, Is.True);
            Call(menu, "Back");
            Assert.That(_root.activeSelf, Is.False, "Back is the separate exit action.");
#else
            Assert.Ignore("Requires production prefabs from the Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator StaleSaveIsRejectedBeforePersistence()
        {
#if UNITY_EDITOR
            IsolateBoundaryLayoutStore("eliteDrumsStaleSaveBoundary");
            var stored = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Stored stale profile");
            PersistBoundaryLayout(stored);
            var menu = MountBoundaryMenu();
            InitializeBoundaryProfile(menu, stored);
            Assert.That(_boundaryCommitCalls, Is.Zero);
            Call(menu, "ShiftRight");
            Assert.That((bool)menu.GetType().GetProperty("IsDirty").GetValue(menu), Is.True);
            _boundaryProfileIsCurrent = false;
            Call(menu, "Save");
            Assert.That(_boundaryCommitCalls, Is.Zero, "A removed or replaced profile must not persist a pending layout save.");
            Assert.That(_boundaryLayoutWrites, Is.Zero);
            Assert.That(_root.activeSelf, Is.False, "Stale context closes rather than leaving a dialog able to mutate another profile.");
            var custom = ((System.Collections.IEnumerable)_layoutContainerType.GetProperty("CustomPresets")
                .GetValue(_layoutContainer)).Cast<EliteDrumsLayoutPreset>().Single();
            Assert.That(custom.Id, Is.EqualTo(stored.Id));
            Assert.That(custom.Name, Is.EqualTo("Stored stale profile"));
            yield break;
#else
            Assert.Ignore("Requires production prefabs from the Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator DeletingSelectedLayoutUpdatesProfileFallback_ProductionDeleteDialogAndContainer()
        {
#if UNITY_EDITOR
            IsolateBoundaryLayoutStore("eliteDrumsDeleteAssignedBoundary");
            var assigned = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Assigned for delete");
            PersistBoundaryLayout(assigned);
            var menu = MountBoundaryMenu();
            InitializeBoundaryProfile(menu, assigned);
            ((Button)Field(menu, "_deleteLayout")).onClick.Invoke();
            yield return null;
            var buttons = BoundaryDialogButtons();
            Assert.That(buttons[1].GetComponentInChildren<Button>().interactable, Is.False);
            yield return new WaitForSecondsRealtime(2.1f);
            InvokeBoundaryDialogButton(buttons[1]);
            yield return null;

            Assert.That(((System.Collections.IEnumerable)_layoutContainerType.GetProperty("CustomPresets")
                .GetValue(_layoutContainer)).Cast<EliteDrumsLayoutPreset>(), Is.Empty);
            Assert.That(_boundaryProfileLayoutId, Is.EqualTo(EliteDrumsLayoutPreset.CompactId));
            Assert.That(_boundaryLayoutWrites, Is.EqualTo(1), "The assigned preset deletion commits exactly one usable fallback.");
            Assert.That(((EliteDrumsLayoutPreset)menu.GetType().GetProperty("Draft").GetValue(menu)).Id,
                Is.EqualTo(EliteDrumsLayoutPreset.CompactId));
#else
            Assert.Ignore("Requires production prefabs from the Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator DeletingUnselectedUnsupportedLayoutKeepsAssignedProfile()
        {
#if UNITY_EDITOR
            IsolateBoundaryLayoutStore("eliteDrumsDeleteUnselectedBoundary");
            var unsupported = CreateUnsupportedBoundaryLayout();
            PersistBoundaryLayout(unsupported);
            var menu = MountBoundaryMenu();
            InitializeBoundaryProfile(menu, EliteDrumsLayoutPreset.Full);
            SelectBoundaryDropdown((Component)Field(menu, "_layoutDropdown"), BoundaryLayoutIndex(menu, unsupported.Id));
            yield return null;
            ((Button)Field(menu, "_deleteLayout")).onClick.Invoke();
            yield return null;
            var buttons = BoundaryDialogButtons();
            Assert.That(buttons[1].GetComponentInChildren<Button>().interactable, Is.False);
            yield return new WaitForSecondsRealtime(2.1f);
            InvokeBoundaryDialogButton(buttons[1]);
            yield return null;

            Assert.That(_boundaryProfileLayoutId, Is.EqualTo(EliteDrumsLayoutPreset.FullId));
            Assert.That(_boundaryLayoutWrites, Is.Zero,
                "Deleting an unsupported, unassigned management item must not replace the profile layout with fallback.");
            Assert.That(((System.Collections.IEnumerable)_layoutContainerType.GetProperty("CustomPresets")
                .GetValue(_layoutContainer)).Cast<EliteDrumsLayoutPreset>(), Is.Empty);
#else
            Assert.Ignore("Requires production prefabs from the Editor asset database.");
            yield break;
#endif
        }

        private static EliteDrumsLayoutPreset CreateUnsupportedBoundaryLayout()
        {
            var unsupported = new EliteDrumsLayoutPreset("Stored unsupported management layout")
            {
                Merges = new[] { new EliteDrumsLayoutMerge
                    { EffectiveId = "tom1+tom2", Sources = new[] { "tom1", "tom2" } } }
            };
            unsupported.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", null);
            unsupported.Slots[1] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat);
            unsupported.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom1+tom2", "left-crash");
            unsupported.Slots[3] = EliteDrumsLayoutSlot.Ordinary("tom3", "ride");
            unsupported.Slots[4] = EliteDrumsLayoutSlot.Ordinary(null, "right-crash");
            return unsupported;
        }
    }
}
