# 02 — Visual Material Foundation

**Status:** Complete

## Outcome

Delivered the Flutter theme and material foundation used to reproduce the prototype's visual
language across Frostbound, Dovah, and Hearth.

## Why it exists

Prototype parity required reusable theme-specific surface treatments and responsive presentation
before individual screens could be considered converged.

## Scope

The delivered foundation covers layered materials and atmospheric backgrounds; theme-specific
button, card, and connection-card surfaces; dialog backdrops and branding marks; focus and hover
behavior; visual previews; responsive layouts; and theme typography/fallback behavior.

## Non-goals

- This foundation did not complete the full app convergence or final audit.
- It did not change real SDK/domain behavior.

## Dependencies / authority

Visual recipes follow the current approved prototype and the Flutter architecture conventions in
[`ai/context/flutter/architecture.md`](../../../ai/context/flutter/architecture.md).

## Acceptance criteria

- The three themes can express their prototype-specific visual materials and component treatments.
- Responsive presentation and theme typography use the established Flutter theme/metrics boundaries.
- Visual previews and focus/hover behavior remain presentation concerns.

## History

The initial [PR #92](https://github.com/Soneka96/DovahLink/pull/92) became too large and difficult to
review and was closed. The work continued in the merged
[PR #93 — Feature/theme material foundation](https://github.com/Soneka96/DovahLink/pull/93) and
[PR #94 — Feature/prototype visual parity](https://github.com/Soneka96/DovahLink/pull/94).

## Current disposition

Complete as the visual foundation. It does not mean all screens or interactions have passed the
final canonical audit.

## Next action

Continue with the separately indexed connection/pairing convergence work after re-planning its
paused steps against current contracts.
