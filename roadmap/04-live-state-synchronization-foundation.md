# Stage 4 — Live State Synchronization Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./03-local-device-pairing-and-reconnection.md) · [Next stage](./05-dart-client-sdk-foundation.md)

## 4. Live State Synchronization Foundation

**Status:** Planned

### Outcome

The bridge pushes changing state from shared authoritative stores without requiring polling or
allowing delivery pressure to block Skyrim.

### Scope and behavior

- Replace the Phase 1 request/response polling loop for the character state area with
  full-duplex asynchronous Snapshot-mode delivery. Incremental Event-mode delivery remains outside
  the production character domain in this phase and is introduced only by later domains that
  define it as their canonical live-delivery mode. Correlated request/response messages remain
  part of the protocol and are not removed.
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

### Update mode

Each stateful domain declares exactly one canonical live-delivery mode, `UpdateMode`: `Snapshot` or
`Event`. A consumer does not choose between them per subscription; the domain's protocol definition
fixes it. `Snapshot` mode is this phase's chosen production mode for the `character` state area: every
delivered update is complete state, coalesced to latest-value under pressure, per the acceptance
criteria above. `Event`-mode domains (not implemented by this phase) still begin from an
authoritative snapshot for initial synchronization and reconnect/gap recovery; only steady-state
delivery between snapshots is ordered incremental events. This section names and fixes the concept;
it does not implement an Event-mode domain, which remains later roadmap stages' concrete choice.

### Stateful domains versus ephemeral notifications

A stateful domain represents persistent, current state and participates in authoritative
revisions, snapshots, synchronization, and recovery, per the mechanics above. An ephemeral
notification represents that something happened rather than reconstructable current state. By
being ephemeral, it does not participate in a state domain's authoritative revision/snapshot/recovery
model. Ephemeral describes state/recovery semantics only; it does not imply that a
notification is optional, unreliable, droppable, or low-priority. Delivery, ordering,
acknowledgement, and handling guarantees are defined independently by that message's own protocol
contract. `session_invalidated` (`roadmap/03`) remains the existing example: ephemeral in this
state-model sense, but security-significant and authoritative when received.

### Explicit snapshot requests

A client may request an authoritative snapshot of a stateful domain independently of whether it
currently has a continuous subscription to that domain. This defines synchronization semantics
only; it does not require implementing future domains now.

### Bounded recovery buffering

Recovery buffering for an Event-mode domain is bounded. If the bound is exceeded, the SDK abandons
that buffered recovery attempt and obtains another authoritative snapshot rather than allowing
unbounded growth or partially trusting incomplete buffered state. The numerical capacity is an
implementation/profiling decision, consistent with this document's existing guidance to select
queue capacities from profiling rather than fixing speculative values.

### Event-mode proof

This phase must implement and prove the generic Event-mode synchronization machinery through
focused automated tests, covering initial synchronization from an authoritative snapshot, ordered
apply, duplicate/stale rejection, gap detection, recovery buffering (including a later
authoritative snapshot superseding already-buffered events), bounded recovery buffering, and
treating recovery-snapshot failure as connection-unhealthy so recovery proceeds through the
applicable bounded reconnect behavior. That proof exercises the same generic engine every future
Event-mode domain will use. This does not require or introduce a
production Event-mode game domain: no Inventory, Quest, or other gameplay domain is pulled forward
into this phase to provide that proof, and no permanent public test-only domain (for example a
`TestDomain`/`TestCounter`) is added to the protocol. Test coverage instead uses existing protocol
infrastructure — the already-generic `state_event`/`state_snapshot` wire messages and reusable test
fixtures — the same way this phase's existing duplicate/stale/gap fixtures already do. The
production `character` state area remains `Snapshot`-only; this proof does not change its canonical
mode.
