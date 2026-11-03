// pattern: Imperative Shell
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;

namespace YARG.Tests.PlayMode
{
    public sealed class ProfileFlamLayoutRuntimePlayModeTests : EliteFlamRuntimeTestBase
    {
        private InitializedClassicPlayerFixture _classic;

        [UnityTearDown]
        public IEnumerator DisposeClassic()
        {
            if (_classic != null) yield return _classic.Dispose();
            _classic = null;
        }

        private static void Layout(YargProfile profile, string layout)
        {
            var property = typeof(YargProfile).GetProperty("FlamLayout");
            Assert.That(property, Is.Not.Null, "Flam layout belongs to each profile, not the experimental global settings.");
            property.SetValue(profile, Enum.Parse(property.PropertyType, layout));
        }

        private static YargProfile Profile(Instrument instrument, string layout)
        {
            var result = new YargProfile
            {
                GameMode = instrument == Instrument.FiveLaneDrums ? GameMode.FiveLaneDrums : GameMode.FourLaneDrums,
                CurrentInstrument = instrument, PreferredInstrument = instrument, CurrentDifficulty = Difficulty.Hard,
                UseCymbalModels = true
            };
            Layout(result, layout);
            return result;
        }

        private static SongChart Chart(Instrument instrument)
        {
            var chart = new SongChart(480);
            chart.SyncTrack.Tempos.Add(new TempoChange(120, 0, 0));
            chart.SyncTrack.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0, 0, 0, 0, 16, 4));
            var difficulty = new InstrumentDifficulty<DrumNote>(instrument, Difficulty.Hard);
            difficulty.Notes.Add(new DrumNote(1, DrumNoteType.Accent, DrumNoteFlags.Flam, NoteFlags.None, 2d, 1920));
            chart.GetDrumsTrack(instrument).AddDifficulty(Difficulty.Hard, difficulty);
            return chart;
        }

        private static Component Spawn(Component player)
        {
            var note = ((System.Collections.Generic.IList<DrumNote>)player.GetType().GetProperty("Notes", INSTANCE).GetValue(player))[0];
            player.GetType().GetMethod("SpawnNote", INSTANCE).Invoke(player, new object[] { note });
            var pool = player.GetType().GetField("NotePool", INSTANCE).GetValue(player);
            return (Component)pool.GetType().GetMethod("GetByKey").Invoke(pool, new object[] { note });
        }

        private static Transform[] Gems(Component element) => element
            .GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
            .Where(group => group.gameObject.activeInHierarchy).OrderBy(group => group.transform.localPosition.x)
            .Select(group => group.transform).ToArray();

        [UnityTest]
        public IEnumerator SimultaneousClassicPlayersKeepIndependentLayoutsThroughStarPowerAndReuse()
        {
            foreach (var instrument in new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums })
            {
                var firstProfile = Profile(instrument, "V");
                var secondProfile = Profile(instrument, "Parallel");
                _classic = new InitializedClassicPlayerFixture();
                yield return _classic.Initialize(Chart(instrument), firstProfile);
                var first = Spawn(_classic.Player);
                yield return _classic.AddPlayer(secondProfile);
                var second = Spawn(_classic.AdditionalPlayer);
                var firstGems = Gems(first);
                var secondGems = Gems(second);
                Assert.That(firstGems, Has.Length.EqualTo(2));
                Assert.That(secondGems, Has.Length.EqualTo(2));
                Assert.That(Quaternion.Angle(firstGems[0].localRotation, firstGems[1].localRotation), Is.GreaterThan(1f));
                Assert.That(Quaternion.Angle(secondGems[0].localRotation, secondGems[1].localRotation), Is.LessThan(.001f),
                    instrument + " must use its own Parallel choice instead of the fixed V or another player's choice.");
                var secondRotations = secondGems.Select(group => group.localRotation).ToArray();
                var firstRotation = firstGems[0].localRotation;
                Layout(firstProfile, "Bucket");
                first.GetType().GetMethod("OnStarPowerUpdated").Invoke(first, null);
                Assert.That(Quaternion.Angle(Gems(first)[0].localRotation, firstRotation), Is.GreaterThan(1f));
                Assert.That(Gems(second).Select(group => group.localRotation).ToArray(), Is.EqualTo(secondRotations));
                var secondNote = ((System.Collections.Generic.IList<DrumNote>)_classic.AdditionalPlayer.GetType()
                    .GetProperty("Notes", INSTANCE).GetValue(_classic.AdditionalPlayer))[0];
                secondNote.ActivateFlag(NoteFlags.StarPower);
                second.GetType().GetMethod("OnStarPowerUpdated").Invoke(second, null);
                Assert.That(Quaternion.Angle(Gems(second)[0].localRotation, Gems(second)[1].localRotation), Is.LessThan(.001f));
                secondNote.IsFlam = false;
                secondNote.IsFlatFlam = true;
                second.GetType().GetMethod("OnStarPowerUpdated").Invoke(second, null);
                Assert.That(Gems(second).All(group => Quaternion.Angle(group.localRotation, Quaternion.identity) < .001f), Is.True,
                    "Flat flams retain their unrotated geometry independently of the profile's regular-flam layout.");
                yield return _classic.Dispose();
                _classic = null;
            }
        }
    }
}
