// pattern: Mixed (needs refactoring)

using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.Song;
using YARG.Integration.Maestro;
using YARG.Player;
using YARG.Song;

namespace YARG.Menu.Maestro
{
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
        public bool SittingOut => Player.SittingOut;
        public bool IsMissingInput => Player.IsMissingInputDevice || Player.IsMissingMicrophone;

        public GameMode GameMode { get; internal set; }
        public Instrument Instrument { get; internal set; }
        public Difficulty Difficulty { get; internal set; }
        public Modifier Modifiers { get; internal set; }
        public bool LeftyFlip { get; internal set; }
        public bool RangeEnabled { get; internal set; }
        public OpenLaneDisplayType OpenLaneDisplayType { get; internal set; }
        public float NoteSpeed { get; internal set; }
        public float HighwayLength { get; internal set; }
        public byte HarmonyIndex { get; internal set; }

        internal MaestroStagedPlayer(YargPlayer player)
        {
            Player = player;
            ProfileId = player.Profile.Id;
            GameMode = player.Profile.GameMode;
            Instrument = player.Profile.CurrentInstrument;
            // Party Vocals profiles store Instrument.PartyVocals, but the Maestro
            // dropdown uses {Vocals, Harmony}. Map based on PartyVocalsChartPreference,
            // which is how Difficulty Select tracks the Solo/Harmony choice.
            if (GameMode == GameMode.PartyVocals && Instrument == Instrument.PartyVocals)
                Instrument = player.Profile.PartyVocalsChartPreference == PartyVocalsChartPreference.Harmony
                    ? Instrument.Harmony
                    : Instrument.Vocals;
            Difficulty = player.Profile.CurrentDifficulty;
            Modifiers = player.Profile.CurrentModifiers;
            LeftyFlip = player.Profile.LeftyFlip;
            RangeEnabled = player.Profile.RangeEnabled;
            OpenLaneDisplayType = player.Profile.OpenLaneDisplayType;
            NoteSpeed = player.Profile.NoteSpeed;
            HighwayLength = player.Profile.HighwayLength;
            HarmonyIndex = player.Profile.HarmonyIndex;
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
            session.OverlayPendingDrafts();
            Active = session;
            return session;
        }

        public void MarkReturningToDifficultySelect() => ReturningToDifficultySelect = true;

        public void ClearReturningToDifficultySelect() => ReturningToDifficultySelect = false;

        public static void ClearActive()
        {
            Active = null;
        }

