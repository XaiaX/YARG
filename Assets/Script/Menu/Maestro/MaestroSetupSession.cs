// pattern: Mixed (needs refactoring)

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YARG.Core;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.Song;
using YARG.Integration.Maestro;
using YARG.Player;
using YARG.Settings;
using YARG.Song;

namespace YARG.Menu.Maestro
{
    /// <summary>
    /// Default profile values used to compute deltas shown in the player row.
    /// </summary>
    public static class MaestroDefaults
    {
        public const float NoteSpeed = 5f;
        public const float HighwayLength = 1f;
        public const long InputCalibrationMilliseconds = 0;
    }

    /// <summary>
    /// The editable and displayable setup values for one active player. The object is
    /// intentionally independent of a live profile so opening Maestro and changing a
    /// control cannot partially mutate the profile before Continue.
    /// </summary>
    public sealed class MaestroStagedPlayer
    {
        public Guid ProfileId { get; }
        public YargPlayer Player { get; }
        public string Name => Player.Profile.Name;
        public bool IsBot => Player.Profile.IsBot;
        public bool SittingOut { get; internal set; }
        public bool IsMissingInput => Player.IsMissingInputDevice || Player.IsMissingMicrophone;

        public GameMode GameMode { get; internal set; }
        public Instrument Instrument { get; internal set; }
        public Instrument PreferredInstrument { get; internal set; }
        public Difficulty Difficulty { get; internal set; }
        public Modifier Modifiers { get; internal set; }
        public bool LeftyFlip { get; internal set; }
        public bool RangeEnabled { get; internal set; }
        public OpenLaneDisplayType OpenLaneDisplayType { get; internal set; }
        public float NoteSpeed { get; internal set; }
        public float HighwayLength { get; internal set; }
        public long InputCalibrationMilliseconds { get; internal set; }
        public byte HarmonyIndex { get; internal set; }

        /// <summary>
        /// The explicit experimental "Elite (To …)" downchart target captured
        /// from the profile, if any. While it still matches <see cref="Instrument"/>
        /// and a drum game mode, the staged instrument is pinned to the chosen
        /// output format: gameplay downcharts the Elite Drums chart instead of
        /// requiring the format's native chart in every song. An explicit restage
        /// of a different instrument or game mode clears it; the session also drops
        /// it at Begin and at commit when it is invalid for this session (toggle off,
        /// outside the valid target domain, or not playable for every show song —
        /// see ClearInvalidDownchartTargets and CanCommitDownchartTarget).
        /// </summary>
        public Instrument? EliteDrumsDownchartTarget { get; internal set; }

        /// <summary>
        /// The staged preferred instrument captured immediately before an explicit
        /// "Elite (To …)" target first pinned it, so clearing or superseding the
        /// target can restore the player's prior native preference instead of
        /// leaving it pinned to a dropped target format. Null when the target was
        /// inherited from the profile (the pre-target preference is no longer
        /// recoverable there — Difficulty Select may already have moved the
        /// profile's preference onto the target under its own policy) or when no
        /// target is staged.
        /// </summary>
        public Instrument? PreferredInstrumentBeforeDownchartTarget { get; internal set; }

        internal MaestroStagedPlayer(YargPlayer player)
        {
            Player = player;
            ProfileId = player.Profile.Id;
            SittingOut = player.SittingOut;
            GameMode = player.Profile.GameMode;
            Instrument = player.Profile.CurrentInstrument;
            // Party Vocals profiles store Instrument.PartyVocals, but the Maestro
            // dropdown uses {Vocals, Harmony}. Map based on PartyVocalsChartPreference,
            // which is how Difficulty Select tracks the Solo/Harmony choice.
            if (GameMode == GameMode.PartyVocals && Instrument == Instrument.PartyVocals)
                Instrument = player.Profile.PartyVocalsChartPreference == PartyVocalsChartPreference.Harmony
                    ? Instrument.Harmony
                    : Instrument.Vocals;
            PreferredInstrument = player.Profile.PreferredInstrument;
            if (GameMode == GameMode.PartyVocals && PreferredInstrument == Instrument.PartyVocals)
                PreferredInstrument = player.Profile.PartyVocalsChartPreference == PartyVocalsChartPreference.Harmony
                    ? Instrument.Harmony
                    : Instrument.Vocals;
            Difficulty = player.Profile.CurrentDifficulty;
            Modifiers = player.Profile.CurrentModifiers;
            LeftyFlip = player.Profile.LeftyFlip;
            RangeEnabled = player.Profile.RangeEnabled;
            OpenLaneDisplayType = player.Profile.OpenLaneDisplayType;
            NoteSpeed = player.Profile.NoteSpeed;
            HighwayLength = player.Profile.HighwayLength;
            InputCalibrationMilliseconds = player.Profile.InputCalibrationMilliseconds;
            HarmonyIndex = player.Profile.HarmonyIndex;
            // Difficulty Select pins CurrentInstrument to the target while an
            // "Elite (To …)" option is active, so Instrument above already
            // equals it; capture the target itself so staging, commit, and
            // rollback can reason about the explicit choice without touching
            // the live profile.
            EliteDrumsDownchartTarget = player.Profile.EliteDrumsDownchartTarget;
        }
    }

    public sealed class MaestroFinalizationResult
    {
        private MaestroFinalizationResult(bool success, string globalError,
            Dictionary<Guid, string> playerErrors)
        {
            Success = success;
            GlobalError = globalError;
            PlayerErrors = playerErrors;
        }

        public bool Success { get; }
        public string GlobalError { get; }
        public IReadOnlyDictionary<Guid, string> PlayerErrors { get; }

        public static MaestroFinalizationResult Applied() =>
            new(true, null, new Dictionary<Guid, string>());

        public static MaestroFinalizationResult Rejected(string globalError,
            Dictionary<Guid, string> playerErrors) =>
            new(false, globalError, playerErrors);
    }

    /// <summary>
    /// A page-local setup session. It captures the already-resolved Difficulty Select
    /// values, overlays remote drafts without consuming them, and commits only after
    /// every active player passes validation.
    /// </summary>
    public sealed class MaestroSetupSession
    {
        private readonly List<SongEntry> _songs;
        private readonly Dictionary<Guid, MaestroStagedPlayer> _players;

        public static MaestroSetupSession Active { get; private set; }
        public bool ReturningToDifficultySelect { get; private set; }
        public int CompletedPlayerBoundary { get; }
        public Guid VocalPrimaryProfileId { get; }
        public IReadOnlyCollection<MaestroStagedPlayer> Players => _players.Values;

