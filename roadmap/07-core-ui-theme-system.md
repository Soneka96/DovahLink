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
- Complete a visual-fidelity pass against the canonical prototype, `DovahLink-Prototype-final`
  (`index.html`, `assets/themes.css`, `assets/branding.js`), restoring component-specific
  textures, layered and inset shadows, geometry, hover and focus states, typography stacks, and
  theme-specific atmosphere where the Flutter foundation currently uses simplified treatments. The
  reusable materials, atmosphere, dialog backdrop, appearance preview, connection cards, and
  pairing marks belong to this pass; fidelity that belongs to a feature screen that does not exist
  yet (session and overview panels, and character or map artwork) lands with that screen.

### Dependencies and boundaries

The native theme works without inspecting Skyrim or requiring a UI mod. Detection, adapters, custom
theme data, and dashboard behavior remain later phases.

### Acceptance criteria

Existing surfaces use shared tokens or components and remain useful at supported sizes and
accessibility settings without optional resources.
