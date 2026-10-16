using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class ElitePreviewEditModeTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).First(t => t != null);

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void ElitePreviewPairGeometry_MatchesGameplayForLeftyOffsetAndPairing(bool lefty,
            bool offset, bool paired)
        {
            var fakeNote = Production("YARG.Settings.Preview.FakeNote");
            var layout = EliteDrumsLayoutResolver.Resolve(EliteDrumsLayoutPreset.Compact);
            var getRegion = fakeNote.GetMethod("GetEliteLayoutRegion", BindingFlags.Static | BindingFlags.NonPublic);
            float width = 2f / 5f;
            float laneCenter = layout.GetLaneX(layout.SourceToLane["tom1"], 2f, lefty);
            foreach (bool cymbal in new[] { false, true })
            {
                var args = new object[] { layout, "tom1", cymbal, lefty, offset, paired, 0f, 0f };
                getRegion.Invoke(null, args);
                float expectedCenter = laneCenter;
                float expectedWidth = width;
                if (offset && paired)
                {
                    float mirror = lefty ? -1f : 1f;
                    expectedWidth = width / 2f;
                    expectedCenter = laneCenter + (cymbal ? 1f : -1f) * mirror * expectedWidth / 2f;
                }
                Assert.That((float) args[6], Is.EqualTo(expectedCenter).Within(0.0001f),
                    $"{(cymbal ? "Cymbal" : "Drum")} center; lefty={lefty}, offset={offset}, paired={paired}");
                Assert.That((float) args[7], Is.EqualTo(expectedWidth).Within(0.0001f),
                    $"{(cymbal ? "Cymbal" : "Drum")} width; lefty={lefty}, offset={offset}, paired={paired}");
            }
        }

        [Test]
        public void RefreshEliteOffsetPairFlags_ClearsStaleFlagsWhenDisabledAndMarksMatchingPairsWhenEnabled()
        {
            var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
            var refresh = playerType.GetMethod("RefreshEliteOffsetPairFlags",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(refresh, Is.Not.Null, "The preview refresh must expose its flag-refresh seam to this test.");

            var generatorType = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
            var noteDataType = Production("YARG.Settings.Preview.FakeNoteData");
            var noteListType = typeof(List<>).MakeGenericType(noteDataType);
            var layout = EliteDrumsLayoutResolver.Resolve(EliteDrumsLayoutPreset.Compact);
            Assert.That(layout.SourceToLane["tom1"], Is.EqualTo(layout.SourceToLane["left-crash"]),
                "The fixture should include a layout lane shared by a tom and cymbal.");
            var describe = generatorType.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static);
            var createRoleNote = generatorType.GetMethod("CreateRoleNote", BindingFlags.Public | BindingFlags.Static);
            var roleType = Production("YARG.Core.Game.EliteDrumsColorRole");
            object Descriptor(string role) => describe.Invoke(null,
                new[] { Enum.Parse(roleType, role), Enum.Parse(Production("YARG.Themes.ThemeNoteType"), "Normal") });
            object CreateNote(object descriptor) => createRoleNote.Invoke(null, new[] { (object)1d, descriptor });
            var tomAndCymbal = (IList)Activator.CreateInstance(noteListType);
            var tom = CreateNote(Descriptor("Tom1"));
            var crash = CreateNote(Descriptor("LeftCrash"));
            var otherTom = CreateNote(Descriptor("Tom2"));
            tomAndCymbal.Add(tom);
            tomAndCymbal.Add(crash);
            tomAndCymbal.Add(otherTom);

            bool IsPair(object note) => (bool)noteDataType.GetField("OffsetElitePair").GetValue(note);
            void SetPair(object note, bool value) => noteDataType.GetField("OffsetElitePair").SetValue(note, value);
            foreach (var note in tomAndCymbal) SetPair(note, true);

            refresh.Invoke(null, new object[] { tomAndCymbal, false, layout });

            Assert.That(tomAndCymbal.Cast<object>().Select(IsPair), Is.All.False,
                "Turning off paired gem offsets must clear stale flags on every existing note.");

            foreach (var note in tomAndCymbal) SetPair(note, true);
            refresh.Invoke(null, new object[] { tomAndCymbal, true, null });
            Assert.That(tomAndCymbal.Cast<object>().Select(IsPair), Is.All.False,
                "A missing resolved layout must still clear stale flags without marking new pairs.");

            refresh.Invoke(null, new object[] { tomAndCymbal, true, layout });

            Assert.That(IsPair(tom), Is.True,
                "A tom sharing a lane with a simultaneous cymbal should be marked when enabled.");
            Assert.That(IsPair(crash), Is.True,
                "The corresponding cymbal should also be marked.");
            Assert.That(IsPair(otherTom), Is.False,
                "A tom without a simultaneous cymbal counterpart must remain unmarked.");
        }

        [Test]
        public void EliteGhostNotes_DoNotUseClassicDrumStripEmission()
        {
            var fakeNote = Production("YARG.Settings.Preview.FakeNote");
            var method = fakeNote.GetMethod("GetStripEmission", System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic);
            var modeType = Production("YARG.Core.GameMode");
            var noteType = Production("YARG.Themes.ThemeNoteType");
            var profile = new ColorProfile("preview ghost emission regression");
            profile.FourLaneDrums.GhostStripEmission = 25f;
            profile.FiveLaneDrums.GhostStripEmission = 75f;

            float Emission(string mode, string note) => (float)method.Invoke(null, new[]
            {
                Enum.Parse(modeType, mode), Enum.Parse(noteType, note), profile,
            });

            Assert.That(Emission("EliteDrums", "Ghost"), Is.EqualTo(-1f));
            Assert.That(Emission("EliteDrums", "CymbalGhost"), Is.EqualTo(-1f));
            Assert.That(Emission("FourLaneDrums", "Ghost"), Is.EqualTo(0.25f));
            Assert.That(Emission("FiveLaneDrums", "Ghost"), Is.EqualTo(0.75f));
        }

        [Test]
        public void SyntheticGenerator_CoversEveryRoleAndDynamicsAcrossTwoRepeatingCycles()
        {
            var type = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
            var generator = Activator.CreateInstance(type);
            var create = type.GetMethod("CreateNote");
            var roles = new HashSet<EliteDrumsColorRole>();
            var models = new HashSet<string>();
            var flamRoles = new HashSet<EliteDrumsColorRole>();
            var firstCycle = new List<EliteDrumsColorRole>();
            Assert.That(type.GetField("NOTE_COUNT").GetRawConstantValue(), Is.EqualTo(126));
            for (int i = 0; i < 126; i++)
            {
                var note = create.Invoke(generator, new object[] { i * 0.2d, null });
                Assert.That(note, Is.Not.Null);
                var descriptor = note.GetType().GetField("EliteDescriptor").GetValue(note);
                var role = (EliteDrumsColorRole)descriptor.GetType().GetProperty("Role").GetValue(descriptor);
                roles.Add(role);
                models.Add(note.GetType().GetField("NoteType").GetValue(note).ToString());
                if ((float) descriptor.GetType().GetProperty("Width").GetValue(descriptor) == 2f / 3f)
                    flamRoles.Add(role);
                if (i < 63) firstCycle.Add(role);
                else Assert.That(role, Is.EqualTo(firstCycle[i - 63]), $"Repeated sample {i}");
            }
            Assert.That(flamRoles, Is.SupersetOf(new[] { EliteDrumsColorRole.Snare,
                EliteDrumsColorRole.Tom1, EliteDrumsColorRole.Tom2, EliteDrumsColorRole.Tom3 }));
            var tomGenerator = Activator.CreateInstance(type);
            object tomFlam = null;
            for (int i = 0; i <= 57; i++) tomFlam = create.Invoke(tomGenerator, new object[] { 5d, null });
            var extras = ((System.Collections.IEnumerable)type.GetMethod("CreateChordNotes").Invoke(
                tomGenerator, new[] { (object)5d, tomFlam, null })).Cast<object>().ToArray();
            Assert.That(extras.Length, Is.EqualTo(1));
            var extraDescriptor = extras[0].GetType().GetField("EliteDescriptor").GetValue(extras[0]);
            Assert.That(extraDescriptor.GetType().GetProperty("Role").GetValue(extraDescriptor), Is.EqualTo(EliteDrumsColorRole.Tom2));
            Assert.That(roles.Count, Is.EqualTo(Enum.GetValues(typeof(EliteDrumsColorRole)).Length));
            Assert.That(models, Does.Contain("CymbalAccent"));
            Assert.That(models, Does.Contain("CymbalGhost"));
            Assert.That(models, Does.Contain("Accent"));
            Assert.That(models, Does.Contain("Ghost"));
            Assert.That(models, Does.Contain("Wildcard"));
            Assert.That(type.GetProperty("HasNext").GetValue(generator), Is.True);
            Assert.That(create.Invoke(generator, new object[] { 25d, null }), Is.Not.Null);
        }

        [Test]
        public void SyntheticDescriptor_SharedPositionsPreserveIndependentRolesAndSplitBars()
        {
            var type = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
            var modelType = Production("YARG.Themes.ThemeNoteType");
            var describe = type.GetMethod("Describe");
            object Describe(EliteDrumsColorRole role) => describe.Invoke(null,
                new[] { (object)role, Enum.Parse(modelType, "Normal") });
            object Get(object descriptor, string name) => descriptor.GetType().GetProperty(name).GetValue(descriptor);
            var crash = Describe(EliteDrumsColorRole.LeftCrash);
            var tom = Describe(EliteDrumsColorRole.Tom1);
            Assert.That(Get(crash, "Fret"), Is.EqualTo(Get(tom, "Fret")));
            Assert.That(Get(crash, "Role"), Is.Not.EqualTo(Get(tom, "Role")));
            Assert.That(Get(crash, "ModelType").ToString(), Is.EqualTo("Cymbal"));
            Assert.That(Get(tom, "ModelType").ToString(), Is.EqualTo("Normal"));
            var kick = Describe(EliteDrumsColorRole.Kick);
            var doubleKick = Describe(EliteDrumsColorRole.DoubleKick);
            Assert.That(Get(kick, "IsBar"), Is.True);
            Assert.That(Get(kick, "Width"), Is.EqualTo(1f));
            Assert.That(Get(doubleKick, "Width"), Is.EqualTo(0.5f));
            Assert.That(Get(doubleKick, "Offset"), Is.EqualTo(0.5f));
            var stomp = Describe(EliteDrumsColorRole.Stomp);
            var splash = Describe(EliteDrumsColorRole.Splash);
            Assert.That(Get(stomp, "Width"), Is.EqualTo(0.5f),
                "Dedicated-lane sizing remains unchanged until a resolved bar layout is available.");
            Assert.That(Get(splash, "Width"), Is.EqualTo(0.5f));
        }

        [Test]
        public void IncompleteElitePreview_FiltersMissingSourcesIncludingHiHat()
        {
            var draft = new EliteDrumsLayoutPreset("incomplete preview");
            draft.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", null);
            Assert.That(EliteDrumsLayoutResolver.TryPreviewResolve(draft, out var resolved), Is.True);

            var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
            var represented = playerType.GetMethod("IsEliteNoteRepresented", BindingFlags.Static | BindingFlags.NonPublic);
            var generatorType = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
            var describe = generatorType.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static);
            var createRoleNote = generatorType.GetMethod("CreateRoleNote", BindingFlags.Public | BindingFlags.Static);
            var generator = Activator.CreateInstance(generatorType);
            var createNote = generatorType.GetMethod("CreateNote");
            var createChordNotes = generatorType.GetMethod("CreateChordNotes");
            var roleType = typeof(EliteDrumsColorRole);
            bool IsRepresented(object sourceNote) => (bool) represented.Invoke(null, new[] { sourceNote, resolved });
            object RoleNote(string role) => createRoleNote.Invoke(null, new[] { (object)0d,
                describe.Invoke(null, new[] { Enum.Parse(roleType, role),
                    Enum.Parse(Production("YARG.Themes.ThemeNoteType"), "Normal") }) });

            object missingHatNote = null;
            for (int index = 0; index < 59; index++)
            {
                var generated = createNote.Invoke(generator, new object[] { index * 0.2d, null });
                var generatedDescriptor = generated.GetType().GetField("EliteDescriptor").GetValue(generated);
                var role = (EliteDrumsColorRole)generatedDescriptor.GetType().GetProperty("Role").GetValue(generatedDescriptor);
                var width = (float)generatedDescriptor.GetType().GetProperty("Width").GetValue(generatedDescriptor);
                if (role == EliteDrumsColorRole.HatIndifferent && width == 2f / 3f)
                {
                    missingHatNote = generated;
                    break;
                }
            }
            Assert.That(missingHatNote, Is.Not.Null,
                "The real deterministic generator emits a missing hi-hat flam that preview must filter.");
            Assert.That(IsRepresented(missingHatNote), Is.False);

            var descriptor = missingHatNote.GetType().GetField("EliteDescriptor").GetValue(missingHatNote);
            var chordExtras = ((IEnumerable)createChordNotes.Invoke(generator,
                new[] { (object)missingHatNote.GetType().GetField("Time").GetValue(missingHatNote), missingHatNote, null }))
                .Cast<object>().ToArray();
            Assert.That(chordExtras.Length, Is.GreaterThan(0),
                "A real hat flam should exercise production chord generation.");
            Assert.That(chordExtras.Any(IsRepresented), Is.False,
                "The generated hat chord companion must be filtered with its missing source.");
            Assert.That(descriptor, Is.Not.Null);

            Assert.That(IsRepresented(RoleNote("Stomp")), Is.False,
                "Stomp requires the represented hi-hat lane in an incomplete layout.");
            Assert.That(IsRepresented(RoleNote("Splash")), Is.False,
                "Splash requires the represented hi-hat lane in an incomplete layout.");

            var spotlightType = Production("YARG.Themes.ThemeNoteType");
            var typeSpotlight = generatorType.GetMethod("CreateTypeSpotlightNote");
            var spotlightNote = typeSpotlight.Invoke(generator, new[] { (object)0d,
                Enum.Parse(spotlightType, "Normal"), null });
            Assert.That(IsRepresented(spotlightNote), Is.True,
                "The real SP/type spotlight creates a snare and remains visible.");
            var roleSource = playerType.GetMethod("EliteSource", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(roleSource.Invoke(null, new object[] { Enum.Parse(roleType, "Stomp") }), Is.EqualTo("hi-hat"));
            Assert.That(roleSource.Invoke(null, new object[] { Enum.Parse(roleType, "Splash") }), Is.EqualTo("hi-hat"));
        }

        [Test]
        public void EliteFretColorProvider_UsesDoubleKickInputEffectForSavedKick2Slot()
        {
            var layoutPreset = new EliteDrumsLayoutPreset("kick slots");
            layoutPreset.Slots[0] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            layoutPreset.Slots[1] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            Assert.That(EliteDrumsLayoutResolver.TryPreviewResolve(layoutPreset, out var layout), Is.True);

            var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
            var createProvider = playerType.GetMethod("CreateEliteDrumsFretColorProvider",
                BindingFlags.Static | BindingFlags.NonPublic);
            var profile = new ColorProfile("preview kick slot provider");
            profile.EliteDrums.KickInputEffect = System.Drawing.Color.FromArgb(255, 240, 10, 20);
            profile.EliteDrums.DoubleKickInputEffect = System.Drawing.Color.FromArgb(255, 30, 220, 40);
            var provider = createProvider.Invoke(null, new object[] { layout, profile.EliteDrums });

            var providerInterface = Production("YARG.Core.Game.ColorProfile+IFretColorProvider");
            var getParticleColor = providerInterface.GetMethod("GetParticleColor");
            int EncodeSlot(int savedSlot) => (int)provider.GetType().GetMethod("EncodeSlot",
                BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { savedSlot });
            object color(int savedSlot) => getParticleColor.Invoke(provider, new object[] { EncodeSlot(savedSlot) });

            Assert.That(color(0), Is.EqualTo(profile.EliteDrums.KickInputEffect), "Saved kick1 slot uses kick input effect.");
            Assert.That(color(1), Is.EqualTo(profile.EliteDrums.DoubleKickInputEffect),
                "Saved kick2 slot uses double-kick input effect.");
            Assert.That(color(0), Is.Not.EqualTo(color(1)));
        }

        [Test]
        public void IncompleteElitePreview_FiltersStompAndSplashRoleNotesWithoutHiHat()
        {
            var draft = new EliteDrumsLayoutPreset("pedal spotlights without hat");
            draft.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", null);
            Assert.That(EliteDrumsLayoutResolver.TryPreviewResolve(draft, out var resolved), Is.True);

            var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
            var represented = playerType.GetMethod("IsEliteNoteRepresented", BindingFlags.Static | BindingFlags.NonPublic);
            var generatorType = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
            var describe = generatorType.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static);
            var createRoleNote = generatorType.GetMethod("CreateRoleNote", BindingFlags.Public | BindingFlags.Static);

            foreach (var role in new[] { EliteDrumsColorRole.Stomp, EliteDrumsColorRole.Splash })
            {
                var descriptor = describe.Invoke(null, new[] { (object)role,
                    Enum.Parse(Production("YARG.Themes.ThemeNoteType"), "Normal") });
                var pedalNote = createRoleNote.Invoke(null, new[] { (object)0d, descriptor });
                Assert.That(pedalNote, Is.Not.Null, $"Production generator must create {role} role notes.");
                Assert.That((bool)represented.Invoke(null, new[] { pedalNote, resolved }), Is.False,
                    $"{role} is represented by the hi-hat source, not snare alone.");
            }
        }

        [Test]
        public void EmptyEliteDraft_ResolvesToZeroLanesForSafeBlankPreview()
        {
            var empty = new EliteDrumsLayoutPreset("empty preview");
            Assert.That(EliteDrumsLayoutResolver.TryPreviewResolve(empty, out var resolved), Is.True);
            Assert.That(resolved.Lanes, Is.Empty);
        }

        private sealed class EnglishLocalizationScope : IDisposable
        {
            private readonly Type _manager = Production("YARG.Localization.LocalizationManager");
            private readonly IDictionary<string, string> _map;
            private readonly KeyValuePair<string, string>[] _previous;
            private readonly string _culture;

            public EnglishLocalizationScope()
            {
                _map = (IDictionary<string, string>)_manager.GetField("_localizationMap",
                    BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                _previous = _map.ToArray();
                _culture = (string)_manager.GetProperty("CultureCode").GetValue(null);
                try
                {
                    Assert.That((bool)_manager.GetMethod("TryParseAndLoadLanguage",
                        BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "en-US" }),
                        Is.True, "Load the production English dictionary before asserting localized labels.");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                _map.Clear();
                foreach (var entry in _previous) _map.Add(entry.Key, entry.Value);
                _manager.GetProperty("CultureCode").GetSetMethod(true).Invoke(null, new object[] { _culture });
            }
        }

        [Test]
        public void PreviewOptionsDropdown_UsesIndependentEditorStateWithoutMenuSingleton()
        {
            using var localization = new EnglishLocalizationScope();
            var helper = Production("YARG.Settings.Metadata.PreviewOptionsDropdown");
            var stateType = helper.GetNestedType("State");
            var stateA = Activator.CreateInstance(stateType, new object[] { true, false, false, false });
            var stateB = Activator.CreateInstance(stateType, new object[] { false, true, true, true });
            var display = helper.GetMethod("DisplayValue", BindingFlags.Public | BindingFlags.Static);
            string labelA = (string)display.Invoke(null, new[] { (object)"Star Power Notes", stateA });
            string labelB = (string)display.Invoke(null, new[] { (object)"Star Power Notes", stateB });
            Assert.That(labelA, Does.Contain("Star Power Notes: On"));
            Assert.That(labelB, Does.Contain("Star Power Notes: Off"));

            var create = helper.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            var providerType = typeof(Func<>).MakeGenericType(stateType);
            Delegate Provider(object state)
            {
                var lambda = Expression.Lambda(providerType, Expression.Constant(state, stateType));
                return lambda.Compile();
            }

            object dropdownA = create.Invoke(null, new object[] { Provider(stateA), null });
            object dropdownB = create.Invoke(null, new object[] { Provider(stateB), null });
            var dropdownInterface = Production("YARG.Settings.Types.IDropdownSetting");
            var indexToString = dropdownInterface.GetMethod("IndexToString");
            Assert.That(indexToString, Is.Not.Null);
            Assert.That((string)indexToString.Invoke(dropdownA, new object[] { 0 }), Does.Contain("Star Power Notes: On"));
            Assert.That((string)indexToString.Invoke(dropdownB, new object[] { 0 }), Does.Contain("Star Power Notes: Off"));
        }
    }
}
