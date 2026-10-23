using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core.Song.Cache;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Menu.Settings;

namespace YARG.Settings.Metadata
{
    public class SongManagerTab : MetadataTab
    {
        public SongManagerTab(string name, string icon = "Generic") : base(name, icon) { }

        private static GameObject Spawn(string address, Transform parent) => UnityEngine.Object.Instantiate(
            Addressables.LoadAssetAsync<GameObject>(address).WaitForCompletion(), parent);

        private static TextMeshProUGUI Text(Transform parent, string text, bool header = false)
        {
            var go = Spawn(header ? "SettingTab/Header" : "SettingTab/Text", parent);
            var label = go.GetComponentInChildren<TextMeshProUGUI>();
            label.text = text;
            label.richText = false;
            if (!header)
            {
                var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
                go.AddComponent<SongSourceTextLayout>().Initialize(label, layout);
            }
            return label;
        }

        private static void Register(GameObject row, NavigationGroup navigation)
        {
            // The original directory/header buttons have no controller navigation component.
            // Attach the existing runtime outline behavior to those actual button controls.
            foreach (var button in row.GetComponentsInChildren<Button>())
            {
                Action confirm = () => { if (button.interactable) button.onClick.Invoke(); };
                var existing = button.GetComponent<NavigatableBehaviour>();
                if (existing is RuntimeNavigatable runtime)
                {
                    runtime.ConfirmCallback = confirm;
                    // Section buttons clone the header template, including its outline, but
                    // Unity does not clone the delegate that toggles that outline.
                    var outline = button.transform.Find("SelectedOutline");
                    if (outline != null)
                    {
                        outline.gameObject.SetActive(false);
                        runtime.SelectionVisual = outline.gameObject.SetActive;
                    }
                }
                else if (existing == null) RuntimeNavigatable.Attach(button.gameObject, confirm);
            }
            // Prefab hierarchy order does not match the buttons' left-to-right placement.
            // Rows are built top-to-bottom; keep navigation in visual order within each row.
            foreach (var control in row.GetComponentsInChildren<NavigatableBehaviour>()
                .OrderBy(control => row.transform.InverseTransformPoint(
                    ((RectTransform)control.transform).TransformPoint(((RectTransform)control.transform).rect.center)).x))
                navigation.AddNavigatable(control);
            foreach (var path in row.GetComponentsInChildren<SongSourcePathDisplay>())
                path.BindNavigation(row);
            row.AddComponent<SongSourceBusyVisual>();
        }

        private static void Buttons(Transform parent, NavigationGroup navigation,
            params SettingsButton.CustomButton[] buttons)
        {
            var go = Spawn("SettingTab/Button", parent);
            var control = go.GetComponent<SettingsButton>();
            control.SetCustomButtons(buttons);
            navigation.AddNavigatable(control);
            go.AddComponent<SongSourceBusyVisual>();
        }

        private static SettingsButton.CustomButton Button(string key, Action action) =>
            new(Localize.Key("Menu.Settings.Sources." + key), action);

        private static void ShowHelp() => SongSourceHelp.Show();

        private static void Section(Transform parent, NavigationGroup navigation, string key,
            YARG.Menu.ColoredButton template, params SettingsButton.CustomButton[] actions)
        {
            var label = Text(parent, Localize.Key("Menu.Settings.Sources." + key), true);
            var row = label.transform;
            while (row.parent != parent) row = row.parent;
            row.name = "SongSources." + key;
            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                var clone = UnityEngine.Object.Instantiate(template, row);
                var rect = (RectTransform)clone.transform;
                rect.anchorMin = new Vector2(1, 0);
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(1, 0.5f);
                rect.sizeDelta = new Vector2(170, 0);
                rect.anchoredPosition = new Vector2(-15 - 180 * (actions.Length - i - 1), 0);
                clone.name = "SongSources." + key + "." + i;
                var button = clone.GetComponentInChildren<Button>();
                button.name = clone.name + ".Button";
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => action.Action());
                clone.Text.text = action.Label;
            }
            label.rectTransform.offsetMax = new Vector2(-195 * actions.Length, label.rectTransform.offsetMax.y);
            Register(row.gameObject, navigation);
        }

        public override void BuildSettingTab(Transform parent, NavigationGroup navigation)
        {
            var settings = SettingsManager.Settings;
            var header = Spawn("SettingTab/SongManagerHeader", parent);
            var headerControl = header.GetComponent<SongManagerHeader>();
            headerControl.Configure(ShowHelp);
            var template = headerControl.ActionTemplate;
            Register(header, navigation);
            if (settings.ShowExplicitSongSourcesWarning)
            {
                Buttons(parent, navigation, Button("MigrationNotice", () =>
                    DialogManager.Instance.ShowMessage(Localize.Key("Menu.Settings.Sources.Help"), Localize.Key("Menu.Settings.Sources.Migration"))),
                    Button("Dismiss", () =>
                    {
                        settings.ShowExplicitSongSourcesWarning = false;
                        SettingsManager.SaveSettings();
                        SettingsMenu.Instance.RefreshAndKeepPosition();
                    }));
            }
            Section(parent, navigation, "Songs", template,
                Button("AddFolder", headerControl.AddNewFolder),
                Button("Scan", () => _ = SongSourceActions.Scan(SongScanScope.Songs)));
            for (int i = 0; i < settings.SongFolders.Count; i++)
            {
                var row = Spawn("SettingTab/SongManagerDirectory", parent);
                row.GetComponent<SettingsDirectory>().SetIndex(i);
                Register(row, navigation);
            }
            BuildSource(parent, navigation, SongScanScope.Updates, template);
            BuildSource(parent, navigation, SongScanScope.Upgrades, template);
            var feedback = Text(parent, SongSourceActions.Feedback);
            feedback.gameObject.AddComponent<SongSourceFeedback>();
            base.BuildSettingTab(parent, navigation);
        }

        private static void BuildSource(Transform parent, NavigationGroup navigation, SongScanScope scope,
            YARG.Menu.ColoredButton template)
        {
            string key = scope == SongScanScope.Updates ? "Updates" : "Upgrades";
            Section(parent, navigation, key, template, Button("Scan", () => _ = SongSourceActions.Scan(scope)));
            var row = Spawn("SettingTab/SongManagerDirectory", parent);
            row.GetComponent<SettingsDirectory>().SetSource(scope);
            Register(row, navigation);
        }
    }
}
