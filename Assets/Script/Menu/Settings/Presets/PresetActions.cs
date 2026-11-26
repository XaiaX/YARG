using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YARG.Helpers;
using YARG.Localization;
using YARG.Menu.Data;
using YARG.Menu.Persistent;
using YARG.Settings.Metadata;
using YARG.Settings.Customization;
using YARG.Core.Game;

namespace YARG.Menu.Settings
{
    public class PresetActions : MonoBehaviour
    {
        private PresetsTab _tab;

        public void Initialize(PresetsTab tab)
        {
            _tab = tab;

            // Disable the delete button for default (OOTB) presets — they
            // can't be deleted, so the button should look and act disabled.
            var preset = tab.SelectedPreset;
            if (preset is { DefaultPreset: true })
            {
                foreach (var button in GetComponentsInChildren<ColoredButton>())
                {
                    for (int i = 0; i < button.OnClick.GetPersistentEventCount(); i++)
                    {
                        if (button.OnClick.GetPersistentMethodName(i) == nameof(DeletePreset))
                        {
                            button.DisableButton();
                            break;
                        }
                    }
                }
            }
        }

        public void RenamePreset()
        {
            var preset = _tab.SelectedPreset;

            if (preset.DefaultPreset) return;

            DialogManager.Instance.ShowRenameDialog("Rename Preset", value =>
            {
                _tab.SelectedContent.RenamePreset(preset, value);

                SettingsMenu.Instance.Refresh();
            });
        }

        public void CopyPreset()
        {
            var preset = _tab.SelectedPreset;

            var copy = preset.CopyWithNewName($"Copy of {preset.Name}");
            _tab.SelectedContent.CopyPreset(preset, copy);
            _tab.SelectedPreset = copy;

            SettingsMenu.Instance.Refresh();
        }

        public void DeletePreset()
        {
            var preset = _tab.SelectedPreset;

            if (preset.DefaultPreset) return;

            // Deleting is irreversible, so confirm first (same compact dialog
            // as "Copy from note"), with the delete button disabled for a moment
            // so it can't be hit by accidental mashing. The cancel button keeps
            // its brighter "safe" color (delete is the destructive action, so it
            // gets the red default).
            PresetSubTab.ShowCompactConfirmation(
                Localize.Key("Settings.PresetSetting.Dialog.DeletePreset.Title"),
                Localize.KeyFormat("Settings.PresetSetting.Dialog.DeletePreset.Message", preset.Name),
                "Menu.Common.Delete", MenuData.Colors.CancelButton, () =>
                {
                    DialogManager.Instance.ClearDialog();

                    _tab.SelectedContent.DeletePreset(preset);
                    _tab.ResetSelectedPreset();

                    SettingsMenu.Instance.Refresh();
                },
                cancelColor: MenuData.Colors.BrightButton,
                armDelaySeconds: 2f);
        }

        public void ImportPreset()
        {
            FileExplorerHelper.OpenChooseFile(null, "preset", ImportSelectedFile);
        }

        private static CustomContent ReadImportDestination(string path)
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.GetEntry("preset.json") ?? throw new InvalidDataException("Missing preset.json");
            using var reader = new StreamReader(entry.Open());
            var type = (string) JObject.Parse(reader.ReadToEnd()).GetValue("Type", System.StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(type)) throw new InvalidDataException("Missing preset type");
            return PresetsTab.FindImportDestination(type) ??
                throw new InvalidDataException(Localize.KeyFormat("Settings.PresetSetting.Dialog.ImportType.Unsupported", type));
        }

        private void ImportSelectedFile(string path)
        {
            if (_tab.SelectedContent is EliteDrumsLayoutPresetContainer layouts)
            {
                ImportEliteLayout(layouts, path);
                return;
            }
            const string dialog = "Settings.PresetSetting.Dialog.ImportType";
            CustomContent destination;
            try { destination = ReadImportDestination(path); }
            catch (System.Exception e)
            {
                DialogManager.Instance.ShowMessage(Localize.Key(dialog + ".Failed"), e.Message);
                return;
            }

            if (destination == _tab.SelectedContent)
            {
                ImportInto(destination, path);
                return;
            }

            string Label(CustomContent store) => Localize.Key("Settings.PresetType", store.GetType().Name);
            PresetSubTab.ShowCompactConfirmation(Localize.Key(dialog + ".Title"),
                Localize.KeyFormat(dialog + ".Message", Label(destination), Label(_tab.SelectedContent),
                    Localize.Key(dialog + ".SelectedType", destination.PresetTypeStringName)),
                "Menu.Common.Import", MenuData.Colors.ConfirmButton, () =>
                {
                    DialogManager.Instance.ClearDialog();
                    ImportInto(destination, path);
                }, panelSize: new Vector2(860f, 340f));
        }

