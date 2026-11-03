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
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.IO;
using YARG.Core.Song;

namespace YARG.Tests.PlayMode
{
    public sealed partial class DifficultySelectDonePlayModeTests
    {
        private EliteConfigurationPointerPlayModeTests _participationTheme;

        [UnityTest]
        public IEnumerator MaestroParticipation_VocalsFirstStaysOutOnNextSong() =>
            ParticipationSurvivesNextSong(GameMode.Vocals, 0);

        [UnityTest]
        public IEnumerator MaestroParticipation_VocalsSecondStaysOutOnNextSong() =>
            ParticipationSurvivesNextSong(GameMode.Vocals, 1);

        [UnityTest]
        public IEnumerator MaestroParticipation_PartyVocalsFirstStaysOutOnNextSong() =>
            ParticipationSurvivesNextSong(GameMode.PartyVocals, 0);

        [UnityTest]
        public IEnumerator MaestroParticipation_PartyVocalsSecondStaysOutOnNextSong() =>
            ParticipationSurvivesNextSong(GameMode.PartyVocals, 1);

        [UnityTest]
        public IEnumerator MaestroParticipation_GuitarFirstStaysOutOnNextSong() =>
            ParticipationSurvivesNextSong(GameMode.FiveFretGuitar, 0);

        private IEnumerator PrepareParticipation()
        {
            _participationTheme = new EliteConfigurationPointerPlayModeTests();
            yield return _participationTheme.SetUpRealTheme();
#if UNITY_EDITOR
            Bootstrap(new Vector2Int(1280, 720));
#endif
        }

        private IEnumerator ParticipationSurvivesNextSong(GameMode mode, int index)
        {
            yield return PrepareParticipation();
            using var scope = new ParticipationScope(this, mode, index, maestroEnabled: true);
            var firstSong = scope.Begin();
            scope.Stage(firstSong, true);
            scope.Commit(firstSong);
            Assert.That(scope.Profile.MaestroSittingOut, Is.True,
                "The first song must have committed the explicit Sit Out preference.");

            // The live player has the participation state restored by the fresh-song
            // OnEnable loop. Exercise the real player-focus method that subsequently runs.
            scope.Focus();
            var nextSong = scope.Begin();
            var staged = scope.Staged(nextSong);
            Assert.That(Get(scope.Player, "SittingOut"), Is.True,
                "Focusing a player must not silently rejoin an explicitly sat-out profile.");
            Assert.That(Property(staged, "SittingOut"), Is.True,
                "The next Maestro session must retain the saved participation decision.");
            var expectedInstrument = mode is GameMode.Vocals or GameMode.PartyVocals
                ? Instrument.Vocals : Instrument.FiveFretGuitar;
            Assert.That(Property(staged, "Instrument"), Is.EqualTo(expectedInstrument),
                "Session defaults must not change a sitting-out player's staged chart.");
            scope.Commit(nextSong);
            Assert.That(scope.Profile.MaestroSittingOut, Is.True);
        }

