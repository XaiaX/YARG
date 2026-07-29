using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Integration;
using YARG.Player;
using YARG.Settings;
using YARG.Settings.Types;

namespace YARG.Integration.Maestro
{
    /// <summary>
    /// Persistent Maestro host/controller.  Owns:
    /// <list type="bullet">
    /// <item>Main-thread draining of the command queue (Update).</item>
    /// <item>Snapshot publication from <see cref="PlayerContainer.Players"/> + <see cref="SettingsManager"/>.</item>
    /// <item>Live volume dispatch through the existing <see cref="VolumeSetting"/> callback path
    /// (SetValueWithoutNotify + ForceInvokeCallback), with focus-mute safety.</item>
    /// <item>Per-profile pending drafts keyed by <see cref="YargProfile.Id"/>.</item>
    /// <item>Pairing token and transport lifecycle.</item>
    /// </list>
    /// <para>
    /// <b>Thread invariant:</b> No Unity API, singleton, live player collection, setting
    /// callback, or YARG state is touched off the Unity main thread.  The transport worker
    /// only enqueues commands and reads the immutable snapshot / host contract.
    /// </para>
    /// <para>
    /// Spawned from <see cref="GlobalVariables"/> so it survives Menu/Gameplay/Score scene
    /// transitions.  The transport starts on <see cref="StartHost"/> and binds loopback by
    /// default; LAN binding requires an explicit opt-in.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-4000)] // After GlobalVariables (-5000) but before most gameplay code
    public class MaestroController : MonoSingleton<MaestroController>, IMaestroHost
    {
        public const int ProtocolVersionConstant = MaestroProtocol.ProtocolVersion;

        /// <summary>Default loopback port for the companion client.</summary>
        public const int DefaultPort = 5151;

        // ---- State ----

        private readonly MaestroCommandQueue _commandQueue = new();
        private readonly Dictionary<Guid, MaestroProfileDraft> _drafts = new();

        private long _revision;
        private volatile MaestroSnapshot _latestSnapshot;
        private bool _snapshotDirty = true;

        private string _pairingToken;
        private bool _isEnabled;

        // Cached game-state fields populated from the GameStateFetcher subscription (main thread).
        private string _currentSceneName = "Persistent";
        private bool _paused;
        private SongEntry _currentSong;
        private bool _startupSettingApplied;

        // Transport lifecycle. Set in StartHost, cleared in StopHost.
        private IMaestroTransport _transport;
        private int _configuredPort = DefaultPort;

        /// <summary>
        /// When true, the transport binds <see cref="System.Net.IPAddress.Any"/> (all
        /// interfaces) instead of loopback.  Must be set before <see cref="StartHost"/>.
        /// Off by default — LAN exposure is opt-in and still requires the pairing token.
        /// </summary>
        [SerializeField] private bool _allowLanConnections = false;

        /// <summary>Public accessor mirroring the LAN opt-in field for settings/toggles.</summary>
        public bool AllowLanConnections
        {
            get => _allowLanConnections;
            set => _allowLanConnections = value;
        }

        /// <summary>Human-readable bound address (for QR/URL display), or null if stopped.</summary>
        public string BoundAddress => _transport?.BoundAddress;

        // ---- IMaestroHost (queried by transport worker threads) ----

        public bool IsEnabled => _isEnabled;

        public string PairingToken => _isEnabled ? _pairingToken : null;

        /// <summary>
        /// Returns the latest snapshot, or null if none has been published.  Safe to call
        /// from any thread because the returned object is immutable once published.
        /// </summary>
        public MaestroSnapshot GetSnapshot() => _latestSnapshot;

        public MaestroDispatch EnqueueCommand(MaestroCommand command)
        {
            var dispatch = new MaestroDispatch(command);
            _commandQueue.Enqueue(dispatch);
            return dispatch;
        }

        public bool ValidateToken(string token)
        {
            if (!_isEnabled || string.IsNullOrEmpty(_pairingToken))
            {
                return false;
            }

            return string.Equals(token, _pairingToken, StringComparison.Ordinal);
        }

        // ---- Lifecycle ----

        protected override void SingletonAwake()
        {
            // Subscribe to game-state changes so the snapshot updates on scene/pause/song transitions.
            GameStateFetcher.GameStateChange += OnGameStateChange;
            PlayerContainer.PlayerAdded   += OnPlayerListChanged;
            PlayerContainer.PlayerRemoved += OnPlayerListChanged;

            // Build an initial snapshot so GET /api/v1/state works before any game event.
            RebuildSnapshot();

            YargLogger.LogInfo("[Maestro] Controller initialized (host disabled by default).");
        }

