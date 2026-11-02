# MIDI diagnostics

Last verified: 2026-11-01

This screen-owned observer collects pedal investigation evidence without changing gameplay or saved mappings.

Contracts:
- `MidiTestCapture` accepts the selected profile's assigned physical ports/channels, including inactive profiles. Resolve Unity devices and localized bindings on the main thread.
- Callback state is managed and synchronized. `RawMidiDelivery` may deliver initial staged messages on the main thread and later messages on the RtMidi thread.
- `PortId` identifies one open connection; saved device descriptions/hashes remain unchanged. Equal names do not establish identity.
- Keep driver delta seconds separate from monotonic receipt milliseconds. Preserve original receipt timing through source-readiness staging.
- Core `MidiTestSession` snapshots matches and pedal values at receipt. Unknown CC/state remains distinct from zero/open; automatic selection requires an unambiguous mapped pedal CC.
- Aftertouch is diagnostic pressure, not a note strike or calibrated pedal position. Format2 preserves kind0–2 IDs and appends polyphonic3/channel4; channel pressure has one data byte, unused Data2=0.
- JSONL recording is opt-in and bounded. Stop/dispose drains outside the capture lock; clear-history affects only the display. Surface dropped/unconfirmed records and I/O errors.

The native consumer is `Menu/MidiTest/MidiTestMenu.cs`; the pure session lives in the Core submodule's `Input/Diagnostics`. `ButtonBinding.SuspendMidiInput()` suppresses menu actions from MIDI while preserving other controls and mappings.
