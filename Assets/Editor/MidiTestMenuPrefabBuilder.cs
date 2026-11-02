// pattern: Imperative Shell
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using YARG.Localization;
using YARG.Menu.MidiTest;
using YARG.Menu.Navigation;

namespace YARG.Editor
{
    public static class MidiTestMenuPrefabBuilder
    {
        private const string RESOURCE = "Assets/Resources/MidiTestMenu.prefab";
        private static TMP_FontAsset Font => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/Barlow/Barlow-Medium.asset");

        [MenuItem("YARG/Build MIDI Test Menu")]
        public static void Build()
        {
            var root = Rect("MidiTestMenu", null, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            root.SetActive(false);
            try
            {
                root.AddComponent<Image>().color = new Color(5f / 255, 6f / 255, 11f / 255);
                var controller = root.AddComponent<MidiTestMenu>();
                Assign(controller, "_navigation", root.AddComponent<NavigationGroup>());
                root.AddComponent<YARG.Menu.MenuObject>().Initialize(YARG.Menu.MenuManager.Menu.MidiTest);
                var header = Clone("Assets/Prefabs/Menu/Common/Headers/Header.prefab", root.transform, "Header");
                var title = header.transform.Find("Single Header Text").GetComponent<TMP_Text>();
                title.text = "MIDI TEST";
                Localize(title.gameObject, "Title");
                var headerBack = header.transform.Find("Button").GetComponent<Button>();
                ClearCallbacks(headerBack);
                Assign(controller, "_headerBack", headerBack);
                float top = 1080 * ((RectTransform)header.transform).anchorMin.y + ((RectTransform)header.transform).offsetMin.y - 12;
                float bottom = HelpBarTop() + 132;
                var left = Rect("Diagnostics", root.transform, Vector2.zero, new Vector2(.34f, 1), new Vector2(28, bottom), new Vector2(-12, top - 1080));
                var right = Rect("History", root.transform, new Vector2(.34f, 0), Vector2.one, new Vector2(12, bottom), new Vector2(-28, top - 1080));
                Panel(left); Panel(right);
                var detailScroll = left.AddComponent<ScrollRect>();
                detailScroll.horizontal = false; detailScroll.movementType = ScrollRect.MovementType.Clamped;
                var detailViewport = Rect("Viewport", left.transform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-24, 0));
                detailViewport.AddComponent<Image>().color = Color.clear;
                detailViewport.AddComponent<RectMask2D>();
                var detailRoot = Rect("Content", detailViewport.transform, new Vector2(0, 1), Vector2.one, new Vector2(0, -820), Vector2.zero);
                ((RectTransform)detailRoot.transform).pivot = new Vector2(.5f, 1);
                ((RectTransform)detailRoot.transform).anchoredPosition = Vector2.zero;
                detailScroll.viewport = (RectTransform)detailViewport.transform;
                detailScroll.content = (RectTransform)detailRoot.transform;
                var detailBarRoot = Clone("Assets/Prefabs/Menu/Common/Scrollbar.prefab", left.transform, "Scrollbar");
                var detailBarRect = (RectTransform)detailBarRoot.transform;
                detailBarRect.anchorMin = new Vector2(1, 0); detailBarRect.anchorMax = Vector2.one;
                detailBarRect.offsetMin = new Vector2(-20, 12); detailBarRect.offsetMax = new Vector2(-6, -12);
                var detailBar = detailBarRoot.GetComponentInChildren<Scrollbar>(true);
                detailBar.direction = Scrollbar.Direction.BottomToTop; detailScroll.verticalScrollbar = detailBar;
                Assign(controller, "_detailScroll", detailScroll);
                Assign(controller, "_profile", Text("Profile", detailRoot.transform, "DRUM PROFILE", 27, 20, -18, -20, -58));
                Assign(controller, "_devices", Text("Devices", detailRoot.transform, "No assigned MIDI devices connected.", 19, 20, -66, -20, -130));
                Caption("Pedal title", detailRoot.transform, "CURRENT HI-HAT STATE", "PedalTitle", -144);
                Assign(controller, "_pedal", Text("Pedal", detailRoot.transform, "Pedal state unknown", 29, 20, -184, -20, -260));
                var dropdown = Dropdown(detailRoot.transform, -278);
                Assign(controller, "_pedalController", dropdown);
                Caption("Note title", detailRoot.transform, "LAST MIDI NOTE", "LastNoteTitle", -354);
                Assign(controller, "_note", Text("Note", detailRoot.transform, "Waiting for a note…", 34, 20, -394, -20, -446));
                Assign(controller, "_velocity", Text("Velocity", detailRoot.transform, "Strike a pad to see its note and velocity.", 23, 20, -451, -20, -490));
                Assign(controller, "_details", Text("Details", detailRoot.transform, "", 18, 20, -497, -20, -572));
                Caption("Matches title", detailRoot.transform, "MATCHING PARTS", "MatchesTitle", -586);
                Assign(controller, "_matches", Text("Matches", detailRoot.transform, "—", 24, 20, -626, -20, -748));
                Assign(controller, "_latestMessage", Text("Latest message", detailRoot.transform, "Waiting for MIDI input…", 18, 20, -760, -20, -796));
                Caption("Title", right.transform, "MIDI INPUT HISTORY", "HistoryTitle", -18);
                var headings = Rect("Column headings", right.transform, new Vector2(0, 1), Vector2.one, new Vector2(18, -100), new Vector2(-34, -62));
                Columns(headings.transform, true);
                var scroll = right.AddComponent<ScrollRect>();
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                var viewport = Rect("Viewport", right.transform, Vector2.zero, Vector2.one, new Vector2(18, 18), new Vector2(-34, -110));
                viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0);
                viewport.AddComponent<RectMask2D>();
                var content = Rect("Content", viewport.transform, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero).GetComponent<RectTransform>();
                content.pivot = new Vector2(.5f, 1);
                scroll.viewport = (RectTransform)viewport.transform;
                scroll.content = content;
                var scrollbarRoot = Clone("Assets/Prefabs/Menu/Common/Scrollbar.prefab", right.transform, "Scrollbar");
                var bar = scrollbarRoot.GetComponentInChildren<Scrollbar>(true);
                var barRect = (RectTransform)scrollbarRoot.transform;
                barRect.anchorMin = new Vector2(1, 0); barRect.anchorMax = Vector2.one;
                barRect.offsetMin = new Vector2(-24, 18); barRect.offsetMax = new Vector2(-8, -110);
                bar.direction = Scrollbar.Direction.BottomToTop;
                scroll.verticalScrollbar = bar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
                var row = Rect("Row template", content, new Vector2(0, 1), Vector2.one, new Vector2(0, -58), Vector2.zero).GetComponent<RectTransform>();
                row.pivot = new Vector2(.5f, 1);
                row.sizeDelta = new Vector2(0, 58);
                row.gameObject.AddComponent<Image>().color = new Color(.03f, .11f, .16f, .7f);
                Columns(row, false);
                row.gameObject.SetActive(false);
                Assign(controller, "_history", scroll);
                Assign(controller, "_rowTemplate", row);
                var footer = Rect("Footer", root.transform, Vector2.zero, new Vector2(1, 0), new Vector2(28, HelpBarTop() + 12), new Vector2(-28, bottom - 12));
                Assign(controller, "_record", Button(footer.transform, "Record", "RECORD LOG: OFF", "RecordingOff", 0, 330));
                Assign(controller, "_recordLabel", footer.transform.Find("Record").GetComponentInChildren<TMP_Text>(true));
                Assign(controller, "_clear", Button(footer.transform, "Clear", "CLEAR HISTORY", "Clear", 350, 300));
                Assign(controller, "_folder", Button(footer.transform, "Folder", "OPEN LOG FOLDER", "Folder", 670, 350));
                var back = Button(footer.transform, "Back", "BACK", "Back", 1040, 220);
                Assign(controller, "_back", back);
                Assign(controller, "_status", Text("Status", footer.transform, "Recording is off. Only this profile’s MIDI devices are monitored.", 19, 0, -74, 0, -108));
                PrefabUtility.SaveAsPrefabAsset(root, RESOURCE);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            InstallProfileButton();
            AssetDatabase.SaveAssets();
        }

