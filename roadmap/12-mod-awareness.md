# Stage 12 — Mod Awareness

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./11-automatic-connection-and-transport-selection.md) · [Next stage](./13-interactive-map-foundation.md)

## 12. Mod Awareness

**Status:** Planned

### Outcome

DovahLink exposes the minimum trustworthy load-order and capability information later features need.

### Scope and behavior

- Identify only information required by approved features.
- Expose stable capabilities instead of client-side mod-name checks.
- Treat missing, unsupported, or ambiguous information as unavailable.
- Version compatibility knowledge independently where practical.
- Document privacy, performance, and failure behavior.

### Dependencies and boundaries

This phase excludes UI theme detection, LOTD behavior, a plugin platform, and broad compatibility
promises.

### Acceptance criteria

Clean and modded setups produce trustworthy capability summaries, unsupported cases fall back safely,
and features do not consume raw CommonLib details.
