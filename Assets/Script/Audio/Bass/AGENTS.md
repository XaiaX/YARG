# BASS microphone capture

Last verified: 2026-11-01

Microphone setup previews the same capture/analyzer used by gameplay. Each device owns its analyzer even when physical input capture is shared across channels.

Contracts:
- `BeginLevelPreview()` grants an idempotently disposable lease. Preview samples raw level without enabling audible monitoring or enqueueing gameplay frames. Gameplay output remains gated by recording state.
- Pitch gating uses global sensitivity plus the owning binding's finite per-mic offset, after percussion detection. Zero offset preserves prior behavior. Raw dBFS display accounts for the existing RMS calibration gain.
- Recreating the analyzer retains the device's sensitivity callback and current preview-lease state.
- A new or intentionally idle capture remains connected. Read failure/recreation failure is explicit health state, distinct from an intentionally stopped worker; UI recovery can release and replace failed captures.
- Device disposal ends worker/source ownership; closing one preview lease cannot stop another lease or active gameplay recording.

Validation includes the actual-source audio harness in branch-level `docs/investigations/microphone-setup/audio-checks` and native microphone setup tests.