        private static GameObject Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = low; rect.offsetMax = high;
            return go;
        }

        private static void Panel(GameObject go)
        {
            var image = go.AddComponent<Image>();
            image.color = new Color(3f / 255, 20f / 255, 33f / 255);
            image.raycastTarget = false;
        }

        private static TMP_Text Text(string name, Transform parent, string value, float size, float left, float top, float right, float bottom)
        {
            var go = Rect(name, parent, new Vector2(0, 1), Vector2.one, new Vector2(left, bottom), new Vector2(right, top));
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = Font; text.fontSize = size; text.color = Color.white;
            text.text = value; text.richText = false; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static void Caption(string name, Transform parent, string value, string key, float top)
        {
            var text = Text(name, parent, value, 22, 20, top, -20, top - 36);
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/Barlow/Barlow-Bold.asset");
            text.color = new Color(.65f, .78f, .85f);
            Localize(text.gameObject, key);
        }

        private static void Columns(Transform parent, bool headings)
        {
            string[] names = { "Time", "Input", "Value", "Pedal" };
            string[] labels = { "TIME (s)", "INPUT", "VELOCITY / VALUE", "PEDAL AT INPUT" };
            string[] keys = { "Time", "Input", "Value", "PedalColumn" };
            float[] x = { 0, .16f, .48f, .69f, 1 };
            for (int i = 0; i < 4; i++)
            {
                var go = Rect(names[i], parent, new Vector2(x[i], 0), new Vector2(x[i + 1], 1), new Vector2(8, 0), new Vector2(-8, 0));
                var text = go.AddComponent<TextMeshProUGUI>();
                text.font = Font; text.fontSize = headings ? 20 : 22;
                text.text = headings ? labels[i] : "—"; text.richText = false;
                text.raycastTarget = false; text.alignment = TextAlignmentOptions.MidlineLeft;
                text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis;
                if (headings) Localize(go, keys[i]);
            }
        }

        private static GameObject Clone(string path, Transform parent, string name)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (template == null) throw new InvalidOperationException("Missing native UI prefab: " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(template);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.SetParent(parent, false); go.name = name;
            return go;
        }

        private static Button Button(Transform parent, string name, string text, string key, float left, float width)
        {
            var go = Clone("Assets/Prefabs/Menu/Common/Buttons/RoundButton.prefab", parent, name);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(left, 0);
            rect.sizeDelta = new Vector2(width, 56);
            var button = go.GetComponentInChildren<Button>(true); ClearCallbacks(button);
            var label = go.GetComponentInChildren<TMP_Text>(true);
            label.text = text; label.fontSize = 24; label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            // Recording label changes at runtime; do not attach LocalizeText to it.
            if (name != "Record") Localize(label.gameObject, key);
            return button;
        }

        private static TMP_Dropdown Dropdown(Transform parent, float top)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab")
                .GetComponentsInChildren<TMP_Dropdown>(true).First();
            var dropdown = UnityEngine.Object.Instantiate(source, parent);
            dropdown.name = "Pedal controller";
            var data = new SerializedObject(dropdown);
            data.FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls").arraySize = 0;
            data.ApplyModifiedPropertiesWithoutUndo(); dropdown.onValueChanged.RemoveAllListeners();
            var rect = (RectTransform)dropdown.transform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(20, top - 54); rect.offsetMax = new Vector2(-20, top);
            dropdown.ClearOptions(); dropdown.AddOptions(new System.Collections.Generic.List<string> { "Choose pedal CC…" });
            return dropdown;
        }

