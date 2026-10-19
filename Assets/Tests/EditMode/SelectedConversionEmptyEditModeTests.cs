using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class SelectedConversionEmptyEditModeTests
    {
        private static MethodInfo LoadingMethod(string name)
        {
            var gameManager = Type.GetType("YARG.Gameplay.GameManager, Assembly-CSharp");
            Assert.That(gameManager, Is.Not.Null);
            var method = gameManager.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return method;
        }

        private static SongChart KickOnlySource()
        {
            var source = new InstrumentDifficulty<DrumNote>(Instrument.FourLaneDrums, Difficulty.Expert);
            source.Notes.Add(new DrumNote(FourLaneDrumPad.Kick, DrumNoteType.Neutral,
                DrumNoteFlags.None, NoteFlags.None, 0, 0));
            var sources = new AuthoredDrumSourceCollection();
            var tier = (AuthoredDrumSourceTier) Activator.CreateInstance(typeof(AuthoredDrumSourceTier),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[]
                {
                    new DrumSourceTierFacts(DrumSourceFormat.FourLane, Difficulty.Expert,
                        true, false, false, false, true), source, null, Array.Empty<uint>()
                }, null);
            typeof(AuthoredDrumSourceCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sources, new object[] { tier });
            var chart = new SongChart(192);
            typeof(SongChart).GetProperty("AuthoredDrumSources").GetSetMethod(true)
                .Invoke(chart, new object[] { sources });
            return chart;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SelectedConversion_NoKicksRecoversButPlayableOutputContinues(bool noKicks)
        {
            var chart = KickOnlySource();
            var resolved = new ResolvedDrumPlayback(Instrument.EliteDrums, DrumSourceFormat.FourLane,
                Difficulty.Expert, Difficulty.Expert, DrumExtraKickPolicy.Remove, false,
                ResolvedDrumPlayback.CategoryFor(Instrument.EliteDrums, DrumSourceFormat.FourLane));
            var prepared = DrumPlaybackPreparer.PrepareForScore(chart, resolved,
                pureEliteModifiers: notes =>
                {
                    if (noKicks) notes.RemoveEliteKickDrumNotes();
                });

            Assert.That(prepared.Elite, Is.Not.Null, "The selected conversion must actually be generated.");
            Assert.That(prepared.Classic, Is.Null, "Do not silently substitute the authored Four Lane output.");
            Assert.That(prepared.Elite.Notes.Count, Is.EqualTo(noKicks ? 0 : 1));
            var failure = (string) LoadingMethod("ValidateSelectedDrumOutput").Invoke(null,
                new object[] { prepared, "Test Drummer" });
            var calls = new List<string>();
            Action<string> showError = message => calls.Add("error: " + message);
            Action returnToSelection = () => calls.Add("selection");

            var stopLoading = (bool) LoadingMethod("RecoverPlayerLoadFailure").Invoke(null,
                new object[] { failure, showError, returnToSelection });

            Assert.That(stopLoading, Is.EqualTo(noKicks),
                "Start must stop before player engines are used when the selected output is empty.");
            if (noKicks)
            {
                const string expected = "The selected MIDI Drumkit output is empty for Test Drummer. " +
                    "Please choose another difficulty or drum output.";
                Assert.That(failure, Is.EqualTo(expected));
                Assert.That(calls, Is.EqualTo(new[] { "error: " + expected, "selection" }),
                    "Report the problem once, then return to selection once.");
            }
            else
            {
                Assert.That(failure, Is.Null);
                Assert.That(calls, Is.Empty, "Playable output must not show an error or leave gameplay.");
            }

            Assert.That(resolved.RequestedOutput, Is.EqualTo(Instrument.EliteDrums));
            Assert.That(resolved.SourceFormat, Is.EqualTo(DrumSourceFormat.FourLane));
            Assert.That(chart.AuthoredDrumSources.Tiers[(DrumSourceFormat.FourLane, Difficulty.Expert)]
                .CloneClassicDifficulty().Notes.Count, Is.EqualTo(1),
                "Recovery and modifiers must not change the authored source or choose a replacement.");
        }

        [Test]
        public void MissingSelectedPlayback_UsesTheSameRecoveryPath()
        {
            var failure = (string) LoadingMethod("ValidateSelectedDrumOutput").Invoke(null,
                new object[] { null, "Test Drummer" });
            var calls = new List<string>();
            Assert.That((bool) LoadingMethod("RecoverPlayerLoadFailure").Invoke(null,
                new object[] { failure, (Action<string>) (message => calls.Add(message)),
                    (Action) (() => calls.Add("selection")) }), Is.True);
            Assert.That(calls.Count, Is.EqualTo(2));
            Assert.That(calls[0], Does.Contain("selected MIDI Drumkit output is empty"));
            Assert.That(calls[1], Is.EqualTo("selection"));
        }
    }
}
