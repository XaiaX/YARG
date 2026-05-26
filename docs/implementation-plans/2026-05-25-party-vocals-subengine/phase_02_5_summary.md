# Phase 2.5 Summary: Post-Implementation Fixups

**Branch:** `feat/free-harmonies`
**Commits on top of:** `cd9d8479` (Phase 2)
**Plan:** `docs/implementation-plans/2026-05-25-party-vocals-subengine/phase_02_5_fixup.md`

## What changed

### Core submodule (`YARG.Core`)

**`MicDevice.cs`** — Added `public abstract string StableId { get; }` property. Every mic device now exposes a within-session-unique ID that distinguishes two physically distinct devices with the same `DisplayName` (e.g. two "Logitech USB Microphone" devices).

**`SerializedMic.cs`** — Added `public string? StableId` field alongside existing `Name`. Two-constructor shape: `SerializedMic(name)` (StableId = null, for legacy payloads) and `SerializedMic(name, stableId)` (for live devices). Old JSON without the field deserializes with `StableId = null`.

### Unity project (`Assets/Script/`)

**`Audio/Bass/BassMicDevice.cs`** — Implements `StableId` as `$"{DisplayName}@{_deviceId}"` (BASS enumeration index provides within-session uniqueness even for identical names). `Serialize()` now writes the StableId into the new field.

**`Input/Bindings/ProfileBindings.cs`** — Three changes:

1. **`AddMicrophone` API cleanup:** Removed the `gameMode` parameter. The cap is now derived from `Profile.GameMode` directly (PartyVocals → 7, anything else → 1). Internally refactored to `TryAddMicrophoneInternal` returning a structured `MicAddResult` enum (`Added`, `CapExceeded`, `DuplicateId`) so the resolver can distinguish rejection reasons.

2. **StableId-based dedup:** The duplicate check now compares `StableId` instead of `DisplayName`. Two same-name devices with different StableIds both bind successfully.

3. **Two-pass `ResolveDevices` resolver:**
   - **Pass 1:** Exact StableId match against currently-enumerated devices. Matches on the string `"{name}@{id}"` without creating BASS recording handles; only calls `CreateInputDevice` for actual bindings.
   - **Pass 2:** Name-based fallback against still-available devices, in original slot order. Handles the cross-session case where BASS reassigns device IDs after a replug.
   - **Pass 3:** Still-unmatched entries stay in `_unresolvedMics` for future device-connect events.
   - Prunes cap-exceeded and duplicate entries from `_unresolvedMics` so the next save writes a self-healing JSON.

**`Menu/ProfileInfo/EditBinds/MicBindGroup.cs`** — Picker dialog filters by StableId instead of DisplayName. Two same-name "Logitech USB Microphone" devices both appear in the picker and can both be bound.

**`Gameplay/Player/VocalsPlayer.cs`** — Three gates added:

1. `if (isPartyVocals && !IsPartyVocals)` on the legacy multi-mic needle cloning block — prevents base class from creating N legacy needles that the subclass would have to destroy.
2. Same gate on the legacy multi-mic particle group cloning block.
3. `if (IsPartyVocals) return;` at the top of `UpdateSingNeedle` — `PartyVocalsPlayer` owns its own needle drawing entirely.

**`Gameplay/Player/PartyVocalsPlayer.cs`** — Two changes:

1. **Bots use sub-engine path:** Removed the `player.Profile.IsBot` short-circuit. Bot Party Vocals profiles now construct N sub-engines with `isBot: true`, each driving its own needle via the same per-slot visual code. Slot count for bots derives from `_partyVocalsMicCount` (set by base.Initialize to the HARM part count).

2. **Removed legacy visual destruction:** The base class no longer creates legacy multi-mic visuals for `PartyVocalsPlayer` (gated behind `!IsPartyVocals`), so the destruction loops are gone.

**`Gameplay/Player/PartyVocalsMicSlot.cs`** — `Device` and `InputContext` fields changed to nullable (`MicDevice?`, `MicInputContext?`) since bot slots don't have real audio devices.

### Tests (`YARG.Core.UnitTests`)

**`Input/ProfileBindingsTests.cs`** — Updated to match new API:

- `TestableMicDevice` implements `StableId` property.
- `TestableProfileBindings` mirrors the real `ProfileBindings.AddMicrophone` logic (profile-aware cap, StableId dedup).
- New test: `SoloVocals_Profile_Rejects_Second_Mic` — verifies Solo Vocals cap of 1.
- Existing tests updated to explicitly set `GameMode.PartyVocals` for multi-mic acceptance tests.
- All 5 tests pass, full suite (490 tests) green.

## Deferred

- **Task 8 (PartyVocalsPlayer integration tests):** Requires Unity PlayMode test infrastructure. No `Assets/Tests/` directory or test assembly definitions exist.
- **Task 9 (Cross-session resolver test):** `ResolveDevices` is on `ProfileBindings` (Unity-side) and depends on `GlobalAudioHandler` statics. Not testable from Core unit tests without Unity.

## Manual smoke tests needed (Unity Editor)

1. **Two identical-name USB mics:** Bind both to a Party Vocals profile. Quit, restart. Both re-bind in slot order.
2. **Bot Party Vocals on 3-HARM song:** Three needles visible, each tracking its assigned HARM pitch (not stuck at bottom).
3. **Solo Vocals with one mic:** Still loads correctly, single needle.
4. **PartyVocalsPlayer prefab:** Must be created in Unity Editor (duplicate VocalPlayer prefab, swap script, wire into VocalTrack's `_partyVocalPlayerPrefab` field).
