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
