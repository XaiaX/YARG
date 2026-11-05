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
            // Test shape and animation independently of the operator's tuned brightness.
            _material.SetVector("_GlowLevels", Vector4.one);
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
            SetIfPresent("_WaveStrength", 0);
            yield return null;
        }

        private void SetIfPresent(string property, float value)
        {
            if (_material.HasProperty(property)) _material.SetFloat(property, value);
        }

        private Color[] Frame(float time, float remaining = 1, float held = 0, bool ramp = false, float splash = 0, bool frontAtMeshStart = false, bool endpointAtMeshEnd = false, float widthProgress = 1, float endMode = 0, float missed = 0, float shapeStart = 0, float shapeEnd = 0)
        {
            _mesh.uv2 = new[] { new Vector2(remaining, held), new Vector2(remaining, held),
                new Vector2(ramp ? 0 : remaining, held), new Vector2(ramp ? 0 : remaining, held) };
            _mesh.uv3 = Enumerable.Repeat(new Vector2(missed, splash), 4).ToArray();
            _mesh.SetUVs(4, new[] { new Vector3(widthProgress, endMode, shapeStart), new Vector3(widthProgress, endMode, shapeStart),
                new Vector3(widthProgress, endMode, shapeEnd), new Vector3(widthProgress, endMode, shapeEnd) });
            float phase = time * 2 - Mathf.Floor(time * 2); // 120 BPM, one wave cycle per quarter
            float front = 1;
            _mesh.SetUVs(3, new[] { new Vector4(0, phase, 4, frontAtMeshStart ? 0 : front), new Vector4(0, phase, 4, frontAtMeshStart ? 0 : front),
                new Vector4(4, phase, endpointAtMeshEnd ? 0 : 4, frontAtMeshStart ? 4 : front), new Vector4(4, phase, endpointAtMeshEnd ? 0 : 4, frontAtMeshStart ? 4 : front) });
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

        [TestCase(.5f)]
        [TestCase(3f)]
        public void WaitingShapeReachesFullWidthAcrossItsWholeLength(float length)
        {
            _mesh.vertices = new[] { new Vector3(-.5f, 0, -length / 2), new Vector3(.5f, 0, -length / 2),
                new Vector3(-.5f, 0, length / 2), new Vector3(.5f, 0, length / 2) };
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            _material.SetVector("_HeadRange", new Vector4(.46f, .54f, 1, 1f / 3));
            var pixels = Frame(0, widthProgress: 0, shapeEnd: 1);
            int Width(float fraction)
            {
                int y = Mathf.FloorToInt((2 - length / 2 + fraction * length) * 128);
                float peak = pixels[y * 128 + 64].r;
                Assert.That(peak, Is.GreaterThan(.01f));
                return Enumerable.Range(0, 128).Count(x => pixels[y * 128 + x].r > peak * .5f);
            }
            int near = Width(.05f), middle = Width(.5f), far = Width(.95f);
            Assert.That(near, Is.InRange(32, 43));
            Assert.That(middle, Is.InRange(62, 73));
            Assert.That(far, Is.InRange(94, 105), "The same broad end must be visible for short and long tails.");
        }

        [Test]
        public void ActivationWidensTheWaitingFlareWithoutNarrowingItsFarEnd()
        {
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            _material.SetVector("_HeadRange", new Vector4(.46f, .54f, 1, 1f / 3));
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            int Width(Color[] pixels, int y)
            {
                float peak = pixels[y * 128 + 64].r;
                return Enumerable.Range(0, 128).Count(x => pixels[y * 128 + x].r > peak * .5f);
            }
            var waiting = Frame(0, widthProgress: 0, shapeEnd: 1);
            var expanding = Frame(0, held: 1, widthProgress: .5f, shapeEnd: 1);
            var full = Frame(0, held: 1, widthProgress: 1, shapeEnd: 1);
            Assert.That(Width(expanding, 26), Is.GreaterThan(Width(waiting, 26) + 20));
            Assert.That(Width(full, 26), Is.GreaterThan(Width(expanding, 26) + 20));
            Assert.That(Width(waiting, 486), Is.GreaterThanOrEqualTo(Width(full, 486) - 5));
            Assert.That(Width(expanding, 486), Is.GreaterThanOrEqualTo(Width(waiting, 486)));
            var pedal = Frame(0, splash: 1, widthProgress: 0, shapeEnd: 1);
            Assert.That(Width(pedal, 26), Is.EqualTo(Width(full, 26)));
            _material.SetVector("_HeadRange", new Vector4(.46f, .54f, 1, 1));
            var oneFret = Frame(0, widthProgress: 0, shapeEnd: 1);
            Assert.That(Width(oneFret, 26), Is.EqualTo(Width(oneFret, 486)), "One-fret mode must not flare into neighboring frets.");
        }

        [Test]
        public void LowHeldOpacityStillShowsHeldAnimation()
        {
            _mesh.colors = Enumerable.Repeat(new Color(1, 1, 1, .1f), 4).ToArray();
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            var full = Frame(0, held: 1);
            SetIfPresent("_WaveStrength", .4f);
            var wave = Frame(0, held: 1);
            Assert.That(wave.Select((pixel, i) => full[i].r - pixel.r).Max(), Is.GreaterThan(.025f),
                "Held state must not be inferred from the chosen opacity.");
        }

        [Test]
        public void RectangularStartUsesAShortSoftFadeAcrossTheWholeWidth()
        {
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            _material.SetVector("_HeadRange", new Vector4(.46f, .54f, 1, 1f / 3));
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0);
            SetIfPresent("_WaveStrength", 0); SetIfPresent("_PulseStrength", 0);
            var full = Frame(0, held: 1);
            var start = Frame(0, held: 1, frontAtMeshStart: true);
            float center = start[1 * 128 + 64].r / full[1 * 128 + 64].r;
            float side = start[1 * 128 + 100].r / full[1 * 128 + 100].r;
            Assert.That(center, Is.InRange(.001f, .5f), "Keep a soft edge instead of a hard rectangular cut.");
            Assert.That(side, Is.EqualTo(center).Within(.001), "The front fade should be flat rather than a curved fan.");
        }

        [Test]
        public void ArcEdgesHaveABroadGradientInsteadOfASharplyDefinedBand()
        {
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            var plain = Frame(0, held: 1);
            SetIfPresent("_WaveStrength", .4f);
            var arcs = Frame(0, held: 1);
            int gradient = Enumerable.Range(80, 32).Count(y =>
            {
                float dark = 1 - arcs[y * 128 + 63].r / plain[y * 128 + 63].r;
                return dark > .05f && dark < .35f;
            });
            Assert.That(gradient, Is.GreaterThanOrEqualTo(14), "Most of each dark arc should blend gradually into the field.");
            float wing = 1 - arcs[104 * 128 + 63].r / plain[104 * 128 + 63].r;
            Assert.That(wing, Is.InRange(.04f, .2f));
            float peak = Enumerable.Range(80, 32).Max(y => 1 - arcs[y * 128 + 63].r / plain[y * 128 + 63].r);
            Assert.That(peak, Is.InRange(.38f, .41f), "Keep the previous peak contrast while broadening the wings.");
        }

        [Test]
        public void EndpointFadeIsFlatAcrossTheHighwayRatherThanCurvingInward()
        {
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            _material.SetVector("_HeadRange", new Vector4(.46f, .54f, 1, 1f / 3));
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0);
            SetIfPresent("_WaveStrength", 0); SetIfPresent("_PulseStrength", 0);
            var full = Frame(0, held: 1);
            var fading = Frame(0, remaining: .3f, held: 1, endpointAtMeshEnd: true);
            float center = fading[480 * 128 + 64].r / full[480 * 128 + 64].r;
            float side = fading[480 * 128 + 24].r / full[480 * 128 + 24].r;
            Assert.That(side, Is.EqualTo(center).Within(.001), "The same ending opacity must apply across the entire width.");
        }

        [Test]
        public void IsolatedOpenWaitsAtOneFretAndWidensAfterActivationWhilePedalStartsWide()
        {
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            _material.SetVector("_HeadRange", new Vector4(.46f, .54f, 1, 1f / 3));
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0);
            SetIfPresent("_WaveStrength", 0); SetIfPresent("_PulseStrength", 0);
            int Width(Color[] pixels)
            {
                float peak = Enumerable.Range(0, 128).Max(x => pixels[256 * 128 + x].r);
                return Enumerable.Range(0, 128).Count(x => pixels[256 * 128 + x].r > peak * .1f);
            }
            int waiting = Width(Frame(0, widthProgress: 0));
            int middle = Width(Frame(0, held: 1, widthProgress: .5f));
            int held = Width(Frame(0, held: 1));
            Assert.That(waiting, Is.InRange(30, 43));
            Assert.That(middle, Is.GreaterThan(waiting + 15));
            Assert.That(held, Is.GreaterThan(middle + 15));
            Assert.That(Width(Frame(0, splash: 1, widthProgress: 0)), Is.EqualTo(held), "A pedal-origin tail is wide before activation.");
        }

        [Test]
        public void CleanPedalEndingStaysOpaqueUntilItsFlatCutoff()
        {
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0);
            SetIfPresent("_WaveStrength", 0); SetIfPresent("_PulseStrength", 0);
            var full = Frame(0, held: 1, endMode: 2);
            var end = Frame(0, remaining: .01f, held: 1, endpointAtMeshEnd: true, endMode: 2);
            Assert.That(end.Select((pixel, i) => Mathf.Abs(pixel.r - full[i].r)).Max(), Is.LessThan(.001f),
                "An explicit pedal-origin cutoff must not lose opacity before its endpoint.");
        }

        [Test]
        public void WaitingGlowHasNoArcsOrAnimationUntilItIsHeld()
        {
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetIfPresent("_WaveStrength", 0); SetIfPresent("_PatternStrength", 0);
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PulseStrength", 0);
            var baseline = Frame(0);
            SetIfPresent("_WaveStrength", .4f); SetIfPresent("_PatternStrength", .75f);
            SetIfPresent("_MotionStrength", 1); SetIfPresent("_PulseStrength", .08f);
            for (int frame = 0; frame < 8; frame++)
            {
                _material.SetFloat("_BeatPhase", frame * .137f % 1);
                var waiting = Frame(frame * .137f);
                Assert.That(waiting.Select((pixel, i) => Mathf.Abs(pixel.r - baseline[i].r)).Max(),
                    Is.LessThan(.0001f), "Waiting tails must contain neither arcs nor time-varying detail.");
            }
        }

        [Test]
        public void HeldWavesTakeOneQuarterNoteToAdvanceOneSpacing()
        {
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            SetIfPresent("_WaveStrength", 0);
            var baseline = Frame(0, held: 1);
            SetIfPresent("_WaveStrength", .4f);
            int Peak(Color[] pixels) => Enumerable.Range(85, 27).OrderByDescending(y =>
                1 - pixels[y * 128 + 63].r / baseline[y * 128 + 63].r).First();
            var start = Frame(0, held: 1);
            _material.SetFloat("_BeatPhase", .25f);
            var afterSixteenth = Frame(.125f, held: 1); // 120 BPM, one sixteenth
            Assert.That(Peak(afterSixteenth) - Peak(start), Is.InRange(7, 9),
                "Travel should advance one quarter of the 32-pixel spacing per sixteenth.");
            _material.SetFloat("_BeatPhase", 0);
            var afterQuarter = Frame(.5f, held: 1);
            Assert.That(afterQuarter.Select((pixel, i) => Mathf.Abs(pixel.r - start[i].r)).Max(), Is.LessThan(.001f));
        }

        [TestCase(.5f, 64)]
        [TestCase(.25f, 32)]
        [TestCase(.75f, 96)]
        public void HeldAnimationMirrorsAcrossTheHiHatInsteadOfWiggling(float center, int axis)
        {
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            _material.SetVector("_HeadRange", new Vector4(center - .04f, center + .04f, 1, 0));
            SetIfPresent("_WaveStrength", .4f);
            for (int frame = 0; frame < 9; frame++)
            {
                var pixels = Frame(frame * .073f, held: 1);
                float error = 0;
                for (int y = 8; y < 504; y++)
                for (int d = 0; d < Math.Min(axis, 128 - axis) - 10; d++)
                    error = Mathf.Max(error, Mathf.Abs(pixels[y * 128 + axis - 1 - d].r - pixels[y * 128 + axis + d].r));
                Assert.That(error, Is.LessThan(.003f), "Width motion, arcs, and glints must share a fixed symmetry axis.");
            }
        }

        [Test]
        public void DarkWavefrontsAreCurvedSpacedAtSixteenthsAndTravelOutward()
        {
            Assert.That(_material.HasProperty("_WaveStrength"), Is.True, "The sustain needs a distinct dark sound-wave layer.");
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PatternStrength", 0); SetIfPresent("_PulseStrength", 0);
            var baseline = Frame(0, held: 1);
            _material.SetFloat("_WaveStrength", .4f);
            _material.SetFloat("_BeatPhase", 0);
            var start = Frame(0, held: 1);
            float Dark(Color[] pixels, int x, int y) => 1 - pixels[y * 128 + x].r / baseline[y * 128 + x].r;
            var bandStarts = new System.Collections.Generic.List<int>();
            bool previous = false;
            for (int y = 8; y < 504; y++)
            {
                bool dark = Dark(start, 63, y) > .15f;
                if (dark && !previous) bandStarts.Add(y);
                previous = dark;
            }
            Assert.That(bandStarts.Count, Is.EqualTo(15), "The crop excludes the two partial endpoint bands.");
            for (int i = 1; i < bandStarts.Count; i++)
                Assert.That(bandStarts[i] - bandStarts[i - 1], Is.EqualTo(32),
                    "Four quarter notes across 512 pixels require sixteen equally spaced cycles.");
            int Peak(Color[] pixels, int x) => Enumerable.Range(80, 32).OrderByDescending(y => Dark(pixels, x, y)).First();
            int center = Peak(start, 63), side = Peak(start, 24);
            Assert.That(center - side, Is.InRange(5, 15), "The arc's apex must extend farther from the cymbal than its sides.");
            var moved = Frame(.125f, held: 1); // one quarter of the wave cycle
            Assert.That(Peak(moved, 63) - center, Is.InRange(6, 10), "Increasing the slow song clock must move arcs away from the base.");
            var repeated = Frame(.5f, held: 1);
            Assert.That(repeated.Select((pixel, j) => Mathf.Abs(pixel.r - start[j].r)).Max(), Is.LessThan(.001f));
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
        public void MaterialUsesProceduralShimmerWithoutTheCymbalWashTexture()
        {
            Assert.That(_material.HasProperty("_PatternStrength"), Is.True);
            Assert.That(_material.HasProperty("_NoiseTex"), Is.False);
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
        public void ShimmerAddsSparseShortGlintsThroughoutTheFieldAndReconstructsIdenticalFrames()
        {
            SetIfPresent("_MotionStrength", 0); SetIfPresent("_PulseStrength", 0);
            _material.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetIfPresent("_PatternStrength", 0);
            var baseline = Frame(0, held: 1);
            SetIfPresent("_PatternStrength", 1);
            var first = Frame(.24f, held: 1);
            Assert.That(first.Select((pixel, j) => pixel.r - baseline[j].r).Max(), Is.GreaterThan(.02f),
                "Shimmer must create small bright glints rather than a broad dark wash.");
            var repeat = Frame(.24f, held: 1);
            Assert.That(repeat.Select((pixel, j) => Mathf.Abs(pixel.r - first[j].r)).Max(), Is.LessThan(.0001f));
            int[] runs = new int[first.Length];
            bool[] regions = new bool[16];
            int longest = 0, lit = 0, samples = 0;
            float minMean = float.MaxValue, maxMean = 0;
            for (int frame = 0; frame < 25; frame++)
            {
                var pixels = Frame(frame * .02f, held: 1);
                float sum = 0;
                for (int y = 32; y < 480; y++)
                for (int x = 8; x < 120; x++)
                {
                    int j = y * 128 + x;
                    float delta = Mathf.Max(0, pixels[j].r - baseline[j].r);
                    bool bright = delta > .02f;
                    runs[j] = bright ? runs[j] + 1 : 0;
                    longest = Math.Max(longest, runs[j]);
                    if (bright) { lit++; regions[Math.Min(3, x / 32) + Math.Min(3, y / 128) * 4] = true; }
                    sum += delta; samples++;
                }
                float mean = sum / (448 * 112);
                minMean = Math.Min(minMean, mean); maxMean = Math.Max(maxMean, mean);
            }
            TestContext.WriteLine($"Shimmer lit fraction={(float)lit / samples}, longest={longest * .02f}s, mean swing={maxMean - minMean}");
            Assert.That((float)lit / samples, Is.InRange(.0005f, .12f), "The bright changes must remain fine and sparse.");
            Assert.That(regions.Count(value => value), Is.GreaterThanOrEqualTo(12), "Shimmer must cover the whole field.");
            Assert.That(longest, Is.LessThanOrEqualTo(7), "Individual glints must die within about140ms.");
            Assert.That(maxMean - minMean, Is.LessThan(.015f), "Independent glints must not flash the entire field together.");
            Assert.That(Frame(.12f, 0, 1).Max(pixel => pixel.r), Is.LessThan(.0001f));
            CaptureMotion();
        }

        [Test]
        public void MissedTailHasNoShimmerAndNoLargeTravelingWash()
        {
            _mesh.colors = Enumerable.Repeat(new Color(1, 1, 1, .12f), 4).ToArray();
            SetIfPresent("_PulseStrength", 0); SetIfPresent("_PatternStrength", 0);
            var baseline = Frame(0);
            SetIfPresent("_PatternStrength", 1);
            var later = Frame(.24f);
            Assert.That(later.Select((pixel, j) => Mathf.Abs(pixel.r - baseline[j].r)).Max(), Is.LessThan(.0001f));
        }

        private void CaptureMotion()
        {
            string directory = Environment.GetEnvironmentVariable("YARG_HIHAT_MOTION_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            System.IO.Directory.CreateDirectory(directory);
            var color = YARG.Core.Game.ColorProfile.Default.EliteDrums.HatOpenNote;
            _mesh.colors = Enumerable.Repeat(new Color(color.R / 255f, color.G / 255f, color.B / 255f, .85f), 4).ToArray();
            SetIfPresent("_MotionStrength", 1); SetIfPresent("_PatternStrength", .75f); SetIfPresent("_PulseStrength", .08f);
            SetIfPresent("_WaveStrength", .4f);
            var png = new Texture2D(128, 512, TextureFormat.RGB24, false);
            try
            {
                for (int i = 0; i < 180; i++)
                {
                    float time = i / 60f;
                    SetIfPresent("_BeatPhase", time * 2 % 1);
                    var values = Frame(time, remaining: 3, held: 1, ramp: true, frontAtMeshStart: true, widthProgress: Mathf.SmoothStep(0, 1, time * 8));
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
