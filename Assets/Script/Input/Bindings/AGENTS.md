# Profile input bindings

Last verified: 2026-11-02

Profiles retain YOLO's existing gameplay model. Microphone setup adds product-side binding metadata without changing Core, packed input actions or replay schemas.

Contracts:
- `ProfileBindings.MicrophoneBindings` is the ordered saved view, including missing devices. `Microphones` remains the compact connected gameplay view. UI setters use saved-row indices; live appearance looks up the actual device.
- Preserve identity, order, needle choice and sensitivity together during resolution, release, reorder and serialization. A compact connected index or display name alone cannot identify saved settings. Exact saved device reservations precede same-name fallback; indistinguishable devices after re-enumeration remain a hardware limitation.
- One live capture reference has one owning binding profile. Release/dispose clears ownership.
- Sensitivity offsets default to zero, remain finite and clamp to −50…+50 dB; needle zero means Auto.
- Historical serialization versions remain immutable. V4 reuses the established V3 control conversion and adds mic settings. Read V0–V3 with Auto/zero defaults. Preserve valid legacy main bytes once before migration; an invalid legacy probe must permit ordinary backup recovery.

Dependencies: `Input/MicrophoneBinding*`, `MicrophoneSetupValues`, BASS devices and the profile microphone dialog. Inactive setup closes by releasing its live captures while retaining saved rows.

Elite cymbal pairs:
- Saved bindings remain individual and profile-local; layout import/export never contains device mappings.
- Gameplay alias subscriptions coalesce only callbacks for the same physical control event and merged pair, before replay recording. Equal timestamps do not identify duplicate hits. Raw diagnostics retain every matching binding.
- A button's causal physical edge must survive deferred debounce, subthreshold updates on other controls, and same-side analog velocity changes. Axis behavior is unchanged.
- Unsubscribe/dispose releases each alias wrapper. Historical packed GameInput/replay input formats remain unchanged.