        private MaestroSetupSession(IEnumerable<YargPlayer> players, IEnumerable<SongEntry> songs,
            int completedPlayerBoundary, Guid vocalPrimaryProfileId)
        {
            _songs = songs?.ToList() ?? throw new ArgumentNullException(nameof(songs));
            _players = players.ToDictionary(player => player.Profile.Id,
                player => new MaestroStagedPlayer(player));
            CompletedPlayerBoundary = completedPlayerBoundary;
            VocalPrimaryProfileId = vocalPrimaryProfileId;
        }

        public static MaestroSetupSession Begin(IEnumerable<YargPlayer> players,
            IEnumerable<SongEntry> songs, int completedPlayerBoundary = 0,
            Guid vocalPrimaryProfileId = default)
        {
            var session = new MaestroSetupSession(players, songs, completedPlayerBoundary,
                vocalPrimaryProfileId);
            var explicitInstruments = session.OverlayPendingDrafts();
            session.ApplyVocalHarmonyDefaults(explicitInstruments);
            // Invalid-target clearing runs after the draft overlay: the overlay
            // normalizes every player it can (preferred-instrument first) and
            // defers players whose staged target is already invalid, so the
            // target-exact native fallback applied here is the last word on the
            // affected players' staged state.
            session.ClearInvalidDownchartTargets();
            Active = session;
            return session;
        }

        /// <summary>
        /// Drops every staged "Elite (To …)" target that is not fully valid for this
        /// session, using the same complete condition as the commit
        /// (<see cref="CanCommitDownchartTarget"/>): the experimental toggle must be on,
        /// the target inside the valid domain, still matching the staged instrument and a
        /// supported drum game mode, and every show song playable for the target. This
        /// covers stale values that merely pass the domain check — e.g. a target captured
        /// before the instrument was changed elsewhere, or one pinned while the show's
        /// song list could still satisfy it. Dropping a target re-resolves the affected
        /// player instead of only nulling the target: the prior native preference
        /// captured at staging time is restored first (so clearing cannot destroy it —
        /// the preference must not stay pinned to a format the player is no longer
        /// playing), then dependent normalization re-runs with the dropped target as the
        /// exact native fallback. At Begin this runs after drafts are overlaid and
        /// selections normalized (so the target-exact re-resolution is the last word on
        /// the staged state), and it is re-run at the start of
        /// <see cref="TryCommit"/> because the toggle and staged state may have
        /// drifted since Begin — players whose targets remain fully valid are
        /// untouched.
        /// </summary>
        private void ClearInvalidDownchartTargets()
        {
            foreach (var staged in _players.Values)
            {
                if (!CanCommitDownchartTarget(staged))
                {
                    var dropped = staged.EliteDrumsDownchartTarget;
                    staged.EliteDrumsDownchartTarget = null;

                    // Clearing must not destroy the prior native preference: restore
                    // the one captured when the target was staged (when there is one)
                    // so the normalization below — and the commit — can restore an
                    // appropriate native fallback instead of staying pinned to the
                    // dropped target format.
                    RestorePreferredInstrumentBeforeDownchartTarget(staged);

                    // Re-run the dependent normalization for the affected player so
                    // Begin/commit judge their *effective* state: the pinned
                    // instrument falls back to the dropped target's own native
                    // format exactly — Elite (To 4-Lane) -> FourLaneDrums,
                    // (To Pro) -> ProDrums, (To 5-Lane) -> FiveLaneDrums — never a
                    // generic fallback that could switch drum formats, and a player
                    // whose mode has no native option for this show sits out
                    // instead of failing validation.
                    if (dropped is { } target)
                        NormalizeDependentSelections(staged, target);
                }
            }
        }

        /// <summary>
        /// Restores the staged preferred instrument captured when an explicit
        /// "Elite (To …)" target was first staged, so superseding or dropping the
        /// target cannot destroy the player's prior native preference. No-op when
        /// the target was inherited from the profile (no capture exists) or when
        /// the preference was never moved onto the target.
        /// </summary>
        private static void RestorePreferredInstrumentBeforeDownchartTarget(
            MaestroStagedPlayer player)
        {
            if (player.PreferredInstrumentBeforeDownchartTarget is { } priorPreferred)
                player.PreferredInstrument = priorPreferred;
            player.PreferredInstrumentBeforeDownchartTarget = null;
        }

        // The experimental downchart toggle, read null-safely: before settings load (or
        // in edit mode) the settings container can be null, and "off" is the correct
        // conservative answer there — identical to how live loading refuses downcharts
        // unless it can confirm the toggle is on.
        private static bool EliteDrumsDownchartsEnabled =>
            SettingsManager.Settings?.EnableEliteDrumsDowncharts.Value ?? false;

        public void MarkReturningToDifficultySelect() => ReturningToDifficultySelect = true;

        public void ClearReturningToDifficultySelect() => ReturningToDifficultySelect = false;

        public static void ClearActive()
        {
            Active = null;
        }

        public bool TryGetPlayer(Guid profileId, out MaestroStagedPlayer player) =>
            _players.TryGetValue(profileId, out player);

        /// <summary>
        /// The player's staged "Elite (To …)" target while it is fully active for
        /// this session (<see cref="HasActiveDownchartTarget"/>): toggle on, valid
        /// domain, still pinning the staged instrument, a drum game mode, and every
        /// show song playable. The instrument control and summary row display
        /// through this so a stale-but-well-formed staged target renders as its
        /// native instrument instead — matching how gameplay and Difficulty Select
        /// resolve it.
        /// </summary>
        public bool TryGetActiveEliteDrumsDownchartTarget(Guid profileId, out Instrument target)
        {
            target = default;
            return _players.TryGetValue(profileId, out var player) &&
                HasActiveDownchartTarget(player, out target);
        }

        public IReadOnlyList<GameMode> GetAvailableGameModes()
        {
            return EnumExtensions<GameMode>.Values
                .Where(IsModeAvailable)
                .ToArray();
        }

        public IReadOnlyList<Instrument> GetAvailableInstruments(Guid profileId)
        {
            if (!_players.TryGetValue(profileId, out var player) || _songs.Count == 0)
                return Array.Empty<Instrument>();

            try
            {
                var available = GetNativeAvailableInstruments(player);

                // Keep the pinned downchart output offered while an "Elite (To …)"
                // target is active so the editor can display the current selection.
                // PossibleInstrumentsForSong narrows the Elite Drums mode per song,
                // which can exclude the pinned format even though the downchart
                // remains playable.
                if (HasActiveDownchartTarget(player, out var target) &&
                    !available.Contains(target) &&
                    IsInstrumentAvailable(player, player.GameMode, target))
                {
                    available.Add(target);
                }

                return available;
            }
            catch (NotImplementedException)
            {
                return Array.Empty<Instrument>();
            }
        }

