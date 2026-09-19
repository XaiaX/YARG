# Deferred: Shader-Driven Harmony Count-In Pulse

## Context

The current Party Vocals harmony count-in uses Core's deterministic per-lane
`PulseStrength` timeline to determine its visual color in
`Assets/Script/Gameplay/HUD/VocalsPlayerHUD.cs`. The scheduler remains the
source of truth for whether a lane is eligible to count in, its target onset,
and its count-in window.

A Discord suggestion noted that visual beat pulsing may not need a dedicated C#
subscription that writes a material/shader parameter: existing global shader
state can provide the beat phase directly to a shader.

## Existing supporting infrastructure

`Assets/Script/Gameplay/TextureManager.cs` already places beat progress in the
global gameplay-state shader texture, including quarter-note progress and
measure progress. Unity also has established `Shader.SetGlobal*` patterns.

## Future direction

When the harmony meter rendering uses a suitable shader/material pipeline,
consider moving the **purely visual** count-in pulse interpolation into that
shader. The shader can combine:

- lane color and count-in/rest endpoints;
- a per-lane count-in-enabled flag and/or count-in progress supplied by the
  existing HUD state;
- globally available beat/denominator phase.

This could remove per-frame C# color interpolation for the pulse itself and
keep all pulse-driven HUD elements phase-consistent.

## Constraints

- Do **not** move count-in eligibility, canonical phrase association, target
  onset selection, or count-in start/end timing into a shader. Those remain
  chart-aware Core scheduler responsibilities.
- The current Core `PulseStrength` supports denominator-aware schedules and
  mixed time signatures. A shader implementation must preserve that behavior
  or retain Core-supplied pulse phase; quarter-note global state alone is not a
  sufficient replacement for all denominator schedules.
- This is a future rendering optimization/design task, not a change requested
  for the current count-in fix.

## Separate deferred UI consistency: advanced score summary order

Gameplay vertically lays out Party Vocals harmony meters in musical order:
**HARM2 (high) → HARM1/lead → HARM3 (low)**. The advanced end-of-song Party
Vocals summary must use the same visual ordering.

The likely implementation point is
`Assets/Script/Menu/ScoreScreen/ScoreCards/VocalsPhraseHistogram.cs`. Its party
bar construction currently follows raw part-index order (`HARM1`, `HARM2`,
`HARM3`) when assigning vertical bands and colors. A future change should apply
an explicit presentation-order mapping—without changing stored `PartIndex`,
scoring, tally semantics, or raw chart-part associations—so its top-to-bottom
bands match the gameplay HUD:

```text
raw part index:  1      0            2
shown as:       HARM2  HARM1/lead   HARM3
vertical order: top    middle       bottom
```

Validate the result with solo, duet, and trio charts, including charts with a
missing/empty harmony lane, and confirm the score summary colors and tallies
remain associated with their original raw parts.

## Separate deferred HUD layout: lead-only Party Vocals

When a player selects Party Vocals for a chart containing only the lead
`VOCALS` part (no actual HARM2/HARM3 content), hide the entire gameplay harmony
meter container, including the HARM1 meter. The regular combo meter already
represents the sole lead-vocal performance, so an additional HARM1 meter is
redundant and leaves an unnecessary gap below the star-power meter.

Use the resolved chart/coordinator part availability rather than the selected
Party Vocals mode alone: duet and trio layouts must remain visible. The current
duet presentation is already top-aligned and should **not** be re-packed,
centered, or otherwise moved.

Do **not** move notifications or the player-name display when hiding the
lead-only harmony container. They should remain in their established locations,
where players expect them. This future task changes only the visibility of the
redundant lead-only harmony-meter area.

Validate lead-only, duet, and trio charts in Party Vocals mode, including a
chart with an empty/malformed harmony upgrade lane; lead-only hides the whole
container, while duet/trio preserve their current placement and behavior.
