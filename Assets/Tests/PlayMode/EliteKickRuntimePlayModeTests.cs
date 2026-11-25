using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Engine;
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

        [UnityTest]
        public IEnumerator DoubleKickWarningFollowsPlayerSettingsAndSeeks()
        {
            var profile = Profile();
            profile.AddSingleModifier(Modifier.Enable2xKicks);
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(KickChart(), profile, EliteDrumsLayoutPreset.Compact, noFail: true);
            var player = Fixture.Player;
            var update = player.GetType().GetMethod("UpdateDoubleKickWarning", INSTANCE);
            var settings = Production("YARG.Settings.SettingsManager").GetProperty("Settings").GetValue(null);
            var style = settings.GetType().GetProperty("EliteDoubleKickWarningStyle").GetValue(settings);
            var duration = settings.GetType().GetProperty("EliteDoubleKickWarningThroughMeasures").GetValue(settings);
            var enumType = style.GetType().GetProperty("Value").PropertyType;
            void Sample(double t) => update.Invoke(player, new object[] { t });
            void SetStyle(string name) => style.GetType().GetMethod("SetValueWithoutNotify")
                .Invoke(style, new[] { Enum.Parse(enumType, name) });
            var gate = settings.GetType().GetProperty("EliteDoubleKickFlamesRequireFullMultiplier").GetValue(settings);
            var stagger = settings.GetType().GetProperty("StaggerEliteKickSparkAnimation")?.GetValue(settings);
            Assert.That(stagger, Is.Not.Null, "The experimental toggle must be available during gameplay.");
            bool savedStagger = (bool) stagger.GetType().GetProperty("Value").GetValue(stagger);
            void SetToggle(object setting, bool value) => setting.GetType().GetMethod("SetValueWithoutNotify")
                .Invoke(setting, new object[] { value });
            var savedStyle = style.GetType().GetProperty("Value").GetValue(style).ToString();
            bool savedDuration = (bool) duration.GetType().GetProperty("Value").GetValue(duration);
            bool savedGate = (bool) gate.GetType().GetProperty("Value").GetValue(gate);
            try
            {
                SetToggle(duration, true);
                SetToggle(gate, true);
                SetStyle("EmberTrailsAndFlames");
                var stats = (BaseStats) player.GetType().GetProperty("BaseStats").GetValue(player);
                var parameters = (BaseEngineParameters) player.GetType().GetProperty("BaseParameters").GetValue(player);
                stats.ScoreMultiplier = parameters.MaxMultiplier;
                float speed = (float) player.GetType().GetProperty("NoteSpeed").GetValue(player);
                const float strike = -2f;

                Sample(1.25);
                var marker = player.transform.Find("Elite double kick warning").GetComponent<MeshRenderer>();
                Assert.That(marker.enabled, Is.True);
                Assert.That(marker.sharedMaterial.shader.name, Is.EqualTo("YARG/Elite Double Kick Warning"));
                var mesh = marker.GetComponent<MeshFilter>().sharedMesh;
                var uv = new List<Vector4>();
                bool HasFlames()
                {
                    mesh.GetUVs(0, uv);
                    return uv.Any(point => point.z == 2);
                }
                Assert.That(mesh.bounds.min.x, Is.LessThan(-1f));
                Assert.That(mesh.bounds.max.x, Is.GreaterThan(1f));
                Assert.That(mesh.bounds.size.y, Is.GreaterThan(.12f), "Approaching embers rise above the edge.");
                Assert.That(mesh.bounds.min.z, Is.EqualTo(strike + .25f * speed).Within(.001f));
                Assert.That(HasFlames(), Is.False);
                Sample(1.375);
                Assert.That(mesh.bounds.min.z, Is.EqualTo(strike + .125f * speed).Within(.001f));
                Sample(1.5);
                Assert.That(mesh.bounds.min.z, Is.EqualTo(strike).Within(.001f));
                Assert.That(HasFlames(), Is.True, "At full combo the flames ignite on the warning beat.");
                stats.IsStarPowerActive = true;
                Sample(1.75);
                Assert.That(HasFlames(), Is.False, "A doubled partial multiplier cannot earn flames.");
                Assert.That(marker.enabled, Is.True, "The approaching cue is independent of score.");
                stats.ScoreMultiplier *= 2;
                Sample(1.75);
                Assert.That(HasFlames(), Is.True);
                stats.IsStarPowerActive = false;
                stats.ScoreMultiplier = 1;
                Sample(1.75);
                Assert.That(HasFlames(), Is.False);
                SetToggle(gate, false);
                Sample(1.75);
                Assert.That(HasFlames(), Is.True);

                SetStyle("BeaterFlames");
                Sample(2.0);
                float heightOnKick = mesh.bounds.size.y;
                Sample(2.15);
                Assert.That(heightOnKick, Is.GreaterThan(mesh.bounds.size.y * 1.5f),
                    "The flame swells on the charted kick.");
                SetStyle("EmberTrails");
                Sample(2.1);
                Assert.That(mesh.bounds.size.z, Is.GreaterThan(1f));
                Assert.That(HasFlames(), Is.False);
                SetToggle(duration, false);
                Sample(2.1);
                Assert.That(marker.enabled, Is.False, "Lead-up only ends on the first 2× kick.");
                Sample(1.75);
                Assert.That(marker.enabled, Is.True, "Seeking back into the lead shows the warning again.");
                // At 120 BPM, 1/32 notes arrive every 62.5 ms.
                // Compare the feet of the cards, which are anchored on their exact chart times.
                var feet = mesh.vertices.Where((_, i) => i % 2 == 0).Select(v => v.z).Distinct().ToArray();
                Assert.That(feet.Length, Is.EqualTo(4));
                for (int i = 0; i < feet.Length; i++)
                    Assert.That(feet[i], Is.EqualTo(strike + i * .0625f * speed).Within(.001f));

                var sparkData = new List<Vector2>();
                SetToggle(stagger, false);
                Sample(1.75);
                mesh.GetUVs(1, sparkData);
                Assert.That(sparkData.Count, Is.EqualTo(32));
                Assert.That(sparkData.All(v => v.x == 0 && v.y == 0), Is.True);
                SetToggle(stagger, true);
                Sample(1.75);
                mesh.GetUVs(0, uv);
                mesh.GetUVs(1, sparkData);
                var phases = sparkData.Where((_, i) => i % 8 == 0).Select(v => v.x).ToArray();
                Assert.That(phases.Distinct().Count(), Is.GreaterThan(2));
                for (int i = 0; i < sparkData.Count; i += 8)
                {
                    Assert.That(sparkData.Skip(i).Take(8).All(v => v.x == phases[i / 8]), Is.True);
                    Assert.That(uv[i].w, Is.EqualTo(uv[i + 6].w), "Facing edges sample mirrored artwork.");
                    Assert.That(uv[i].w, Is.Not.EqualTo(uv[i + 4].w));
                }
                SetToggle(duration, true);
                Sample(2.0);
                mesh.GetUVs(1, sparkData);
                Assert.That(sparkData.Take(8).All(v => v.y == 1), Is.True, "The charted kick gets both layers.");
                Sample(1.75);
                mesh.GetUVs(1, sparkData);
                Assert.That(sparkData.Where((_, i) => i % 8 == 0).Take(4).Select(v => v.x), Is.EqualTo(phases));
                SetStyle("Off");
                Sample(1.75);
                Assert.That(marker.enabled, Is.False);
            }
            finally
            {
                SetStyle(savedStyle);
                SetToggle(duration, savedDuration);
                SetToggle(gate, savedGate);
                SetToggle(stagger, savedStagger);
            }
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
