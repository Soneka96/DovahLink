# Stage 21 — Customizable Dashboard

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./20-favorites-and-hotkeys.md) · [Next stage](./22-secure-lan-transport-and-network-discovery.md)

## 21. Customizable Dashboard

**Status:** Planned

### Outcome

Players arrange delivered modules for their second-screen workflow.

### Scope and behavior

- Provide movable and resizable modules for delivered features.
- Enforce desktop grid, minimum-size, overflow, and responsive constraints.
- Save, restore, migrate, reset, and recover local preferences.
- Keep dashboard and navigation state local to each client.
- Keep the default dashboard useful without configuration.

### Dependencies and boundaries

This phase depends on the core theme and real modules. It excludes cloud layouts, arbitrary widgets,
and protocol-level dashboard configuration.

### Acceptance criteria

Valid layouts persist and recover, modules cannot be resized into broken states, and corrupt
preferences fall back safely.
