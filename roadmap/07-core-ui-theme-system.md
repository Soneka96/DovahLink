# Stage 7 — Core UI Theme System

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./06-pc-second-screen-baseline.md) · [Next stage](./08-live-player-state.md)

## 7. Core UI Theme System

**Status:** Planned

The shared theme foundation and much of its application to the current PC client have landed ahead of
order. Stage 7 remains open because its final prototype screenshot comparison is still unverified.

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

- **COMPLETE — Shared presentation foundation:** current Connections, Pairing, Session Shell, and
  Overview surfaces use shared theme tokens, materials, metrics, and widgets.
- **COMPLETE — Accessibility and responsive behavior:** existing widget tests cover supported
  window sizes, overflow, tap targets, keyboard focus, text scaling, semantics, and reduced motion
  where the component animates.
- **PARTIALLY DELIVERED — Canonical visual fidelity:** code-to-code audits compare the production
  Flutter screens with the prototype, and this closeout corrects remaining Session Shell and
  Overview differences. A rendered screenshot comparison has not been completed; the final
  convergence audit remains partial.

Stage 7 remains **Planned** until that visual-fidelity acceptance is verified; the implemented theme
work is recorded as pulled forward, not as whole-stage completion.
