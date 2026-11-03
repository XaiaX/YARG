// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Song;

namespace YARG.Tests.EditMode
{
    public sealed class ClassicDrumModifierMenuTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const Modifier NO_KICK_DYNAMICS = (Modifier) (1UL << 19);
        private GameObject _host;
        private readonly List<IDisposable> _players = new();
        private readonly List<(IList Content, object Layout)> _testLayouts = new();
        private PropertyInfo _menuDataInstance;
        private object _previousMenuData;
        private PropertyInfo _activeSession;
        private object _previousSession;
        private IDictionary<string, string> _localization;
        private KeyValuePair<string, string>[] _previousLocalization;

        [SetUp]
        public void SetUp()
        {
            // Inactive hosts bypass global menu startup, hardware registration and profile I/O.
            _host = new GameObject("Classic modifier menu private fixture");
            _host.SetActive(false);
            var dataType = Production("YARG.Menu.Data.MenuData");
            _menuDataInstance = dataType.BaseType.GetProperty("Instance", STATIC);
            _previousMenuData = _menuDataInstance.GetValue(null);
            var data = _host.AddComponent(dataType);
            Set(data, "_colors", AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                "Assets/Art/Menu/Data/MenuColors.asset"));
            _menuDataInstance.GetSetMethod(true).Invoke(null, new object[] { data });

