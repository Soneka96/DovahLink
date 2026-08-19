# Stage 8 — Live Player State

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./07-core-ui-theme-system.md) · [Next stage](./09-multi-client-runtime-foundation.md)

## 8. Live Player State

**Status:** Planned

### Outcome

The connected, themed client proves a useful single-client flow with focused character information.

### Scope and behavior

- Select approved values such as identity, level, health, magicka, stamina, location, or combat.
- Expose only values sourced reliably from the supported runtime.
- Add a versioned slice with snapshots and live updates.
- Mark unavailable, delayed, recovering, and stale values honestly.
- Reuse shared synchronization and UI foundations.

### Dependencies and boundaries

This read-only phase validates the complete single-client path. It excludes inventory, equipment,
map navigation, and commands.

### Acceptance criteria

Approved values remain accurate through play, reconnect, and play-context replacement; unavailable
state degrades clearly; and cross-side tests prove bridge/client agreement.