        protected override void SingletonDestroy()
        {
            GameStateFetcher.GameStateChange -= OnGameStateChange;
            PlayerContainer.PlayerAdded   -= OnPlayerListChanged;
            PlayerContainer.PlayerRemoved -= OnPlayerListChanged;

            StopHost();
            YargLogger.LogInfo("[Maestro] Controller destroyed.");
        }

        private void Update()
        {
            // Settings may finish loading before or after this persistent controller is created.
            // Reconcile once on the main thread so a saved enabled value starts the host without
            // requiring the operator to toggle Maestro off and back on.
            if (!_startupSettingApplied && SettingsManager.SettingContainer.IsInitialized)
            {
                _startupSettingApplied = true;
                if (SettingsManager.Settings.MaestroEnable.Value)
                {
                    StartHost();
                }
            }

            // 1. Drain and process queued commands on the main thread.
            var dispatches = _commandQueue.Drain();
            if (dispatches.Count > 0)
            {
                foreach (var dispatch in dispatches)
                {
                    ProcessCommand(dispatch);
                }
                _snapshotDirty = true;
            }

            // 2. Rebuild the snapshot if anything changed.
            if (_snapshotDirty)
            {
                RebuildSnapshot();
                _snapshotDirty = false;
            }
        }

        // ---- Event handlers (main thread) ----

        private void OnGameStateChange(GameStateFetcher.State state)
        {
            _currentSceneName = state.CurrentScene.ToString();
            _paused = state.Paused;
            _currentSong = state.SongEntry;
            _snapshotDirty = true;
        }

        private void OnPlayerListChanged(YargPlayer _)
        {
            _snapshotDirty = true;
        }

        // ---- Host enable/disable ----

        /// <summary>
        /// Enable Maestro hosting: generate a pairing token and start the transport on the
        /// configured port.  Binds loopback by default; binds all interfaces only when
        /// <see cref="AllowLanConnections"/> is true (explicit opt-in).
        /// </summary>
        public void StartHost()
        {
            StartHost(_configuredPort);
        }

        /// <summary>
        /// Enable Maestro hosting on a specific port (0 = OS-assigned free port).
        /// </summary>
        public void StartHost(int port)
        {
            if (_isEnabled)
            {
                return;
            }

            _configuredPort = port;
            _pairingToken = GeneratePairingToken();

            try
            {
                _transport = new MaestroHttpTransport(port, _allowLanConnections);
                _transport.Start(this);
            }
            catch (Exception ex)
            {
                YargLogger.LogError($"[Maestro] Transport failed to start: {ex.Message}");
                _transport = null;
                _pairingToken = null;
                return;
            }

            _isEnabled = true;
            _snapshotDirty = true;

            YargLogger.LogInfo($"[Maestro] Host enabled on {_transport.BoundAddress} " +
                               $"(LAN={_allowLanConnections}). Pairing token generated.");
        }

        /// <summary>
        /// Disable Maestro hosting: stop the transport and clear the token.  Drafts are
        /// preserved so a reconnect doesn't lose pending edits.
        /// </summary>
        public void StopHost()
        {
            if (!_isEnabled)
            {
                return;
            }

            _transport?.Stop();
            _transport = null;
            _isEnabled = false;
            _pairingToken = null;
            _snapshotDirty = true;

            YargLogger.LogInfo("[Maestro] Host disabled.");
        }

        // ---- Command processing (main thread only) ----

        private void ProcessCommand(MaestroDispatch dispatch)
        {
            var cmd = dispatch.Command;
            MaestroCommandResponse response;
            try
            {
                response = cmd.Type switch
                {
                    MaestroCommandType.SetVolume              => HandleSetVolume(cmd),
                    MaestroCommandType.SetPendingProfileField => HandleSetPendingField(cmd),
                    MaestroCommandType.SetPendingModifier     => HandleSetPendingModifier(cmd),
                    MaestroCommandType.ApplyPending           => HandleApplyPending(cmd),
                    MaestroCommandType.DiscardPending         => HandleDiscardPending(cmd),
                    MaestroCommandType.RequestSnapshot        => HandleRequestSnapshot(cmd),
                    _ => Error(cmd.Id, MaestroErrorCode.BadRequest, $"Unknown command type: {cmd.Type}"),
                };
            }
            catch (Exception ex)
            {
                YargLogger.LogException(ex, "[Maestro] Error processing command");
                response = Error(cmd.Id, MaestroErrorCode.InternalError, ex.Message);
            }

            // Re-stamp the response with the current revision so the client can reconcile.
            response.Revision = Interlocked.Read(ref _revision);
            dispatch.Result.SetResult(response);
        }

