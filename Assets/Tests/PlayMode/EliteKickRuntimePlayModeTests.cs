using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteKickRuntimePlayModeTests : EliteFlamRuntimeTestBase
    {
        private static SongChart KickChart()
        {
            var notes = new EliteDrumNote[9];
            int index = 0;
            foreach (var strength in new[] { DrumNoteType.Ghost, DrumNoteType.Neutral, DrumNoteType.Accent })
            for (int kind = 0; kind < 3; kind++)
            {
                double time = index + 1;
                notes[index++] = new EliteDrumNote(EliteDrumPad.Kick, strength,
                    EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, kind == 2,
                    DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None,
                    time, (uint)(time * 960), kind == 1);
            }
            return PreparedElitePlayerFixture.CreateChart(notes);
        }

        private static EliteDrumsLayoutPreset Layout(EliteDrumsLayoutKickMode mode)
        {
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Kick runtime test");
            if (mode == EliteDrumsLayoutKickMode.Combined)
                layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
            if (mode == EliteDrumsLayoutKickMode.Split)
            {
                layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
                layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            }
            return layout;
        }

        private static Bounds RenderedBounds(Component element)
        {
            var renderers = element.GetComponentsInChildren<Renderer>();
            Assert.That(renderers, Is.Not.Empty, "The selected model must actually render.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        [UnityTest]
        public IEnumerator SpawnedKicksKeepTheirModelsAndRegionsAcrossLayoutsStarPowerAndPoolReuse()
        {
            foreach (bool lefty in new[] { false, true })
            foreach (var mode in new[] { EliteDrumsLayoutKickMode.Bars, EliteDrumsLayoutKickMode.Combined,
                EliteDrumsLayoutKickMode.Split })
            {
                var profile = Profile();
                profile.LeftyFlip = lefty;
                profile.AddSingleModifier(Modifier.Enable2xKicks);
                Fixture = new InitializedElitePlayerFixture();
                yield return Fixture.Initialize(KickChart(), profile, Layout(mode));
                var colors = new ColorProfile("kick runtime colors");
                colors.EliteDrums.KickNote = System.Drawing.Color.Red;
                colors.EliteDrums.DoubleKickNote = System.Drawing.Color.Blue;
                colors.EliteDrums.KickStarpower = System.Drawing.Color.Lime;
                colors.EliteDrums.DoubleKickStarpower = System.Drawing.Color.Magenta;
                Fixture.Session.GetType().GetProperty("ColorProfile").GetSetMethod(true).Invoke(Fixture.Session, new object[] { colors });
                Assert.That(Playable.Notes.Count, Is.EqualTo(9));
                var player = Fixture.Player;
                var resolved = (EliteDrumsResolvedLayout)player.GetType().GetProperty("Layout").GetValue(player);
                var pool = player.GetType().GetField("NotePool", INSTANCE).GetValue(player);
                float expectedWidth = mode == EliteDrumsLayoutKickMode.Bars ? 2f : resolved.GetLaneWidth(2f);
                Component previous = null;
                foreach (var note in Playable.Notes)
                {
                    pool.GetType().GetMethod("ReturnAllObjects").Invoke(pool, null);
                    player.GetType().GetMethod("SpawnNote", INSTANCE).Invoke(player, new object[] { note });
                    var element = (Component)pool.GetType().GetMethod("GetByKey").Invoke(pool, new object[] { note });
                    Assert.That(element, Is.Not.Null);
                    if (previous != null) Assert.That(element, Is.SameAs(previous), "Exercise the same pooled element with different models.");
                    previous = element;
                    string expectedType = (mode == EliteDrumsLayoutKickMode.Bars ? "Kick" : "DedicatedLaneKick")
                        + (note.IsFlam ? "Flam" : "") + (note.IsGhost ? "Ghost" : note.IsAccent ? "Accent" : "");
                    float expectedCenter = mode == EliteDrumsLayoutKickMode.Bars ? 0f
                        : resolved.GetSourceX(mode == EliteDrumsLayoutKickMode.Combined ? "kick"
                            : note.IsDoubleKick ? "kick2" : "kick1", 2f, profile.LeftyFlip);
                    foreach (bool star in new[] { false, true, false })
                    {
                        if (star) note.ActivateFlag(NoteFlags.StarPower);
                        else note.ClearFlag(NoteFlags.StarPower);
                        element.GetType().GetMethod("OnStarPowerUpdated").Invoke(element, null);
                        var groups = element.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
                            .Where(group => group.gameObject.activeInHierarchy).ToArray();
                        Assert.That(groups, Has.Length.EqualTo(1), "A source kick renders one assembly, including flams.");
                        var theme = groups[0].GetComponentInChildren(Production("YARG.Themes.ThemeNote"));
                        Assert.That(theme.GetType().GetProperty("NoteType").GetValue(theme).ToString(), Is.EqualTo(expectedType));
                        if (note.IsFlam)
                        for (int side = 0; side < 2; side++)
                        {
                            int pedal = lefty && mode == EliteDrumsLayoutKickMode.Bars ? 1 - side : side;
                            var expected = star ? pedal == 0 ? colors.EliteDrums.KickStarpower : colors.EliteDrums.DoubleKickStarpower
                                : pedal == 0 ? colors.EliteDrums.KickNote : colors.EliteDrums.DoubleKickNote;
                            foreach (var entry in (IEnumerable)theme.GetType().GetProperty(side == 0 ? "ColoredMaterials" : "ColoredKick2Materials").GetValue(theme))
                            {
                                var renderer = (Renderer)entry.GetType().GetField("Mesh").GetValue(entry);
                                int index = (int)entry.GetType().GetField("MaterialIndex").GetValue(entry);
                                float addition = (float)entry.GetType().GetField("EmissionAddition").GetValue(entry);
                                var actual = renderer.sharedMaterials[index].color;
                                Assert.That(actual.r, Is.EqualTo(expected.R / 255f + addition).Within(.001f));
                                Assert.That(actual.g, Is.EqualTo(expected.G / 255f + addition).Within(.001f));
                                Assert.That(actual.b, Is.EqualTo(expected.B / 255f + addition).Within(.001f));
                            }
                        }
                        var bounds = RenderedBounds(element);
                        Assert.That(bounds.size.x / Mathf.Abs(element.transform.lossyScale.x),
                            Is.EqualTo(expectedWidth).Within(.001f), expectedType);
                        float center = element.transform.parent.InverseTransformPoint(bounds.center).x;
                        Assert.That(center, Is.EqualTo(expectedCenter).Within(.001f), expectedType);
                    }
                }
                yield return Fixture.Dispose();
                Fixture = null;
            }
        }
    }
}
