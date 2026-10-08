# Stage 8 — Live Player State

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./07-core-ui-theme-system.md) · [Next stage](./09-multi-client-runtime-foundation.md)

## 8. Live Player State

**Status:** Planned

The eight-domain live-state pipeline and state-backed Overview have landed ahead of order. Stage 8
remains open: the supplied runtime validation did not replace the active play context, and this
roadmap review records acceptance status without closing the stage.

### Outcome

The connected, themed client proves a useful single-client flow with focused character information.

### Scope and behavior

- Select and expose approved values sourced reliably from the supported runtime. The current
  connected Overview covers Character Vitals, XP, Level, Identity, Supernatural Traits, Location,
  Skyrim Game Time, and Tracked Quests. Combat remains an unselected optional candidate.
- Consume the versioned snapshot/event contract through the shared SDK and client synchronization
  foundations.
- Keep unavailable, delayed, recovering, and stale values truthful in state and presentation.
- Retain the read-only, single-client boundary; inventory, equipment, map navigation, and commands
  remain outside this phase.

### Dependencies and boundaries

This read-only phase validates the complete single-client path. It excludes inventory, equipment,
map navigation, and commands.

### Acceptance criteria

- **COMPLETE — Supported state sources:** the production path exposes Vitals, XP, Level, Identity,
  Supernatural Traits, Location, Game Time, and Tracked Quests from the supported runtime through
  Adapter, Host, protocol, Dart SDK, and Flutter state.
- **COMPLETE — Snapshot and live-update contract:** domains use the versioned state contract and
  shared synchronization metadata; deterministic Host/Adapter/client integration tests prove
  agreement for synthetic captures.
- **COMPLETE — Honest unavailable and recovery presentation:** tests cover unavailable, stale,
  recovering, failed, and restored state without fake gameplay values or developer diagnostics in
  normal Overview.
- **PARTIALLY DELIVERED — Runtime accuracy through play-context replacement:** the maintainer
  confirmed initial connection, state population, hot restart without changing Skyrim state,
  repopulation, reconnect, tracked quests, and general Overview live-state behavior. A runtime
  replacement of the active play context was not part of that validation; automated state-boundary
  tests do not replace that runtime evidence.

Stage 8 remains **Planned**. Its current work is a strong pulled-forward foundation, but the
play-context replacement criterion and whole-stage acceptance remain open.