        // ---- Volume dispatch ----

        private MaestroCommandResponse HandleSetVolume(MaestroCommand cmd)
        {
            if (!_isEnabled)
            {
                return Error(cmd.Id, MaestroErrorCode.Forbidden, "Maestro host is not enabled.");
            }

            // Focus-mute safety: master volume is decoupled from MasterMusicVolume.Value
            // while _mutedFromFocusLoss is active.  Reject with a structured error so the
            // command cannot unmute YARG through the master path.
            bool isMaster = string.Equals(cmd.VolumeKey, "Master", StringComparison.OrdinalIgnoreCase);
            if (isMaster && GlobalVariables.Instance != null && GlobalVariables.Instance.IsMutedFromFocusLoss)
            {
                return Error(cmd.Id, MaestroErrorCode.UnsafeTiming,
                    "Master volume is locked while audio is muted from focus loss.");
            }

            if (!TryGetVolumeSetting(cmd.VolumeKey, out VolumeSetting setting))
            {
                return Error(cmd.Id, MaestroErrorCode.UnsupportedField,
                    $"Unknown volume surface: {cmd.VolumeKey}");
            }

            // Apply via the callback path — SetValueWithoutNotify bypasses SettingsMenu.Instance,
            // then ForceInvokeCallback fires the GlobalAudioHandler volume callback.  Never touch
            // the audio backend directly from the command path.
            setting.SetValueWithoutNotify(cmd.VolumeValue);
            setting.ForceInvokeCallback();

            return Ok(cmd.Id, MaestroCommandStatus.Applied, cmd.VolumeValue,
                "applies now");
        }

        /// <summary>
        /// Resolves a wire volume key to the corresponding live
        /// <see cref="VolumeSetting"/> in <see cref="SettingsManager.Settings"/>.
        /// Returns false if settings are not loaded or the key is unknown.
        /// </summary>
        private static bool TryGetVolumeSetting(string key, out VolumeSetting setting)
        {
            setting = null;
            var settings = SettingsManager.Settings;
            if (settings == null || string.IsNullOrEmpty(key))
            {
                return false;
            }

            setting = key switch
            {
                "Master"          => settings.MasterMusicVolume,
                "Guitar"          => settings.GuitarVolume,
                "Rhythm"          => settings.RhythmVolume,
                "Bass"            => settings.BassVolume,
                "Keys"            => settings.KeysVolume,
                "Drums"           => settings.DrumsVolume,
                "Vocals"          => settings.VocalsVolume,
                "Song"            => settings.SongVolume,
                "Crowd"           => settings.CrowdVolume,
                "Sfx"             => settings.SfxVolume,
                "DrumSfx"         => settings.DrumSfxVolume,
                "Metronome"       => settings.MetronomeVolume,
                "VocalMonitoring" => settings.VocalMonitoring,
                _ => null,
            };

            return setting != null;
        }

        // ---- Draft commands ----

        private MaestroProfileDraft GetOrCreateDraft(Guid profileId)
        {
            if (!_drafts.TryGetValue(profileId, out var draft))
            {
                draft = new MaestroProfileDraft(profileId);
                _drafts[profileId] = draft;
            }
            return draft;
        }

        /// <summary>
        /// Reads a pending profile draft from the Unity main thread. The caller must apply it
        /// only at the authoritative DifficultySelect finalization boundary.
        /// </summary>
        public bool TryGetPendingDraft(Guid profileId, out MaestroProfileDraft draft)
        {
            draft = null;
            if (!_isEnabled)
            {
                return false;
            }

            return _drafts.TryGetValue(profileId, out draft) && draft.HasPendingChanges;
        }

