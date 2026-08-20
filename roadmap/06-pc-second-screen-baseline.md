# Stage 6 — PC / Second-Screen Baseline

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./05-dart-client-sdk-foundation.md) · [Next stage](./07-core-ui-theme-system.md)

## 6. PC / Second-Screen Baseline

**Status:** Planned

### Outcome

The first native Flutter product client connects on the same PC and makes connection, recovery, and
sample state understandable without developer documentation.

### Scope and behavior

- Extend the Phase 0.5 shell into a connected desktop-sized client.
- Connect through the Dart Client SDK's public API rather than app-private protocol/client code,
  proving the SDK sufficient to build a complete connected client.
- Present connecting, connected, recovering, incompatible, unavailable, stale, and disconnected
  states clearly.
- Keep the client useful when Skyrim is absent or optional state is unavailable.
- Add only the client structure needed by this thin connected slice.

### Dependencies and boundaries

This phase validates Phases 2 through 5. It remains loopback-only and excludes automatic discovery,
broad player state, mobile packaging, dashboard customization, and actions.

### Acceptance criteria

The desktop client connects, shows trustworthy sample state, reconnects after interruption, rejects
stale context, and explains actionable failures without developer guidance.