        /// <summary>
        /// The natively playable instruments for the player's staged game mode —
        /// the Maestro counterpart of Difficulty Select's <c>_possibleInstruments</c>.
        /// This public view intentionally excludes a pinned downchart target that was
        /// appended by <see cref="GetAvailableInstruments"/> for display. It is used
        /// when building native-vs-target dropdown rows so a downchart-only show does
        /// not present the target's output format as a misleading native choice.
        /// </summary>
        public IReadOnlyList<Instrument> GetAvailableNativeInstruments(Guid profileId)
        {
            if (!_players.TryGetValue(profileId, out var player) || _songs.Count == 0)
                return Array.Empty<Instrument>();

            return GetNativeAvailableInstruments(player);
        }

        private List<Instrument> GetNativeAvailableInstruments(MaestroStagedPlayer player)
        {
            try
            {
                return GetPossibleInstruments(player.GameMode)
                    .Where(instrument => IsNativeInstrumentAvailable(player, player.GameMode,
                        instrument))
                    .ToList();
            }
            catch (NotImplementedException)
            {
                return new List<Instrument>();
            }
        }

        public IReadOnlyList<Difficulty> GetAvailableDifficulties(Guid profileId)
        {
            if (!_players.TryGetValue(profileId, out var player))
                return Array.Empty<Difficulty>();

            if (player.SittingOut)
                return Array.Empty<Difficulty>();

            return EnumExtensions<Difficulty>.Values
                .Where(difficulty => IsDifficultyAvailableForPlayer(player, difficulty))
                .ToArray();
        }

        /// <summary>
        /// The explicit "Elite (To …)" downchart targets offered in this player's
        /// instrument control, mirroring Difficulty Select's row offering: MIDI
        /// e-kit (Elite Drums) profiles choose any of the three output formats,
        /// while four-lane/pro/five-lane profiles get exactly the one matching
        /// their staged native drum format. Each candidate must also satisfy the
        /// shared session playability predicate for the whole show
        /// (<see cref="EliteDrumsDownchartRules.IsSongPlayableForTarget"/>). Empty
        /// when the experimental toggle is off, the player is not in a drum mode
        /// that supports downchart outputs, or no candidate is playable.
        /// </summary>
        public IReadOnlyList<Instrument> GetAvailableEliteDrumsDownchartTargets(Guid profileId)
        {
            if (!_players.TryGetValue(profileId, out var player) || _songs.Count == 0 ||
                !EliteDrumsDownchartsEnabled)
            {
                return Array.Empty<Instrument>();
            }

            if (player.GameMode == GameMode.EliteDrums)
            {
                return new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums }
                    .Where(target => _songs.All(song =>
                        EliteDrumsDownchartRules.IsSongPlayableForTarget(song, target)))
                    .ToArray();
            }

            // While an explicit target is active the staged instrument is pinned to
            // it — exactly how Difficulty Select resolves CurrentInstrument before
            // offering — so this stays the single applicable row either way.
            if (player.GameMode is GameMode.FourLaneDrums or GameMode.FiveLaneDrums &&
                EliteDrumsDownchartRules.IsValidTarget(player.Instrument) &&
                _songs.All(song => EliteDrumsDownchartRules.IsSongPlayableForTarget(song, player.Instrument)))
            {
                return new[] { player.Instrument };
            }

            return Array.Empty<Instrument>();
        }

        public IReadOnlyList<Modifier> GetAvailableModifiers(Guid profileId) =>
            GetAvailableModifiers(profileId, false);

        public IReadOnlyList<Modifier> GetAvailableAccessibilityModifiers(Guid profileId) =>
            GetAvailableModifiers(profileId, true)
                .Where(modifier => MaestroSelectionRules.IsAccessibilityModifier(modifier) &&
                    (!(_players[profileId].GameMode is GameMode.Vocals or GameMode.PartyVocals) ||
                     modifier is not Modifier.UnpitchedOnly and not Modifier.UnpitchedHarm2
                         and not Modifier.UnpitchedHarm3))
                .ToArray();

        public IReadOnlyList<Modifier> GetAvailableModifiers(Guid profileId,
            bool includeAccessibility)
        {
            if (!_players.TryGetValue(profileId, out var player))
                return Array.Empty<Modifier>();

            if (player.SittingOut)
                return Array.Empty<Modifier>();

            try
            {
                var (possible, _) = player.GameMode.PossibleModifiers(player.Instrument);
                return EnumExtensions<Modifier>.Values
                    .Where(modifier => modifier != Modifier.None && (possible & modifier) != 0 &&
                        (includeAccessibility || !MaestroSelectionRules.IsAccessibilityModifier(modifier)))
                    .ToArray();
            }
            catch (NotImplementedException)
            {
                return Array.Empty<Modifier>();
            }
        }

        public void StageGameMode(Guid profileId, GameMode gameMode)
        {
            if (!_players.TryGetValue(profileId, out var player) || !IsModeAvailable(gameMode))
                return;

            // Staging a different game mode is an explicit selection: an "Elite
            // (To …)" downchart target belonged to the previously staged mode,
            // so the choice is superseded instead of carried onto the new mode.
            // Re-selecting the current mode changes nothing and keeps it.
            if (gameMode != player.GameMode)
            {
                player.EliteDrumsDownchartTarget = null;
                RestorePreferredInstrumentBeforeDownchartTarget(player);
            }

            player.GameMode = gameMode;
            NormalizeDependentSelections(player);
        }

        public void StageInstrument(Guid profileId, Instrument instrument)
        {
            if (!_players.TryGetValue(profileId, out var player) ||
                !IsInstrumentAvailable(player, player.GameMode, instrument))
                return;

            // An explicit native instrument selection supersedes any "Elite (To …)"
            // downchart target — including one pinned to this same instrument. The
            // instrument control lists explicit target rows separately from native
            // instruments, so choosing the native chart of the pinned format is how
            // the player steps back from a downchart; a target can no longer be
            // re-selected implicitly. Mirrors Difficulty Select's native rows.
            var priorPreferred = player.PreferredInstrument;
            player.EliteDrumsDownchartTarget = null;
            player.PreferredInstrumentBeforeDownchartTarget = null;

            player.Instrument = instrument;

            // Mirror Difficulty Select's native rows: the preferred instrument
            // only follows an explicit native selection when the prior preference
            // was itself an available native option, so a player who was forced
            // onto a fallback never has their real preference overwritten.
            if (instrument != priorPreferred &&
                GetNativeAvailableInstruments(player).Contains(priorPreferred))
            {
                player.PreferredInstrument = instrument;
            }

            NormalizeDependentSelections(player);
        }

