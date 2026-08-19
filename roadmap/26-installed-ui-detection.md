# Stage 26 — Installed UI Detection

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./25-legacy-of-the-dragonborn-integration.md) · [Next stage](./27-optional-ui-mod-adapters.md)

## 26. Installed UI Detection

**Status:** Planned

### Outcome

DovahLink identifies selected UI or font resources without weakening its native theme.

### Scope and behavior

- Detect only explicitly supported installations and versions.
- Expose presentation capabilities rather than filesystem paths.
- Handle conflicts, overrides, incomplete installs, and unsupported versions.
- Cache only with clear invalidation.
- Let the player return to the native theme.

### Dependencies and boundaries

Detection follows the core theme and dashboard and does not modify Skyrim files or require one mod
manager.

### Acceptance criteria

Supported installs are identified reproducibly, ambiguous setups do not activate adapters, and
failure cannot prevent startup.
