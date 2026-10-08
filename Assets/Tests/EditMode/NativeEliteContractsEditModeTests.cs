using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YARG.Core.Input;

// pattern: Imperative Shell

namespace YARG.Tests.EditMode
{
    public sealed class NativeEliteContractsEditModeTests
    {
        [Test]
        public void Bindings()
        {
            // EditMode tests do not directly reference Assembly-CSharp.
            var type = Type.GetType("YARG.Input.BindingCollection, Assembly-CSharp");
            Assert.That(type, Is.Not.Null);
            var bindings = (System.Collections.IEnumerable) type.GetMethod("CreateEliteDrumsBindings")
                .Invoke(null, null);
            var actions = bindings.Cast<object>().Select(binding =>
                (int) binding.GetType().GetProperty("Action").GetValue(binding)).ToArray();
            foreach (var action in new[]
            {
                EliteDrumsAction.Kick, EliteDrumsAction.EliteStomp, EliteDrumsAction.EliteSplash,
                EliteDrumsAction.EliteSnare, EliteDrumsAction.EliteClosedHiHat,
                EliteDrumsAction.EliteSizzleHiHat, EliteDrumsAction.EliteOpenHiHat,
                EliteDrumsAction.EliteLeftCrash, EliteDrumsAction.EliteTom1,
                EliteDrumsAction.EliteTom2, EliteDrumsAction.EliteTom3,
                EliteDrumsAction.EliteRide, EliteDrumsAction.EliteRightCrash,
                EliteDrumsAction.FourLaneRedDrum, EliteDrumsAction.FiveLaneRedDrum
            })
            {
                Assert.That(actions, Does.Contain((int) action),
                    $"Missing native or legacy compatibility action {action}");
            }
            Assert.That(actions.Distinct().Count(), Is.EqualTo(actions.Length),
                "Action IDs must not collide.");
        }

        [Test]
        public void MenuFallbackLabel()
        {
            var rules = Type.GetType("YARG.Menu.Maestro.MaestroSelectionRules, Assembly-CSharp");
            Assert.That(rules, Is.Not.Null);
            var method = rules.GetMethod("OrderNativeInstruments");
            Assert.That(method, Is.Not.Null);
            var available = new[] { YARG.Core.Instrument.FiveLaneDrums,
                YARG.Core.Instrument.FourLaneDrums, YARG.Core.Instrument.ProDrums };
            var ordered = (System.Collections.Generic.IReadOnlyList<YARG.Core.Instrument>)
                method.Invoke(null, new object[] { YARG.Core.GameMode.EliteDrums, available });
            Assert.That(ordered[0], Is.EqualTo(YARG.Core.Instrument.ProDrums));
        }

