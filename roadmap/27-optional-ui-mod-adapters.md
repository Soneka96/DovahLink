# Stage 27 — Optional UI Mod Adapters

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./26-installed-ui-detection.md) · [Next stage](./28-safe-companion-authorization-foundation.md)

## 27. Optional UI Mod Adapters

**Status:** Planned

### Outcome

Players may complement supported Skyrim UI setups without coupling behavior to those mods.

### Scope and behavior

- Add opt-in adapters for approved versions.
- Map approved presentation values into the theme boundary.
- Define ownership and fallback for every value.
- Evaluate a constrained versioned `dovahlink-theme.json` before implementation approval.
- Reject executable behavior and protocol changes from theme data.

### Dependencies and boundaries

Adapters cannot replace dashboard structure, behavior, protocol models, or executable code.

### Acceptance criteria

Adapters pass visual, fallback, accessibility, and version checks; invalid resources fall back to
the native theme.
