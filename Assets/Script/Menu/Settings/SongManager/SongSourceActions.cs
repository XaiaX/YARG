using System;
using System.IO;
using System.Threading.Tasks;
using YARG.Core.Song.Cache;
using YARG.Helpers;
using YARG.Song;

namespace YARG.Menu.Settings
{
    /// <summary>Shared boundaries for the Song Manager and its production UI tests.</summary>
    public static class SongSourceActions
    {
        public static Action<string, string, Action<string>> ChooseFile = FileExplorerHelper.OpenChooseSourceDtaFile;
        public static Func<SongScanScope, Task> Refresh = async scope =>
        {
            using var context = new LoadingContext();
            await SongContainer.RunRefresh(scope, false, context);
        };
        public static Func<bool> IsRefreshing = () => SongContainer.IsRebuildingLibrary;
        public static bool Busy { get; private set; }
        public static event Action Changed;
        public static string Feedback { get; private set; } = string.Empty;

        public static string NormalizePath(string path) => string.IsNullOrWhiteSpace(path)
            ? string.Empty : Path.GetFullPath(path);

        public static bool PathsEqual(string left, string right)
        {
            try
            {
                return string.Equals(NormalizePath(left), NormalizePath(right),
                    Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }
            catch { return string.Equals(left, right, StringComparison.Ordinal); }
        }

        public static string ValidateSelection(string path)
        {
            var extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".dta", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".yargup", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Select a .dta or .yargup file.");
            var normalized = NormalizePath(path);
            using var stream = File.Open(normalized, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return normalized;
        }

        public static async Task Scan(SongScanScope scope)
        {
            if (Busy || IsRefreshing()) return;
            Busy = true;
            Feedback = YARG.Localization.Localize.Key("Menu.Settings.Sources.Scanning");
            Changed?.Invoke();
            try
            {
                await Refresh(scope);
                var result = SongContainer.LastScanResult;
                Feedback = result == null ? string.Empty : result.Status == SongScanStatus.Success
                    ? YARG.Localization.Localize.KeyFormat("Menu.Settings.Sources.Completed", result.AffectedCount, result.UnmatchedCount)
                    : result.Message;
            }
            catch (Exception ex) { Feedback = ex.Message; }
            finally
            {
                Busy = false;
                Changed?.Invoke();
                if (SettingsMenu.Instance != null) SettingsMenu.Instance.RefreshAndKeepPosition();
            }
        }
    }
}
