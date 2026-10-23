using System.IO;
using UnityEngine;
using YARG.Core.Song.Cache;
using YARG.Helpers;
using YARG.Settings;
using YARG.Song;

namespace YARG.Menu.Settings
{
    public class SongManagerHeader : MonoBehaviour
    {
        [SerializeField]
        private ColoredButton _badSongsButton;

        private void Awake()
        {
            CheckBadSongsFile();
        }

        public ColoredButton ActionTemplate { get; private set; }

        public void Configure(System.Action showHelp)
        {
            foreach (var button in GetComponentsInChildren<ColoredButton>(true))
            {
                if (button == _badSongsButton) continue;
                var unityButton = button.GetComponentInChildren<UnityEngine.UI.Button>();
                if (unityButton == null) continue;
                for (int i = 0; i < unityButton.onClick.GetPersistentEventCount(); i++)
                {
                    string method = unityButton.onClick.GetPersistentMethodName(i);
                    if (method == nameof(RefreshSongs))
                    {
                        button.Text.text = YARG.Localization.Localize.Key("Menu.Settings.Sources.ScanAll");
                        ActionTemplate = button;
                    }
                    else if (method == nameof(AddNewFolder))
                    {
                        button.Text.text = YARG.Localization.Localize.Key("Menu.Settings.Sources.Help");
                        // Replace the persistent Add Folder callback, not just runtime listeners.
                        unityButton.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                        unityButton.onClick.AddListener(() => showHelp());
                        break;
                    }
                }
            }
            CheckBadSongsFile();
        }

        public void AddNewFolder()
        {
            SettingsManager.Settings.SongFolders.Add(string.Empty);
            SettingsManager.SaveSettings();
            SettingsMenu.Instance.RefreshAndKeepPosition();
        }

        public async void RefreshSongs()
        {
            await SongSourceActions.Scan(SongScanScope.All);
        }

        private void CheckBadSongsFile()
        {
            _badSongsButton.gameObject.SetActive(File.Exists(PathHelper.BadSongsPath));
            
            var numErrors = CacheHandler.Progress.BadSongCount;

            if (numErrors > 0)
            {
                var errors = numErrors == 1 ? "ERROR" : "ERRORS";
                _badSongsButton.Text.text = $"{numErrors} {errors} FOUND";
            }
        }

        public void OpenBadSongs()
        {
            FileExplorerHelper.OpenFolder(PathHelper.BadSongsPath);
        }
    }
}