            _activeSession = Production("YARG.Menu.Maestro.MaestroSetupSession").GetProperty("Active", STATIC);
            _previousSession = _activeSession.GetValue(null);
            var localizationType = Production("YARG.Localization.LocalizationManager");
            _localization = (IDictionary<string, string>) localizationType
                .GetField("_localizationMap", STATIC).GetValue(null);
            _previousLocalization = _localization.ToArray();
            foreach (var modifier in Enum.GetValues(typeof(Modifier)).Cast<Modifier>())
                _localization["Enum.Modifier." + modifier] = modifier.ToString();
            _localization["Menu.DifficultySelect.Done"] = "Done";
        }

        [TearDown]
        public void TearDown()
        {
            _menuDataInstance?.GetSetMethod(true).Invoke(null, new[] { _previousMenuData });
            _activeSession?.GetSetMethod(true).Invoke(null, new[] { _previousSession });
            if (_localization != null && _previousLocalization != null)
            {
                _localization.Clear();
                foreach (var pair in _previousLocalization) _localization.Add(pair);
            }
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            foreach (var player in _players) player.Dispose();
            _players.Clear();
            foreach (var (content, layout) in _testLayouts) content.Remove(layout);
            _testLayouts.Clear();
        }

        [Test]
        public void ProductionModifierRowTogglesThroughConfirmAndPointer()
        {
            var row = InstantiateComponent("Assets/Prefabs/Menu/DifficultySelect/ModifierItem.prefab",
                "YARG.Menu.DifficultySelect.ModifierItem");
            int changes = 0;
            Call(row, "Initialize", "No Dynamics", true, (Action<bool>) (_ => changes++));
            var origin = Enum.Parse(Production("YARG.Menu.Navigation.SelectionOrigin"), "Navigation");
            Call(row, "SetSelected", true, origin);

            Call(row, "Confirm");
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(Property(row, "Active"), Is.False);
            Call(row, "OnPointerDown", new object[] { null });
            Assert.That(changes, Is.EqualTo(2));
            Assert.That(Property(row, "Active"), Is.True);
        }

        [TestCase(Modifier.UnpitchedHarm2)]
        [TestCase(Modifier.UnpitchedHarm3)]
        [TestCase(NO_KICK_DYNAMICS)]
        public void RetainedModifierScoreIconsStillUseTheAuthoredAtlas(Modifier modifier)
        {
            var type = Production("YARG.Menu.ScoreScreen.ModifierIcon");
            var method = type.GetMethod("GetSpriteId", STATIC);
            Assert.That(method, Is.Not.Null);
            var id = (string) method.Invoke(null, new object[] { modifier });
            var sprites = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Menu/Common/ModifierIcons.png")
                .OfType<Sprite>().Select(sprite => sprite.name).ToArray();
            Assert.That(id, Is.EqualTo(modifier == NO_KICK_DYNAMICS
                ? nameof(Modifier.NoKicks) : nameof(Modifier.UnpitchedOnly)));
            Assert.That(sprites, Does.Contain(id), "Retained modifier mappings need an authored sprite.");
        }

        [Test]
        public void RetiredKickDynamicsHasNoUserFacingLocalizationEntry()
        {
            var language = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath,
                "StreamingAssets/lang/en-US.json"));
            Assert.That(language, Does.Not.Contain("\"NoKickDynamics\""));
        }

        [TestCase(GameMode.FourLaneDrums, Instrument.FourLaneDrums)]
        [TestCase(GameMode.FourLaneDrums, Instrument.ProDrums)]
        [TestCase(GameMode.FiveLaneDrums, Instrument.FiveLaneDrums)]
        [TestCase(GameMode.EliteDrums, Instrument.ProDrums)]
        [TestCase(GameMode.EliteDrums, Instrument.FourLaneDrums)]
        [TestCase(GameMode.EliteDrums, Instrument.FiveLaneDrums)]
        [TestCase(GameMode.EliteDrums, Instrument.EliteDrums)]
        [TestCase(GameMode.FiveFretGuitar, Instrument.FiveFretGuitar)]
        public void DifficultyMenuNeverOffersRetiredKickDynamics(GameMode mode, Instrument instrument)
        {
            var profile = Profile(mode, instrument);
            var menu = DifficultyMenu(profile);
            Call(menu, "UpdatePossibleModifiers");
            Call(menu, "CreateModifierMenu");
            var modifiers = ((IEnumerable) Get(menu, "_itemModifiers")).Cast<Modifier>().ToArray();

            Assert.That(modifiers, Has.No.Member(NO_KICK_DYNAMICS));
            if (mode is GameMode.FourLaneDrums or GameMode.FiveLaneDrums or GameMode.EliteDrums)
            {
                Assert.That(modifiers, Does.Contain(Modifier.NoDynamics), "Hand dynamics remain configurable.");
                Assert.That(modifiers, Has.No.Member(Modifier.NoKicks),
                    "Kick removal belongs to Accessibility, not the Modifiers menu.");
                Call(menu, "CreateAccessibilityMenu");
                var accessibility = ((IEnumerable) Get(menu, "_itemModifiers")).Cast<Modifier>().ToArray();
                Assert.That(accessibility, Does.Contain(Modifier.NoKicks), "Kick removal remains configurable.");
                Assert.That(accessibility, Has.No.Member(NO_KICK_DYNAMICS));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DifficultyMenuKeepsHandDynamicsAndKickRemovalIndependent(bool dynamicsDisabled)
        {
            var profile = Profile(GameMode.FourLaneDrums, Instrument.ProDrums);
            if (dynamicsDisabled) profile.AddSingleModifier(Modifier.NoDynamics);
            var menu = DifficultyMenu(profile);
            Call(menu, "UpdatePossibleModifiers");
            Call(menu, "CreateModifierMenu");
            var modifiers = ((IEnumerable) Get(menu, "_itemModifiers")).Cast<Modifier>().ToArray();
            var rows = ((IEnumerable) Get(menu, "_modifierItems")).Cast<Component>().ToArray();
            Assert.That(modifiers, Has.No.Member(NO_KICK_DYNAMICS));
            var dynamics = rows[Array.IndexOf(modifiers, Modifier.NoDynamics)];
            Call(menu, "CreateAccessibilityMenu");
            var accessibility = ((IEnumerable) Get(menu, "_itemModifiers")).Cast<Modifier>().ToArray();
            var accessibilityRows = ((IEnumerable) Get(menu, "_modifierItems")).Cast<Component>().ToArray();
            Assert.That(accessibility, Does.Contain(Modifier.NoKicks));
            Assert.That(accessibility, Has.No.Member(NO_KICK_DYNAMICS));
            var kicks = accessibilityRows[Array.IndexOf(accessibility, Modifier.NoKicks)];

            Call(dynamics, "Confirm");
            Call(kicks, "Confirm");
            Assert.That(profile.IsModifierActive(Modifier.NoDynamics), Is.EqualTo(!dynamicsDisabled));
            Assert.That(profile.IsModifierActive(Modifier.NoKicks), Is.True);
            Assert.That(Property(dynamics, "Active"), Is.EqualTo(!dynamicsDisabled));
            Assert.That(Property(kicks, "Active"), Is.True);
            Call(dynamics, "OnPointerDown", new object[] { null });
            Assert.That(profile.IsModifierActive(Modifier.NoDynamics), Is.EqualTo(dynamicsDisabled));
            Assert.That(profile.IsModifierActive(Modifier.NoKicks), Is.True,
                "Changing hand dynamics must not alter kick removal.");
            Call(kicks, "Confirm");
            profile.RestoreSavedModifiers();
            Assert.That(profile.CurrentModifiers, Is.EqualTo(dynamicsDisabled ? Modifier.NoDynamics : Modifier.None));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OpenMaestroModifierDialogKeepsHandDynamicsEditsSessionOnly(bool dynamicsDisabled)
        {
            var profile = Profile(GameMode.FourLaneDrums, Instrument.ProDrums);
            if (dynamicsDisabled) profile.AddSingleModifier(Modifier.NoDynamics);
            var roster = Roster(profile);
            var sessionType = _activeSession.DeclaringType;
            var session = sessionType.GetMethod("Create", STATIC).Invoke(null,
                new object[] { roster, Array.Empty<SongEntry>(), 0, Guid.Empty });
            _activeSession.GetSetMethod(true).Invoke(null, new[] { session });
            var staged = ((IEnumerable) Property(session, "Players")).Cast<object>().Single();
            var menu = _host.AddComponent(Production("YARG.Menu.Maestro.MaestroSetupMenu"));
            Set(menu, "_selectedProfileId", profile.Id);
            Set(menu, "_modifierItemPrefab", PrefabComponent(
                "Assets/Prefabs/Menu/DifficultySelect/ModifierItem.prefab",
                "YARG.Menu.DifficultySelect.ModifierItem"));
            var dialog = InstantiateComponent("Assets/Prefabs/Menu/Common/Dialogs/ListDialog.prefab",
                "YARG.Menu.Dialogs.ListDialog");
            var modifiers = ((IEnumerable) Call(menu, "GetModifierOptions", staged)).Cast<Modifier>().ToArray();
            Assert.That(modifiers, Has.No.Member(NO_KICK_DYNAMICS));
            Assert.That(modifiers, Does.Contain(Modifier.NoDynamics));
            Assert.That(modifiers, Has.No.Member(Modifier.NoKicks));
            foreach (var modifier in modifiers) Call(menu, "AddModifierToggle", dialog, staged, modifier);
            var rowType = Production("YARG.Menu.DifficultySelect.ModifierItem");
            var rows = dialog.GetComponentsInChildren(rowType, true).Cast<Component>().ToArray();
            var dynamics = rows[Array.IndexOf(modifiers, Modifier.NoDynamics)];
            Call(dynamics, "Confirm");
            Assert.That(Property(dynamics, "Active"), Is.EqualTo(!dynamicsDisabled));
            Assert.That(dialog.GetComponentsInChildren(rowType, true).Cast<Component>(),
                Is.EqualTo(rows), "The modifier dialog keeps its existing rows while toggling.");

            // Open the actual Accessibility construction path for No Kicks.
            var accessibility = ((IEnumerable) Call(session, "GetAvailableAccessibilityModifiers", profile.Id))
                .Cast<Modifier>().ToArray();
            Assert.That(accessibility, Does.Contain(Modifier.NoKicks));
            Assert.That(accessibility, Has.No.Member(NO_KICK_DYNAMICS));
            var accessibilityDialog = InstantiateComponent("Assets/Prefabs/Menu/Common/Dialogs/ListDialog.prefab",
                "YARG.Menu.Dialogs.ListDialog");
            ((IDictionary) Get(menu, "_modifierDialogItems")).Clear();
            Call(menu, "AddAccessibilityOptions", accessibilityDialog, staged, accessibility, 0);
            var kicks = (Component) ((IDictionary) Get(menu, "_modifierDialogItems"))[Modifier.NoKicks];
            var accessibilityRows = accessibilityDialog.GetComponentsInChildren(rowType, true).Cast<Component>().ToArray();
            Call(kicks, "Confirm");
            var expected = Modifier.NoKicks | (dynamicsDisabled ? Modifier.None : Modifier.NoDynamics);
            Assert.That(Property(staged, "Modifiers"), Is.EqualTo(expected));
            Assert.That(Property(kicks, "Active"), Is.True);
            Call(kicks, "OnPointerDown", new object[] { null });
            Assert.That(Property(staged, "Modifiers"),
                Is.EqualTo(dynamicsDisabled ? Modifier.None : Modifier.NoDynamics),
                "Changing kick removal must preserve the staged hand-dynamics choice.");
            Assert.That(accessibilityDialog.GetComponentsInChildren(rowType, true).Cast<Component>(),
                Is.EqualTo(accessibilityRows), "The Accessibility dialog keeps its existing rows while toggling.");
            Assert.That(profile.CurrentModifiers, Is.EqualTo(dynamicsDisabled ? Modifier.NoDynamics : Modifier.None),
                "Staging modifiers must not update the live or saved profile.");
        }

        private void AssignMergedLayout(YargProfile profile)
        {
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Unmerge toggle fixture");
            layout = EliteDrumsLayoutEditor.Place(layout, 2, "left-crash+ride");
            var manager = Production("YARG.Settings.Customization.CustomContentManager");
            var container = manager.GetField("EliteDrumsLayouts", STATIC).GetValue(null);
            var content = (IList)Get(container, "_content");
            content.Add(layout);
            _testLayouts.Add((content, layout));
            profile.EliteDrumsLayout = layout.Id;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnmergeCymbalColors_IsCompactAndOnlyInteractiveWithAMergedLayout(bool merged)
        {
            _localization["Menu.DifficultySelect.KeepOriginalCymbalColors"] = "Unmerge Cymbal Colors";
            var profile = Profile(GameMode.EliteDrums, Instrument.EliteDrums);
            profile.KeepOriginalCymbalColors = true;
            if (merged) AssignMergedLayout(profile);
            var menu = DifficultyMenu(profile);
            Call(menu, "UpdatePossibleModifiers");
            Call(menu, "CreateAccessibilityMenu");
            var row = menu.GetComponentsInChildren(Production("YARG.Menu.DifficultySelect.ModifierItem"), true)
                .Cast<Component>().Single(item => (string)Property(Get(item, "_title"), "text") == "Unmerge Cymbal Colors");
            var interactable = row.GetType().GetProperty("Interactable", INSTANCE);
            Assert.That(interactable, Is.Not.Null, "The inactive option needs an input guard, not just dimmed text.");
            Assert.That(interactable.GetValue(row), Is.EqualTo(merged));
            Assert.That(row.transform.Find("Description"), Is.Null);
            Assert.That(row.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight, Is.EqualTo(60f));
            Call(row, "Confirm");
            Assert.That(profile.KeepOriginalCymbalColors, Is.EqualTo(!merged));
            Call(row, "OnPointerDown", new object[] { null });
            Assert.That(profile.KeepOriginalCymbalColors, Is.True);
            Assert.That(row.GetComponent<CanvasGroup>().alpha, Is.EqualTo(merged ? 1f : .45f).Within(.01f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnmergeCymbalColors_MaestroUsesTheSameLayoutGateWithoutChangingSavedPreference(bool merged)
        {
            _localization["Menu.DifficultySelect.KeepOriginalCymbalColors"] = "Unmerge Cymbal Colors";
            var profile = Profile(GameMode.EliteDrums, Instrument.EliteDrums);
            if (merged) AssignMergedLayout(profile);
            var roster = Roster(profile);
            var session = _activeSession.DeclaringType.GetMethod("Create", STATIC).Invoke(null,
                new object[] { roster, Array.Empty<SongEntry>(), 0, Guid.Empty });
            _activeSession.GetSetMethod(true).Invoke(null, new[] { session });
            var staged = ((IEnumerable)Property(session, "Players")).Cast<object>().Single();
            var menu = _host.AddComponent(Production("YARG.Menu.Maestro.MaestroSetupMenu"));
            Set(menu, "_selectedProfileId", profile.Id);
            Set(menu, "_modifierItemPrefab", PrefabComponent("Assets/Prefabs/Menu/DifficultySelect/ModifierItem.prefab",
                "YARG.Menu.DifficultySelect.ModifierItem"));
            var dialog = InstantiateComponent("Assets/Prefabs/Menu/Common/Dialogs/ListDialog.prefab", "YARG.Menu.Dialogs.ListDialog");
            Call(menu, "AddAccessibilityOptions", dialog, staged, Array.Empty<Modifier>(), 0);
            var row = dialog.GetComponentsInChildren(Production("YARG.Menu.DifficultySelect.ModifierItem"), true)
                .Cast<Component>().Single(item => (string)Property(Get(item, "_title"), "text") == "Unmerge Cymbal Colors");
            var interactable = row.GetType().GetProperty("Interactable", INSTANCE);
            Assert.That(interactable, Is.Not.Null);
            Assert.That(interactable.GetValue(row), Is.EqualTo(merged));
            Assert.That(row.transform.Find("Description"), Is.Null);
            Call(row, "Confirm");
            Assert.That(Property(staged, "KeepOriginalCymbalColors"), Is.EqualTo(merged));
            Assert.That(profile.KeepOriginalCymbalColors, Is.False);
        }

        private const string CYMBAL_COLOR_LABEL = "Unmerge Cymbal Colors";

        private void InstallCymbalStrings()
        {
            _localization["Menu.DifficultySelect.KeepOriginalCymbalColors"] = CYMBAL_COLOR_LABEL;
        }

        private Component CymbalColorRow(Component owner) => owner.GetComponentsInChildren(
            Production("YARG.Menu.DifficultySelect.ModifierItem"), true).Cast<Component>()
            .Single(row => ((string)Property(Get(row, "_title"), "text")).Contains(CYMBAL_COLOR_LABEL));

        private void AssertCompactCymbalRow(Component row)
        {
            Assert.That(row.transform.Find("Description"), Is.Null);
            Assert.That(row.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight, Is.EqualTo(60f));
            Assert.That(((RectTransform)row.transform).rect.height, Is.EqualTo(60f).Within(.1f));
        }

        [Test]
        public void EliteCymbalColors_DifficultyAccessibilityIsCompactAndTogglesOnlyDisplayPreference()
        {
            InstallCymbalStrings();
            var profile = Profile(GameMode.EliteDrums, Instrument.EliteDrums);
            AssignMergedLayout(profile);
            var menu = DifficultyMenu(profile);
            Call(menu, "UpdatePossibleModifiers");
            Call(menu, "CreateModifierMenu");
            var rowType = Production("YARG.Menu.DifficultySelect.ModifierItem");
            Assert.That(menu.GetComponentsInChildren(rowType, true).Cast<Component>()
                .Any(row => ((string)Property(Get(row, "_title"), "text")).Contains(CYMBAL_COLOR_LABEL)), Is.False,
                "The preference belongs to Accessibility.");
            Call(menu, "CreateAccessibilityMenu");
            var row = CymbalColorRow(menu);
            AssertCompactCymbalRow(row);
            Assert.That(Property(row, "Active"), Is.False);
            var modifiers = profile.CurrentModifiers;
            Call(row, "Confirm");
            Assert.That(profile.KeepOriginalCymbalColors, Is.True);
            Assert.That(profile.CurrentModifiers, Is.EqualTo(modifiers));
            Assert.That((string)Call(menu, "BuildAccessibilitySummary", profile), Does.Contain(CYMBAL_COLOR_LABEL));
            Call(row, "OnPointerDown", new object[] { null });
            Assert.That(profile.KeepOriginalCymbalColors, Is.False);
        }

        [TestCase(GameMode.FourLaneDrums, Instrument.ProDrums)]
        [TestCase(GameMode.FiveFretGuitar, Instrument.FiveFretGuitar)]
        public void EliteCymbalColors_OtherModesDoNotOfferThePreference(GameMode mode, Instrument instrument)
        {
            InstallCymbalStrings();
            var profile = Profile(mode, instrument);
            profile.KeepOriginalCymbalColors = true;
            var menu = DifficultyMenu(profile);
            Call(menu, "UpdatePossibleModifiers");
            Call(menu, "CreateAccessibilityMenu");
            Assert.That(menu.GetComponentsInChildren(Production("YARG.Menu.DifficultySelect.ModifierItem"), true)
                .Cast<Component>().Any(row => ((string)Property(Get(row, "_title"), "text")).Contains(CYMBAL_COLOR_LABEL)), Is.False);
            Assert.That((string)Call(menu, "BuildAccessibilitySummary", profile), Does.Not.Contain(CYMBAL_COLOR_LABEL));
        }

        [Test]
        public void EliteCymbalColors_MaestroStagesCompactlyAndPreservesLiveProfileOnRejectedCommit()
        {
            InstallCymbalStrings();
            var profile = Profile(GameMode.EliteDrums, Instrument.EliteDrums);
            AssignMergedLayout(profile);
            var roster = Roster(profile);
            var session = _activeSession.DeclaringType.GetMethod("Create", STATIC).Invoke(null,
                new object[] { roster, Array.Empty<SongEntry>(), 0, Guid.Empty });
            _activeSession.GetSetMethod(true).Invoke(null, new[] { session });
            var staged = ((IEnumerable)Property(session, "Players")).Cast<object>().Single();
            Assert.That(staged.GetType().GetProperty("KeepOriginalCymbalColors"), Is.Not.Null);
            Assert.That(Property(staged, "KeepOriginalCymbalColors"), Is.False);
            var menu = _host.AddComponent(Production("YARG.Menu.Maestro.MaestroSetupMenu"));
            Set(menu, "_selectedProfileId", profile.Id);
            Set(menu, "_modifierItemPrefab", PrefabComponent("Assets/Prefabs/Menu/DifficultySelect/ModifierItem.prefab",
                "YARG.Menu.DifficultySelect.ModifierItem"));
            var dialog = InstantiateComponent("Assets/Prefabs/Menu/Common/Dialogs/ListDialog.prefab", "YARG.Menu.Dialogs.ListDialog");
            Call(menu, "AddAccessibilityOptions", dialog, staged, Array.Empty<Modifier>(), 0);
            var row = CymbalColorRow(dialog);
            AssertCompactCymbalRow(row);
            Call(row, "Confirm");
            Assert.That(Property(staged, "KeepOriginalCymbalColors"), Is.True);
            Assert.That(profile.KeepOriginalCymbalColors, Is.False);
            var result = Call(session, "TryCommit");
            Assert.That(Property(result, "Success"), Is.False);
            Assert.That(profile.KeepOriginalCymbalColors, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EliteCymbalColors_MaestroCapturesPreferenceAndSnapshotRestoresIt(bool initial)
        {
            var profile = Profile(GameMode.EliteDrums, Instrument.EliteDrums);
            profile.KeepOriginalCymbalColors = initial;
            var roster = Roster(profile);
            var session = _activeSession.DeclaringType.GetMethod("Create", STATIC).Invoke(null,
                new object[] { roster, Array.Empty<SongEntry>(), 0, Guid.Empty });
            var staged = ((IEnumerable)Property(session, "Players")).Cast<object>().Single();
            Assert.That(staged.GetType().GetProperty("KeepOriginalCymbalColors"), Is.Not.Null);
            Assert.That(Property(staged, "KeepOriginalCymbalColors"), Is.EqualTo(initial));
            var stage = session.GetType().GetMethod("StageKeepOriginalCymbalColors");
            Assert.That(stage, Is.Not.Null);
            stage.Invoke(session, new object[] { profile.Id, !initial });
            Assert.That(Property(staged, "KeepOriginalCymbalColors"), Is.EqualTo(!initial));
            Assert.That(profile.KeepOriginalCymbalColors, Is.EqualTo(initial));
            var snapshotType = session.GetType().GetNestedType("ProfileSnapshot", BindingFlags.NonPublic);
            var snapshot = Activator.CreateInstance(snapshotType, new[] { roster.GetValue(0) });
            profile.KeepOriginalCymbalColors = !initial;
            Call(snapshot, "Restore");
            Assert.That(profile.KeepOriginalCymbalColors, Is.EqualTo(initial));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EliteCymbalColors_MaestroCommitsOnlyAfterCompleteSetupPasses(bool initial)
        {
            var profile = Profile(GameMode.EliteDrums, Instrument.EliteDrums);
            profile.KeepOriginalCymbalColors = initial;
            var roster = Roster(profile);
            var session = _activeSession.DeclaringType.GetMethod("Create", STATIC).Invoke(null,
                new object[] { roster, new SongEntry[] { new CymbalAccessibilitySong() }, 0, Guid.Empty });
            var stage = session.GetType().GetMethod("StageKeepOriginalCymbalColors");
            Assert.That(stage, Is.Not.Null);
            stage.Invoke(session, new object[] { profile.Id, !initial });
            Assert.That(profile.KeepOriginalCymbalColors, Is.EqualTo(initial));
            var result = Call(session, "TryCommit");
            Assert.That(Property(result, "Success"), Is.True, (string)Property(result, "GlobalError"));
            Assert.That(profile.KeepOriginalCymbalColors, Is.EqualTo(!initial));
        }

        private sealed class CymbalAccessibilitySong : SongEntry
        {
            public CymbalAccessibilitySong()
            {
                _parts.EliteDrums.ActivateDifficulty(Difficulty.Expert);
                _parts.NativeEliteDrums = DifficultyMask.Expert;
            }
            public override EntryType SubType => EntryType.Ini;
            public override string SortBasedLocation => "cymbal-accessibility-fixture";
            public override string ActualLocation => "cymbal-accessibility-fixture";
            public override DateTime GetLastWriteTime() => DateTime.UnixEpoch;
            public override YARG.Core.Chart.SongChart LoadChart(IReadOnlyCollection<Instrument> outputs = null) =>
                throw new InvalidOperationException("Accessibility setup tests never load charts.");
            public override YARG.Core.Audio.StemMixer LoadAudio(float speed, double volume, bool censoring, params YARG.Core.Audio.SongStem[] ignored) =>
                throw new InvalidOperationException("Accessibility setup tests never load audio.");
            public override YARG.Core.Audio.StemMixer LoadPreviewAudio(float speed, bool censoring) =>
                throw new InvalidOperationException("Accessibility setup tests never load audio.");
            public override YARG.Core.IO.YARGImage LoadAlbumData() => null;
            public override BackgroundResult LoadBackground(bool censoring, bool excludeYarground = false) => null;
            public override YARG.Core.IO.FixedArray<byte> LoadMiloData() => null;
            public override YARG.Core.IO.FixedArray<byte> LoadVocData() => null;
        }

        [Test]
        public void EliteCymbalColors_CompactRowRenderedAtReferenceResolutions()
        {
            InstallCymbalStrings();
            var profile = Profile(GameMode.EliteDrums, Instrument.EliteDrums);
            AssignMergedLayout(profile);
            var menu = DifficultyMenu(profile);
            Call(menu, "UpdatePossibleModifiers");
            Call(menu, "CreateAccessibilityMenu");
            var row = CymbalColorRow(menu);
            GameObject canvasObject = null;
            Camera camera = null;
            RenderTexture target = null;
            try
            {
                canvasObject = new GameObject("Cymbal Accessibility capture Canvas", typeof(RectTransform),
                    typeof(Canvas), typeof(UnityEngine.UI.Image));
                canvasObject.GetComponent<UnityEngine.UI.Image>().color = new Color32(5, 6, 11, 255);
                var canvas = canvasObject.GetComponent<Canvas>();
                camera = new GameObject("Cymbal Accessibility capture Camera", typeof(Camera)).GetComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.transform.position = new Vector3(0f, 0f, -100f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10f;
                row.transform.SetParent(canvasObject.transform, false);
                var rowRect = (RectTransform)row.transform;
                rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 0.5f);
                rowRect.anchoredPosition = Vector2.zero;
                foreach (int height in new[] { 720, 1080 })
                {
                    if (target != null)
                    {
                        camera.targetTexture = null;
                        target.Release();
                        UnityEngine.Object.DestroyImmediate(target);
                    }
                    target = new RenderTexture(height * 16 / 9, height, 24);
                    target.Create();
                    camera.targetTexture = target;
                    camera.aspect = 16f / 9f;
                    camera.pixelRect = new Rect(0f, 0f, target.width, height);
                    canvas.scaleFactor = height / 1080f;
                    rowRect.sizeDelta = new Vector2(500f, 60f);
                    AssertCompactCymbalRow(row);
                    Canvas.ForceUpdateCanvases();
                    foreach (bool active in new[] { false, true })
                    {
                        if ((bool)Property(row, "Active") != active) Call(row, "Confirm");
                        foreach (var text in row.GetComponentsInChildren(Production("TMPro.TMP_Text"), true).Cast<Component>())
                        {
                            Call(text, "ForceMeshUpdate", false, false);
                            var info = Property(text, "textInfo");
                            Assert.That((int)info.GetType().GetField("lineCount").GetValue(info), Is.EqualTo(1),
                                "The shortened Accessibility title must stay on one line.");
                            var bounds = (Bounds)Property(text, "textBounds");
                            var rect = (RectTransform)text.transform;
                            Assert.That(bounds.size.x, Is.LessThanOrEqualTo(rect.rect.width + 1f));
                            Assert.That(bounds.size.y, Is.LessThanOrEqualTo(rect.rect.height + 1f));
                        }
                        camera.Render();
                        var previous = RenderTexture.active;
                        var pixels = new Texture2D(target.width, height, TextureFormat.RGB24, false);
                        try
                        {
                            RenderTexture.active = target;
                            pixels.ReadPixels(new Rect(0f, 0f, target.width, height), 0, 0);
                            pixels.Apply();
                            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                $"elite-cymbal-accessibility-{height}-{(active ? "on" : "off")}-{Guid.NewGuid():N}.png");
                            System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
                            Debug.Log("[Cymbal Accessibility capture] " + path);
                        }
                        finally
                        {
                            RenderTexture.active = previous;
                            UnityEngine.Object.DestroyImmediate(pixels);
                        }
                    }
                }
            }
            finally
            {
                if (camera != null) camera.targetTexture = null;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (canvasObject != null) UnityEngine.Object.DestroyImmediate(canvasObject);
                if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }

        private Component DifficultyMenu(YargProfile profile)
        {
            var menu = InstantiateComponent("Assets/Prefabs/Menu/DifficultySelect/DifficultySelectMenu.prefab",
                "YARG.Menu.DifficultySelect.DifficultySelectMenu");
            Set(menu, "_explicitPlayers", Roster(profile));
            return menu;
        }

        private Array Roster(YargProfile profile)
        {
            var type = Production("YARG.Player.YargPlayer");
            var player = Activator.CreateInstance(type, new object[] { profile, null });
            _players.Add((IDisposable) player);
            var roster = Array.CreateInstance(type, 1);
            roster.SetValue(player, 0);
            return roster;
        }

        private static YargProfile Profile(GameMode mode, Instrument instrument) => new(Guid.NewGuid())
        {
            Name = "Classic modifier fixture", GameMode = mode, CurrentInstrument = instrument,
            PreferredInstrument = instrument, CurrentDifficulty = Difficulty.Expert,
        };

        private Component InstantiateComponent(string path, string type) =>
            UnityEngine.Object.Instantiate(PrefabComponent(path, type), _host.transform);

        private static Component PrefabComponent(string path, string type)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            var component = prefab.GetComponent(Production(type));
            Assert.That(component, Is.Not.Null, type);
            return component;
        }

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private static object Get(object value, string field) => value.GetType().GetField(field, INSTANCE).GetValue(value);
        private static void Set(object value, string field, object data) => value.GetType().GetField(field, INSTANCE).SetValue(value, data);
        private static object Property(object value, string property) => value.GetType().GetProperty(property, INSTANCE).GetValue(value);
        private static object Call(object value, string name, params object[] args)
        {
            var method = value.GetType().GetMethod(name, INSTANCE);
            Assert.That(method, Is.Not.Null, "Production modifier API is missing: " + name);
            return method.Invoke(value, args);
        }
    }
}
