// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.Events;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Game;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YARG.Tests.PlayMode
{
    public sealed partial class ProfileListSelectionPlayModeTests
    {
        [UnityTest]
        public IEnumerator MicrophoneSetup_JoinedProfileHasCorrectLabelsAndCanConfigureReorderAndClose()
        {
#if UNITY_EDITOR
            var profile = _addedProfiles[2];
            profile.GameMode = GameMode.PartyVocals;
            var menu = GetProfileMenu();
            var sidebar = (Component)PrivateField(menu, "_profileSidebar").GetValue(menu);
            var bindingType = Production("YARG.Input.Bindings.BindingsContainer");
            var bindings = bindingType.GetMethod("GetBindingsForProfile").Invoke(null, new object[] { profile });
            var playerType = Production("YARG.Player.YargPlayer");
            var player = playerType.GetConstructor(new[] { typeof(YargProfile), Production("YARG.Input.ProfileBindings") })
                .Invoke(new[] { (object)profile, bindings });
            _players.Add(player);
            _playersByProfile.Add(profile, player);
            var localization = Production("YARG.Localization.LocalizationManager");
            var dictionary = (IDictionary)PrivateField(localization, "_localizationMap", true).GetValue(null);
            var previous = new List<DictionaryEntry>();
            foreach (DictionaryEntry entry in dictionary) previous.Add(entry);
            var oldCulture = localization.GetProperty("CultureCode").GetValue(null);
            var setup = (Component)null;
            try
            {
                var load = localization.GetMethod("TryParseAndLoadLanguage", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That((bool)load.Invoke(null, new object[] { "en-US" }), Is.True);
                Call(bindings, "AddMicrophone", new SetupMic(391));
                Call(bindings, "AddMicrophone", new SetupMic(392));
                Call(bindings, "AddMicrophone", new SetupMic(393));
                var dialogType = Production("YARG.Menu.Persistent.DialogManager");
                var manager = dialogType.GetProperty("Instance", INHERITED_STATIC).GetValue(null);
                PrivateField(dialogType, "_listPrefab").SetValue(manager,
                    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Menu/Common/Dialogs/ListDialog.prefab")
                        .GetComponent(Production("YARG.Menu.Dialogs.ListDialog")));
                var container = new GameObject("Microphone dialogs", typeof(RectTransform));
                container.transform.SetParent(_canvasObject.transform, false);
                var containerRect = (RectTransform)container.transform;
                containerRect.anchorMin = Vector2.zero; containerRect.anchorMax = Vector2.one;
                containerRect.offsetMin = containerRect.offsetMax = Vector2.zero;
                PrivateField(dialogType, "_dialogContainer").SetValue(manager, container.transform);
                Call(sidebar, "UpdateSidebar", profile, ProfileRow(menu, profile));
                yield return null;
                var setupRow = (Component)PrivateField(sidebar, "_microphoneSetupRow").GetValue(sidebar);
                Assert.That(setupRow.gameObject.activeInHierarchy, Is.True);
                var textType = Production("TMPro.TextMeshProUGUI");
                var rowLabel = setupRow.transform.Find("Option Name").GetComponent(textType);
                var configureButton = setupRow.GetComponentInChildren<Button>(true);
                var buttonLabel = setupRow.transform.Find("Configure").GetComponent(textType);
                Assert.That(buttonLabel, Is.Not.Null, "The authored row keeps its caption beside the button hit target.");
                Assert.That((string)textType.GetProperty("text").GetValue(rowLabel), Is.EqualTo("MICROPHONE CONFIGURATION"));
                Assert.That((string)textType.GetProperty("text").GetValue(buttonLabel), Is.EqualTo("CONFIGURE"));
                // A relocalization or subsequent activation must retain the new labels,
                // rather than restoring the cloned MIDI diagnostic translations.
                foreach (var localized in setupRow.GetComponentsInChildren(Production("YARG.Localization.LocalizeText"), true))
                    localized.GetType().GetMethod("UpdateText").Invoke(localized, null);
                setupRow.gameObject.SetActive(false);
                setupRow.gameObject.SetActive(true);
                Assert.That((string)textType.GetProperty("text").GetValue(rowLabel), Is.EqualTo("MICROPHONE CONFIGURATION"));
                Assert.That((string)textType.GetProperty("text").GetValue(buttonLabel), Is.EqualTo("CONFIGURE"));
                int playerCount = _players.Count;
                var setupFocus = PrivateField(sidebar, "_microphoneSetupNavigation").GetValue(sidebar);
                var profileNavigation = PrivateField(menu, "_navigationGroup").GetValue(menu);
                var origin = Production("YARG.Menu.Navigation.SelectionOrigin");
                profileNavigation.GetType().GetMethod("Select").Invoke(profileNavigation,
                    new object[] { setupFocus, Enum.Parse(origin, "Navigation") });
                Assert.That(Call(menu, "GetSelectedProfile", true), Is.SameAs(profile),
                    "Focusing a sidebar control must retain the owning profile selection.");
                configureButton.onClick.Invoke();
                yield return null;
                setup = sidebar.GetComponent(Production("YARG.Menu.ProfileList.MicrophoneSetup"));
                var dialog = (Component)PrivateField(setup, "_dialog").GetValue(setup);
                Assert.That((bool)dialog.GetType().GetProperty("IsOpen").GetValue(dialog), Is.True);
                Assert.That(((IList)PrivateField(setup, "_rows").GetValue(setup)).Count, Is.EqualTo(3));
                Assert.That(_players.Count, Is.EqualTo(playerCount), "Opening microphone setup must not change the joined roster.");
                foreach (var arrow in dialog.GetComponentsInChildren<Button>(true).Where(b => b.name == "MoveUp" || b.name == "MoveDown"))
                {
                    var icon = arrow.transform.parent.Find("Icon");
                    Assert.That(icon, Is.Not.Null, "Reorder arrows must retain the existing icon.");
                    Assert.That(icon.GetComponent<Image>().sprite, Is.Not.Null);
                }
                yield return CaptureMicrophoneSetup("overview");
                var overviewNavigation = PrivateField(setup, "_navigation").GetValue(setup);
                overviewNavigation.GetType().GetMethod("SelectAt").Invoke(overviewNavigation,
                    new object[] { (int?)0, Enum.Parse(origin, "Programmatically") });
                Call(setup, "Navigate", -1, false);
                int lastAction = (int)overviewNavigation.GetType().GetProperty("Count").GetValue(overviewNavigation) - 1;
                Assert.That(overviewNavigation.GetType().GetProperty("SelectedIndex").GetValue(overviewNavigation), Is.EqualTo(lastAction),
                    "Keyboard/controller navigation must reach Add Microphone after the last mic row.");
                Call(overviewNavigation, "ConfirmSelection");
                Assert.That(PrivateField(setup, "_dialog").GetValue(setup), Is.Not.SameAs(dialog),
                    "Confirming Add must open the microphone picker.");
                Call(setup, "ShowMicrophone", 1);
                yield return null;
                var slider = (Component)PrivateField(setup, "_sensitivity").GetValue(setup);
                slider.GetType().GetProperty("Value").SetValue(slider, -5.7f);
                yield return null;
                Assert.That(slider.GetType().GetProperty("Value").GetValue(slider), Is.EqualTo(-5.5f),
                    "The displayed sensitivity must match the quantized value that is actually applied.");
                var navigation = PrivateField(setup, "_navigation").GetValue(setup);
                navigation.GetType().GetMethod("SelectAt").Invoke(navigation,
                    new object[] { (int?)7, Enum.Parse(origin, "Programmatically") });
                Call(navigation, "ConfirmSelection");
                yield return null;
                var rows = (IEnumerable)bindings.GetType().GetProperty("MicrophoneBindings").GetValue(bindings);
                var second = rows.Cast<object>().ElementAt(1);
                Assert.That(second.GetType().GetProperty("NeedleId").GetValue(second), Is.EqualTo(5));
                Assert.That(second.GetType().GetProperty("SensitivityOffsetDb").GetValue(second), Is.EqualTo(-5.5f));
                yield return CaptureMicrophoneSetup("detail");
                navigation = PrivateField(setup, "_navigation").GetValue(setup);
                navigation.GetType().GetMethod("SelectAt").Invoke(navigation,
                    new object[] { (int?)8, Enum.Parse(origin, "Programmatically") });
                Call(setup, "Navigate", 1, false);
                Assert.That(navigation.GetType().GetProperty("SelectedIndex").GetValue(navigation), Is.EqualTo(10),
                    "Down from the last needle row must reach Remove.");
                Assert.DoesNotThrow(() => Call(setup, "Navigate", 1, true), "Right on Remove must not leave the navigation bounds.");
                Call(setup, "ShowMicrophones", 1);
                yield return null;
                dialog = (Component)PrivateField(setup, "_dialog").GetValue(setup);
                var move = dialog.GetComponentsInChildren<Button>(true).First(b => b.name == "MoveUp" && b.interactable);
                move.onClick.Invoke();
                yield return null;
                rows = (IEnumerable)bindings.GetType().GetProperty("MicrophoneBindings").GetValue(bindings);
                Assert.That(rows.Cast<object>().First(), Is.SameAs(second), "Reordering must carry the whole microphone configuration.");
                Call(setup, "ShowMicrophonePicker");
                Assert.DoesNotThrow(() => Call(setup, "ShowMicrophoneOpenFailure"),
                    "An unavailable device must replace the picker with a useful message, never stack dialogs.");
                var currentDialog = PrivateField(manager, "_currentDialog").GetValue(manager);
                Assert.That(currentDialog.GetType().Name, Is.EqualTo("MessageDialog"));
                Call(manager, "ClearDialog");
                Call(setup, "Close");
                Assert.That(((IList)bindings.GetType().GetProperty("Microphones").GetValue(bindings)).Count, Is.EqualTo(3),
                    "Closing configuration retains captures owned by a joined profile.");
                Assert.That(((IEnumerable)bindings.GetType().GetProperty("MicrophoneBindings").GetValue(bindings)).Cast<object>().Count(), Is.EqualTo(3));
                Assert.That((bool)manager.GetType().GetProperty("IsDialogShowing").GetValue(manager), Is.False);
                Assert.That(_preferenceSaveCount, Is.GreaterThan(0));
                Assert.That(_players.Count, Is.EqualTo(playerCount));

                configureButton.onClick.Invoke();
                yield return null;
                _players.Remove(player);
                _playersByProfile.Remove(profile);
                try
                {
                    Call(sidebar, "UpdateSidebar", profile, ProfileRow(menu, profile));
                    Assert.That(((IList)bindings.GetType().GetProperty("Microphones").GetValue(bindings)).Count, Is.Zero,
                        "Disconnecting an open configuration releases its profile's captures.");
                    Assert.That(setupRow.gameObject.activeSelf, Is.False);
                    Assert.That(configureButton.interactable, Is.False);
                    Assert.That((bool)manager.GetType().GetProperty("IsDialogShowing").GetValue(manager), Is.False);
                    Assert.That(((IEnumerable)bindings.GetType().GetProperty("MicrophoneBindings").GetValue(bindings))
                        .Cast<object>().Count(), Is.EqualTo(3), "Disconnecting preserves saved microphone assignments.");
                    Assert.DoesNotThrow(() => Call(sidebar, "OpenMicrophoneSetup"));
                    Assert.That((bool)manager.GetType().GetProperty("IsDialogShowing").GetValue(manager), Is.False,
                        "A stale configure action cannot reopen a disconnected profile.");
                }
                finally
                {
                    if (!_players.Contains(player)) _players.Add(player);
                    if (!_playersByProfile.Contains(profile)) _playersByProfile.Add(profile, player);
                }
            }
            finally
            {
                if (setup != null) Call(setup, "Close");
                _players.Remove(player);
                _playersByProfile.Remove(profile);
                ((IDisposable)bindings).Dispose();
                ((IDictionary)PrivateField(bindingType, "_bindings", true).GetValue(null)).Remove(profile.Id);
                dictionary.Clear();
                foreach (var entry in previous) dictionary.Add(entry.Key, entry.Value);
                localization.GetProperty("CultureCode").GetSetMethod(true).Invoke(null, new[] { oldCulture });
            }
#else
            Assert.Ignore("Requires production Editor assets.");
            yield break;
#endif
        }

        private IEnumerator CaptureMicrophoneSetup(string name)
        {
            string directory = Environment.GetEnvironmentVariable("YARG_MICROPHONE_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) yield break;
            var canvas = _canvasObject.GetComponent<Canvas>();
            var cameraObject = new GameObject("Microphone capture camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            var target = new RenderTexture(1920, 1080, 24);
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
            var oldScale = canvas.scaleFactor;
            var scaler = _canvasObject.GetComponent<CanvasScaler>();
            var oldScaleMode = scaler != null ? scaler.uiScaleMode : CanvasScaler.ScaleMode.ConstantPixelSize;
            var oldScalerFactor = scaler != null ? scaler.scaleFactor : 1f;
            var oldTarget = RenderTexture.active;
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                camera.targetTexture = target;
                if (scaler != null) { scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1f; }
                canvas.scaleFactor = 1f;
                Canvas.ForceUpdateCanvases();
                yield return null;
                camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera;
                if (scaler != null) { scaler.uiScaleMode = oldScaleMode; scaler.scaleFactor = oldScalerFactor; }
                canvas.scaleFactor = oldScale;
                RenderTexture.active = oldTarget; camera.targetTexture = null;
                UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(cameraObject);
            }
        }

        private sealed class SetupMic : MicDevice
        {
            private readonly int _id;
            public SetupMic(int id) : base("Studio microphone") { _id = id; }
            public override string StableId => ComputeStableId(_id, DisplayName);
            public override int Reset() => 0;
            public override bool DequeueOutputFrame(out MicOutputFrame frame) { frame = default; return false; }
            public override void ClearOutputQueue() { }
            public override void SetMonitoringLevel(float value) { }
            public override void SetReverbLevel(float value) { }
            public override MicBufferInfo? GetBufferInfo() => null;
            public override SerializedMic Serialize() => new(DisplayName, 0, StableId);
            protected override void DisposeUnmanagedResources() { }
        }
    }
}