        [Test]
        public void ProfileGameModeSelectsNativeElitePreference()
        {
            var sidebar = Type.GetType("YARG.Menu.ProfileList.ProfileSidebar, Assembly-CSharp");
            Assert.That(sidebar, Is.Not.Null);
            var select = sidebar.GetMethod("ApplyGameModeSelection",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(select, Is.Not.Null);

            var profile = new YARG.Core.Game.YargProfile
            {
                GameMode = YARG.Core.GameMode.FourLaneDrums,
                CurrentInstrument = YARG.Core.Instrument.ProDrums,
                PreferredInstrument = YARG.Core.Instrument.ProDrums,
                EliteDrumsDownchartTarget = YARG.Core.Instrument.FiveLaneDrums,
            };
            select.Invoke(null, new object[] { profile, YARG.Core.GameMode.EliteDrums });
            Assert.That(profile.GameMode, Is.EqualTo(YARG.Core.GameMode.EliteDrums));
            Assert.That(profile.CurrentInstrument, Is.EqualTo(YARG.Core.Instrument.EliteDrums));
            Assert.That(profile.PreferredInstrument, Is.EqualTo(YARG.Core.Instrument.EliteDrums));
            Assert.That(profile.EliteDrumsDownchartTarget, Is.Null);

            select.Invoke(null, new object[] { profile, YARG.Core.GameMode.FourLaneDrums });
            Assert.That(profile.CurrentInstrument, Is.EqualTo(YARG.Core.Instrument.FourLaneDrums));
            Assert.That(profile.PreferredInstrument, Is.EqualTo(profile.CurrentInstrument));
        }

        [Test]
        public void ReplayEntryPoint()
        {
            // Replay dispatch must use the recorded CurrentInstrument and native typed analyzer.
            var type = typeof(YARG.Core.Replays.Analyzer.ReplayAnalyzer);
            Assert.That(type.GetMethod("AnalyzeReplay", System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static), Is.Not.Null);
            Assert.That(Type.GetType("YARG.Gameplay.Player.EliteDrumsPlayer, Assembly-CSharp"), Is.Not.Null);
        }

        [Test]
        public void GemColorsUseDedicatedEliteRoles()
        {
            var cases = new[]
            {
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.Kick),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick, true, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.DoubleKick),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Snare, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.Snare),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.LeftCrash, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.LeftCrash),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Ride, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.Ride),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.RightCrash, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.RightCrash),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom1, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.Tom1),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom2, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.Tom2),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom3, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.Tom3),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HiHat, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.HatIndifferent),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HiHat, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Open, YARG.Core.Game.EliteDrumsColorRole.HatOpen),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HiHat, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Closed, YARG.Core.Game.EliteDrumsColorRole.HatClosed),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HatPedal, false, false,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.Stomp),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HatPedal, false, true,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.Splash),
            };
            foreach (var (pad, doubleKick, splash, hatState, expectedRole) in cases)
            {
                var note = new YARG.Core.Chart.EliteDrumNote(pad,
                    YARG.Core.Chart.DrumNoteType.Neutral, hatState,
                    splash ? YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Splash :
                        YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp,
                    false, YARG.Core.Chart.DrumNoteFlags.None, YARG.Core.Chart.NoteFlags.None,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsChannelFlag.None, 1d, 480, doubleKick);
                Assert.That(YARG.Core.Game.EliteDrumsColorRoles.GetRole(note), Is.EqualTo(expectedRole),
                    $"Color role for {pad}/{hatState}/{splash}/{doubleKick}");
            }
        }

        [Test]
        public void FlamsUseProSnareColorAndAccentGemWithoutMovingLanes()
        {
            var element = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp");
            var group = element.GetMethod("GetGemGroup",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var lane = Type.GetType("YARG.Gameplay.Player.EliteDrumsPlayer, Assembly-CSharp")
                .GetMethod("GetLane", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            foreach (YARG.Core.Chart.EliteDrumNote.EliteDrumPad pad in Enum.GetValues(
                typeof(YARG.Core.Chart.EliteDrumNote.EliteDrumPad)))
            {
                var note = new YARG.Core.Chart.EliteDrumNote(pad,
                    YARG.Core.Chart.DrumNoteType.Ghost,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Open,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp,
                    true, YARG.Core.Chart.DrumNoteFlags.None, YARG.Core.Chart.NoteFlags.None,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsChannelFlag.None, 1d, 480, false);
                var normalNote = new YARG.Core.Chart.EliteDrumNote(pad,
                    YARG.Core.Chart.DrumNoteType.Ghost,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Open,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp,
                    false, YARG.Core.Chart.DrumNoteFlags.None, YARG.Core.Chart.NoteFlags.None,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsChannelFlag.None, 1d, 480, false);
                Assert.That((int) lane.Invoke(null, new object[] { note.Pad }),
                    Is.EqualTo((int) lane.Invoke(null, new object[] { normalNote.Pad })));
                bool isKick = pad == YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick;
                // Wildcard has its own centered highway-wide model, independent of kick.
                bool isWildcard = pad == YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Wildcard;
                var expectedRole = pad switch
                {
                    YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick => YARG.Core.Game.EliteDrumsColorRole.KickFlam,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HatPedal => YARG.Core.Game.EliteDrumsColorRole.Stomp,
                    _ => YARG.Core.Game.EliteDrumsColorRole.HandFlam,
                };
                Assert.That(YARG.Core.Game.EliteDrumsColorRoles.GetRole(note), Is.EqualTo(expectedRole),
                    $"Flam color role for {pad}");
                Assert.That((int) group.Invoke(null, new object[] { note, true }), Is.EqualTo(isWildcard ? 14 : isKick ? 2 : 3),
                    $"Flam model for {pad}");
            }
        }

        [Test]
        public void SplitEliteFlamSettingDefaultsOff()
        {
            const string path = "Assets/Script/Settings/SettingsManager.Settings.cs";
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Assert.That(script, Is.Not.Null, $"Could not load {path}.");
            // Check the declared default without constructing audio-dependent settings.
            Assert.That(script.text, Does.Match(
                @"public\s+ToggleSetting\s+SplitEliteFlamGems\s*\{\s*get;\s*\}\s*=\s*new\s*\(\s*false\s*\)\s*;"),
                "Split-flam presentation must be declared with an off default.");
        }

        [Test]
        public void SplitFlamsPreservePadModelsAndColorsWhenEnabled()
        {
            var element = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp");
            var group = element.GetMethod("GetGemGroupForSplitFlam",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
                null, new[] { typeof(YARG.Core.Chart.EliteDrumNote), typeof(bool), typeof(bool) }, null);
            var splitGroup = element.GetMethod("GetSplitGroup",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
                null, new[] { typeof(YARG.Core.Chart.EliteDrumNote), typeof(bool) }, null);
            var isSplit = element.GetMethod("IsSplitFlam", System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);
            Assert.That(group, Is.Not.Null);
            Assert.That(splitGroup, Is.Not.Null);
            Assert.That(isSplit, Is.Not.Null);

            var neutral = MakeFlam(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Snare,
                YARG.Core.Chart.DrumNoteType.Neutral, YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent,
                isFlatFlam: true);
            Assert.That(neutral.IsFlatFlam, Is.True);
            Assert.That(neutral.IsFlam, Is.False);
            Assert.That((bool) isSplit.Invoke(null, new object[] { neutral, true }), Is.True);
            var kickFlam = MakeFlam(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick,
                YARG.Core.Chart.DrumNoteType.Neutral,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent);
            var flatKickFlam = MakeFlam(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick,
                YARG.Core.Chart.DrumNoteType.Neutral,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, isFlatFlam: true);
            foreach (var kick in new[] { kickFlam, flatKickFlam })
            {
                Assert.That((int) group.Invoke(null, new object[] { kick, false, false }), Is.EqualTo(2),
                    "Kick flams use the highway-wide KICK model with split flams disabled.");
                Assert.That((int) group.Invoke(null, new object[] { kick, false, true }), Is.EqualTo(2),
                    "Kick flams use the highway-wide KICK model with split flams enabled.");
                Assert.That(YARG.Core.Game.EliteDrumsColorRoles.GetRole(kick),
                    Is.EqualTo(YARG.Core.Game.EliteDrumsColorRole.KickFlam),
                    "Kick flam keeps its dedicated role regardless of splitting.");
            }
            Assert.That((int) group.Invoke(null, new object[] { neutral, false, true }), Is.EqualTo(0));
            Assert.That((int) group.Invoke(null, new object[] { neutral, false, false }), Is.EqualTo(3));
            Assert.That((int) element.GetMethod("GetSplitFlamTickOffset",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { 0, neutral.IsFlatFlam, false }), Is.Zero);
            var accent = MakeFlam(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Snare,
                YARG.Core.Chart.DrumNoteType.Accent, YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent);
            var ghost = MakeFlam(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Snare,
                YARG.Core.Chart.DrumNoteType.Ghost, YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent);
            Assert.That((int) splitGroup.Invoke(null, new object[] { neutral, false }), Is.EqualTo(0));
            Assert.That((int) splitGroup.Invoke(null, new object[] { accent, false }), Is.EqualTo(3));
            Assert.That((int) splitGroup.Invoke(null, new object[] { ghost, false }), Is.EqualTo(4));
            Assert.That((int) group.Invoke(null, new object[] { accent, false, true }), Is.EqualTo(3));
            Assert.That((int) group.Invoke(null, new object[] { ghost, false, true }), Is.EqualTo(4));

            var hiHatRoles = new[]
            {
                (YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Open, YARG.Core.Game.EliteDrumsColorRole.HatOpen),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Closed, YARG.Core.Game.EliteDrumsColorRole.HatClosed),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, YARG.Core.Game.EliteDrumsColorRole.HatIndifferent),
            };
            foreach (var (state, expectedRole) in hiHatRoles)
            {
                var hiHat = MakeFlam(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HiHat,
                    YARG.Core.Chart.DrumNoteType.Ghost, state);
                Assert.That(YARG.Core.Game.EliteDrumsColorRoles.GetRole(hiHat, splitHandFlam: true),
                    Is.EqualTo(expectedRole), $"Split flam color role for hi-hat state {state}");
                Assert.That((bool) isSplit.Invoke(null, new object[] { hiHat, true }), Is.True);
            }
        }

        [Test]
        public void SplitFlamsAreRestrictedToHandNotes()
        {
            var element = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp");
            var isSplit = element.GetMethod("IsSplitFlam", System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);
            var handPad = element.GetMethod("IsHandFlamPad", System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);
            foreach (YARG.Core.Chart.EliteDrumNote.EliteDrumPad pad in Enum.GetValues(
                typeof(YARG.Core.Chart.EliteDrumNote.EliteDrumPad)))
            {
                var note = MakeFlam(pad, YARG.Core.Chart.DrumNoteType.Neutral,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent);
                bool expectedHand = pad is YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Snare or
                    YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HiHat or
                    YARG.Core.Chart.EliteDrumNote.EliteDrumPad.LeftCrash or
                    YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom1 or
                    YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom2 or
                    YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom3 or
                    YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Ride or
                    YARG.Core.Chart.EliteDrumNote.EliteDrumPad.RightCrash;
                Assert.That((bool) handPad.Invoke(null, new object[] { (int) pad }), Is.EqualTo(expectedHand),
                    $"Hand-pad classification for {pad}");
                Assert.That((bool) isSplit.Invoke(null, new object[] { note, true }), Is.EqualTo(expectedHand),
                    $"Split eligibility for {pad}");
                if (!expectedHand)
                    Assert.That((int) element.GetMethod("GetGemGroupForSplitFlam",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                        .Invoke(null, new object[] { note, false, true }), Is.Not.EqualTo(0));
            }
        }

        private static YARG.Core.Chart.EliteDrumNote MakeFlam(
            YARG.Core.Chart.EliteDrumNote.EliteDrumPad pad, YARG.Core.Chart.DrumNoteType type,
            YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState state, bool isFlatFlam = false) => new(pad,
            type, state, YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp, !isFlatFlam,
            YARG.Core.Chart.DrumNoteFlags.None, YARG.Core.Chart.NoteFlags.None,
            YARG.Core.Chart.EliteDrumNote.EliteDrumsChannelFlag.None, 1d, 720, false,
            isFlatFlam: isFlatFlam);

        [Test]
        public void SplitFlamTimingUsesSyncTicksAndPreservesAuthoredNoteTime()
        {
            var syncType = typeof(YARG.Core.Chart.SyncTrack);
            Assert.That(syncType, Is.Not.Null);
            var element = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp");
            var timing = element.GetMethod("GetFlamStaggerTime",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var offset = element.GetMethod("GetSplitFlamTickOffset",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(timing, Is.Not.Null);
            Assert.That(offset, Is.Not.Null);
            Assert.That((int) offset.Invoke(null, new object[] { 0, true, false }), Is.Zero,
                "Flat visual-left gem has no time offset.");
            Assert.That((int) offset.Invoke(null, new object[] { 1, true, true }), Is.Zero,
                "Both flat gems stay at authored time.");
            Assert.That((int) offset.Invoke(null, new object[] { 0, false, false }), Is.EqualTo(-7));
            Assert.That((int) offset.Invoke(null, new object[] { 1, false, false }), Is.Zero,
                "The trailing right gem stays on the authored beat.");
            Assert.That((int) offset.Invoke(null, new object[] { 0, false, true }), Is.Zero,
                "Mirrored lefty visual-left gem stays on beat.");
            Assert.That((int) offset.Invoke(null, new object[] { 1, false, true }), Is.EqualTo(-7),
                "Lefty reverses the early gem to visual-right.");
            var sync = new YARG.Core.Chart.SyncTrack(960,
                new System.Collections.Generic.List<YARG.Core.Chart.TempoChange>
                {
                    new(120, 0, 0), new(60, 0.5, 960),
                }, new System.Collections.Generic.List<YARG.Core.Chart.TimeSignatureChange>(),
                new System.Collections.Generic.List<YARG.Core.Chart.Beatline>());
            uint authoredTick = 1440;
            var note = new YARG.Core.Chart.EliteDrumNote(
                YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Snare,
                YARG.Core.Chart.DrumNoteType.Neutral,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp,
                true, YARG.Core.Chart.DrumNoteFlags.None, YARG.Core.Chart.NoteFlags.None,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsChannelFlag.None, 1d, authoredTick, false);
            double authoredTime = note.Time;
            double expectedEarlier = sync.TickToTime(authoredTick - 7) - sync.TickToTime(authoredTick);
            Assert.That((double) timing.Invoke(null, new object[] { sync, authoredTick, -7 }), Is.EqualTo(expectedEarlier));
            Assert.That((double) timing.Invoke(null, new object[] { sync, authoredTick, 0 }), Is.Zero);
            Assert.That(note.Time, Is.EqualTo(authoredTime), "Visual stagger must not change scoring time.");
        }

        [Test]
        public void SplitFlamCentersFitInsideHighwayAndMirrorAtBoundaries()
        {
            var element = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp");
            var centers = element.GetMethod("GetSplitFlamCenters",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(centers, Is.Not.Null);
            const float laneWidth = 0.4f;
            const float gemWidth = laneWidth * 2f / 3f;
            foreach (float laneCenter in new[] { -0.8f, -0.4f, 0f, 0.4f, 0.8f })
            {
                object[] args = { laneCenter, laneWidth, false, 0f, 0f };
                centers.Invoke(null, args);
                float left = (float) args[3];
                float right = (float) args[4];
                Assert.That(left - gemWidth / 2f, Is.GreaterThanOrEqualTo(-1f - 0.001f));
                Assert.That(left + gemWidth / 2f, Is.LessThanOrEqualTo(1f + 0.001f));
                Assert.That(right - gemWidth / 2f, Is.GreaterThanOrEqualTo(-1f - 0.001f));
                Assert.That(right + gemWidth / 2f, Is.LessThanOrEqualTo(1f + 0.001f));
                Assert.That(right - left, Is.EqualTo(gemWidth).Within(0.001f));
                float expectedCenter = Mathf.Clamp(laneCenter, -1f + gemWidth, 1f - gemWidth);
                Assert.That((left + right) / 2f, Is.EqualTo(expectedCenter).Within(0.001f),
                    "Interior pairs center on their fret; only outer pairs shift inward.");
                if (Mathf.Abs(laneCenter) > 0.79f)
                    Assert.That(laneCenter < 0 ? left - gemWidth / 2f : right + gemWidth / 2f,
                        Is.EqualTo(Mathf.Sign(laneCenter)).Within(0.001f),
                        "Outer-fret flam touches the highway edge.");
                object[] mirrored = { laneCenter, laneWidth, true, 0f, 0f };
                centers.Invoke(null, mirrored);
                Assert.That((float) mirrored[3], Is.EqualTo(-right).Within(0.001f));
                Assert.That((float) mirrored[4], Is.EqualTo(-left).Within(0.001f));
            }
        }

        [Test]
        public void HatPedalStompAndSplashUseDedicatedLaneModel()
        {
            var element = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp");
            Assert.That(element, Is.Not.Null);
            var group = element.GetMethod("GetGemGroup",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(group, Is.Not.Null);

            var stomp = new YARG.Core.Chart.EliteDrumNote(
                YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HatPedal,
                YARG.Core.Chart.DrumNoteType.Ghost,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp,
                false, YARG.Core.Chart.DrumNoteFlags.None, YARG.Core.Chart.NoteFlags.None,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsChannelFlag.None, 1d, 480, false);
            var splash = new YARG.Core.Chart.EliteDrumNote(
                YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HatPedal,
                YARG.Core.Chart.DrumNoteType.Ghost,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Splash,
                false, YARG.Core.Chart.DrumNoteFlags.None, YARG.Core.Chart.NoteFlags.None,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsChannelFlag.None, 1d, 480, false);

            Assert.That((int) group.Invoke(null, new object[] { stomp, true }), Is.EqualTo(7),
                "Stomp must use the dedicated-lane bar model.");
            Assert.That((int) group.Invoke(null, new object[] { splash, true }), Is.EqualTo(7),
                "Splash must use the same dedicated-lane bar model as stomp.");

            var scaleStomp = element.GetMethod("ScaleStompModel",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var model = new GameObject("Stomp scale contract");
            try
            {
                var noteGroupType = Type.GetType("YARG.Gameplay.Visuals.NoteGroup, Assembly-CSharp");
                Assert.That(noteGroupType, Is.Not.Null);
                var noteGroup = model.AddComponent(noteGroupType);
                noteGroup.transform.localScale = new Vector3(2f, 3f, 4f);
                scaleStomp.Invoke(null, new object[] { noteGroup });
                Assert.That(noteGroup.transform.localScale, Is.EqualTo(new Vector3(6f, 3f, 4f)),
                    "Stomp must scale laterally by three without changing its vertical/depth scale.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(model);
            }
        }

        [Test]
        public void PairedPedalAndSingleKickSplitHighwayAndResetForUnpairedNotes()
        {
            var element = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp");
            var layout = element.GetMethod("TryGetPairedPedalKickLayout",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(layout, Is.Not.Null);

            YARG.Core.Chart.EliteDrumNote Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad pad,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType pedalType, bool doubleKick = false) =>
                new(pad, YARG.Core.Chart.DrumNoteType.Neutral,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent, pedalType,
                    false, YARG.Core.Chart.DrumNoteFlags.None, YARG.Core.Chart.NoteFlags.None,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsChannelFlag.None, 1d, 480, doubleKick);

            var kick = Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp);
            var pedal = Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HatPedal,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Splash);
            kick.AddChildNote(pedal);
            foreach (bool lefty in new[] { false, true })
            {
                foreach (var (note, pedalSide, targetWidth) in new[]
                {
                    (kick, false, 1f), (pedal, true, 1f)
                })
                {
                    object[] args = { note, lefty, 0f, 0f };
                    Assert.That((bool) layout.Invoke(null, args), Is.True);
                    Assert.That((float) args[2], Is.EqualTo((pedalSide == lefty ? 0.5f : -0.5f)),
                        "The bars should occupy opposite halves and mirror in lefty mode.");
                    Assert.That((float) args[3], Is.EqualTo(targetWidth));
                }
            }

            foreach (var unpaired in new[]
            {
                Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp),
                Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HatPedal,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp)
            })
            {
                object[] args = { unpaired, false, 0f, 0f };
                Assert.That((bool) layout.Invoke(null, args), Is.False);
                Assert.That((float) args[3], Is.EqualTo(1f));
            }
            var parentPedal = Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HatPedal,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp);
            var childKick = Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp);
            parentPedal.AddChildNote(childKick);
            foreach (var member in parentPedal.AllNotes)
            {
                object[] args = { member, false, 0f, 0f };
                Assert.That((bool) layout.Invoke(null, args), Is.True,
                    "Pairing must work regardless of which note is the chord parent.");
            }
            var invisible = Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HatPedal,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.InvisibleTerminator);
            var ordinaryKick = Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp);
            ordinaryKick.AddChildNote(invisible);
            object[] invisibleArgs = { ordinaryKick, false, 0f, 0f };
            Assert.That((bool) layout.Invoke(null, invisibleArgs), Is.False,
                "Invisible pedal controls must not shrink a visible kick.");

            var invalidKick = Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp, doubleKick: true);
            invalidKick.AddChildNote(Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.HatPedal,
                YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp));
            foreach (var member in invalidKick.AllNotes)
            {
                object[] args = { member, false, 0f, 0f };
                Assert.That((bool) layout.Invoke(null, args), Is.False,
                    "An invalid 2x kick/pedal chord should not be formatted as a valid pair.");
            }
        }

        [Test]
        public void TomCymbalCollisionLayoutsFitLaneAndMirrorForLefty()
        {
            var element = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp");
            var layout = element.GetMethod("TryGetTomCymbalLayout",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(layout, Is.Not.Null);
            var tomPairs = new[]
            {
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom1, YARG.Core.Chart.EliteDrumNote.EliteDrumPad.LeftCrash),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom2, YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Ride),
                (YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom3, YARG.Core.Chart.EliteDrumNote.EliteDrumPad.RightCrash),
            };
            YARG.Core.Chart.EliteDrumNote Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad pad) =>
                new(pad, YARG.Core.Chart.DrumNoteType.Neutral,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatState.Indifferent,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsHatPedalType.Stomp,
                    false, YARG.Core.Chart.DrumNoteFlags.None, YARG.Core.Chart.NoteFlags.None,
                    YARG.Core.Chart.EliteDrumNote.EliteDrumsChannelFlag.None, 1d, 480, false);

            const float laneWidth = 0.4f;
            const float highwayWidth = 2f;
            foreach (var (tomPad, cymbalPad) in tomPairs)
            {
                var tom = Make(tomPad);
                var cymbal = Make(cymbalPad);
                tom.AddChildNote(cymbal);
                foreach (var member in tom.ParentOrSelf.AllNotes)
                {
                    foreach (bool lefty in new[] { false, true })
                    {
                        var tinyArgs = new object[] { member, lefty, false, 0f, 0f };
                        Assert.That((bool) layout.Invoke(null, tinyArgs), Is.True);
                        Assert.That((float) tinyArgs[4], Is.EqualTo(laneWidth / 2f).Within(0.001f));
                        var offsetArgs = new object[] { member, lefty, true, 0f, 0f };
                        Assert.That((bool) layout.Invoke(null, offsetArgs), Is.True);
                        Assert.That((float) offsetArgs[4], Is.EqualTo(laneWidth *
                            (tomPad == YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom3 ? 2f / 3f : 5f / 6f)).Within(0.001f));

                        float tinyCenter = (float) tinyArgs[3];
                        float offsetCenter = (float) offsetArgs[3];
                        float laneCenter = tomPad switch
                        {
                            YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom1 => 0f,
                            YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom2 => 0.4f,
                            _ => 0.8f,
                        };
                        float mirror = lefty ? -1f : 1f;
                        float expectedTinyCenter = tomPad == YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom3
                            ? mirror * (highwayWidth / 2f - laneWidth / 4f) +
                                (member.Pad == (int) tomPad ? -mirror * laneWidth / 2f : 0f)
                            : mirror * laneCenter + mirror * (member.Pad == (int) tomPad ? -1f : 1f) * laneWidth / 4f;
                        float expectedOffsetCenter = tomPad == YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom3
                            ? mirror * (highwayWidth / 2f - laneWidth / 3f) +
                                (member.Pad == (int) tomPad ? -mirror * 2f * laneWidth / 3f : 0f)
                            : mirror * laneCenter + mirror * (member.Pad == (int) tomPad ? -1f : 1f) * 5f * laneWidth / 12f;
                        Assert.That(tinyCenter, Is.EqualTo(expectedTinyCenter).Within(0.001f));
                        Assert.That(offsetCenter, Is.EqualTo(expectedOffsetCenter).Within(0.001f));
                        Assert.That(tinyCenter - (float) tinyArgs[4] / 2f,
                            Is.GreaterThanOrEqualTo(-highwayWidth / 2f - 0.001f));
                        Assert.That(tinyCenter + (float) tinyArgs[4] / 2f,
                            Is.LessThanOrEqualTo(highwayWidth / 2f + 0.001f));
                        if (member.Pad == (int) tomPad)
                        {
                            float mirroredLaneCenter = lefty ? -laneCenter : laneCenter;
                            float facingEdge = lefty ? -1f : 1f;
                            Assert.That(Mathf.Abs(tinyCenter + facingEdge * (float) tinyArgs[4] / 2f - mirroredLaneCenter),
                                Is.EqualTo(0f).Within(0.001f), "Tiny gems must touch at the shared-lane center.");
                            if (tomPad != YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom3)
                            {
                                Assert.That(Mathf.Abs(offsetCenter + facingEdge * (float) offsetArgs[4] / 2f - mirroredLaneCenter),
                                    Is.EqualTo(0f).Within(0.001f), "Offset gems must touch at the shared-lane center.");
                                Assert.That(Mathf.Abs(offsetCenter - facingEdge * (float) offsetArgs[4] / 2f -
                                    (mirroredLaneCenter - facingEdge * laneWidth * 5f / 6f)),
                                    Is.EqualTo(0f).Within(0.001f), "Offset tom must reach 1/3 lane past the shared lane.");
                            }
                        }
                        else if (tomPad == YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Tom3)
                        {
                            float edge = (lefty ? -1f : 1f) * highwayWidth / 2f;
                            float facingEdge = lefty ? -1f : 1f;
                            Assert.That(tinyCenter + facingEdge * (float) tinyArgs[4] / 2f,
                                Is.EqualTo(edge).Within(0.001f),
                                "Tiny right-crash gem should meet the outer lane edge without extending outside.");
                            Assert.That(offsetCenter + facingEdge * (float) offsetArgs[4] / 2f,
                                Is.EqualTo(edge).Within(0.001f),
                                "Offset right-crash gem should be flush with the highway edge.");
                        }
                    }
                }
            }

            var separateTom = Make(tomPairs[0].Item1);
            var separateCrash = Make(tomPairs[0].Item2);
            var nearby = Make(YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Snare);
            separateTom.AddChildNote(nearby);
            foreach (var note in new[] { separateTom, separateCrash })
            {
                object[] args = { note, false, true, 0f, 0f };
                Assert.That((bool) layout.Invoke(null, args), Is.False,
                    "A same-time note outside the exact paired lane must not trigger the collision layout.");
            }

            var reverseParent = Make(tomPairs[0].Item2);
            var reverseChild = Make(tomPairs[0].Item1);
            reverseParent.AddChildNote(reverseChild);
            foreach (var note in reverseParent.ParentOrSelf.AllNotes)
            {
                object[] args = { note, false, true, 0f, 0f };
                Assert.That((bool) layout.Invoke(null, args), Is.True,
                    "Pairing must be independent of chord parent order.");
            }
        }

        [Test]
        public void ThemedEliteGemBaselinesSurvivePoolClone()
        {
            // ThemeManager builds groups on a template and the pool Instantiates
            // that template. Regression: nonserialized baseline arrays become
            // all-zero on clones, so ResetGemGroups hides every non-stomp gem.
            var elementType = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp");
            var groupType = Type.GetType("YARG.Gameplay.Visuals.NoteGroup, Assembly-CSharp");
            var template = new GameObject("Elite note template");
            GameObject clone = null;
            try
            {
                var element = template.AddComponent(elementType);
                int count = (int) elementType.GetField("COUNT", System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic).GetRawConstantValue();
                var groups = Array.CreateInstance(groupType, count);
                var starGroups = Array.CreateInstance(groupType, count);
                for (int i = 0; i < count; i++)
                {
                    var groupObject = new GameObject($"Gem {i}");
                    groupObject.transform.SetParent(template.transform, false);
                    groupObject.transform.localScale = new Vector3(1f + i, 1f, 1f);
                    var group = groupObject.AddComponent(groupType);
                    groups.SetValue(group, i);
                    starGroups.SetValue(group, i);
                }
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var noteGroups = elementType.BaseType.GetField("NoteGroups", flags);
                var starNoteGroups = elementType.BaseType.GetField("StarPowerNoteGroups", flags);
                noteGroups.SetValue(element, groups);
                starNoteGroups.SetValue(element, starGroups);
                var baselines = new Vector3[count];
                for (int i = 0; i < baselines.Length; i++) baselines[i] = new Vector3(1f + i, 1f, 1f);
                elementType.GetField("_normalGemScales", flags).SetValue(element, baselines);
                elementType.GetField("_starGemScales", flags).SetValue(element, baselines);
                elementType.GetField("_normalGemPositions", flags).SetValue(element, new Vector3[count]);
                elementType.GetField("_starGemPositions", flags).SetValue(element, new Vector3[count]);
                clone = UnityEngine.Object.Instantiate(template);
                var clonedElement = clone.GetComponent(elementType);
                elementType.GetMethod("ResetGemGroups", flags).Invoke(clonedElement, null);
                var clonedGroups = (Array) noteGroups.GetValue(clonedElement);
                for (int i = 0; i < count; i++)
                    Assert.That(((Component) clonedGroups.GetValue(i)).transform.localScale.x,
                        Is.EqualTo(1f + i), $"Pooled gem group {i} must retain its baseline width.");
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        [Test]
        public void SplitGemCloneBaselinesAreSerializedAndResetForPoolReuse()
        {
            var elementType = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp");
            var groupType = Type.GetType("YARG.Gameplay.Visuals.NoteGroup, Assembly-CSharp");
            var template = new GameObject("Elite split gem template");
            GameObject clone = null;
            try
            {
                var element = template.AddComponent(elementType);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                int count = (int) elementType.GetField("COUNT", System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic).GetRawConstantValue() * 2;
                var groups = Array.CreateInstance(groupType, count);
                var starGroups = Array.CreateInstance(groupType, count);
                for (int i = 0; i < count; i++)
                {
                    var normalObject = new GameObject($"Split normal {i}");
                    normalObject.transform.SetParent(template.transform, false);
                    normalObject.transform.localScale = new Vector3(i + 1, 1, 1);
                    groups.SetValue(normalObject.AddComponent(groupType), i);
                    var starObject = new GameObject($"Split star {i}");
                    starObject.transform.SetParent(template.transform, false);
                    starObject.transform.localScale = new Vector3(i + 5, 1, 1);
                    starGroups.SetValue(starObject.AddComponent(groupType), i);
                }
                elementType.GetField("_splitGemGroups", flags).SetValue(element, groups);
                elementType.GetField("_splitStarGemGroups", flags).SetValue(element, starGroups);
                var scales = new Vector3[count];
                var starScales = new Vector3[count];
                var positions = new Vector3[count];
                var starPositions = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    scales[i] = new Vector3(i + 1, 1, 1);
                    starScales[i] = new Vector3(i + 5, 1, 1);
                }
                elementType.GetField("_splitGemScales", flags).SetValue(element, scales);
                elementType.GetField("_splitStarGemScales", flags).SetValue(element, starScales);
                elementType.GetField("_splitGemPositions", flags).SetValue(element, positions);
                elementType.GetField("_splitStarGemPositions", flags).SetValue(element, starPositions);
                clone = UnityEngine.Object.Instantiate(template);
                var clonedGroups = (Array) elementType.GetField("_splitGemGroups", flags).GetValue(
                    clone.GetComponent(elementType));
                var reset = elementType.GetMethod("ResetSplitGemGroups", flags);
                var clonedElement = clone.GetComponent(elementType);
                var clonedStarGroups = (Array) elementType.GetField("_splitStarGemGroups", flags).GetValue(clonedElement);
                for (int i = 0; i < count; i++)
                {
                    ((Component) clonedGroups.GetValue(i)).transform.localScale = Vector3.one * 99;
                    ((Component) clonedGroups.GetValue(i)).gameObject.SetActive(true);
                    ((Component) clonedStarGroups.GetValue(i)).transform.localScale = Vector3.one * 99;
                    ((Component) clonedStarGroups.GetValue(i)).gameObject.SetActive(true);
                }
                reset.Invoke(clonedElement, null);
                for (int i = 0; i < count; i++)
                {
                    Assert.That(((Component) clonedGroups.GetValue(i)).transform.localScale.x,
                        Is.EqualTo(i + 1), "Each pooled split gem must restore its baseline scale.");
                    Assert.That(((Component) clonedGroups.GetValue(i)).gameObject.activeSelf, Is.False);
                    Assert.That(((Component) clonedStarGroups.GetValue(i)).transform.localScale.x,
                        Is.EqualTo(i + 5), "Each pooled star split gem must restore its baseline scale.");
                    Assert.That(((Component) clonedStarGroups.GetValue(i)).gameObject.activeSelf, Is.False);
                }
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        [Test]
        public void PairedBarFitsRenderedHalfHighwayBounds()
        {
            var elementType = Type.GetType("YARG.Gameplay.Visuals.EliteDrumsNoteElement, Assembly-CSharp");
            var groupType = Type.GetType("YARG.Gameplay.Visuals.NoteGroup, Assembly-CSharp");
            var fit = elementType.GetMethod("FitBarWidth",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, new[] { groupType, typeof(float), typeof(float) }, null);
            Assert.That(fit, Is.Not.Null);
            var root = new GameObject("Elite paired bar test");
            try
            {
                var element = root.AddComponent(elementType);
                var groupObject = new GameObject("Bar group");
                groupObject.transform.SetParent(root.transform, false);
                var group = groupObject.AddComponent(groupType);
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.transform.SetParent(groupObject.transform, false);
                mesh.transform.localScale = new Vector3(2f, 1f, 1f);
                // A second renderer models theme end caps extending well beyond
                // the central mesh; both must fit inside the same half.
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cap.transform.SetParent(groupObject.transform, false);
                cap.transform.localPosition = new Vector3(2f, 0, 0);
                cap.transform.localScale = new Vector3(0.2f, 1f, 1f);
                groupObject.transform.localScale = new Vector3(3f, 1f, 1f);
                root.transform.localPosition = new Vector3(-0.5f, 0, 0);
                fit.Invoke(element, new object[] { group, 1f, -0.5f });
                var meshBounds = mesh.GetComponent<Renderer>().bounds;
                var capBounds = cap.GetComponent<Renderer>().bounds;
                float left = Mathf.Min(meshBounds.min.x, capBounds.min.x);
                float right = Mathf.Max(meshBounds.max.x, capBounds.max.x);
                Assert.That(right - left, Is.EqualTo(1f).Within(0.001f));
                Assert.That((left + right) / 2f, Is.EqualTo(-0.5f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void VisualPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/FiveLaneDrumsVisual.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
        }
    }
}
