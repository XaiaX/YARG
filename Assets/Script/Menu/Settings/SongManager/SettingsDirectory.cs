using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using YARG.Core.Song.Cache;
using YARG.Helpers;
using YARG.Localization;
using YARG.Menu.Persistent;
using YARG.Settings;
using YARG.Song;

namespace YARG.Menu.Settings
{
    public class SettingsDirectory : MonoBehaviour
    {
        private static List<string> SongFolders => SettingsManager.Settings.SongFolders;

        [SerializeField] private TextMeshProUGUI _pathText;
        [SerializeField] private TextMeshProUGUI _songCountText;

        private int _index;
        private SongScanScope? _sourceScope;

        public void SetIndex(int index)
        {
            _index = index;
            RefreshText();
        }

        public void SetSource(SongScanScope scope)
        {
            _sourceScope = scope;
            RefreshText();
        }

        private string SourcePath => _sourceScope == SongScanScope.Updates
            ? SettingsManager.Settings.SongUpdatesDtaPath : SettingsManager.Settings.SongUpgradesDtaPath;

        private void RefreshText()
        {
            string path = _sourceScope.HasValue ? SourcePath : SongFolders[_index];
            _pathText.richText = false;
            string displayPath = string.IsNullOrEmpty(path)
                ? Localize.Key(_sourceScope.HasValue ? "Menu.Settings.Sources.None" : "Menu.Settings.NoFolder") : path;
            SongSourcePathDisplay.SetPath(_pathText, displayPath);
            _pathText.GetComponent<SongSourcePathDisplay>().BindNavigation(gameObject);

            if (_sourceScope.HasValue)
            {
                var applied = SongContainer.AppliedSources;
                string appliedPath = _sourceScope == SongScanScope.Updates ? applied?.UpdatesDtaPath : applied?.UpgradesDtaPath;
                string status = !string.IsNullOrEmpty(path) && !File.Exists(path) ? "MissingCompact"
                    : string.IsNullOrEmpty(path) && string.IsNullOrEmpty(appliedPath) ? "None"
                    : applied == null || !SongSourceActions.PathsEqual(path, appliedPath) ? "PendingCompact" : "Applied";
                // Entry totals describe DTA definitions, not the number of affected library songs.
                int? count = _sourceScope == SongScanScope.Updates
                    ? SongContainer.UpdatesDefinitionCount : SongContainer.UpgradesDefinitionCount;
                _songCountText.text = status == "Applied" && count.HasValue
                    ? Localize.KeyFormat("Menu.Settings.Sources.EntryCount", count.Value)
                    : Localize.Key("Menu.Settings.Sources." + status);
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                _songCountText.text = string.Empty;
                return;
            }
            bool appliedFolder = false;
            if (SongContainer.AppliedSources != null)
                foreach (string folder in SongContainer.AppliedSources.SongFolders)
                    if (SongSourceActions.PathsEqual(path, folder)) { appliedFolder = true; break; }
            if (!appliedFolder)
            {
                _songCountText.text = Localize.Key("Menu.Settings.ScanNeeded");
                return;
            }
            int songCount = 0;
            foreach (var song in SongContainer.UnfilteredSongs)
                if (IsWithinFolder(song.ActualLocation, path)) songCount++;
            _songCountText.text = Localize.KeyFormat("Menu.Settings.SongCount", songCount);
        }

        public static bool IsWithinFolder(string location, string folder)
        {
            if (string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(folder)) return false;
            try
            {
                string root = TrimFolderSeparator(Path.GetFullPath(folder));
                string full = TrimFolderSeparator(Path.GetFullPath(location));
                var comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return string.Equals(full, root, comparison) ||
                    full.StartsWith(root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                        ? root : root + Path.DirectorySeparatorChar, comparison);
            }
            catch (Exception) { return false; }
        }

        private static string TrimFolderSeparator(string path)
        {
            string root = Path.GetPathRoot(path);
            return path.Length > root.Length ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
        }

        private void SetSourcePath(string path)
        {
            if (_sourceScope == SongScanScope.Updates) SettingsManager.Settings.SongUpdatesDtaPath = path;
            else SettingsManager.Settings.SongUpgradesDtaPath = path;
            SettingsManager.SaveSettings();
            SettingsMenu.Instance.RefreshAndKeepPosition();
        }

        public void Remove()
        {
            if (SongSourceActions.Busy || SongSourceActions.IsRefreshing()) return;
            if (_sourceScope.HasValue) { SetSourcePath(string.Empty); return; }
            SongFolders.RemoveAt(_index);
            SettingsManager.SaveSettings();
            SettingsMenu.Instance.RefreshAndKeepPosition();
        }

        public void Browse()
        {
            if (SongSourceActions.Busy || SongSourceActions.IsRefreshing()) return;
            if (_sourceScope.HasValue)
            {
                string path = SourcePath;
                SongSourceActions.ChooseFile(string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path), "dta", selected =>
                {
                    if (!this || !gameObject.activeInHierarchy || SongSourceActions.Busy || SongSourceActions.IsRefreshing() ||
                        !SongSourceActions.PathsEqual(SourcePath, path)) return;
                    try { SetSourcePath(SongSourceActions.ValidateSelection(selected)); }
                    catch (Exception ex) { DialogManager.Instance.ShowMessage(Localize.Key("Menu.Settings.Sources.Invalid"), ex.Message); }
                });
                return;
            }
            var folders = SongFolders;
            string original = folders[_index];
            FileExplorerHelper.OpenChooseFolder(original, folder =>
            {
                if (!this || !gameObject.activeInHierarchy || SongSourceActions.Busy || SongSourceActions.IsRefreshing() ||
                    !ReferenceEquals(folders, SongFolders) || _index >= folders.Count || folders[_index] != original) return;
                folders[_index] = folder;
                SettingsManager.SaveSettings();
                SettingsMenu.Instance.RefreshAndKeepPosition();
            });
        }
    }
}