        /// <summary>
        /// Stages an explicit "Elite (To …)" downchart target: the chosen output
        /// format is stored AND the staged instrument is pinned to it, so the
        /// engine mode, highway, and track lookup all agree on the output
        /// format — the same consistency Difficulty Select establishes by
        /// pinning CurrentInstrument, and that the commit re-checks through
        /// <see cref="CanCommitDownchartTarget"/>. Only targets currently offered
        /// (<see cref="GetAvailableEliteDrumsDownchartTargets"/>) can be staged;
        /// normalization runs afterwards like any other staging call. The preferred
        /// instrument follows Difficulty Select's exact policy: it moves onto the
        /// target only when the prior preference was itself an available native
        /// option for this show (an active switch away from a real choice) and is
        /// otherwise preserved, and the prior value is captured first so a later
        /// invalidation can restore it rather than leave the preference pinned to
        /// the target format.
        /// </summary>
        public void StageEliteDrumsDownchartTarget(Guid profileId, Instrument target)
        {
            if (!_players.TryGetValue(profileId, out var player) ||
                !GetAvailableEliteDrumsDownchartTargets(profileId).Contains(target))
                return;

            // Capture the prior native preference at the moment the target first
            // pins the staged state (a restage keeps the original capture), and
            // evaluate the policy against native availability only — the same
            // "_possibleInstruments.Contains(preferred)" check Difficulty Select
            // applies, never counting the pinned target itself as a native option.
            var priorPreferred = player.PreferredInstrument;
            if (player.EliteDrumsDownchartTarget is null)
                player.PreferredInstrumentBeforeDownchartTarget = priorPreferred;

            player.EliteDrumsDownchartTarget = target;
            player.Instrument = target;

            if (target != priorPreferred &&
                GetNativeAvailableInstruments(player).Contains(priorPreferred))
            {
                player.PreferredInstrument = target;
            }

            NormalizeDependentSelections(player);
        }

        public void StageSittingOut(Guid profileId, bool sittingOut)
        {
            if (_players.TryGetValue(profileId, out var player))
                player.SittingOut = sittingOut;
        }

        public void StageDifficulty(Guid profileId, Difficulty difficulty)
        {
            if (_players.TryGetValue(profileId, out var player) &&
                IsDifficultyAvailableForPlayer(player, difficulty))
                player.Difficulty = difficulty;
        }

        public void StageModifiers(Guid profileId, Modifier modifiers)
        {
            if (_players.TryGetValue(profileId, out var player))
            {
                player.Modifiers = modifiers;
                NormalizeModifiers(player);
            }
        }

        public void StageModifier(Guid profileId, Modifier modifier, bool enabled)
        {
            if (!_players.TryGetValue(profileId, out var player) ||
                !GetAvailableModifiers(profileId, true).Contains(modifier))
                return;

            player.Modifiers = MaestroSelectionRules.ToggleModifier(
                player.Modifiers, modifier, enabled);
            NormalizeModifiers(player);
        }

        public void StageLeftyFlip(Guid profileId, bool enabled)
        {
            if (_players.TryGetValue(profileId, out var player) &&
                MaestroSelectionRules.SupportsLeftyFlip(player.GameMode))
            {
                player.LeftyFlip = enabled;
            }
        }

        public void StageRangeEnabled(Guid profileId, bool enabled)
        {
            if (_players.TryGetValue(profileId, out var player) &&
                MaestroSelectionRules.SupportsRangeShifts(player.GameMode))
            {
                player.RangeEnabled = enabled;
            }
        }

        public void StageOpenLaneDisplayType(Guid profileId, OpenLaneDisplayType displayType)
        {
            if (_players.TryGetValue(profileId, out var player) &&
                player.GameMode == GameMode.ProKeys)
            {
                player.OpenLaneDisplayType = displayType;
            }
        }

        public void StageNoteSpeed(Guid profileId, float speed)
        {
            if (_players.TryGetValue(profileId, out var player))
                player.NoteSpeed = Mathf.Clamp(speed, 0f, 100f);
        }

        public void StageHighwayLength(Guid profileId, float length)
        {
            if (_players.TryGetValue(profileId, out var player))
                player.HighwayLength = Mathf.Clamp(length, 0.1f, 10f);
        }

        public void StageInputCalibration(Guid profileId, long calibration)
        {
            if (_players.TryGetValue(profileId, out var player))
                player.InputCalibrationMilliseconds = calibration;
        }

        private sealed class ProfileSnapshot
        {
            public readonly YargProfile Profile;
            public readonly YargPlayer Player;
            public readonly GameMode GameMode;
            public readonly Instrument PreferredInstrument;
            public readonly Instrument CurrentInstrument;
            public readonly Difficulty DifficultyFallback;
            public readonly Difficulty CurrentDifficulty;
            public readonly float NoteSpeed;
            public readonly float HighwayLength;
            public readonly long InputCalibrationMilliseconds;
            public readonly byte EffectiveHarmonyIndex;
            public readonly byte HarmonyIndexFallback;
            public readonly Modifier CurrentModifiers;
            public readonly bool LeftyFlip;
            public readonly bool RangeEnabled;
            public readonly OpenLaneDisplayType OpenLaneDisplayType;
            public readonly bool SittingOut;
            public readonly Instrument? EliteDrumsDownchartTarget;

            public ProfileSnapshot(YargPlayer player)
            {
                Player = player;
                var profile = player.Profile;
                Profile = profile;
                GameMode = profile.GameMode;
                PreferredInstrument = profile.PreferredInstrument;
                CurrentInstrument = profile.CurrentInstrument;
                DifficultyFallback = profile.DifficultyFallback;
                CurrentDifficulty = profile.CurrentDifficulty;
                NoteSpeed = profile.NoteSpeed;
                HighwayLength = profile.HighwayLength;
                InputCalibrationMilliseconds = profile.InputCalibrationMilliseconds;
                EffectiveHarmonyIndex = profile.EffectiveHarmonyIndex;
                HarmonyIndexFallback = profile.HarmonyIndexFallback;
                CurrentModifiers = profile.CurrentModifiers;
                LeftyFlip = profile.LeftyFlip;
                RangeEnabled = profile.RangeEnabled;
                OpenLaneDisplayType = profile.OpenLaneDisplayType;
                SittingOut = player.SittingOut;
                EliteDrumsDownchartTarget = profile.EliteDrumsDownchartTarget;
            }

