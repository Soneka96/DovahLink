# Stage 4 — Live State Synchronization Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./03-local-device-pairing-and-reconnection.md) · [Next stage](./05-dart-client-sdk-foundation.md)

## 4. Live State Synchronization Foundation

**Status:** Planned

### Outcome

The bridge pushes changing state from shared authoritative stores without requiring polling or
allowing delivery pressure to block Skyrim.

### Scope and behavior

- Replace the Phase 1 request/response loop with full-duplex asynchronous delivery.
- Prefer native events and sample only where no trustworthy event exists.
- Treat rate classes as maximum frequencies and publish unsolicited replaceable state only on
  authoritative change.
- Separate replaceable state, ordered reliable events, and recovery/control traffic.
- Coalesce replaceable state to its latest value under pressure.
- Always deliver initial, recovery, and explicitly requested snapshots, even when the state is
  unchanged; these snapshots reuse the current authoritative revision.
- Keep game-thread capture small and perform network I/O and serialization elsewhere.
- Instrument capture, queues, coalescing, disconnects, and recovery before tuning thresholds.

### Dependencies and boundaries

This phase depends on Phases 2 and 3, uses only the Phase 1 `character` state area, and keeps the
one-connected-client limit. Heavy resources remain outside the live stream.

Reliable-event delivery is scoped to one authenticated session. Reconnection establishes fresh
state snapshots and does not replay the previous session's queued events. Durable cross-session
replay requires a separately approved acknowledgement and persistence contract.

### Acceptance criteria

A subscriber receives an initial snapshot followed by complete post-change state rather than a patch;
initial, recovery, and explicitly requested snapshots are delivered even when unchanged, while
unchanged unsolicited replaceable state produces no traffic; replaceable state coalesces, reliable
events stay ordered, and a client that cannot consume them in time is explicitly disconnected
without stalling Skyrim.
