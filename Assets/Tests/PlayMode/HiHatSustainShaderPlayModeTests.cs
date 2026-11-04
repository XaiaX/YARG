// pattern: Imperative Shell
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace YARG.Tests.PlayMode
{
    public sealed class HiHatSustainShaderPlayModeTests
    {
        private GameObject _root;
        private Camera _camera;
        private Mesh _mesh;
        private Material _material;
        private RenderTexture _render;
        private Texture2D _pixels;
        private int _oldHighways;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _oldHighways = Shader.GetGlobalInt("_YargHighwaysN");
            Shader.SetGlobalInt("_YargHighwaysN", 0);
            _root = new GameObject("Hi-hat shader pixel test");
            var surface = new GameObject("Sustain surface");
            surface.transform.SetParent(_root.transform, false);
            surface.layer = 31;
            _mesh = new Mesh { vertices = new[] { new Vector3(-.5f, 0, -2), new Vector3(.5f, 0, -2),
                new Vector3(-.5f, 0, 2), new Vector3(.5f, 0, 2) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 2), new Vector2(1, 2) },
                triangles = new[] { 0, 2, 1, 1, 2, 3 } };
            _mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray();
            surface.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _material = new Material(Resources.Load<Material>("HiHatSustain"));
            surface.AddComponent<MeshRenderer>().sharedMaterial = _material;
            _camera = new GameObject("Shader test camera").AddComponent<Camera>();
            _camera.transform.SetParent(_root.transform, false);
            _camera.enabled = false;
            _camera.transform.position = new Vector3(0, 5, 0);
            _camera.transform.LookAt(Vector3.zero, Vector3.forward);
            _camera.orthographic = true;
            _camera.orthographicSize = 2;
            _camera.cullingMask = 1 << 31;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _render = new RenderTexture(128, 512, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _pixels = new Texture2D(128, 512, TextureFormat.RGBAFloat, false, true);
            _camera.targetTexture = _render;
            yield return null;
        }

        private void SetIfPresent(string property, float value)
        {
            if (_material.HasProperty(property)) _material.SetFloat(property, value);
        }

        private Color[] Frame(float time, float remaining = 1, float held = 0, bool ramp = false, float onset = 1, float splash = 0)
        {
            _mesh.uv2 = new[] { new Vector2(remaining, held), new Vector2(remaining, held),
                new Vector2(ramp ? 0 : remaining, held), new Vector2(ramp ? 0 : remaining, held) };
            _mesh.uv3 = Enumerable.Repeat(new Vector2(onset, splash), 4).ToArray();
            _material.SetFloat("_VisualTime", time);
            var previous = RenderTexture.active;
            try
            {
                _camera.Render();
                RenderTexture.active = _render;
                _pixels.ReadPixels(new Rect(0, 0, 128, 512), 0, 0);
                _pixels.Apply();
                return _pixels.GetPixels();
            }
            finally { RenderTexture.active = previous; }
        }

        private static float CenterBrightness(Color[] pixels) => pixels[256 * 128 + 64].r;

        [Test]
        public void OpenOnsetKeepsCenterAndSpreadsSoftlyIntoTheOuterColumns()
        {
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            if (_material.HasProperty("_HeadRange")) _material.SetVector("_HeadRange", new Vector4(1f / 3, 2f / 3, 0, 0));
            var full = Frame(0, held: 1);
            var start = Frame(0, held: 1, onset: 0);
            Assert.That(start[256 * 128 + 64].r, Is.EqualTo(full[256 * 128 + 64].r).Within(.001));
            Assert.That(start[256 * 128 + 32].r, Is.LessThan(.0001f), "The extra width must begin transparent.");
            var spreading = Frame(0, held: 1, onset: .25f);
            Assert.That(spreading[256 * 128 + 16].r, Is.GreaterThan(.01f));
            Assert.That(spreading[256 * 128 + 16].r, Is.LessThan(spreading[256 * 128 + 32].r * .6f),
                "During the spread, opacity must fall smoothly toward the outer edge.");
            Assert.That(Frame(0, held: 1, onset: 2).Select((pixel, j) => Mathf.Abs(pixel.r - full[j].r)).Max(),
                Is.LessThan(.001f), "The full body must be restored after the attack.");
        }

        [TestCase(0f, .5f, 32, 96)]
        [TestCase(.5f, 1f, 96, 32)]
        public void EdgeOnsetStartsAtTheActualCymbalRatherThanThePedalRegionsCenter(
            float first, float last, int core, int outside)
        {
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            if (_material.HasProperty("_HeadRange")) _material.SetVector("_HeadRange", new Vector4(first, last, 0, 0));
            var full = Frame(0, held: 1);
            var start = Frame(0, held: 1, onset: 0);
            Assert.That(start[256 * 128 + core].r, Is.EqualTo(full[256 * 128 + core].r).Within(.001));
            Assert.That(start[256 * 128 + outside].r, Is.LessThan(.0001f));
            var splash = Frame(0, held: 1, onset: 0, splash: 1);
            Assert.That(splash.Select((pixel, j) => Mathf.Abs(pixel.r - full[j].r)).Max(), Is.LessThan(.001f),
                "A wide splash gem needs no extra-width attack.");
        }

        [Test]
        public void BodyOccupiesMostOfItsFootprintAndFadeRunsAlongLength()
        {
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            var pixels = Frame(0, held: 1, ramp: true);
            float peak = Enumerable.Range(0, 128).Max(x => pixels[128 * 128 + x].r);
            int broad = Enumerable.Range(0, 128).Count(x => pixels[128 * 128 + x].r > peak * .25f);
            Assert.That(broad, Is.GreaterThan(85), "The visible body must fill at least two thirds of the gem footprint.");
            float near = pixels[128 * 128 + 64].r;
            float far = pixels[480 * 128 + 64].r;
            Assert.That(near, Is.GreaterThan(far * 1.3f), "The lifetime fade must run along the highway, not across it.");
        }

        [Test]
        public void OpacityDecaysGraduallyAndReachesExactlyZero()
        {
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            float full = CenterBrightness(Frame(0));
            float half = CenterBrightness(Frame(0, .5f));
            float quarter = CenterBrightness(Frame(0, .25f));
            float end = CenterBrightness(Frame(0, 0));
            Assert.That(full, Is.GreaterThan(.1f), "The pixel test must actually render its sustain surface.");
            Assert.That(half / full, Is.InRange(.7f, .85f));
            Assert.That(quarter / full, Is.InRange(.35f, .5f));
            Assert.That(quarter, Is.LessThan(half));
            Assert.That(end, Is.LessThan(.0001f));
        }

        [Test]
        public void FinalQuarterDissipatesClearlyBeforeTheEndpoint()
        {
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            float full = CenterBrightness(Frame(0));
            Assert.That(CenterBrightness(Frame(0, .25f)) / full, Is.InRange(.35f, .5f));
            Assert.That(CenterBrightness(Frame(0, .1f)) / full, Is.LessThan(.15f));
            Assert.That(CenterBrightness(Frame(0, 0)), Is.LessThan(.0001f));
        }

        [Test]
        public void HeldEmissionDoesNotClipAwayTheVisibleFade()
        {
            _camera.targetTexture = null;
            _render.Release(); UnityEngine.Object.DestroyImmediate(_render);
            _render = new RenderTexture(128, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            _camera.targetTexture = _render;
            _mesh.colors = Enumerable.Repeat(new Color(1, 1, 1, .85f), 4).ToArray();
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            float full = CenterBrightness(Frame(0, held: 1));
            float half = CenterBrightness(Frame(0, .5f, 1));
            Assert.That(half / full, Is.LessThan(.97f), "Display-range clipping must not turn the logarithmic falloff into a constant bright stripe.");
        }

        [Test]
        public void ResourcesMaterialIncludesTheCymbalHighlightTexture()
        {
            Assert.That(_material.HasProperty("_NoiseTex"), Is.True);
            Assert.That(_material.GetTexture("_NoiseTex"), Is.Not.Null);
            Assert.That(_material.GetTexture("_NoiseTex").name, Is.EqualTo("Note_Shader"));
        }

        [Test]
        public void TransparentProfilePixelsCannotProduceGlowingWedges()
        {
            var transparent = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            transparent.SetPixels(Enumerable.Repeat(new Color(1, 1, 1, 0), 4).ToArray());
            transparent.Apply();
            try
            {
                _material.SetTexture("_MainTex", transparent);
                Assert.That(Frame(.2f, held: 1).Max(pixel => pixel.r), Is.LessThan(.0001f),
                    "Imported RGB in fully transparent texels is not part of the sustain silhouette.");
            }
            finally { UnityEngine.Object.DestroyImmediate(transparent); }
        }

        [Test]
        public void HeldRibbonHasVisibleTravelingDistortionAndDeterministicFrames()
        {
            SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            var first = Frame(0, held: 1);
            var later = Frame(.24f, held: 1);
            var repeated = Frame(.24f, held: 1);
            float totalShift = 0;
            int rows = 0;
            for (int y = 32; y < 480; y += 8)
            {
                float Center(Color[] values)
                {
                    float weight = 0, sum = 0;
                    for (int x = 0; x < 128; x++) { float v = values[y * 128 + x].r; weight += v; sum += x * v; }
                    return weight > 0 ? sum / weight / 128 : .5f;
                }
                totalShift += Mathf.Abs(Center(first) - Center(later)); rows++;
            }
            Assert.That(totalShift / rows, Is.GreaterThan(.03f), "The moving body should visibly shift within its reserved fret width.");
            Assert.That(repeated.Select((pixel, i) => Mathf.Abs(pixel.r - later[i].r)).Max(), Is.LessThan(.0001f),
                "Paused/replayed visual time must reproduce the same image.");
            CaptureMotion();
        }

        private void CaptureMotion()
        {
            string directory = Environment.GetEnvironmentVariable("YARG_HIHAT_MOTION_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            System.IO.Directory.CreateDirectory(directory);
            var color = YARG.Core.Game.ColorProfile.Default.EliteDrums.HatOpenNote;
            _mesh.colors = Enumerable.Repeat(new Color(color.R / 255f, color.G / 255f, color.B / 255f, .85f), 4).ToArray();
            SetIfPresent("_PatternStrength", .75f); SetIfPresent("_PulseStrength", .08f);
            var png = new Texture2D(128, 512, TextureFormat.RGB24, false);
            try
            {
                for (int i = 0; i < 60; i++)
                {
                    float time = i / 30f;
                    SetIfPresent("_BeatPhase", time * 2 % 1);
                    var values = Frame(time, held: 1, ramp: true);
                    for (int p = 0; p < values.Length; p++) values[p] = values[p].gamma;
                    png.SetPixels(values); png.Apply();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, $"motion-{i:D3}.png"), png.EncodeToPNG());
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(png); }
        }

        [Test]
        public void BeatPulseChangesBrightnessWithoutChangingOpacity()
        {
            Assert.That(_material.HasProperty("_BeatPhase"), Is.True);
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0);
            _material.SetFloat("_BeatPhase", .5f);
            float trough = CenterBrightness(Frame(0, held: 1));
            _material.SetFloat("_BeatPhase", 0);
            float beat = CenterBrightness(Frame(0, held: 1));
            Assert.That(beat / trough, Is.InRange(1.04f, 1.12f));
            Assert.That(CenterBrightness(Frame(0, 0, 1)), Is.LessThan(.0001f));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Shader.SetGlobalInt("_YargHighwaysN", _oldHighways);
            if (_camera != null) _camera.targetTexture = null;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_pixels != null) UnityEngine.Object.Destroy(_pixels);
            if (_render != null) { _render.Release(); UnityEngine.Object.Destroy(_render); }
            yield return null;
        }
    }
}
