namespace YARG.Gameplay.Player
{
    /// <summary>
    /// Player class for GameMode.PartyVocals. The base VocalsPlayer's multi-mic
    /// visual path (UpdateSingNeedle's _micNeedles/_micParticleGroups branch)
    /// owns needle and trail rendering for any free-vocals profile with 2+ mics,
    /// including this one. Phase 3 will add coordinator scoring and per-mic
    /// part-hit accumulators here; for Phase 2 this class is just a typed
    /// landing point so the VocalTrack's _partyVocalPlayerPrefab field can
    /// distinguish Party Vocals from Solo/Harmony at prefab-dispatch time.
    /// </summary>
    public sealed class PartyVocalsPlayer : VocalsPlayer
    {
        protected override bool IsPartyVocals => true;
    }
}