            public void Restore()
            {
                Profile.GameMode = GameMode;
                Profile.PreferredInstrument = PreferredInstrument;
                Profile.CurrentInstrument = CurrentInstrument;
                Profile.DifficultyFallback = DifficultyFallback;
                Profile.CurrentDifficulty = CurrentDifficulty;
                Profile.NoteSpeed = NoteSpeed;
                Profile.HighwayLength = HighwayLength;
                Profile.InputCalibrationMilliseconds = InputCalibrationMilliseconds;
                Profile.RestoreHarmonyIndexState(EffectiveHarmonyIndex, HarmonyIndexFallback);
                Profile.RestoreSessionModifiers(CurrentModifiers);
                Profile.LeftyFlip = LeftyFlip;
                Profile.RangeEnabled = RangeEnabled;
                Profile.OpenLaneDisplayType = OpenLaneDisplayType;
                Profile.EliteDrumsDownchartTarget = EliteDrumsDownchartTarget;
                Player.SittingOut = SittingOut;
            }
        }

        public MaestroFinalizationResult TryCommit()
        {
            // Re-run the same cleanup Begin performed before validating: the experimental
            // toggle and the staged selections may have drifted since the session opened,
            // and Validate must judge every player against their *effective* state.
            // The cleanup re-resolves each affected player — restoring the prior native
            // preference and falling the instrument back to the dropped target's exact
            // native format (or sitting the player out when the mode has no native
            // option) — so a toggle flip can no longer strand a pinned instrument that
            // Validate would reject. This only touches staged values — the rollback
            // snapshots below are taken after it, and profile restoration on failure is
            // unaffected.
            ClearInvalidDownchartTargets();

            var errors = Validate(out string globalError);
            if (globalError != null || errors.Count > 0)
                return MaestroFinalizationResult.Rejected(globalError, errors);

            var snapshots = _players.Values
                .Select(player => new ProfileSnapshot(player.Player))
                .ToList();

            try
            {
                // No profile is mutated until the complete active-player set has passed.
                foreach (var staged in _players.Values)
                {
                    staged.Player.SittingOut = staged.SittingOut;
                    staged.Player.Profile.MaestroSittingOut = staged.SittingOut;
                    if (staged.SittingOut)
                        continue;

                    var profile = staged.Player.Profile;
                    profile.GameMode = staged.GameMode;
                    profile.PreferredInstrument = staged.PreferredInstrument;
                    profile.CurrentInstrument = staged.Instrument;

                    // Preserve the explicit "Elite (To …)" downchart target through
                    // the commit only while it is fully valid for this session — the
                    // same validity Difficulty Select enforces (toggle on, valid target
                    // domain, matching instrument and drum mode, and every show song
                    // playable for the target). Staging a different instrument or game
                    // mode already cleared the staged value, so a still-valid choice is
                    // never dropped just because a show song lacks the target format's
                    // native chart; conversely an invalid one is never silently kept
                    // while live loading would refuse its downchart.
                    profile.EliteDrumsDownchartTarget =
                        CanCommitDownchartTarget(staged) ? staged.EliteDrumsDownchartTarget : null;
                    // Restore PartyVocals instrument and HarmonyIndex — the Maestro
                    // dropdown uses Vocals/Harmony but the profile must store
                    // PartyVocals + correct HarmonyIndex for this game mode.
                    if (staged.GameMode == GameMode.PartyVocals)
                    {
                        profile.CurrentInstrument = Instrument.PartyVocals;
                        profile.PreferredInstrument = Instrument.PartyVocals;
                        profile.PartyVocalsChartPreference = staged.Instrument == Instrument.Harmony
                            ? PartyVocalsChartPreference.Harmony
                            : PartyVocalsChartPreference.Solo;
                        profile.HarmonyIndex = staged.HarmonyIndex;
                        profile.ResolveHarmonyIndex(MaxHarmonyParts());
                    }
                    profile.DifficultyFallback = staged.Difficulty;
                    profile.CurrentDifficulty = staged.Difficulty;
                    profile.LeftyFlip = staged.LeftyFlip;
                    profile.RangeEnabled = staged.RangeEnabled;
                    profile.OpenLaneDisplayType = staged.OpenLaneDisplayType;
                    if (staged.GameMode is not GameMode.Vocals and not GameMode.PartyVocals)
                    {
                        profile.NoteSpeed = staged.NoteSpeed;
                        profile.HighwayLength = staged.HighwayLength;
                    }
                    profile.InputCalibrationMilliseconds = staged.InputCalibrationMilliseconds;
                    if (staged.Instrument is Instrument.Harmony or Instrument.PartyVocals)
                    {
                        profile.HarmonyIndex = staged.HarmonyIndex;
                        profile.ResolveHarmonyIndex(MaxHarmonyParts());
                    }

                    var modifierProfile = new YargProfile
                    {
                        GameMode = staged.GameMode,
                        CurrentInstrument = staged.Instrument,
                    };
                    foreach (var modifier in EnumExtensions<Modifier>.Values)
                    {
                        if (modifier != Modifier.None && (staged.Modifiers & modifier) != 0)
                            modifierProfile.AddSingleModifier(modifier);
                    }
                    profile.ApplySessionModifiers(modifierProfile);
                }

                // Vocal modifiers are synchronized after the primary player's staged value
                // is known, using the stable profile ID captured by Difficulty Select.
                if (VocalPrimaryProfileId != default &&
                    _players.TryGetValue(VocalPrimaryProfileId, out var primary) &&
                    !primary.SittingOut && IsVocal(primary.GameMode))
                {
                    foreach (var staged in _players.Values)
                    {
                        if (staged.SittingOut || staged.ProfileId == primary.ProfileId ||
                            !IsVocal(staged.GameMode))
                            continue;
                        staged.Player.Profile.ApplySessionModifiers(primary.Player.Profile);
                    }
                }
            }
            catch (Exception exception)
            {
                foreach (var snapshot in snapshots)
                    snapshot.Restore();

                return MaestroFinalizationResult.Rejected(
                    $"Could not apply the complete setup: {exception.Message}",
                    new Dictionary<Guid, string>());
            }

            // Drafts are acknowledged only after the complete commit succeeded. This
            // includes note speed, highway length, and harmony fields that have no MVP
            // editor control but remain part of the remote contract.
            var maestro = MaestroController.Instance;
            if (maestro != null)
            {
                foreach (var staged in _players.Values)
                {
                    if (maestro.TryGetPendingDraft(staged.ProfileId, out var draft))
                        draft.MarkApplied();
                }
                maestro.MarkPendingApplied();
            }

            return MaestroFinalizationResult.Applied();
        }

