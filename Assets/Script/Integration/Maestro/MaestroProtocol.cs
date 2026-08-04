using System;
using System.Collections.Generic;

namespace YARG.Integration.Maestro
{
    /// <summary>
    /// Maestro companion-client protocol constants for the Phase 1 HTTP/1.1 polling transport.
    /// YARG remains authoritative; this is a parallel read/live-control + deferred-edit surface.
    /// </summary>
    public static class MaestroProtocol
    {
        /// <summary>Wire protocol version. Bump on any breaking change to the message shapes.</summary>
        public const int ProtocolVersion = 1;

        /// <summary>Stable server identity advertised in hello/snapshot.</summary>
        public const string ServerIdentity = "YARG-Maestro";

        // --- Routes (strict allowlist; unknown paths return 404) ---
        public const string StateRoute = "/api/v1/state";
        public const string CommandsRoute = "/api/v1/commands";
        public const string HelloRoute = "/api/v1/hello";
        public const string RootRoute = "/";

        public const string JsonContentType = "application/json; charset=utf-8";
        public const string HtmlContentType = "text/html; charset=utf-8";

        public const string BearerPrefix = "Bearer ";

        // --- Live volume keys backed by SettingsManager.Settings (VolumeSetting, range 0..1).
        // PreviewVolume and MusicPlayerVolume are intentionally excluded (not gameplay-relevant).
        public static readonly string[] VolumeKeys =
        {
            "Master",
            "Guitar",
            "Rhythm",
            "Bass",
            "Keys",
            "Drums",
            "Vocals",
            "Song",
            "Crowd",
            "Sfx",
            "DrumSfx",
            "Metronome",
            "VocalMonitoring",
        };

        // --- Pending (next-song) profile fields.
        public static readonly string[] ProfileFields =
        {
            "instrument",
            "gameMode",
            "difficulty",
            "harmonyIndex",
            "noteSpeed",
            "highwayLength",
        };

        public static bool IsKnownVolumeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            foreach (var known in VolumeKeys)
            {
                if (string.Equals(known, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsKnownProfileField(string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return false;
            }

            foreach (var known in ProfileFields)
            {
                if (string.Equals(known, field, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Structured error codes returned in command/HTTP error responses. Names are stable on the wire.
    /// </summary>
    public static class MaestroErrorCode
    {
        public const string BadRequest = "bad_request";
        public const string Unauthorized = "unauthorized";
        public const string Forbidden = "forbidden";
        public const string NotFound = "not_found";
        public const string MethodNotAllowed = "method_not_allowed";
        public const string PayloadTooLarge = "payload_too_large";
        public const string UnsupportedProtocolVersion = "unsupported_protocol_version";
        public const string Conflict = "conflict";          // stale revision
        public const string UnsupportedField = "unsupported_field";
        public const string InvalidRange = "invalid_range";
        public const string UnsafeTiming = "unsafe_timing";
        public const string InternalError = "internal_error";
    }

    /// <summary>
    /// Discriminated command type names accepted by POST /api/v1/commands.
    /// </summary>
    public static class MaestroCommandType
    {
        public const string SetVolume = "setVolume";
        public const string SetPendingProfileField = "setPendingProfileField";
        public const string SetPendingModifier = "setPendingModifier";
        public const string ApplyPending = "applyPending";
        public const string DiscardPending = "discardPending";
        public const string RequestSnapshot = "requestSnapshot";

        public static readonly HashSet<string> All = new()
        {
            SetVolume,
            SetPendingProfileField,
            SetPendingModifier,
            ApplyPending,
            DiscardPending,
            RequestSnapshot,
        };

        public static bool IsKnown(string type)
            => !string.IsNullOrEmpty(type) && All.Contains(type);
    }

    /// <summary>
    /// Command acknowledgement status values.
    /// </summary>
    public static class MaestroCommandStatus
    {
        public const string Queued = "queued";       // pending at an in-game or main-thread boundary
        public const string Accepted = "accepted";
        public const string Applied = "applied";     // committed at the authoritative page boundary
    }

    /// <summary>
    /// Connection bind mode advertised to clients.
    /// </summary>
    public static class MaestroConnectionMode
    {
        public const string Loopback = "loopback";
        public const string Lan = "lan";
    }

    /// <summary>
    /// Server capability tokens advertised in hello/snapshot.
    /// </summary>
    public static class MaestroCapability
    {
        public const string LiveVolume = "liveVolume";
        public const string DeferredProfile = "deferredProfile";
        public const string RevisionPolling = "revisionPolling";
    }
}