        public bool TryGetPlayer(Guid profileId, out MaestroStagedPlayer player) =>
            _players.TryGetValue(profileId, out player);

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
                return GetPossibleInstruments(player.GameMode)
                    .Where(instrument => IsInstrumentAvailable(player, player.GameMode, instrument))
                    .ToArray();
            }
            catch (NotImplementedException)
            {
                return Array.Empty<Instrument>();
            }
        }

        public IReadOnlyList<Difficulty> GetAvailableDifficulties(Guid profileId)
        {
            if (!_players.TryGetValue(profileId, out var player))
                return Array.Empty<Difficulty>();

            return EnumExtensions<Difficulty>.Values
                .Where(difficulty => IsDifficultyAvailable(player.Instrument, difficulty))
                .ToArray();
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

            player.GameMode = gameMode;
            NormalizeDependentSelections(player);
        }

        public void StageInstrument(Guid profileId, Instrument instrument)
        {
            if (!_players.TryGetValue(profileId, out var player) ||
                !IsInstrumentAvailable(player, player.GameMode, instrument))
                return;

            player.Instrument = instrument;
            NormalizeDependentSelections(player);
        }

        public void StageDifficulty(Guid profileId, Difficulty difficulty)
        {
            if (_players.TryGetValue(profileId, out var player) &&
                IsDifficultyAvailable(player.Instrument, difficulty))
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

        private sealed class ProfileSnapshot
        {
            public readonly YargProfile Profile;
            public readonly GameMode GameMode;
            public readonly Instrument PreferredInstrument;
            public readonly Instrument CurrentInstrument;
            public readonly Difficulty DifficultyFallback;
            public readonly Difficulty CurrentDifficulty;
            public readonly float NoteSpeed;
            public readonly float HighwayLength;
            public readonly byte EffectiveHarmonyIndex;
            public readonly byte HarmonyIndexFallback;
            public readonly Modifier CurrentModifiers;
            public readonly bool LeftyFlip;
            public readonly bool RangeEnabled;
            public readonly OpenLaneDisplayType OpenLaneDisplayType;

            public ProfileSnapshot(YargProfile profile)
            {
                Profile = profile;
                GameMode = profile.GameMode;
                PreferredInstrument = profile.PreferredInstrument;
                CurrentInstrument = profile.CurrentInstrument;
                DifficultyFallback = profile.DifficultyFallback;
                CurrentDifficulty = profile.CurrentDifficulty;
                NoteSpeed = profile.NoteSpeed;
                HighwayLength = profile.HighwayLength;
                EffectiveHarmonyIndex = profile.EffectiveHarmonyIndex;
                HarmonyIndexFallback = profile.HarmonyIndexFallback;
                CurrentModifiers = profile.CurrentModifiers;
                LeftyFlip = profile.LeftyFlip;
                RangeEnabled = profile.RangeEnabled;
                OpenLaneDisplayType = profile.OpenLaneDisplayType;
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
                Profile.RestoreHarmonyIndexState(EffectiveHarmonyIndex, HarmonyIndexFallback);
                Profile.RestoreSessionModifiers(CurrentModifiers);
                Profile.LeftyFlip = LeftyFlip;
                Profile.RangeEnabled = RangeEnabled;
                Profile.OpenLaneDisplayType = OpenLaneDisplayType;
            }
        }

        public MaestroFinalizationResult TryCommit()
        {
            var errors = Validate(out string globalError);
            if (globalError != null || errors.Count > 0)
                return MaestroFinalizationResult.Rejected(globalError, errors);

            var snapshots = _players.Values
                .Select(player => new ProfileSnapshot(player.Player.Profile))
                .ToList();

            try
            {
                // No profile is mutated until the complete active-player set has passed.
                foreach (var staged in _players.Values)
                {
                    if (staged.SittingOut)
                        continue;

                    var profile = staged.Player.Profile;
                    profile.GameMode = staged.GameMode;
                    profile.PreferredInstrument = staged.Instrument;
                    profile.CurrentInstrument = staged.Instrument;
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

                if (!IsModeAvailable(staged.GameMode))
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

                if (!IsDifficultyAvailable(staged.Instrument, staged.Difficulty))
                {
                    errors[staged.ProfileId] = $"{staged.Difficulty} is not available for this show.";
                    continue;
                }

                if (!AreModifiersValid(staged.GameMode, staged.Instrument, staged.Modifiers))
                    errors[staged.ProfileId] = "The selected modifiers are unavailable or conflict.";
            }

            return errors;
        }

        private void OverlayPendingDrafts()
        {
            var maestro = MaestroController.Instance;
            if (maestro != null)
            {
                foreach (var staged in _players.Values)
                {
                    if (!maestro.TryGetPendingDraft(staged.ProfileId, out var draft))
                        continue;

                    if (draft.PendingGameMode.HasValue)
                        staged.GameMode = draft.PendingGameMode.Value;
                    if (draft.PendingInstrument.HasValue)
                    {
                        staged.Instrument = draft.PendingInstrument.Value;
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
                                player.Profile.PartyVocalsChartPreference == PartyVocalsChartPreference.Harmony;
                            staged.Instrument = preferHarmony
                                ? Instrument.Harmony
                                : Instrument.Vocals;
                        }
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
                }
            }

            foreach (var staged in _players.Values)
                NormalizeDependentSelections(staged);
        }

        private void NormalizeDependentSelections(MaestroStagedPlayer player)
        {
            if (!IsModeAvailable(player.GameMode))
            {
                var gameMode = GetAvailableGameModes().FirstOrDefault();
                if (!IsModeAvailable(gameMode))
                    return;
                player.GameMode = gameMode;
            }

            var instruments = GetAvailableInstruments(player.ProfileId);
            if (!instruments.Contains(player.Instrument) && instruments.Count > 0)
                player.Instrument = instruments[0];

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

        private bool IsInstrumentAvailable(MaestroStagedPlayer target, GameMode mode,
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

        private bool IsDifficultyAvailable(Instrument instrument, Difficulty difficulty) =>
            _songs.All(song => HasPlayableDifficulty(song, instrument, difficulty));

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
    }
}