        [UnityTest]
        public IEnumerator MaestroParticipation_ReadyExplicitlyRejoinsAndPersists()
        {
            yield return PrepareParticipation();
            foreach (var mode in new[] { GameMode.Vocals, GameMode.PartyVocals })
            {
                using var scope = new ParticipationScope(this, mode, 0, maestroEnabled: true);
                var firstSong = scope.Begin();
                scope.Stage(firstSong, true);
                scope.Commit(firstSong);
                scope.Focus();
                // Isolate the Ready callback from the independent focus regression.
                Set(scope.Player, "SittingOut", true);
                Call(_menu, "UpdateForPlayer");
                yield return Settle();

                ActivateLabel(Header("Ready"));

                Assert.That(Get(scope.Player, "SittingOut"), Is.False,
                    "Explicit Ready must reactivate the departing player, not merely focus the next one.");
                var session = scope.Begin();
                scope.Commit(session);
                Assert.That(scope.Profile.MaestroSittingOut, Is.False,
                    "Committing the joined player must clear the saved Sit Out preference.");
                var nextSong = scope.Begin();
                Assert.That(Property(scope.Staged(nextSong), "SittingOut"), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator MaestroParticipation_ActiveVocalProfilesStillDefaultToHarmony()
        {
            yield return PrepareParticipation();
            foreach (var mode in new[] { GameMode.Vocals, GameMode.PartyVocals })
            {
                using var scope = new ParticipationScope(this, mode, 0, maestroEnabled: true);
                scope.Focus();
                var session = scope.Begin();
                Assert.That(Property(scope.Staged(session), "SittingOut"), Is.False);
                Assert.That(Property(scope.Staged(session), "Instrument"), Is.EqualTo(Instrument.Harmony),
                    "An active profile should still prefer the available harmony chart over its prior Solo choice.");
            }
        }

        [UnityTest]
        public IEnumerator MaestroParticipation_DisabledMaestroRetainsOrdinaryFreshPlayerReset()
        {
            yield return PrepareParticipation();
            foreach (var mode in new[] { GameMode.Vocals, GameMode.PartyVocals })
            foreach (int index in new[] { 0, 1 })
            {
                using var scope = new ParticipationScope(this, mode, index, maestroEnabled: false);
                scope.Profile.MaestroSittingOut = true;
                Set(scope.Player, "SittingOut", true);

                scope.Focus();

                Assert.That(Get(scope.Player, "SittingOut"), Is.False,
                    "Ordinary Difficulty Select must still reset transient participation when Maestro is disabled.");
                Assert.That(scope.Profile.MaestroSittingOut, Is.True,
                    "Ordinary selection must not erase the independent saved Maestro preference.");
            }
        }

        private sealed class ParticipationScope : IDisposable
        {
            private readonly DifficultySelectDonePlayModeTests _owner;
            private readonly IList _livePlayers;
            private readonly object[] _previousPlayers;
            private readonly PropertyInfo _settings;
            private readonly object _previousSettings;
            private readonly FieldInfo _state;
            private readonly object _previousState;
            private readonly PropertyInfo _activeSession;
            private readonly object _previousSession;
            private readonly PropertyInfo _maestro;
            private readonly object _previousMaestro;
            private readonly Array _roster;
            private readonly int _index;
            private readonly ParticipationSong _song = new();
            private readonly List<SongEntry> _songs;

            public YargProfile Profile { get; }
            public object Player => _roster.GetValue(_index);

            public ParticipationScope(DifficultySelectDonePlayModeTests owner, GameMode mode,
                int index, bool maestroEnabled)
            {
                _owner = owner;
                _index = index;
                _songs = new List<SongEntry> { _song };
                _settings = Production("YARG.Settings.SettingsManager").GetProperty("Settings", STATIC);
                _previousSettings = _settings.GetValue(null);
                var settings = Activator.CreateInstance(_settings.PropertyType);
                var toggle = settings.GetType().GetProperty("MaestroEnable").GetValue(settings);
                Call(toggle, "SetValueWithoutNotify", maestroEnabled);
                _settings.GetSetMethod(true).Invoke(null, new[] { settings });

                _state = Production("YARG.GlobalVariables").GetField("State", STATIC);
                _previousState = _state.GetValue(null);
                var state = _state.GetValue(null);
                _state.FieldType.GetField("CurrentSong").SetValue(state, _song);
                _state.SetValue(null, state);
                var sessionType = Production("YARG.Menu.Maestro.MaestroSetupSession");
                _activeSession = sessionType.GetProperty("Active", STATIC);
                _previousSession = _activeSession.GetValue(null);
                _maestro = Production("YARG.Integration.Maestro.MaestroController").BaseType.GetProperty("Instance", STATIC);
                _previousMaestro = _maestro.GetValue(null);
                _maestro.GetSetMethod(true).Invoke(null, new object[] { null });

                _livePlayers = (IList)Production("YARG.Player.PlayerContainer").GetField("_players", STATIC).GetValue(null);
                _previousPlayers = _livePlayers.Cast<object>().ToArray();
                _livePlayers.Clear();
                var playerType = Production("YARG.Player.YargPlayer");
                _roster = Array.CreateInstance(playerType, 3);
                for (int i = 0; i < _roster.Length; i++)
                {
                    bool selected = i == index;
                    var playerMode = selected ? mode : GameMode.FiveFretGuitar;
                    var instrument = playerMode switch
                    {
                        GameMode.Vocals => Instrument.Vocals,
                        GameMode.PartyVocals => Instrument.PartyVocals,
                        _ => Instrument.FiveFretGuitar,
                    };
                    var profile = new YargProfile(Guid.NewGuid())
                    {
                        Name = "Participation fixture " + i,
                        GameMode = playerMode,
                        CurrentInstrument = instrument,
                        PreferredInstrument = instrument,
                        PartyVocalsChartPreference = PartyVocalsChartPreference.Solo,
                        CurrentDifficulty = Difficulty.Expert,
                        NoteSpeed = 5f,
                        HighwayLength = 1f,
                        IsBot = true,
                    };
                    if (selected) Profile = profile;
                    var bindings = Activator.CreateInstance(Production("YARG.Input.ProfileBindings"), new object[] { profile });
                    var player = Activator.CreateInstance(playerType, new[] { (object)profile, bindings });
                    owner._players.Add((IDisposable)player);
                    _roster.SetValue(player, i);
                    _livePlayers.Add(player);
                }
                Set(owner._menu, "_explicitPlayers", null);
                Set(owner._menu, "_songList", _songs);
            }

            public void Focus()
            {
                Set(_owner._menu, "_playerIndex", _index);
                Call(_owner._menu, "ChangePlayer", 0);
            }

            public object Begin() => _activeSession.DeclaringType.GetMethod("Begin")
                .Invoke(null, new object[] { _roster, _songs, _roster.Length, Guid.Empty });

            public void Stage(object session, bool sittingOut) => Call(session, "StageSittingOut", Profile.Id, sittingOut);

            public object Staged(object session) => ((IEnumerable)Property(session, "Players")).Cast<object>()
                .Single(staged => (Guid)Property(staged, "ProfileId") == Profile.Id);

            public void Commit(object session)
            {
                var result = Call(session, "TryCommit");
                Assert.That(Property(result, "Success"), Is.True,
                    "The private playable roster must commit: " + Property(result, "GlobalError"));
            }

            public void Dispose()
            {
                _activeSession.GetSetMethod(true).Invoke(null, new[] { _previousSession });
                _maestro.GetSetMethod(true).Invoke(null, new[] { _previousMaestro });
                _state.SetValue(null, _previousState);
                _settings.GetSetMethod(true).Invoke(null, new[] { _previousSettings });
                _livePlayers.Clear();
                foreach (var player in _previousPlayers) _livePlayers.Add(player);
            }
        }

        private sealed class ParticipationSong : SongEntry
        {
            public ParticipationSong()
            {
                _parts.FiveFretGuitar.ActivateDifficulty(Difficulty.Expert);
                _parts.LeadVocals.SubTracks = 1;
                _parts.HarmonyVocals.SubTracks = 7;
            }

            public override EntryType SubType => EntryType.Ini;
            public override string SortBasedLocation => "maestro-participation-fixture";
            public override string ActualLocation => "maestro-participation-fixture";
            public override DateTime GetLastWriteTime() => DateTime.UnixEpoch;
            public override SongChart LoadChart(IReadOnlyCollection<Instrument> eliteDrumsDownchartOutputs = null) =>
                throw new InvalidOperationException("Participation tests never load charts.");
            public override StemMixer LoadAudio(float speed, double volume, bool enableCensoring, params SongStem[] ignoreStems) =>
                throw new InvalidOperationException("Participation tests never open audio streams.");
            public override StemMixer LoadPreviewAudio(float speed, bool enableCensoring) =>
                throw new InvalidOperationException("Participation tests never open preview audio.");
            public override YARGImage LoadAlbumData() => null;
            public override BackgroundResult LoadBackground(bool censoringEnabled, bool excludeYarground = false) => null;
            public override FixedArray<byte> LoadMiloData() => null;
            public override FixedArray<byte> LoadVocData() => null;
        }
    }
}