        /// <summary>
        /// The complete validity condition for a staged "Elite (To …)" downchart target,
        /// shared by the Begin cleanup, per-player availability checks
        /// (<see cref="HasActiveDownchartTarget"/>), and the commit itself: the
        /// experimental toggle must be on (consistent with Difficulty Select and live
        /// chart loading, which build no downcharts for live players while it is off),
        /// the target must be inside the valid domain and still match the staged
        /// instrument and a drum game mode, and every show song must satisfy the shared
        /// playability predicate — preserving a target that is invalid for the selected
        /// session would pin gameplay to a track some songs cannot provide.
        /// </summary>
        private bool CanCommitDownchartTarget(MaestroStagedPlayer staged)
        {
            return EliteDrumsDownchartsEnabled &&
                staged.EliteDrumsDownchartTarget is { } target &&
                EliteDrumsDownchartRules.IsValidTarget(target) &&
                target == staged.Instrument &&
                staged.GameMode is GameMode.FourLaneDrums or GameMode.FiveLaneDrums
                    or GameMode.EliteDrums &&
                _songs.All(song => EliteDrumsDownchartRules.IsSongPlayableForTarget(song, target));
        }

        private Dictionary<Guid, string> Validate(out string globalError)
        {
            globalError = null;
            var errors = new Dictionary<Guid, string>();
            if (_songs.Count == 0)
            {
                globalError = "No song is selected.";
                return errors;
            }

            if (_players.Count == 0 || _players.Values.All(player => player.SittingOut))
            {
                globalError = "Nobody's Playing!";
                return errors;
            }

            Instrument? vocalInstrument = null;
            foreach (var staged in _players.Values)
            {
                if (staged.SittingOut)
                    continue;

                if (!IsModeAvailableForPlayer(staged))
                {
                    errors[staged.ProfileId] = $"{staged.GameMode} is not available for this show.";
                    continue;
                }

                if (!IsInstrumentAvailable(staged, staged.GameMode, staged.Instrument))
                {
                    errors[staged.ProfileId] = $"{staged.Instrument} is not playable for this show.";
                    continue;
                }

                if (IsVocal(staged.GameMode) &&
                    (staged.Instrument is Instrument.Vocals or Instrument.Harmony))
                {
                    vocalInstrument ??= staged.Instrument;
                    if (vocalInstrument != staged.Instrument)
                    {
                        errors[staged.ProfileId] = "All vocal players must use the same vocal chart.";
                        continue;
                    }
                }

                if (!IsDifficultyAvailableForPlayer(staged, staged.Difficulty))
                {
                    errors[staged.ProfileId] = $"{staged.Difficulty} is not available for this show.";
                    continue;
                }

                if (!AreModifiersValid(staged.GameMode, staged.Instrument, staged.Modifiers))
                    errors[staged.ProfileId] = "The selected modifiers are unavailable or conflict.";
            }

            return errors;
        }

        private HashSet<Guid> OverlayPendingDrafts()
        {
            var maestro = MaestroController.Instance;
            var explicitInstruments = new HashSet<Guid>();
            if (maestro != null)
            {
                foreach (var staged in _players.Values)
                {
                    if (!maestro.TryGetPendingDraft(staged.ProfileId, out var draft))
                        continue;

                    var priorGameMode = staged.GameMode;
                    var priorInstrument = staged.Instrument;

                    if (draft.PendingGameMode.HasValue)
                        staged.GameMode = draft.PendingGameMode.Value;
                    if (draft.PendingInstrument.HasValue)
                    {
                        explicitInstruments.Add(staged.ProfileId);
                        staged.Instrument = draft.PendingInstrument.Value;
                        staged.PreferredInstrument = staged.Instrument;
                        // Remote drafts may carry Instrument.PartyVocals for
                        // Party Vocals players. Map it to the dropdown's
                        // {Vocals, Harmony} representation so normalization
                        // does not reset it to Vocals (losing Harmony).
                        if (staged.GameMode == GameMode.PartyVocals &&
                            staged.Instrument == Instrument.PartyVocals)
                        {
                            // Map based on chart preference (same logic as the
                            // constructor) or the draft's harmony index.
                            bool preferHarmony =
                                staged.Player.Profile.PartyVocalsChartPreference == PartyVocalsChartPreference.Harmony;
                            staged.Instrument = preferHarmony
                                ? Instrument.Harmony
                                : Instrument.Vocals;
                        }
                        staged.PreferredInstrument = staged.Instrument;
                    }
                    if (draft.PendingDifficulty.HasValue)
                        staged.Difficulty = draft.PendingDifficulty.Value;
                    if (draft.PendingModifiers.HasValue)
                        staged.Modifiers = draft.PendingModifiers.Value;
                    if (draft.PendingNoteSpeed.HasValue)
                        staged.NoteSpeed = draft.PendingNoteSpeed.Value;
                    if (draft.PendingHighwayLength.HasValue)
                        staged.HighwayLength = draft.PendingHighwayLength.Value;
                    if (draft.PendingHarmonyIndex.HasValue)
                        staged.HarmonyIndex = draft.PendingHarmonyIndex.Value;

                    // A remote draft that explicitly selects a different game
                    // mode or instrument supersedes an "Elite (To …)" downchart
                    // target, exactly like the menu's own dropdowns. Re-sending
                    // the current values — or only speed/length/harmony fields —
                    // keeps the explicit target intact. The draft sets the
                    // preferred instrument itself above, so only the capture is
                    // dropped here — there is nothing older to restore over the
                    // draft's explicit choice.
                    if ((draft.PendingGameMode.HasValue && staged.GameMode != priorGameMode) ||
                        (draft.PendingInstrument.HasValue && staged.Instrument != priorInstrument))
                    {
                        staged.EliteDrumsDownchartTarget = null;
                        staged.PreferredInstrumentBeforeDownchartTarget = null;
                    }
                }
            }

            foreach (var staged in _players.Values)
            {
                // A staged target that is already invalid (e.g. the toggle is off)
                // is re-resolved target-exact by ClearInvalidDownchartTargets after
                // this overlay; normalizing it here first would flip the pinned
                // instrument to the preferred one and lose that exact fallback.
                if (staged.EliteDrumsDownchartTarget is not null &&
                    !CanCommitDownchartTarget(staged))
                {
                    continue;
                }

                NormalizeDependentSelections(staged);
            }

            return explicitInstruments;
        }

