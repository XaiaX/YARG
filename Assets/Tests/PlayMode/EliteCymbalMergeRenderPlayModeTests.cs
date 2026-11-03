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
using YARG.Core.Chart;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteCymbalMergeRenderPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly EliteLayoutRuntimePlayModeTests _environment = new();
        private InitializedElitePlayerFixture _fixture;
        private static Type Production(string name) => PreparedElitePlayerFixture.ProductionType(name);

        [UnitySetUp]
        public IEnumerator SetUp() => _environment.SetUpSettingsAndAudio();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_fixture != null) yield return _fixture.Dispose();
            _fixture = null;
            yield return _environment.TearDown();
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator InitializedPlayerUsesDestinationGeometryAndIndependentSourcePaletteForAllPairs() => RenderPairs(false);

        [UnityTest, Timeout(180000)]
        public IEnumerator OrderedPairsRenderBothSourcesWithPrimaryOrOriginalColorsAcrossNativePoolReuse() => RenderPairs(true);

        private IEnumerator RenderPairs(bool ordered)
        {
            foreach (int pair in Enumerable.Range(0, ordered ? 6 : 3))
            foreach (bool lefty in new[] { false, true })
            foreach (bool cymbalModels in new[] { false, true })
            {
                var definition = ordered ? EliteCymbalMergePreviewPlayModeTests.OrderedPairs[pair]
                    : (Primary: pair == 0 ? "ride" : "right-crash",
                        Secondary: pair == 2 ? "ride" : "left-crash",
                        Destination: pair == 2 ? "left-crash" : "hi-hat", Sample: pair);
                var displaced = EliteCymbalMergePreviewPlayModeTests.SourcePad(definition.Secondary);
                var keeper = EliteCymbalMergePreviewPlayModeTests.SourcePad(definition.Primary);
                var destination = EliteCymbalMergePreviewPlayModeTests.SourcePad(definition.Destination);
                var source = new List<EliteDrumNote>();
                for (int shape = 0; shape < 5; shape++)
                {
                    var chord = Note(keeper, 10d + shape, (uint)(9600 + shape * 960));
                    var member = Note(displaced, 10d + shape, (uint)(9600 + shape * 960));
                    member.Dynamics = shape == 1 ? DrumNoteType.Accent : shape == 2 ? DrumNoteType.Ghost : DrumNoteType.Neutral;
                    member.IsFlam = shape == 3;
                    member.IsFlatFlam = shape == 4;
                    chord.AddChildNote(member);
                    source.Add(chord);
                }
                var single = Note(displaced, 16d, 15360);
                source.Add(single);
                var primarySingle = Note(keeper, 17d, 16320);
                source.Add(primarySingle);
                var profile = new YargProfile
                {
                    GameMode = GameMode.EliteDrums, CurrentInstrument = Instrument.EliteDrums,
                    PreferredInstrument = Instrument.EliteDrums, CurrentDifficulty = Difficulty.Hard,
                    UseCymbalModels = cymbalModels, LeftyFlip = lefty
                };
                var layout = ordered
                    ? EliteCymbalMergePreviewPlayModeTests.OrderedLayout(definition.Primary, definition.Secondary)
                    : EliteCymbalMergePreviewPlayModeTests.MergedLayout(pair);
                _fixture = new InitializedElitePlayerFixture();
                yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(source.ToArray()), profile, layout);
                var difficulty = (InstrumentDifficulty<EliteDrumNote>)_fixture.Session.GetType()
                    .GetProperty("PlayableEliteDrumDifficulty").GetValue(_fixture.Session);
                var colors = EliteCymbalMergePreviewPlayModeTests.Palette();
                _fixture.Session.GetType().GetProperty("ColorProfile").SetValue(_fixture.Session, colors);
                var resolved = (EliteDrumsResolvedLayout)_fixture.Player.GetType().GetProperty("Layout").GetValue(_fixture.Player);
                var projected = new List<EliteDrumNote>();
                foreach (var chord in difficulty.Notes)
                foreach (var note in chord.AllNotes)
                    projected.Add(note);
                Assert.That(projected, Has.Count.EqualTo(12));
                var seen = new HashSet<Component>();
                foreach (var note in projected)
                {
                    bool expanded = note.OriginalPad == (int)displaced && note.Tick != single.Tick;
                    Assert.That(note.Pad, Is.EqualTo((int)(expanded ? destination : keeper)));
                    Assert.That(note.IsCymbalExpanded, Is.EqualTo(expanded));
                    if (expanded && destination == EliteDrumPad.HiHat)
                        Assert.That(note.HatState, Is.EqualTo(EliteDrumsHatState.Indifferent));
                    _fixture.Player.GetType().GetMethod("SpawnNote", INSTANCE).Invoke(_fixture.Player, new object[] { note });
                    var element = _fixture.Player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true)
                        .Single(item => item.gameObject.activeInHierarchy && ReferenceEquals(item.GetType().GetProperty("NoteRef").GetValue(item), note));
                    if (seen.Count > 0) Assert.That(seen, Does.Contain(element), "Projected variants must survive native pool reuse.");
                    seen.Add(element);
                    var position = element.transform.localPosition;
                    string target = expanded ? definition.Destination : definition.Primary;
                    Assert.That(position.x, Is.EqualTo(resolved.GetSourceX(target, 2f, lefty)).Within(.001f));
                    foreach (bool star in new[] { false, true, false })
                    foreach (bool keepOriginal in new[] { false, true, false })
                    {
                        profile.KeepOriginalCymbalColors = keepOriginal;
                        if (star) note.ActivateFlag(NoteFlags.StarPower); else note.ClearFlag(NoteFlags.StarPower);
                        element.GetType().GetMethod("UpdateElement", INSTANCE).Invoke(element, null);
                        var groups = element.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
                            .Where(group => group.gameObject.activeInHierarchy).ToArray();
                        Assert.That(groups.Length, Is.EqualTo(note.IsFlam || note.IsFlatFlam ? 2 : 1));
                        string colorSource = keepOriginal
                            ? note.OriginalPad == (int)displaced ? definition.Secondary : definition.Primary : target;
                        var role = EliteCymbalMergePreviewPlayModeTests.SourceRole(colorSource);
                        var expected = star ? colors.EliteDrums.GetNoteStarPowerColor(role) : colors.EliteDrums.GetNoteColor(role);
                        foreach (var group in groups)
                        {
                            var theme = group.GetComponentInChildren(Production("YARG.Themes.ThemeNote"));
                            string model = theme.GetType().GetProperty("NoteType").GetValue(theme).ToString();
                            string strength = note.IsGhost ? "Ghost" : note.IsAccent ? "Accent" : "Normal";
                            string expectedModel = cymbalModels ? strength == "Normal" ? "Cymbal" : "Cymbal" + strength : strength;
                            Assert.That(model, Is.EqualTo(expectedModel));
                            EliteCymbalMergePreviewPlayModeTests.AssertBodyColor(theme, expected);
                        }
                        Assert.That(element.transform.localPosition, Is.EqualTo(position));
                        Assert.That(note.Pad, Is.EqualTo((int)(expanded ? destination : keeper)));
                        if (!star && !lefty && cymbalModels && note.Tick == 9600)
                            EliteCymbalMergePreviewPlayModeTests.CaptureModels(new[] { element },
                                $"gameplay-{(ordered ? "ordered" : "legacy")}-pair{pair}-source{note.OriginalPad}-original{keepOriginal}");
                    }
                    note.SetMissState(true, false);
                    element.GetType().GetMethod("UpdateElement", INSTANCE).Invoke(element, null);
                    foreach (var group in element.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
                        .Where(group => group.gameObject.activeInHierarchy))
                        EliteCymbalMergePreviewPlayModeTests.AssertBodyColor(group.GetComponentInChildren(Production("YARG.Themes.ThemeNote")), colors.EliteDrums.Miss);
                    var pool = element.GetType().GetProperty("ParentPool").GetValue(element);
                    pool.GetType().GetMethod("Return").Invoke(pool, new object[] { element });
                }
                foreach (var chord in source.Take(5))
                {
                    Assert.That(chord.Pad, Is.EqualTo((int)keeper));
                    Assert.That(chord.ChildNotes.Single().Pad, Is.EqualTo((int)displaced), "One player's playable projection cannot rewrite the source chart.");
                }
                Assert.That(single.Pad, Is.EqualTo((int)displaced));
                Assert.That(primarySingle.Pad, Is.EqualTo((int)keeper));
                yield return _fixture.Dispose();
                _fixture = null;
            }
        }

        private static EliteDrumNote Note(EliteDrumPad pad, double time, uint tick) => new(pad,
            DrumNoteType.Neutral, EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp,
            false, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, tick, false);
    }
}
