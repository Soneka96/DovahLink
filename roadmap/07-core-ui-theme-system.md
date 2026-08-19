# Stage 7 — Core UI Theme System

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./06-pc-second-screen-baseline.md) · [Next stage](./08-live-player-state.md)

## 7. Core UI Theme System

**Status:** Planned

### Outcome

DovahLink establishes reusable Skyrim-inspired presentation before feature screens multiply.

### Scope and behavior

- Define shared Flutter tokens and components for typography, color, panels, icons, spacing, shape,
  elevation, motion, and responsive sizing.
- Cover loading, empty, unavailable, stale, recovering, and error states.
- Support contrast, text scaling, accessibility, and reduced motion.
- Apply the system to the PC baseline before broad features.
- Keep future adapters declarative and presentation-only.

### Dependencies and boundaries

The native theme works without inspecting Skyrim or requiring a UI mod. Detection, adapters, custom
theme data, and dashboard behavior remain later phases.

### Acceptance criteria

Existing surfaces use shared tokens or components and remain useful at supported sizes and
accessibility settings without optional resources.