        /// <summary>
        /// Vocal players open on the harmony chart whenever the show offers one,
        /// regardless of what the previous song resolved to (solo-only songs still
        /// open on Solo as before). An explicit choice — this menu's dropdown or a
        /// remote draft — still selects Solo for the current show; it simply does
        /// not become the default for the next one.
        /// </summary>
        private void ApplyVocalHarmonyDefaults(HashSet<Guid> explicitInstruments)
        {
            foreach (var staged in _players.Values)
            {
                if (staged.SittingOut || explicitInstruments.Contains(staged.ProfileId) ||
                    !IsVocal(staged.GameMode))
                    continue;

                // Availability is evaluated live so later vocal players stay
                // locked to the first player's (harmony) chart.
                if (GetAvailableInstruments(staged.ProfileId).Contains(Instrument.Harmony))
                {
                    staged.Instrument = Instrument.Harmony;
                    NormalizeModifiers(staged);
                }
            }
        }

        private void NormalizeDependentSelections(MaestroStagedPlayer player,
            Instrument? droppedDownchartTarget = null)
        {
            if (!IsModeAvailableForPlayer(player))
            {
                // An existing profile's game mode is also its binding contract. Do
                // not silently replace it with the first mode this song supports;
                // keep the profile unchanged and stage the player as sitting out.
                player.SittingOut = true;
                return;
            }

            // While an explicit "Elite (To …)" downchart target is active the
            // staged instrument is pinned to the chosen output format, exactly
            // like Difficulty Select pins CurrentInstrument: the downchart does
            // not need the format's native chart in every song, so availability-
            // based reselection here would silently replace — and later clear —
            // the user's explicit choice.
            if (!HasActiveDownchartTarget(player, out _))
            {
                var instruments = GetAvailableInstruments(player.ProfileId);
                if (instruments.Count > 0)
                {
                    if (droppedDownchartTarget is { } dropped && dropped == player.Instrument &&
                        EliteDrumsDownchartRules.IsValidTarget(dropped) &&
                        instruments.Contains(dropped))
                    {
                        // Target-exact native fallback: a dropped "Elite (To …)"
                        // target resolves to that same format's own native chart
                        // whenever it is playable for the show — Elite (To 4-Lane)
                        // -> FourLaneDrums, (To Pro) -> ProDrums, (To 5-Lane) ->
                        // FiveLaneDrums — never the generic preferred/chain
                        // resolution, which could switch the player to a
                        // different drum format they did not choose. The
                        // pinned-instrument guard keeps a malformed or
                        // inconsistent staged target from forcing a format the
                        // player was never on.
                        player.Instrument = dropped;
                    }
                    else
                    {
                        // Revert to the user's preferred instrument when it's available,
                        // or follow the game-mode fallback chain when it isn't.
                        if (instruments.Contains(player.PreferredInstrument))
                            player.Instrument = player.PreferredInstrument;
                        else
                            player.Instrument = SelectInstrumentFallback(
                                player.PreferredInstrument, player.GameMode, instruments);
                    }
                }
            }

            var difficulties = GetAvailableDifficulties(player.ProfileId);
            if (!difficulties.Contains(player.Difficulty) && difficulties.Count > 0)
                player.Difficulty = MaestroSelectionRules.SelectDifficultyFallback(
                    player.Difficulty, difficulties);

            NormalizeModifiers(player);
        }

        private static void NormalizeModifiers(MaestroStagedPlayer player)
        {
            try
            {
                var (possible, excusable) = player.GameMode.PossibleModifiers(player.Instrument);
                player.Modifiers &= possible | excusable;
            }
            catch (NotImplementedException)
            {
                player.Modifiers = Modifier.None;
            }
        }

        private bool IsModeAvailable(GameMode mode)
        {
            if (_songs.Count == 0)
                return false;

            try
            {
                return GetPossibleInstruments(mode)
                    .Any(instrument => _songs.All(song => HasPlayableInstrument(song, instrument)));
            }
            catch (NotImplementedException)
            {
                return false;
            }
        }

        /// <summary>
        /// True while the staged player carries an explicit "Elite (To …)" downchart
        /// target that is fully valid for this session — the same complete condition the
        /// commit applies (<see cref="CanCommitDownchartTarget"/>): toggle on, inside the
        /// valid target domain (only 4-lane/Pro/5-lane — see EliteDrumsDownchartRules),
        /// still matching the staged instrument and a drum game mode, AND every show song
        /// playable for the target. Begin clears targets that fail this, so anything still
        /// staged as active here can actually be honored. While active, the staged
        /// instrument is the chosen downchart output format, so song availability must be
        /// judged against the Elite Drums chart (with per-song native fallback), not
        /// against the output format's native chart.
        /// </summary>
        private bool HasActiveDownchartTarget(MaestroStagedPlayer player,
            out Instrument target)
        {
            target = default;
            if (!CanCommitDownchartTarget(player))
            {
                return false;
            }

            target = player.EliteDrumsDownchartTarget!.Value;
            return true;
        }

        private bool IsModeAvailableForPlayer(MaestroStagedPlayer player)
        {
            if (IsModeAvailable(player.GameMode))
                return true;

            // A downchart target keeps the drum mode playable for this player even when
            // no native drum chart satisfies the generic mode check. HasActiveDownchart
            // Target applies the full session condition, including that every show song
            // is playable for the target.
            return HasActiveDownchartTarget(player, out _);
        }

        private bool IsDifficultyAvailableForPlayer(MaestroStagedPlayer player,
            Difficulty difficulty)
        {
            if (HasActiveDownchartTarget(player, out var target))
                return _songs.All(song => HasPlayableDownchartDifficulty(song, target, difficulty));

            return _songs.All(song => HasPlayableDifficulty(song, player.Instrument, difficulty));
        }

