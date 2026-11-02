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
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Input.Diagnostics;

namespace YARG.Tests.PlayMode
{
    public sealed class MidiTestMenuPlayModeTests
    {
        private const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private EliteConfigurationPointerPlayModeTests _theme;
        private GameObject _canvas, _menu, _camera;
        private YargProfile _profile;
        private string _oldPath, _directory;
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).First(t => t != null);
        private static object Field(object owner, string name) => owner.GetType().GetField(name, FLAGS).GetValue(owner);
        private static string Text(object owner) => (string)owner.GetType().GetProperty("text").GetValue(owner);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Reuse the established native theme/audio/localization fixture; it restores all globals.
            _theme = new EliteConfigurationPointerPlayModeTests();
            yield return _theme.SetUpRealTheme();
            var path = Production("YARG.Helpers.PathHelper").GetProperty("PersistentDataPath");
            _oldPath = (string)path.GetValue(null);
            _directory = Path.Combine(Path.GetTempPath(), "yarg-midi-screen-" + Guid.NewGuid().ToString("N"));
            path.GetSetMethod(true).Invoke(null, new object[] { _directory });
        }

        [UnityTest]
        public IEnumerator NativeScreenRetainsLastNoteRendersReceiptStateAndDrainsLogOnClose()
        {
            _canvas = new GameObject("MIDI test native canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = _canvas.GetComponent<Canvas>();
            _camera = new GameObject("MIDI test render camera", typeof(Camera));
            var camera = _camera.GetComponent<Camera>();
            camera.orthographic = true;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            var prefab = Resources.Load<GameObject>("MidiTestMenu");
            Assert.That(prefab, Is.Not.Null);
            _menu = UnityEngine.Object.Instantiate(prefab, _canvas.transform);
            _menu.SetActive(true);
            var type = Production("YARG.Menu.MidiTest.MidiTestMenu");
            var controller = _menu.GetComponent(type);
            _profile = new YargProfile { Name = "MIDI diagnostic example", GameMode = GameMode.EliteDrums };
            type.GetMethod("Initialize").Invoke(controller, new object[] { _profile });
            var capture = Field(controller, "_capture");
            var session = (MidiTestSession)Field(capture, "_session");
            session.Configuration = new MidiTestConfiguration(new[] { new MidiTestEndpoint(100, "Test drum module", 9) }, new[] {
                new MidiTestBinding(100, 9, true, 4, "stomp", "Hi-Hat Pedal", 1, true),
                new MidiTestBinding(100, 9, false, 42, "hat", "Closed Hi-Hat", 2),
                new MidiTestBinding(100, 9, false, 42, "crash", "Left Crash", 3)
            });
            session.Receive(new MidiTestMessage(100, "Test drum module", 1000, .001, 0xB9, 4, 0));
            session.Receive(new MidiTestMessage(100, "Test drum module", 1001, .001, 0x99, 42, 96));
            session.Receive(new MidiTestMessage(100, "Test drum module", 1002, .001, 0xB9, 4, 127));
            session.Receive(new MidiTestMessage(100, "Test drum module", 1003, .001, 0xA9, 42, 127));
            session.Receive(new MidiTestMessage(100, "Test drum module", 1004, .001, 0xD9, 61, 0));
            type.GetMethod("RefreshDisplay", FLAGS).Invoke(controller, null);
            Assert.That(Text(Field(controller, "_note")), Does.Contain("#42"));
            Assert.That(Text(Field(controller, "_matches")), Does.Contain("Closed Hi-Hat").And.Contain("Left Crash"));
            Assert.That(Text(Field(controller, "_pedal")), Does.Contain("Closed"));
            Assert.That(Text(Field(controller, "_recordLabel")), Does.Contain("OFF"));
            yield return null;
            foreach (int width in new[] { 1280, 1920 })
            {
                int height = width * 9 / 16;
                var target = new RenderTexture(width, height, 24);
                var previous = RenderTexture.active;
                var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                try
                {
                    camera.targetTexture = target;
                    camera.aspect = (float)width / height;
                    camera.pixelRect = new Rect(0, 0, width, height);
                    canvas.scaleFactor = width / 1920f;
                    yield return null; // Let the camera target drive the Canvas rect before measuring/rendering.
                    Canvas.ForceUpdateCanvases();
                    Assert.That(canvas.pixelRect.width, Is.EqualTo(width).Within(1));
                    Assert.That(((RectTransform)_menu.transform).rect.height, Is.EqualTo(1080).Within(1));
                    type.GetMethod("RefreshDisplay", FLAGS).Invoke(controller, null);
                    var title = _menu.transform.Find("Header/Single Header Text");
                    Assert.That(Text(title.GetComponent(Production("TMPro.TextMeshProUGUI"))), Is.EqualTo("MIDI TEST"));
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                    string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/investigations/midi-test-screen/aftertouch"));
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, $"screen-{width}x{height}.png"), image.EncodeToPNG());
                    int brightHeaderPixels = 0;
                    for (int y = height - height * 100 / 720; y < height; y++)
                    for (int x = 0; x < width / 2; x++)
                    {
                        var pixel = image.GetPixel(x, y);
                        if (pixel.r > .7f && pixel.g > .7f && pixel.b > .7f) brightHeaderPixels++;
                    }
                    Assert.That(brightHeaderPixels, Is.GreaterThan(100), "The native header title must be visible in the rendered screen");
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = previous;
                    target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(image);
                }
            }
            var rows = ((IEnumerable)Field(controller, "_rows")).Cast<object>().ToArray();
            Assert.That(Text(rows[0].GetType().GetField("Input").GetValue(rows[0])), Does.Contain("CHANNEL AFTERTOUCH"));
            Assert.That(Text(rows[0].GetType().GetField("Value").GetValue(rows[0])), Is.EqualTo("61"),
                "Channel pressure comes from Data1, not the unused Data2 padding.");
            Assert.That(Text(rows[1].GetType().GetField("Input").GetValue(rows[1])), Does.Contain("KEY AFTERTOUCH").And.Contain("#42"));
            Assert.That(Text(rows[1].GetType().GetField("Value").GetValue(rows[1])), Is.EqualTo("127"));
            Assert.That(Text(rows[3].GetType().GetField("Input").GetValue(rows[3])), Does.Contain("Test drum module").And.Contain("CH 10"));
            Assert.That(Text(rows[3].GetType().GetField("Pedal").GetValue(rows[3])), Does.Contain("Open"),
                "The note keeps the pedal state received with it, despite the subsequent CC update.");
            var detailScroll = (ScrollRect)Field(controller, "_detailScroll");
            var messageRect = ((Component)Field(controller, "_latestMessage")).GetComponent<RectTransform>();
            Assert.That(-messageRect.offsetMin.y, Is.LessThan(detailScroll.content.rect.height),
                "All detail lines must remain accessible inside the scroll content.");
            Assert.That(detailScroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null,
                "Long details must stay clipped above the footer.");
            ((Button)Field(controller, "_record")).onClick.Invoke();
            Assert.That((bool)capture.GetType().GetProperty("IsRecording").GetValue(capture), Is.True);
            string log = (string)capture.GetType().GetProperty("LogPath").GetValue(capture);
            ((Button)Field(controller, "_clear")).onClick.Invoke();
            Assert.That(session.History, Is.Empty);
            Assert.That(session.CurrentPedalValue, Is.EqualTo(127));
            _menu.SetActive(false);
            Assert.That((bool)capture.GetType().GetProperty("IsRecording").GetValue(capture), Is.False);
            Assert.That(File.ReadAllText(log), Does.Contain("recording_end"));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_menu != null) UnityEngine.Object.Destroy(_menu);
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
            if (_camera != null) UnityEngine.Object.Destroy(_camera);
            yield return null;
            if (_profile != null)
            {
                var container = Production("YARG.Input.Bindings.BindingsContainer");
                var dictionary = (IDictionary)container.GetField("_bindings", FLAGS).GetValue(null);
                (dictionary[_profile.Id] as IDisposable)?.Dispose(); dictionary.Remove(_profile.Id);
            }
            if (_oldPath != null) Production("YARG.Helpers.PathHelper").GetProperty("PersistentDataPath")
                .GetSetMethod(true).Invoke(null, new object[] { _oldPath });
            if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            if (_theme != null) yield return _theme.TearDown();
        }
    }
}
