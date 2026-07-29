using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace YARG.Integration.Maestro
{
    /// <summary>
    /// Pure, Unity-free parser that normalizes a <see cref="MaestroCommandEnvelope"/>
    /// (whose <see cref="MaestroCommandEnvelope.Payload"/> deserializes as a
    /// <see cref="JObject"/>) into a validated <see cref="MaestroCommand"/>.
    /// <para>
    /// Validation here is structural only (required keys, value types, basic ranges).
    /// Mode/applicability rules (e.g. "vocals has no highway") are enforced later on the
    /// main thread by the controller/draft layer, which has access to live profile/song
    /// context.  This keeps the parser free of YARG.Core/Unity dependencies.
    /// </para>
    /// </summary>
    internal static class MaestroCommandParser
    {
        /// <summary>
        /// Parses and structurally validates the envelope into a command.  Returns false
        /// with a human-readable <paramref name="error"/> when the payload is missing or
        /// malformed; the caller turns that into a structured bad_request acknowledgement.
        /// </summary>
        public static bool TryParse(MaestroCommandEnvelope env, out MaestroCommand command, out string error)
        {
            command = null;
            error = null;

            string id = env.Id;
            string type = env.Type;

            switch (type)
            {
                case MaestroCommandType.SetVolume:
                    return ParseSetVolume(env, out command, out error);

                case MaestroCommandType.SetPendingProfileField:
                    return ParseSetPendingField(env, id, out command, out error);

                case MaestroCommandType.SetPendingModifier:
                    return ParseSetPendingModifier(env, id, out command, out error);

                case MaestroCommandType.ApplyPending:
                case MaestroCommandType.DiscardPending:
                case MaestroCommandType.RequestSnapshot:
                    // No payload required; optional profileId for applyPending/discardPending.
                    command = new MaestroCommand { Id = id, Type = type };
                    TryCopyProfileId(env, command);
                    return true;

                default:
                    error = $"Unknown command type: {type}";
                    return false;
            }
        }

        private static bool ParseSetVolume(MaestroCommandEnvelope env, out MaestroCommand command, out string error)
        {
            command = null;
            error = null;

            if (!TryGetPayload(env, out JObject payload, out error))
            {
                return false;
            }

            if (!TryGetString(payload, "key", out string key, out error))
            {
                return false;
            }

            if (!MaestroProtocol.IsKnownVolumeKey(key))
            {
                error = $"Unknown volume key: {key}";
                return false;
            }

            if (!TryGetFloat(payload, "value", out float value, out error))
            {
                return false;
            }

            if (!MaestroValidation.IsInRange(value, MaestroValidation.VolumeMin, MaestroValidation.VolumeMax))
            {
                error = $"Volume value out of range [0,1]: {value.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            command = new MaestroCommand
            {
                Id = env.Id,
                Type = MaestroCommandType.SetVolume,
                VolumeKey = key,
                VolumeValue = MaestroValidation.ClampVolume(value),
            };
            return true;
        }

        private static bool ParseSetPendingField(MaestroCommandEnvelope env, string id, out MaestroCommand command, out string error)
        {
            command = null;
            error = null;

            if (!TryGetPayload(env, out JObject payload, out error))
            {
                return false;
            }

            if (!TryGetGuid(payload, "profileId", out string profileIdText, out error))
            {
                return false;
            }

            if (!TryGetString(payload, "field", out string field, out error))
            {
                return false;
            }

            if (!MaestroProtocol.IsKnownProfileField(field))
            {
                error = $"Unknown profile field: {field}";
                return false;
            }

            command = new MaestroCommand
            {
                Id = id,
                Type = MaestroCommandType.SetPendingProfileField,
                ProfileId = profileIdText,
                FieldName = field,
            };

            // Numeric fields (noteSpeed/highwayLength) take a number; enum-ish fields
            // (instrument/gameMode/difficulty) take a string.
            switch (field)
            {
                case "noteSpeed":
                    if (!TryGetFloat(payload, "value", out float ns, out error))
                    {
                        return false;
                    }
                    if (!MaestroValidation.IsInRange(ns, MaestroValidation.NoteSpeedMin, MaestroValidation.NoteSpeedMax))
                    {
                        error = $"noteSpeed out of range [0,100]: {ns.ToString(CultureInfo.InvariantCulture)}";
                        return false;
                    }
                    command.FieldValueNumber = MaestroValidation.NormalizeHighwayDecimal(
                        MaestroValidation.ClampNoteSpeed(ns));
                    break;

                case "harmonyIndex":
                    if (!TryGetFloat(payload, "value", out float harmony, out error))
                    {
                        return false;
                    }
                    if (!MaestroValidation.IsInRange(harmony, 0f, 2f) || harmony != MathF.Truncate(harmony))
                    {
                        error = "harmonyIndex must be an integer in [0,2].";
                        return false;
                    }
                    command.FieldValueNumber = harmony;
                    break;

                case "highwayLength":
                    if (!TryGetFloat(payload, "value", out float hl, out error))
                    {
                        return false;
                    }
                    if (!MaestroValidation.IsInRange(hl, MaestroValidation.HighwayLengthMin, MaestroValidation.HighwayLengthMax))
                    {
                        error = $"highwayLength out of range [0.1,10]: {hl.ToString(CultureInfo.InvariantCulture)}";
                        return false;
                    }
                    command.FieldValueNumber = MaestroValidation.NormalizeHighwayDecimal(
                        MaestroValidation.ClampHighwayLength(hl));
                    break;

                default: // instrument / gameMode / difficulty
                    if (!TryGetString(payload, "value", out string textVal, out error))
                    {
                        return false;
                    }
                    command.FieldValueText = textVal;
                    break;
            }

            return true;
        }

        private static bool ParseSetPendingModifier(MaestroCommandEnvelope env, string id, out MaestroCommand command, out string error)
        {
            command = null;
            error = null;

            if (!TryGetPayload(env, out JObject payload, out error))
            {
                return false;
            }

            if (!TryGetGuid(payload, "profileId", out string profileIdText, out error))
            {
                return false;
            }

            if (!TryGetString(payload, "modifier", out string modifier, out error))
            {
                return false;
            }

            if (!TryGetBool(payload, "enabled", out bool enabled, out error))
            {
                return false;
            }

            command = new MaestroCommand
            {
                Id = id,
                Type = MaestroCommandType.SetPendingModifier,
                ProfileId = profileIdText,
                Modifier = modifier,
                ModifierEnabled = enabled,
            };
            return true;
        }

        // --- JToken accessors (reject wrong types explicitly) ---

        private static bool TryGetPayload(MaestroCommandEnvelope env, out JObject payload, out string error)
        {
            payload = null;
            error = null;

            if (env.Payload == null)
            {
                error = "Missing command payload.";
                return false;
            }

            if (env.Payload is JObject jo)
            {
                payload = jo;
                return true;
            }

            // Newtonsoft may deserialize a bare object as JObject; anything else is malformed.
            error = "Command payload must be a JSON object.";
            return false;
        }

        private static bool TryGetString(JObject o, string key, out string value, out string error)
        {
            value = null;
            error = null;

            if (!o.TryGetValue(key, out JToken tok) || tok == null || tok.Type == JTokenType.Null)
            {
                error = $"Missing '{key}'.";
                return false;
            }

            if (tok.Type == JTokenType.String)
            {
                value = tok.Value<string>();
                return true;
            }

            // Tolerate a string-encoded number/bool only for generic fields; here we want a real string.
            error = $"'{key}' must be a string.";
            return false;
        }

        private static bool TryGetFloat(JObject o, string key, out float value, out string error)
        {
            value = 0f;
            error = null;

            if (!o.TryGetValue(key, out JToken tok) || tok == null || tok.Type == JTokenType.Null)
            {
                error = $"Missing '{key}'.";
                return false;
            }

            if (tok.Type == JTokenType.Float || tok.Type == JTokenType.Integer)
            {
                value = (float) tok.Value<double>();
                return true;
            }

            // Tolerate numeric strings.
            if (tok.Type == JTokenType.String &&
                float.TryParse(tok.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            error = $"'{key}' must be a number.";
            return false;
        }

        private static bool TryGetBool(JObject o, string key, out bool value, out string error)
        {
            value = false;
            error = null;

            if (!o.TryGetValue(key, out JToken tok) || tok == null || tok.Type == JTokenType.Null)
            {
                error = $"Missing '{key}'.";
                return false;
            }

            if (tok.Type == JTokenType.Boolean)
            {
                value = tok.Value<bool>();
                return true;
            }

            error = $"'{key}' must be a boolean.";
            return false;
        }

        private static bool TryGetGuid(JObject o, string key, out string value, out string error)
        {
            value = null;
            error = null;

            if (!TryGetString(o, key, out string raw, out error))
            {
                return false;
            }

            if (!System.Guid.TryParse(raw, out _))
            {
                error = $"'{key}' must be a valid GUID.";
                return false;
            }

            value = raw;
            return true;
        }

        private static void TryCopyProfileId(MaestroCommandEnvelope env, MaestroCommand command)
        {
            if (env.Payload is JObject jo &&
                jo.TryGetValue("profileId", out JToken tok) &&
                tok != null && tok.Type == JTokenType.String)
            {
                command.ProfileId = tok.Value<string>();
            }
        }
    }
}
