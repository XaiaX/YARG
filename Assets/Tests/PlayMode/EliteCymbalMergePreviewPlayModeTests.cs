// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteCymbalMergePreviewPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const int PREVIOUS_SAMPLE_COUNT = 17 * 3 + 16 + 4;
        private static readonly string[][] PAIRS =
        {
            new[] { "left-crash", "ride" },
            new[] { "left-crash", "right-crash" },
            new[] { "ride", "right-crash" }
        };
        internal static readonly (string Primary, string Secondary, string Destination, int Sample)[] OrderedPairs =
        {
            ("left-crash", "right-crash", "ride", 1),
            ("left-crash", "ride", "right-crash", 0),
            ("ride", "left-crash", "hi-hat", 0),
            ("ride", "right-crash", "left-crash", 2),
            ("right-crash", "left-crash", "hi-hat", 1),
            ("right-crash", "ride", "left-crash", 2)
        };
        private readonly ElitePreviewGeometryPlayModeTests _preview = new();

        private static Type Production(string name) => PreparedElitePlayerFixture.ProductionType(name);
        private static object Get(object target, string name) => target.GetType().GetProperty(name, INSTANCE)?.GetValue(target)
            ?? target.GetType().GetField(name, INSTANCE).GetValue(target);
        private static object Call(object target, string name, params object[] args) => target.GetType()
            .GetMethods(INSTANCE).Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(target, args);
        private static object Data(Component note) => Get(note, "NoteRef");
        private static object Descriptor(Component note) => Data(note).GetType().GetField("EliteDescriptor").GetValue(Data(note));
        private static EliteDrumsColorRole Role(Component note) => (EliteDrumsColorRole)Get(Descriptor(note), "Role");

        [UnitySetUp]
        public IEnumerator SetUp() => _preview.SetUpProductionTheme();

        [UnityTearDown]
        public IEnumerator TearDown() => _preview.TearDown();

        [UnityTest]
        public IEnumerator OrdinaryCycleAppendsAllThreeCymbalPairsWithoutRemovingExistingSamples()
        {
            var (player, pool) = _preview.CreatePreparedPlayer(EliteDrumsLayoutPreset.Full);
            var generator = Get(Get(player, "CurrentGameModeInfo"), "Generator");
            // Info uses fields; retain the production generator without substituting a fake.
            Assert.That(generator, Is.Not.Null);
            var pairs = new HashSet<string>();
            var strengths = new HashSet<string>();
            bool handFlam = false, pedalChord = false;
            for (int index = 0; index < PREVIOUS_SAMPLE_COUNT + 3; index++)
            {
                Call(pool, "ReturnAllObjects");
                Call(player, "Advance", .21d);
                var notes = ((IList)Get(pool, "AllSpawned")).Cast<Component>().ToArray();
                var roles = notes.Select(Role).ToArray();
                foreach (var note in notes)
                {
                    strengths.Add(Get(Descriptor(note), "ModelType").ToString());
                    handFlam |= (bool)Data(note).GetType().GetField("EliteHandFlam").GetValue(Data(note));
                }
                pedalChord |= roles.Contains(EliteDrumsColorRole.Kick) && roles.Contains(EliteDrumsColorRole.Stomp);
                if (roles.Length == 2 && roles.All(role => role is EliteDrumsColorRole.LeftCrash
                    or EliteDrumsColorRole.Ride or EliteDrumsColorRole.RightCrash))
                    pairs.Add(string.Join("+", roles.OrderBy(role => role)));
            }
            Assert.That(pairs, Is.EquivalentTo(new[]
            {
                "LeftCrash+Ride", "LeftCrash+RightCrash", "Ride+RightCrash"
            }), "The ordinary Elite cycle must append the three authored cymbal-pair chords.");
            Assert.That(handFlam, Is.True);
            Assert.That(pedalChord, Is.True);
            Assert.That(strengths, Does.Contain("Ghost").And.Contain("Accent").And.Contain("OpenHiHat").And.Contain("ClosedHiHat"));
            Call(pool, "ReturnAllObjects");
            Call(player, "Advance", .21d);
            Assert.That(((IList)Get(pool, "AllSpawned")).Cast<Component>().Select(Role),
                Is.EqualTo(new[] { EliteDrumsColorRole.Snare }),
                "Ordered merge choices must not duplicate the three unordered chord samples in the 74-sample cycle.");
            yield return null;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator OrderedPairsKeepPrimaryGeometryAndUseSelectedColorsForBothSinglesAndChords()
        {
            var modelType = Production("YARG.Themes.ThemeNoteType");
            var describe = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator").GetMethod("Describe");
            foreach (var pair in OrderedPairs)
            foreach (bool lefty in new[] { false, true })
            {
                var (player, pool) = _preview.CreatePreparedPlayer(OrderedLayout(pair.Primary, pair.Secondary));
                player.GetType().GetProperty("LeftyFlip").SetValue(player, lefty);
                var colors = Palette();
                Call(player, "RefreshColors", colors);
                var info = Get(player, "CurrentGameModeInfo");
                var generator = Get(info, "Generator");
                var resolved = (EliteDrumsResolvedLayout)Get(info, "EliteLayout");
                Component[] previous = null;
                foreach (int sample in new[] { -1, -2, pair.Sample })
                {
                    Call(pool, "ReturnAllObjects");
                    if (sample < 0)
                        Call(player, "SpotlightEliteRole", SourceRole(sample == -1 ? pair.Primary : pair.Secondary), false, null);
                    else
                    {
                        Call(player, "ClearSpotlights");
                        generator.GetType().GetField("_index", INSTANCE).SetValue(generator, PREVIOUS_SAMPLE_COUNT + sample);
                    }
                    Call(player, "Advance", .21d);
                    var notes = ((IList)Get(pool, "AllSpawned")).Cast<Component>().ToArray();
                    Assert.That(notes, Has.Length.EqualTo(sample < 0 ? 1 : 2));
                    if (previous != null)
                        Assert.That(notes.Any(note => previous.Contains(note)), Is.True,
                            "Primary, secondary and expanded notes must survive reuse of the actual preview pool.");
                    previous = notes;
                    foreach (var note in notes)
                    {
                        var playable = (YARG.Core.Chart.EliteDrumNote)Get(Data(note), "ElitePlayableNote");
                        bool expanded = sample >= 0 && playable.OriginalPad == (int)SourcePad(pair.Secondary);
                        string target = expanded ? pair.Destination : pair.Primary;
                        var targetRole = SourceRole(target);
                        var originalRole = SourceRole(playable.OriginalPad == (int)SourcePad(pair.Primary)
                            ? pair.Primary : pair.Secondary);
                        Assert.That(Role(note), Is.EqualTo(targetRole));
                        Assert.That(playable.IsCymbalExpanded, Is.EqualTo(expanded));
                        float x = resolved.GetSourceX(target, 2f, lefty);
                        Assert.That(note.transform.localPosition.x, Is.EqualTo(x).Within(.001f));
                        foreach (bool models in new[] { false, true, false })
                        foreach (string strength in new[] { "Normal", "Accent", "Ghost" })
                        foreach (bool star in new[] { false, true, false })
                        foreach (bool original in new[] { false, true, false })
                        {
                            player.GetType().GetProperty("UseCymbalModels").SetValue(player, models);
                            player.GetType().GetProperty("KeepOriginalCymbalColors").SetValue(player, original);
                            var descriptor = describe.Invoke(null, new[] { (object)targetRole, Enum.Parse(modelType, strength) });
                            Data(note).GetType().GetField("EliteDescriptor").SetValue(Data(note), descriptor);
                            Data(note).GetType().GetField("NoteType").SetValue(Data(note), Get(descriptor, "ModelType"));
                            Data(note).GetType().GetField("ForceStarPower").SetValue(Data(note), (bool?)star);
                            Call(note, "OnSettingChanged");
                            var theme = ActiveGroup(note).GetComponentInChildren(Production("YARG.Themes.ThemeNote"));
                            string expectedModel = models ? strength == "Normal" ? "Cymbal" : "Cymbal" + strength : strength;
                            Assert.That(Get(theme, "NoteType").ToString(), Is.EqualTo(expectedModel));
                            var role = original ? originalRole : targetRole;
                            AssertBodyColor(theme, star ? colors.EliteDrums.GetNoteStarPowerColor(role) : colors.EliteDrums.GetNoteColor(role));
                            Assert.That(note.transform.localPosition.x, Is.EqualTo(x).Within(.001f));
                            Assert.That(Role(note), Is.EqualTo(targetRole));
                        }
                    }
                }
            }
            yield return null;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator MergedPreviewRendersDestinationModelsAndChangesOnlyColorAcrossPoolReuse()
        {
            var preference = Production("YARG.Settings.Preview.FakeTrackPlayer").GetProperty("KeepOriginalCymbalColors");
            Assert.That(preference, Is.Not.Null, "Each preview needs an explicit local cymbal color preference.");
            foreach (int pairIndex in new[] { 0, 1, 2 })
            foreach (bool lefty in new[] { false, true })
            {
                var layout = MergedLayout(pairIndex);
                var (player, pool) = _preview.CreatePreparedPlayer(layout);
                player.GetType().GetProperty("LeftyFlip").SetValue(player, lefty);
                var colors = Palette();
                Call(player, "RefreshColors", colors);
                var info = Get(player, "CurrentGameModeInfo");
                var generator = info.GetType().GetField("Generator").GetValue(info);
                generator.GetType().GetField("_index", INSTANCE).SetValue(generator, PREVIOUS_SAMPLE_COUNT + pairIndex);
                Call(player, "Advance", .21d);
                var notes = ((IList)Get(pool, "AllSpawned")).Cast<Component>().ToArray();
                Assert.That(notes, Has.Length.EqualTo(2), "An expanded pair retains both scoring obligations.");
                var destination = pairIndex == 2 ? EliteDrumsColorRole.LeftCrash : EliteDrumsColorRole.HatIndifferent;
                var keeper = pairIndex == 0 ? EliteDrumsColorRole.Ride : EliteDrumsColorRole.RightCrash;
                Assert.That(notes.Select(Role), Is.EquivalentTo(new[] { destination, keeper }));
                var moved = notes.Single(note => Role(note) == destination);
                var original = pairIndex == 2 ? EliteDrumsColorRole.Ride : EliteDrumsColorRole.LeftCrash;
                var resolved = (EliteDrumsResolvedLayout)info.GetType().GetField("EliteLayout").GetValue(info);
                string targetSource = pairIndex == 2 ? "left-crash" : "hi-hat";
                float x = resolved.GetSourceX(targetSource, 2f, lefty);
                Assert.That(moved.transform.localPosition.x, Is.EqualTo(x).Within(.001f));
                foreach (bool starPower in new[] { false, true, false })
                foreach (bool keepOriginal in new[] { false, true, false })
                {
                    preference.SetValue(player, keepOriginal);
                    Data(moved).GetType().GetField("ForceStarPower").SetValue(Data(moved), (bool?)starPower);
                    Call(moved, "OnSettingChanged");
                    Assert.That(moved.transform.localPosition.x, Is.EqualTo(x).Within(.001f));
                    Assert.That(Role(moved), Is.EqualTo(destination), "The option must never change the destination identity.");
                    var group = ActiveGroup(moved);
                    var theme = group.GetComponentInChildren(Production("YARG.Themes.ThemeNote"));
                    Assert.That(Get(theme, "NoteType").ToString(), Is.EqualTo("Cymbal"),
                        "Indifferent hat and crash destinations use the destination's ordinary cymbal model.");
                    var colorRole = keepOriginal ? original : destination;
                    AssertBodyColor(theme, starPower ? colors.EliteDrums.GetNoteStarPowerColor(colorRole)
                        : colors.EliteDrums.GetNoteColor(colorRole));
                    if (!starPower && !lefty)
                        CaptureModels(notes, $"preview-pair{pairIndex}-original{keepOriginal}");
                }
                Call(pool, "ReturnAllObjects");
                Call(player, "SpotlightEliteRole", original, false, null);
                Call(player, "Advance", .21d);
                var single = (Component)((IList)Get(pool, "AllSpawned"))[0];
                Assert.That(notes, Does.Contain(single), "The single must reuse one of the expanded pair's pooled notes.");
                AssertBodyColor(ActiveGroup(single).GetComponentInChildren(Production("YARG.Themes.ThemeNote")),
                    colors.EliteDrums.GetNoteColor(keeper));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator OnlyTheSelectedSampleExpandsAndIncompleteDraftFiltersMissingDestinations()
        {
            foreach (int selected in new[] { 0, 1, 2 })
            {
                var layout = MergedLayout(selected);
                var (player, pool) = _preview.CreatePreparedPlayer(layout);
                var generator = Get(Get(player, "CurrentGameModeInfo"), "Generator");
                foreach (int sample in new[] { 0, 1, 2 })
                {
                    Call(pool, "ReturnAllObjects");
                    generator.GetType().GetField("_index", INSTANCE).SetValue(generator, PREVIOUS_SAMPLE_COUNT + sample);
                    Call(player, "Advance", .21d);
                    var notes = ((IList)Get(pool, "AllSpawned")).Cast<Component>().ToArray();
                    Assert.That(notes, Has.Length.EqualTo(2));
                    int expanded = notes.Count(note => (bool)Get(Data(note).GetType().GetField("ElitePlayableNote").GetValue(Data(note)), "IsCymbalExpanded"));
                    Assert.That(expanded, Is.EqualTo(sample == selected ? 1 : 0));
                }
                string missing = selected == 2 ? "left-crash" : "hi-hat";
                var incomplete = layout.Copy();
                for (int index = 0; index < incomplete.Slots.Length; index++)
                    if (incomplete.Slots[index].Cymbal == missing) incomplete.Slots[index] = new EliteDrumsLayoutSlot();
                var partial = _preview.CreatePreparedPlayer(incomplete, GameMode.EliteDrums, true);
                var partialGenerator = Get(Get(partial.player, "CurrentGameModeInfo"), "Generator");
                partialGenerator.GetType().GetField("_index", INSTANCE).SetValue(partialGenerator, PREVIOUS_SAMPLE_COUNT + selected);
                Call(partial.player, "Advance", .21d);
                Assert.That(((IList)Get(partial.pool, "AllSpawned")).Count, Is.EqualTo(1),
                    "A partial draft omits the temporarily unassigned destination after shared projection.");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator CymbalModelPreferenceSelectsDestinationDynamicsForAllPairs()
        {
            var modelType = Production("YARG.Themes.ThemeNoteType");
            var describe = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator").GetMethod("Describe");
            foreach (int pair in new[] { 0, 1, 2 })
            {
                var (player, pool) = _preview.CreatePreparedPlayer(MergedLayout(pair));
                var generator = Get(Get(player, "CurrentGameModeInfo"), "Generator");
                generator.GetType().GetField("_index", INSTANCE).SetValue(generator, PREVIOUS_SAMPLE_COUNT + pair);
                Call(player, "Advance", .21d);
                var destination = pair == 2 ? EliteDrumsColorRole.LeftCrash : EliteDrumsColorRole.HatIndifferent;
                var moved = ((IList)Get(pool, "AllSpawned")).Cast<Component>().Single(note => Role(note) == destination);
                foreach (bool models in new[] { false, true, false })
                foreach (string strength in new[] { "Normal", "Ghost", "Accent" })
                foreach (bool star in new[] { false, true })
                {
                    player.GetType().GetProperty("UseCymbalModels").SetValue(player, models);
                    var descriptor = describe.Invoke(null, new[] { (object)destination, Enum.Parse(modelType, strength) });
                    Data(moved).GetType().GetField("EliteDescriptor").SetValue(Data(moved), descriptor);
                    Data(moved).GetType().GetField("NoteType").SetValue(Data(moved), Get(descriptor, "ModelType"));
                    Data(moved).GetType().GetField("ForceStarPower").SetValue(Data(moved), (bool?)star);
                    Call(moved, "OnSettingChanged");
                    var theme = ActiveGroup(moved).GetComponentInChildren(Production("YARG.Themes.ThemeNote"));
                    string expected = models ? strength == "Normal" ? "Cymbal" : "Cymbal" + strength : strength;
                    Assert.That(Get(theme, "NoteType").ToString(), Is.EqualTo(expected));
                }
            }
            yield return null;
        }

        [Test]
        public void BuilderDefaultsToDestinationColorsAndForwardsAnExplicitPreference()
        {
            var type = Production("YARG.Settings.Metadata.TrackPreviewBuilder");
            var builder = Activator.CreateInstance(type, new object[] { false, false, false, null });
            var preference = type.GetProperty("KeepOriginalCymbalColors");
            Assert.That(preference, Is.Not.Null, "The profile layout editor needs a display-preference seam.");
            Assert.That(preference.GetValue(builder), Is.False, "Generic previews default to destination colors.");
            var (player, _) = _preview.CreatePreparedPlayer(EliteDrumsLayoutPreset.Full);
            type.GetField("_currentTrackPreview", INSTANCE).SetValue(builder, player);
            preference.SetValue(builder, true);
            Assert.That(Get(player, "KeepOriginalCymbalColors"), Is.True);
        }

        internal static Component ActiveGroup(Component note) => note.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
            .Single(group => group.gameObject.activeInHierarchy);

        internal static void AssertBodyColor(Component theme, System.Drawing.Color expected)
        {
            var entries = ((IEnumerable)Get(theme, "ColoredMaterials")).Cast<object>().ToArray();
            Assert.That(entries, Is.Not.Empty, "The actual selected theme must expose colored materials.");
            foreach (var entry in entries)
            {
                var renderer = (Renderer)entry.GetType().GetField("Mesh").GetValue(entry);
                int index = (int)entry.GetType().GetField("MaterialIndex").GetValue(entry);
                float addition = (float)entry.GetType().GetField("EmissionAddition").GetValue(entry);
                var material = renderer.sharedMaterials[index];
                var actual = material.color;
                // Unity's shader color round-trip can clamp negative channels. Use
                // a separate same-shader material as the reference for authored darkening.
                var probe = new Material(material);
                Color reference;
                try
                {
                    probe.color = new Color(expected.R / 255f + addition,
                        expected.G / 255f + addition, expected.B / 255f + addition, expected.A / 255f);
                    reference = probe.color;
                    if (expected.R / 255f + addition < 0 || expected.G / 255f + addition < 0 ||
                        expected.B / 255f + addition < 0)
                        Debug.Log($"CYMBAL COLOR shader round-trip model={theme.name} expected={expected} " +
                            $"addition={addition} reference={reference} actual={actual}");
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
                Assert.That(actual.r, Is.EqualTo(reference.r).Within(.001f));
                Assert.That(actual.g, Is.EqualTo(reference.g).Within(.001f));
                Assert.That(actual.b, Is.EqualTo(reference.b).Within(.001f));
            }
        }

        internal static ColorProfile Palette()
        {
            var colors = new ColorProfile("Cymbal projection preview");
            colors.EliteDrums.LeftCrashNote = System.Drawing.Color.Red;
            colors.EliteDrums.RideNote = System.Drawing.Color.Blue;
            colors.EliteDrums.RightCrashNote = System.Drawing.Color.Green;
            colors.EliteDrums.HatIndifferentNote = System.Drawing.Color.Magenta;
            colors.EliteDrums.LeftCrashStarpower = System.Drawing.Color.Yellow;
            colors.EliteDrums.RideStarpower = System.Drawing.Color.Cyan;
            colors.EliteDrums.RightCrashStarpower = System.Drawing.Color.White;
            colors.EliteDrums.HatIndifferentStarpower = System.Drawing.Color.Black;
            return colors;
        }

        internal static void CaptureModels(IEnumerable<Component> notes, string scenario)
        {
            string directory = Environment.GetEnvironmentVariable("YARG_CYMBAL_MERGE_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            var renderers = notes.SelectMany(note => note.GetComponentsInChildren<Renderer>())
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            Assert.That(renderers, Is.Not.Empty);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var root = new GameObject("Cymbal merge appearance capture camera");
            var camera = root.AddComponent<Camera>();
            camera.enabled = false;
            var render = new RenderTexture(960, 540, 24);
            var pixels = new Texture2D(render.width, render.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var layers = renderers.Select(renderer => renderer.gameObject).Distinct().ToDictionary(item => item, item => item.layer);
            try
            {
                foreach (var item in layers.Keys) item.layer = 31;
                camera.cullingMask = 1 << 31;
                camera.transform.position = bounds.center + Vector3.up * 5f;
                camera.transform.LookAt(bounds.center, Vector3.forward);
                camera.orthographic = true;
                camera.aspect = 16f / 9f;
                camera.orthographicSize = Mathf.Max(.4f, bounds.extents.x / camera.aspect + .15f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.055f, .065f, .085f);
                camera.targetTexture = render;
                camera.Render();
                RenderTexture.active = render;
                pixels.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0);
                pixels.Apply();
                Assert.That(pixels.GetPixels32().Any(pixel => pixel.r > 40 || pixel.g > 40 || pixel.b > 40), Is.True,
                    "Capture must contain the active themed models.");
                System.IO.Directory.CreateDirectory(directory);
                string path = System.IO.Path.Combine(directory, $"{scenario}.png");
                System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
                Debug.Log($"Cymbal merge appearance capture: {path}");
            }
            finally
            {
                foreach (var item in layers) if (item.Key != null) item.Key.layer = item.Value;
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(pixels);
                render.Release();
                UnityEngine.Object.DestroyImmediate(render);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        internal static EliteDrumsLayoutPreset MergedLayout(int pairIndex)
        {
            var sources = PAIRS[pairIndex];
            string id = EliteDrumsLayoutSources.PairId(sources[0], sources[1]);
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Cymbal pair preview");
            layout.SchemaVersion = 1;
            foreach (var slot in layout.Slots)
            {
                if (slot.Cymbal == sources[0]) { slot.Cymbal = null; slot.Kind = EliteDrumsLayoutSlotKind.Empty; }
                else if (slot.Cymbal == sources[1]) slot.Cymbal = id;
            }
            layout.Merges = new[] { new EliteDrumsLayoutMerge { EffectiveId = id, Sources = sources } };
            return layout;
        }

        internal static EliteDrumsLayoutPreset OrderedLayout(string primary, string secondary)
        {
            string id = EliteDrumsLayoutSources.PairId(primary, secondary);
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Ordered cymbal pair preview");
            layout.SchemaVersion = 2;
            foreach (var slot in layout.Slots)
            {
                if (slot.Cymbal == secondary) { slot.Cymbal = null; slot.Kind = EliteDrumsLayoutSlotKind.Empty; }
                else if (slot.Cymbal == primary) slot.Cymbal = id;
            }
            var merge = new EliteDrumsLayoutMerge
            {
                EffectiveId = id,
                Sources = new[] { primary, secondary }.OrderBy(source => source, StringComparer.Ordinal).ToArray()
            };
            var orientation = typeof(EliteDrumsLayoutMerge).GetField("PrimarySource");
            Assert.That(orientation, Is.Not.Null, "Native layouts must persist the chosen primary source.");
            orientation.SetValue(merge, primary);
            layout.Merges = new[] { merge };
            return layout;
        }

        internal static EliteDrumPad SourcePad(string source) => source switch
        {
            "left-crash" => EliteDrumPad.LeftCrash,
            "ride" => EliteDrumPad.Ride,
            "right-crash" => EliteDrumPad.RightCrash,
            "hi-hat" => EliteDrumPad.HiHat,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

        internal static EliteDrumsColorRole SourceRole(string source) => source switch
        {
            "left-crash" => EliteDrumsColorRole.LeftCrash,
            "ride" => EliteDrumsColorRole.Ride,
            "right-crash" => EliteDrumsColorRole.RightCrash,
            "hi-hat" => EliteDrumsColorRole.HatIndifferent,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }
}