        private MaestroCommandResponse HandleSetPendingField(MaestroCommand cmd)
        {
            if (!_isEnabled)
            {
                return Error(cmd.Id, MaestroErrorCode.Forbidden, "Maestro host is not enabled.");
            }

            if (!Guid.TryParse(cmd.ProfileId, out Guid profileId))
            {
                return Error(cmd.Id, MaestroErrorCode.BadRequest, "Missing or invalid profileId.");
            }

            YargProfile profile = PlayerContainer.GetProfileById(profileId);
            if (profile == null || PlayerContainer.GetPlayerFromProfile(profile) == null)
            {
                return Error(cmd.Id, MaestroErrorCode.NotFound, $"No active profile with ID {cmd.ProfileId}.");
            }

            var draft = GetOrCreateDraft(profileId);

            try
            {
                switch (cmd.FieldName)
                {
                    case "gameMode":
                        draft.SetGameMode(ParseEnum<GameMode>(cmd.FieldValueText));
                        break;
                    case "instrument":
                        draft.SetInstrument(ParseEnum<Instrument>(cmd.FieldValueText));
                        break;
                    case "difficulty":
                        draft.SetDifficulty(ParseEnum<Difficulty>(cmd.FieldValueText));
                        break;
                    case "noteSpeed":
                        draft.SetNoteSpeed(cmd.FieldValueNumber ?? 0f);
                        break;
                    case "harmonyIndex":
                        draft.SetHarmonyIndex((byte) (cmd.FieldValueNumber ?? 0f));
                        break;
                    case "highwayLength":
                        draft.SetHighwayLength(cmd.FieldValueNumber ?? 0f);
                        break;
                    default:
                        return Error(cmd.Id, MaestroErrorCode.UnsupportedField,
                            $"Unknown profile field: {cmd.FieldName}");
                }
            }
            catch (Exception)
            {
                return Error(cmd.Id, MaestroErrorCode.UnsupportedField,
                    $"Could not apply value for field '{cmd.FieldName}'.");
            }

            return Ok(cmd.Id, MaestroCommandStatus.Queued, null,
                "Next song setup (DifficultySelect boundary)");
        }

        private MaestroCommandResponse HandleSetPendingModifier(MaestroCommand cmd)
        {
            if (!_isEnabled)
            {
                return Error(cmd.Id, MaestroErrorCode.Forbidden, "Maestro host is not enabled.");
            }

            if (!Guid.TryParse(cmd.ProfileId, out Guid profileId))
            {
                return Error(cmd.Id, MaestroErrorCode.BadRequest, "Missing or invalid profileId.");
            }

            YargProfile profile = PlayerContainer.GetProfileById(profileId);
            if (profile == null || PlayerContainer.GetPlayerFromProfile(profile) == null)
            {
                return Error(cmd.Id, MaestroErrorCode.NotFound, $"No active profile with ID {cmd.ProfileId}.");
            }

            if (!Enum.TryParse(cmd.Modifier, ignoreCase: true, out Modifier flag) ||
                flag == Modifier.None ||
                !IsModifierApplicable(profile.GameMode, profile.CurrentInstrument, flag))
            {
                return Error(cmd.Id, MaestroErrorCode.UnsupportedField,
                    $"Modifier '{cmd.Modifier}' is not applicable to this profile.");
            }

            var draft = GetOrCreateDraft(profileId);
            draft.SetModifierFlag(flag, cmd.ModifierEnabled, profile.CurrentModifiers);

            return Ok(cmd.Id, MaestroCommandStatus.Queued, null,
                "Next song setup (DifficultySelect boundary)");
        }

        private MaestroCommandResponse HandleApplyPending(MaestroCommand cmd)
        {
            // The safe boundary (DifficultySelectMenu all-players-finalized) is not yet wired.
            // Acknowledge as queued and report the target boundary; never pretend it applied.
            return Ok(cmd.Id, MaestroCommandStatus.Queued, null,
                "Queued for DifficultySelect all-players-finalized boundary (not yet wired).");
        }

        private MaestroCommandResponse HandleDiscardPending(MaestroCommand cmd)
        {
            if (!Guid.TryParse(cmd.ProfileId, out Guid profileId))
            {
                return Error(cmd.Id, MaestroErrorCode.BadRequest, "Missing or invalid profileId.");
            }

            if (_drafts.TryGetValue(profileId, out var draft))
            {
                draft.Discard();
            }

            _snapshotDirty = true;
            return Ok(cmd.Id, MaestroCommandStatus.Applied, null, "Draft discarded.");
        }

        private MaestroCommandResponse HandleRequestSnapshot(MaestroCommand cmd)
        {
            _snapshotDirty = true;
            RebuildSnapshot();
            return Ok(cmd.Id, MaestroCommandStatus.Applied, null, "Snapshot rebuilt.");
        }

        // ---- Snapshot building (main thread only) ----