        private bool IsInstrumentAvailable(MaestroStagedPlayer target, GameMode mode,
            Instrument instrument)
        {
            // While an "Elite (To …)" target pins this player's instrument, the
            // pinned format stays playable whenever every show song has a usable
            // Elite Drums downchart or a native chart to fall back to — it must not
            // require the format's own native chart in every song. The all-songs
            // playability is part of HasActiveDownchartTarget.
            if (HasActiveDownchartTarget(target, out var pinned) && pinned == instrument)
                return true;

            return IsNativeInstrumentAvailable(target, mode, instrument);
        }

        /// <summary>
        /// Native-chart availability only — the check Difficulty Select's instrument
        /// rows and preference policy use, with no special case for a pinned
        /// downchart target.
        /// </summary>
        private bool IsNativeInstrumentAvailable(MaestroStagedPlayer target, GameMode mode,
            Instrument instrument)
        {
            try
            {
                if (!GetPossibleInstruments(mode).Contains(instrument) ||
                    !_songs.All(song => HasPlayableInstrument(song, instrument)))
                    return false;

                if (instrument is Instrument.Vocals or Instrument.Harmony)
                {
                    foreach (var prior in _players.Values)
                    {
                        if (prior.ProfileId == target.ProfileId || prior.SittingOut)
                            continue;
                        if ((prior.GameMode is GameMode.Vocals or GameMode.PartyVocals) &&
                            (prior.Instrument is Instrument.Vocals or Instrument.Harmony))
                            return prior.Instrument == instrument;
                    }
                }

                return true;
            }
            catch (NotImplementedException)
            {
                return false;
            }
        }

        private bool AreModifiersValid(GameMode mode, Instrument instrument, Modifier modifiers)
        {
            try
            {
                var (possible, excusable) = mode.PossibleModifiers(instrument);
                if ((modifiers & ~(possible | excusable)) != Modifier.None)
                    return false;

                Modifier[] conflictGroup =
                {
                    Modifier.AllStrums, Modifier.AllHopos, Modifier.AllTaps,
                    Modifier.HoposToTaps, Modifier.TapsToHopos,
                };
                return conflictGroup.Count(modifier => (modifiers & modifier) != 0) <= 1;
            }
            catch (NotImplementedException)
            {
                return false;
            }
        }

        private int MaxHarmonyParts() => _songs.Count == 0 ? 1 : _songs.Min(song => song.VocalsCount);

        private static bool IsVocal(GameMode mode) => mode is GameMode.Vocals or GameMode.PartyVocals;

        private IEnumerable<Instrument> GetPossibleInstruments(GameMode mode)
        {
            // Party Vocals is a game mode, not a chart. Its instrument control must
            // choose the same Solo/Harmony chart options as Difficulty Select.
            if (mode == GameMode.PartyVocals)
                return new[] { Instrument.Vocals, Instrument.Harmony };

            return mode.PossibleInstrumentsForSong(_songs[0]);
        }

        private static bool HasPlayableInstrument(SongEntry entry, Instrument instrument)
        {
            if (instrument == Instrument.PartyVocals)
                return entry.HasInstrument(Instrument.Vocals) || entry.HasInstrument(Instrument.Harmony);
            if (entry.HasInstrument(instrument))
                return true;
            return instrument switch
            {
                Instrument.FourLaneDrums or Instrument.ProDrums => entry.HasInstrument(Instrument.FiveLaneDrums),
                Instrument.FiveLaneDrums => entry.HasInstrument(Instrument.ProDrums),
                _ => false,
            };
        }

        /// <summary>
        /// Difficulties while a downchart target is active. Delegates to the shared
        /// Core predicate (<see cref="EliteDrumsDownchartRules.HasTargetDifficulty"/>):
        /// they come from the song's usable Elite Drums downchart (Beginner is
        /// synthesized from Easy, like the Core loader); songs whose Elite chart
        /// downcharts to nothing keep their native difficulties because no downchart
        /// is built for them. Mirrors Difficulty Select.
        /// </summary>
        private static bool HasPlayableDownchartDifficulty(SongEntry song, Instrument target,
            Difficulty difficulty)
        {
            return EliteDrumsDownchartRules.HasTargetDifficulty(song, target, difficulty);
        }

        private static bool HasPlayableDifficulty(SongEntry entry, Instrument instrument,
            Difficulty difficulty)
        {
            if (instrument is Instrument.Vocals or Instrument.Harmony or Instrument.PartyVocals)
                return difficulty is not Difficulty.ExpertPlus;
            if (instrument == Instrument.ProKeys && difficulty == Difficulty.Beginner)
                return false;
            return entry[instrument][difficulty] || instrument switch
            {
                Instrument.FourLaneDrums or Instrument.ProDrums => entry[Instrument.FiveLaneDrums][difficulty],
                Instrument.FiveLaneDrums => entry[Instrument.ProDrums][difficulty],
                _ => false,
            };
        }

        /// <summary>
        /// Selects the best available instrument when the preferred one isn't
        /// available, following a game-mode-specific priority chain.
        /// </summary>
        private static Instrument SelectInstrumentFallback(
            Instrument preferred, GameMode mode, IReadOnlyList<Instrument> available)
        {
            foreach (var candidate in GetInstrumentFallbackChain(mode))
            {
                if (candidate != preferred && available.Contains(candidate))
                    return candidate;
            }

            return available.Count > 0 ? available[0] : preferred;
        }

        /// <summary>
        /// Ordered fallback instruments per game mode. The caller tries the
        /// preferred instrument first; if it isn't available these chains
        /// provide the next-best option in priority order.
        /// </summary>
        private static IReadOnlyList<Instrument> GetInstrumentFallbackChain(GameMode mode)
        {
            return mode switch
            {
                GameMode.FiveFretGuitar => new[]
                {
                    Instrument.FiveFretGuitar,
                    Instrument.FiveFretBass,
                    Instrument.Keys,
                },
                GameMode.ProKeys => new[]
                {
                    Instrument.ProKeys,
                    Instrument.Keys,
                    Instrument.FiveFretGuitar,
                    Instrument.FiveFretBass,
                },
                GameMode.Vocals or GameMode.PartyVocals => new[]
                {
                    Instrument.Harmony,
                    Instrument.Vocals,
                },
                GameMode.FourLaneDrums => new[]
                {
                    Instrument.ProDrums,
                    Instrument.FourLaneDrums,
                },
                // Five-lane drums are cross-mapped in HasPlayableInstrument
                // so no explicit chain is needed.
                _ => Array.Empty<Instrument>(),
            };
        }
    }
}
