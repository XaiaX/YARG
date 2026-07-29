using System.Collections.Generic;

namespace YARG.Integration.Maestro
{
    // ====================================================================================
    // Snapshot DTOs (GET /api/v1/state). Pure data; serialized to camelCase JSON by the
    // transport. All enum-like values are strings so the wire stays language-neutral.
    // ====================================================================================

    /// <summary>
    /// The complete authoritative state snapshot. Clients reconcile by revision.
    /// </summary>
    public sealed class MaestroSnapshot
    {
        public long Revision { get; set; }
        public int ProtocolVersion { get; set; }
        public MaestroServerInfo Server { get; set; }
        public MaestroConnectionInfo Connection { get; set; }
        public MaestroSceneInfo Scene { get; set; }
        public MaestroSongInfo Song { get; set; }
        public List<MaestroPlayerInfo> Players { get; set; } = new();
        public Dictionary<string, float> Volumes { get; set; } = new();
        public MaestroLiveMuteInfo LiveMute { get; set; }

        /// <summary>Set by the transport when the snapshot was unchanged vs. ?since=.</summary>
        public bool Unchanged { get; set; }
    }

    public sealed class MaestroServerInfo
    {
        public string Identity { get; set; }
        public string Version { get; set; }
        public List<string> Capabilities { get; set; } = new();
    }

    public sealed class MaestroConnectionInfo
    {
        public string Mode { get; set; }       // "loopback" | "lan"
        public string BindAddress { get; set; }
        public int Port { get; set; }
        public bool Paired { get; set; }        // a client has presented the pairing token
        public bool WriteEnabled { get; set; }  // commands are accepted
    }

    public sealed class MaestroSceneInfo
    {
        public string Name { get; set; }   // "Menu" | "Gameplay" | "Score" | "Calibration" | "Persistent"
        public bool Paused { get; set; }
    }

    public sealed class MaestroSongInfo
    {
        public string Name { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Charter { get; set; }
    }

    public sealed class MaestroPlayerInfo
    {
        /// <summary>Stable profile identifier (YargProfile.Id Guid). Commands target this only.</summary>
        public string Id { get; set; }
        public string Name { get; set; }
        /// <summary>Transient display ordering (PlayerContainer list index). Never a command target.</summary>
        public int Order { get; set; }
        public bool IsBot { get; set; }
        public bool SittingOut { get; set; }

        // Applied values currently used by the next setup / consumed by gameplay.
        public string GameMode { get; set; }       // GameMode enum name
        public string Instrument { get; set; }     // Instrument enum name
        public string Difficulty { get; set; }     // Difficulty enum name

        public MaestroHighwayInfo Highway { get; set; } = new();
        public MaestroModifierInfo Modifiers { get; set; } = new();
        public MaestroPlayerPending Pending { get; set; } = new();
    }

    public sealed class MaestroHighwayInfo
    {
        public bool Visible { get; set; }
        public float? NoteSpeed { get; set; }       // null when not visible (vocals)
        public float? HighwayLength { get; set; }   // null when not visible
    }

    public sealed class MaestroModifierInfo
    {
        /// <summary>Modifier names currently active on the profile.</summary>
        public List<string> Active { get; set; } = new();
        /// <summary>Modifier names selectable for this mode/instrument (Maestro applicability).</summary>
        public List<string> Available { get; set; } = new();
    }

    public sealed class MaestroPlayerPending
    {
        public bool HasPending { get; set; }
        public string PendingGameMode { get; set; }
        public string PendingInstrument { get; set; }
        public string PendingDifficulty { get; set; }
        public float? PendingNoteSpeed { get; set; }
        public float? PendingHighwayLength { get; set; }
        public byte? PendingHarmonyIndex { get; set; }
        public List<string> PendingModifiers { get; set; } = new();
    }

    public sealed class MaestroLiveMuteInfo
    {
        /// <summary>True while focus-loss mute is active; master-volume commands are rejected then.</summary>
        public bool MasterMutedFromFocus { get; set; }
    }

    // ====================================================================================
    // Command DTOs (POST /api/v1/commands)
    // ====================================================================================

    /// <summary>
    /// Incoming command envelope. <c>payload</c> is parsed per <c>type</c> by the command parser.
    /// </summary>
    public sealed class MaestroCommandEnvelope
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public object Payload { get; set; }
        /// <summary>Optional client-assigned base revision for optimistic-concurrency checks.</summary>
        public long? Since { get; set; }
    }

    /// <summary>
    /// Normalized command produced by validation; enqueued for main-thread processing.
    /// Only the fields relevant to <see cref="Type"/> are populated.
    /// </summary>
    public sealed class MaestroCommand
    {
        public string Id { get; set; }
        public string Type { get; set; }

        // setVolume
        public string VolumeKey { get; set; }
        public float VolumeValue { get; set; }

        // setPendingProfileField
        public string ProfileId { get; set; }
        public string FieldName { get; set; }
        public string FieldValueText { get; set; }
        public float? FieldValueNumber { get; set; }

        // setPendingModifier
        public string Modifier { get; set; }
        public bool ModifierEnabled { get; set; }
    }

    /// <summary>
    /// Immediate command acknowledgement, returned independently of the snapshot stream.
    /// </summary>
    public sealed class MaestroCommandResponse
    {
        public string Id { get; set; }
        public bool Ok { get; set; }
        public string Status { get; set; }    // queued | accepted | applied
        public long Revision { get; set; }
        /// <summary>Human-readable detail, e.g. the apply boundary or queued reason.</summary>
        public string Message { get; set; }
        /// <summary>Normalized/applied value where applicable (e.g. clamped volume).</summary>
        public object Result { get; set; }
        public MaestroError Error { get; set; }
    }

    public sealed class MaestroError
    {
        public string Code { get; set; }      // MaestroErrorCode.*
        public string Message { get; set; }
        public string Field { get; set; }     // optional offending field/key
    }

    /// <summary>
    /// Top-level error envelope for non-command HTTP failures (e.g. bad route, oversized body).
    /// </summary>
    public sealed class MaestroErrorResponse
    {
        public MaestroError Error { get; set; }
    }
}