        private static void Assign(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Localize(GameObject go, string key)
        {
            var component = go.GetComponent<LocalizeText>() ?? go.AddComponent<LocalizeText>();
            var data = new SerializedObject(component);
            data.FindProperty("_localizationKey").stringValue = "Menu.MidiTest." + key;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ClearCallbacks(Button button)
        {
            var data = new SerializedObject(button);
            data.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").arraySize = 0;
            data.ApplyModifiedPropertiesWithoutUndo(); button.onClick.RemoveAllListeners();
        }

        private static float HelpBarTop()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/PersistentScene.unity");
            try
            {
                var rect = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RectTransform>(true))
                    .First(r => r.name == "Help Bar");
                return 1080 * rect.anchorMax.y + rect.anchoredPosition.y + rect.sizeDelta.y * (1 - rect.pivot.y);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void InstallProfileButton()
        {
            const string path = "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var sidebar = root.GetComponentInChildren<YARG.Menu.ProfileList.ProfileSidebar>(true);
                var data = new SerializedObject(sidebar);
                var content = (GameObject)data.FindProperty("_sidebarContent").objectReferenceValue;
                var row = content.transform.Find("MIDI Test");
                if (row == null)
                {
                    var template = content.transform.Find("Configure 4-Lane Drums Highway");
                    row = UnityEngine.Object.Instantiate(template, content.transform);
                    row.name = "MIDI Test";
                }
                // Keep the configuration controls together, including when updating an existing row.
                int lastConfigure = content.transform.Cast<Transform>()
                    .Where(child => child.name.StartsWith("Configure ", StringComparison.Ordinal))
                    .Max(child => child.GetSiblingIndex());
                row.SetSiblingIndex(lastConfigure + (row.GetSiblingIndex() > lastConfigure ? 1 : 0));
                var button = row.GetComponentInChildren<Button>(true);
                ClearCallbacks(button);
                foreach (var label in row.GetComponentsInChildren<TMP_Text>(true))
                {
                    label.text = "MIDI TEST";
                    Localize(label.gameObject, "ProfileRow");
                }
                data.Update();
                data.FindProperty("_midiTestButton").objectReferenceValue = button;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
