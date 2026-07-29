using System;
using System.Collections.Generic;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Integration.Maestro
{
    /// <summary>
    /// Per-profile pending draft for Maestro's deferred (next-song) edits.
    /// Keyed by <see cref="YargProfile.Id"/> (<see cref="Guid"/>).
    /// <para>
    /// <b>This class never mutates the real <see cref="YargProfile"/>.</b>
    /// It holds nullable overrides separately from the serialized profile fields.
    /// The safe-boundary apply (DifficultySelectMenu finalization, not yet wired)
    /// will read these overrides; until then they are display-only pending state.
    /// </para>
    /// </summary>
    public sealed class MaestroProfileDraft
    {
        public Guid ProfileId { get; }

        // Nullable overrides — null means "no pending change for this field."
        public GameMode?    PendingGameMode       { get; private set; }
        public Instrument?  PendingInstrument    { get; private set; }
        public Difficulty?  PendingDifficulty    { get; private set; }
        public float?       PendingNoteSpeed     { get; private set; }
        public float?       PendingHighwayLength { get; private set; }
        public byte?        PendingHarmonyIndex  { get; private set; }

        /// <summary>
        /// The full desired modifier set for the next song, or null if no modifier
        /// override is pending.  When non-null the apply boundary will assemble this
        /// on a scratch profile and call <c>ApplySessionModifiers</c>.
        /// </summary>
        public Modifier?    PendingModifiers     { get; private set; }

        public MaestroProfileDraft(Guid profileId)
        {
            ProfileId = profileId;
        }

        public bool HasPendingChanges =>
            PendingGameMode.HasValue       ||
            PendingInstrument.HasValue     ||
            PendingDifficulty.HasValue     ||
            PendingNoteSpeed.HasValue      ||
            PendingHighwayLength.HasValue  ||
            PendingHarmonyIndex.HasValue   ||
            PendingModifiers.HasValue;

        // ---- Mutators (called on the main thread only) ----

        public void SetGameMode(GameMode value)      => PendingGameMode = value;
        public void SetInstrument(Instrument value)  => PendingInstrument = value;
        public void SetDifficulty(Difficulty value)  => PendingDifficulty = value;

        public void SetNoteSpeed(float value)
        {
            // Clamp to the same bounds as the UI (0–100).
            PendingNoteSpeed = MaestroValidation.ClampNoteSpeed(value);
        }

        public void SetHighwayLength(float value)
        {
            // Clamp to the same bounds as the UI (0.1–10).
            PendingHighwayLength = MaestroValidation.ClampHighwayLength(value);
        }

        public void SetHarmonyIndex(byte value)
        {
            // Harmony parts are 0-based; allow 0–2 (HARM 1/2/3).
            PendingHarmonyIndex = Math.Min(value, (byte) 2);
        }

        /// <summary>
        /// Toggles a single modifier flag in the pending override set.
        /// Lazily initialises <see cref="PendingModifiers"/> to the current applied
        /// value if this is the first modifier command for the profile.
        /// </summary>
        public void SetModifierFlag(Modifier flag, bool enabled, Modifier currentApplied)
        {
            Modifier basis = PendingModifiers ?? currentApplied;
            PendingModifiers = enabled
                ? (basis | flag)
                : (basis & ~flag);
        }

        /// <summary>Restore to applied snapshot — used by DiscardPending.</summary>
        public void Discard()
        {
            PendingGameMode       = null;
            PendingInstrument     = null;
            PendingDifficulty     = null;
            PendingNoteSpeed      = null;
            PendingHighwayLength  = null;
            PendingHarmonyIndex   = null;
            PendingModifiers      = null;
        }

        public void ClearGameMode() => PendingGameMode = null;
        public void ClearInstrument() => PendingInstrument = null;
        public void ClearDifficulty() => PendingDifficulty = null;
        public void ClearNoteSpeed() => PendingNoteSpeed = null;
        public void ClearHighwayLength() => PendingHighwayLength = null;
        public void ClearHarmonyIndex() => PendingHarmonyIndex = null;
        public void ClearModifiers() => PendingModifiers = null;

        /// <summary>
        /// Clears the draft after the DifficultySelect boundary has consumed it.
        /// </summary>
        public void MarkApplied()
        {
            Discard();
        }

        /// <summary>
        /// Produces a wire-friendly <see cref="MaestroPlayerPending"/> view for snapshot
        /// publication.  Uses string enum names to match the existing DTO convention.
        /// </summary>
        public MaestroPlayerPending ToPendingView()
        {
            var view = new MaestroPlayerPending
            {
                HasPending = HasPendingChanges,
            };

            if (PendingGameMode.HasValue)
                view.PendingGameMode = PendingGameMode.Value.ToString();

            if (PendingInstrument.HasValue)
                view.PendingInstrument = PendingInstrument.Value.ToString();

            if (PendingDifficulty.HasValue)
                view.PendingDifficulty = PendingDifficulty.Value.ToString();

            if (PendingHarmonyIndex.HasValue)
                view.PendingHarmonyIndex = PendingHarmonyIndex.Value;

            if (PendingNoteSpeed.HasValue)
                view.PendingNoteSpeed = PendingNoteSpeed.Value;

            if (PendingHighwayLength.HasValue)
                view.PendingHighwayLength = PendingHighwayLength.Value;

            if (PendingModifiers.HasValue)
            {
                view.PendingModifiers = ModifierNames(PendingModifiers.Value);
            }

            return view;
        }

        /// <summary>
        /// Converts a <see cref="Modifier"/> flags value into a list of individual
        /// modifier name strings for the wire DTO.
        /// </summary>
        private static List<string> ModifierNames(Modifier mods)
        {
            var names = new List<string>();
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
    }
}