        private void ImportInto(CustomContent store, string path)
        {
            if (store is ColorProfileContainer colors)
            {
                ImportColorProfile(colors, path);
                return;
            }
            SelectImported(store, store.ImportPreset(path));
        }

        private void SelectImported(CustomContent store, BasePreset preset)
        {
            if (preset == null) return;
            _tab.SelectedContent = store;
            _tab.SelectedPreset = preset;
            SettingsMenu.Instance.Refresh();
        }

        private void ImportColorProfile(ColorProfileContainer store, string path)
        {
            const string dialog = "Settings.PresetSetting.Dialog.ImportColor";
            var imported = store.ImportWithConflictHandler(path, (existing, json) =>
                PresetSubTab.ShowCompactConfirmation(Localize.Key(dialog + ".Title"),
                    Localize.KeyFormat(dialog + ".Message", existing.Name), dialog + ".Overwrite",
                    MenuData.Colors.CancelButton, () =>
                    {
                        DialogManager.Instance.ClearDialog();
                        try
                        {
                            SelectImported(store, store.OverwriteImportedProfile(existing, json));
                        }
                        catch (System.Exception e)
                        {
                            DialogManager.Instance.ShowMessage(Localize.Key(dialog + ".Failed"), e.Message);
                        }
                    }, cancelColor: MenuData.Colors.BrightButton, armDelaySeconds: 2f, panelSize: new Vector2(860f, 340f)));
            SelectImported(store, imported);
        }

        private void ImportEliteLayout(EliteDrumsLayoutPresetContainer store, string path)
        {
            var plan = store.PlanImport(path);
            if (plan.Preset is null || plan.Errors.Count > 0)
            {
                PresetSubTab.ShowCompactConfirmation(
                    Localize.Key("Settings.PresetSetting.Dialog.ImportLayout.Failed.Title"),
                    Localize.KeyFormat("Settings.PresetSetting.Dialog.ImportLayout.Failed.Message",
                        string.Join("\n", plan.Errors)), "Menu.Common.Ok", MenuData.Colors.BrightButton,
                    () => DialogManager.Instance.ClearDialog());
                return;
            }
            void Import()
            {
                var imported = store.ImportPreset(path);
                if (imported is null) return;
                _tab.SelectedPreset = imported;
                SettingsMenu.Instance.Refresh();
            }
            if (!plan.IdConflict) { Import(); return; }
            PresetSubTab.ShowCompactConfirmation(
                Localize.Key("Settings.PresetSetting.Dialog.ImportLayout.Duplicate.Title"),
                Localize.KeyFormat("Settings.PresetSetting.Dialog.ImportLayout.Duplicate.Message", plan.Preset.Name),
                "Menu.Common.Import", MenuData.Colors.CancelButton, () =>
                {
                    DialogManager.Instance.ClearDialog();
                    // Re-identify as a copy so the existing layout is not replaced.
                    var copy = (BasePreset)plan.Preset.CopyWithNewName(plan.Preset.Name + " (Imported)");
                    store.CommitPreset((EliteDrumsLayoutPreset)copy);
                    _tab.SelectedPreset = copy;
                    SettingsMenu.Instance.Refresh();
                },
                cancelColor: MenuData.Colors.BrightButton);
        }

        public void ExportPreset()
        {
            var preset = _tab.SelectedPreset;

            if (preset.DefaultPreset) return;

            // Ask the user for an ending location
            FileExplorerHelper.OpenSaveFile(null, preset.Name, "preset", path => {
                // Delete the file if it already exists
                if (File.Exists(path)) File.Delete(path);

                _tab.SelectedContent.ExportPreset(preset, path);
            });
        }
    }
}