        private void RebuildSnapshot()
        {
            long rev = Interlocked.Increment(ref _revision);

            var snap = new MaestroSnapshot
            {
                Revision = rev,
                ProtocolVersion = MaestroProtocol.ProtocolVersion,
                Server = new MaestroServerInfo
                {
                    Identity = MaestroProtocol.ServerIdentity,
                    Version = GlobalVariables.Instance != null ? GlobalVariables.Instance.CurrentVersion : "?",
                    Capabilities = new List<string>
                    {
                        MaestroCapability.LiveVolume,
                        MaestroCapability.DeferredProfile,
                        MaestroCapability.RevisionPolling,
                    },
                },
                Connection = new MaestroConnectionInfo
                {
                    Mode = _allowLanConnections ? MaestroConnectionMode.Lan : MaestroConnectionMode.Loopback,
                    BindAddress = _transport?.BoundAddress,
                    Port = _configuredPort,
                    Paired = _isEnabled,            // a token exists ⇒ a client may pair
                    WriteEnabled = _isEnabled,
                },
                Scene = new MaestroSceneInfo
                {
                    Name = _currentSceneName,
                    Paused = _paused,
                },
                Song = BuildSongInfo(),
                LiveMute = new MaestroLiveMuteInfo
                {
                    MasterMutedFromFocus =
                        GlobalVariables.Instance != null && GlobalVariables.Instance.IsMutedFromFocusLoss,
                },
            };

            // Active players.
            var players = PlayerContainer.Players;
            snap.Players.Capacity = players.Count;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                var profile = player.Profile;
                if (profile == null) continue;

                _drafts.TryGetValue(profile.Id, out var draft);

                bool showHighway = profile.GameMode != GameMode.Vocals &&
                                    profile.GameMode != GameMode.PartyVocals;
                snap.Players.Add(new MaestroPlayerInfo
                {
                    Id = profile.Id.ToString(),
                    Name = profile.Name,
                    Order = i,
                    IsBot = profile.IsBot,
                    SittingOut = player.SittingOut,
                    GameMode = profile.GameMode.ToString(),
                    Instrument = profile.CurrentInstrument.ToString(),
                    Difficulty = profile.CurrentDifficulty.ToString(),
                    Highway = new MaestroHighwayInfo
                    {
                        Visible = showHighway,
                        NoteSpeed = showHighway ? profile.NoteSpeed : (float?) null,
                        HighwayLength = showHighway ? profile.HighwayLength : (float?) null,
                    },
                    Modifiers = new MaestroModifierInfo
                    {
                        Active = ModifierNames(profile.CurrentModifiers),
                    },
                    Pending = draft != null ? draft.ToPendingView() : new MaestroPlayerPending(),
                });
            }

            // Volumes.
            snap.Volumes = BuildVolumes();

            // Publish atomically — the transport reads this from worker threads.
            _latestSnapshot = snap;
        }

        private MaestroSongInfo BuildSongInfo()
        {
            var entry = _currentSong;
            if (entry == null)
            {
                return null;
            }

            return new MaestroSongInfo
            {
                Name = entry.Name.ToString(),
                Artist = entry.Artist.ToString(),
                Album = entry.Album.ToString(),
                Charter = entry.Charter.ToString(),
            };
        }

        private static Dictionary<string, float> BuildVolumes()
        {
            var settings = SettingsManager.Settings;
            var volumes = new Dictionary<string, float>(StringComparer.Ordinal);
            if (settings == null)
            {
                return volumes;
            }

            foreach (var key in MaestroProtocol.VolumeKeys)
            {
                if (TryGetVolumeSetting(key, out var setting))
                {
                    volumes[key] = setting.Value;
                }
            }

            return volumes;
        }

        private static bool IsModifierApplicable(GameMode mode, Instrument instrument, Modifier modifier)
        {
            try
            {
                var (possible, excusable) = mode.PossibleModifiers(instrument);
                return ((possible | excusable) & modifier) == modifier;
            }
            catch (NotImplementedException)
            {
                return false;
            }
        }

        private static List<string> ModifierNames(Modifier mods)
        {
            var names = new List<string>();
            if (mods == Modifier.None)
            {
                return names;
            }

            foreach (Modifier m in Enum.GetValues(typeof(Modifier)))
            {
                if (m == Modifier.None) continue;
                if ((mods & m) != 0)
                {
                    names.Add(m.ToString());
                }
            }

            return names;
        }

        // ---- Helpers ----

        private static T ParseEnum<T>(string value) where T : struct
        {
            if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
            {
                return result;
            }
            throw new FormatException($"Cannot parse '{value}' as {typeof(T).Name}.");
        }

        private static string GeneratePairingToken()
        {
            // 6-digit numeric PIN for easy entry on a phone/tablet.
            byte[] bytes = new byte[4];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            uint raw = BitConverter.ToUInt32(bytes, 0);
            int pin = (int) (raw % 1_000_000);
            return pin.ToString("D6", CultureInfo.InvariantCulture);
        }

        private static MaestroCommandResponse Ok(string id, string status, object result, string message)
            => new()
            {
                Id = id,
                Ok = true,
                Status = status,
                Result = result,
                Message = message,
            };

        private static MaestroCommandResponse Error(string id, string code, string message)
            => new()
            {
                Id = id,
                Ok = false,
                Error = new MaestroError { Code = code, Message = message },
            };
    }
}
