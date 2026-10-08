# Stage 6 — PC / Second-Screen Baseline

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./05a-android-wifi-development-path.md) · [Next stage](./07-core-ui-theme-system.md)

## 6. PC / Second-Screen Baseline

**Status:** Planned

Implementation has been pulled forward into the Session Shell and live-state Overview. The audit below
records delivered work without closing Stage 6 ahead of Stage 5 or treating implementation order as
phase completion.

### Outcome

The first native Flutter product client connects on the same PC and makes connection, recovery, and
sample state understandable without developer documentation.

### Scope and behavior

- Establish the same-PC, single-client desktop workflow in the Flutter client.
- Connect through the Dart Client SDK's public API rather than app-private protocol/client code,
  proving the SDK sufficient to build a complete connected client.
- Present connection, recovery, compatibility, unavailable, stale, and disconnected states
  truthfully at the player-facing surface that owns the user's next decision.
- Keep the client useful when Skyrim is absent or optional state is unavailable.
- Add only the client structure needed by this thin connected slice.

### Dependencies and boundaries

This phase validates Phases 2 through 5. It remains loopback-only and excludes automatic discovery,
mobile packaging, dashboard customization, and actions. Character and world-state coverage pulled
forward from Stage 8 does not broaden or close this baseline.

### Acceptance criteria

### Acceptance review against current implementation

- **COMPLETE — Same-PC connection:** the Windows Flutter client connects through the public SDK API.
  The current CI workflow analyzes, tests, and builds the Windows client.
- **COMPLETE — Trustworthy state:** the Session Overview displays synchronized Skyrim values and
  truthful unavailable state; no prototype or test values are substituted in production.
- **COMPLETE — Recovery and stale-context rejection:** the maintainer confirmed reconnect and
  hot-restart repopulation. Host and Adapter tests cover context-bound and stale-baseline rejection;
  SDK and app tests cover synchronization recovery and its presentation.
- **PARTIALLY DELIVERED — Comprehension without developer guidance:** connection, compatibility,
  unavailable, stale, and recovery copy is tested at its actionable UI boundary. No dedicated
  usability validation establishes that an unfamiliar player can understand every failure.

Stage 6 remains **Planned**. Its core connection and state behavior has landed ahead of order, while
the player-comprehension criterion still needs evidence and Stage 5 has not closed.
