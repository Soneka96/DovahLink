# Stage 4 — Live State Synchronization Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./03-local-device-pairing-and-reconnection.md) · [Next stage](./05-dart-client-sdk-foundation.md)

## 4. Live State Synchronization Foundation

**Status:** Planned

Delivery is decomposed into protocol migration, Bridge publication, internal SDK synchronization,
and final cross-boundary cutover. The protocol migration may temporarily read
the previous contract to keep independently reviewable PRs green, but the completed stage supports
only the redesigned contract.

### Outcome

The Bridge and its client-side synchronization foundation push changing state from shared
authoritative stores without requiring polling or allowing delivery pressure to block Skyrim. The
first production domains are deliberately small: `character_xp` uses Snapshot mode and
`character_level` uses Event mode. The existing aggregate `character` area is retired in the
redesigned contract; a future phase may add it as a composed character view without creating a
second authority for progression values.

### Scope and behavior

- Replace the Phase 1 request/response polling loop with full-duplex asynchronous delivery. The
  `character_xp` state area is Snapshot mode: every update is complete current XP state and
  replaceable under pressure. The `character_level` state area is Event mode: an initial snapshot
  establishes the current level and a later level change is an ordered `state_event` whose data is
  the complete post-change level state. Correlated request/response messages remain part of the
  protocol and are not removed.
- Represent capture, sampling, and delivery as separate policy decisions. Each captured value has a
  `CapturePolicy` (`NativeEvent` or `Sampled`); sampled values additionally declare a `RateClass`
  (`Fast`, `Medium`, or `Slow`); each stateful publication area declares one canonical `UpdateMode`
  (`Snapshot` or `Event`). These policies are related but must not be collapsed into one combined
  mode: how Skyrim supplies a value is not the same question as how the client receives it.
- Use one shared, bounded sampler for recurring capture. Rate classes are maximum capture
  frequencies, not publication or network-send cadences. A provisional scheduling hypothesis is
  `Fast = 1 second`, `Medium = 2 seconds`, and `Slow = 3 seconds`, subject to profiling rather than
  becoming a protocol constant. With those periods, an aligned cadence captures Fast/Medium/Slow at
  second 0, Fast at 1, Fast/Medium at 2, Fast/Slow at 3, Fast/Medium at 4, Fast at 5, and all three
  again at second 6. The shared scheduler may stagger due captures when profiling shows that an
  aligned batch creates a noticeable game-thread spike; either way, capture work remains bounded.
- Run both native-event capture and sampled capture through approved game-thread callbacks or hooks.
  The callback synchronously copies a small, validated, DovahLink-owned value; workers never defer
  a Skyrim read. Unchanged sampled values stop before they create a revision, publication, queue
  entry, serialization work, or WebSocket traffic.
- Keep capture units, authoritative state areas, and publication units distinct. Independently
  captured values may update independently owned state areas; a future composed view may combine
  them without becoming a second authority. The retired aggregate `character` area is not revived
  by this phase.
- A reliable Event mode requires a capture source that cannot silently miss semantically required
  occurrences. Sampling may feed Event mode only when the supported runtime proves that sampling
  cannot miss an occurrence; otherwise sampled data is classified as Snapshot mode.
- Redesign the protocol as typed message families rather than one broadly nullable message model.
  Each message has one canonical JSON shape and a dedicated DTO/codec boundary. The redesign covers
  connection, pairing, state, error, invalidation, and control messages.
- Prefer native events and sample only where no trustworthy event exists.
- Treat rate classes as maximum frequencies and publish unsolicited replaceable state only on
  authoritative change.
- Separate replaceable state, ordered reliable events, and recovery/control traffic.
- Coalesce replaceable state to its latest value under pressure.
- Always deliver initial, recovery, and explicitly requested snapshots, even when the state is
  unchanged; these snapshots reuse the current authoritative revision.
- A subscriber receives complete post-change state rather than a patch. Initial, recovery, and
  explicitly requested snapshots are always delivered, while unchanged unsolicited replaceable state
  produces no traffic.
- Slow-client behavior is explicit: a client that cannot consume them in time is explicitly disconnected without stalling Skyrim or healthy clients.
- Keep game-thread capture small and perform network I/O and serialization elsewhere.
- Instrument capture, queues, coalescing, disconnects, and recovery before tuning thresholds.
- Establish trustworthy runtime capture contracts before adding each value. XP fields are not
  guessed: the exact fields and unavailable-value behavior are fixed only after the supported
  Skyrim runtime proves that the values can be captured reliably.

### Dependencies and boundaries

This phase depends on Phases 2 and 3 and keeps the one-connected-client limit. Heavy resources remain
outside the live stream. The redesigned contract supersedes the current `0.3.x` wire contract at
cutover; the new Bridge release is expected to use `0.4.x` before `1.0.0` because the redesign is a
breaking minor release under the repository's pre-1.0 compatibility policy.

Reliable-event delivery is scoped to one authenticated session. Reconnection establishes fresh
state snapshots and does not replay the previous session's queued events. Durable cross-session
replay requires a separately approved acknowledgement and persistence contract. The Bridge only
advertises its `bridgeVersion` in `hello_ack`; the SDK owns compatibility comparison and user-facing
incompatibility explanation. The Bridge does not reject SDK versions.

### Protocol migration and compatibility

The current protocol remains the repository's current wire contract until the migration begins. The
migration uses a temporary boundary adapter so staged PRs can remain independently testable:

1. New typed DTOs and family codecs learn to read the old and redesigned shapes while existing
   writers continue emitting the old shape.
2. Bridge and SDK writers switch to the redesigned shape only after all required consumers and the
   independent .NET validator understand it.
3. The final cleanup removes old readers and fixtures before Stage 4 is complete; no permanent dual
   protocol implementation remains.

Compatibility is evaluated against the Bridge release version sent in `hello_ack`:

- before `1.0.0`, major and minor must match and patch is ignored (`0.3.x` does not mix with `0.4.x`);
- after `1.0.0`, the major must match, a Bridge minor greater than the SDK's accepted minor is
  rejected, and patch is ignored;
- Bridge and SDK package versions remain independent; the SDK declares which Bridge versions it
  accepts.

Release versions are not bumped in ordinary phase PRs. The phase-completion audit performs the
Bridge version cutover and updates the compatibility/changelog records. A later bugfix may invoke
the version-audit skill manually; a contract-breaking bugfix is not silently classified as a patch.

### Phase breakdown

#### 4.1 Typed Protocol Contract Redesign and Migration

**Status:** Complete

Create the typed message-family contract for connection, pairing, state, error, invalidation, and
control messages. Keep a small shared header and give each complete message its own DTO, generated
structural JSON serialization, and handwritten semantic validation. Unknown optional fields remain
forward-compatible; missing or invalid required fields fail closed.

Temporary old/new boundary readers are allowed only for this migration. The phase does not add a
permanent protocol-generation field: the Bridge's `bridgeVersion` remains the compatibility signal,
and the SDK performs the comparison before capabilities or state traffic.

Completion requires canonical fixtures for every redesigned message family, updated Bridge/SDK/.NET
adapters, explicit rejection behavior for the retired contract, and a removal plan for every
temporary compatibility reader.

#### 4.2 Bridge Live Publication and Bounded Transport

Add the Bridge-side publisher path from authoritative state stores to the full-duplex session writer.
Game callbacks capture trustworthy values and update owned stores; they do not serialize JSON or
touch WebSocket state. A worker-owned publisher builds typed snapshots/events and submits them to a
bounded outbound organization with:

- a shared capture-policy registry and cadence scheduler for sampled values, plus explicit native
  event adapters;
- one serialized publication path per state area so state changes and revisions have a deterministic
  order even when capture callbacks arrive from different runtime sources;
- latest-value replacement for replaceable Snapshot state;
- ordered, non-coalesced delivery for reliable Event state;
- reserved capacity for recovery/control traffic;
- explicit slow-consumer diagnostics and disconnect behavior;
- instrumentation for capture timing, depth, coalescing, enqueue/dequeue latency, recovery, and
  disconnects;
- explicit queue bounds in both message count and encoded-byte size. The current security baseline
  is 128 outbound messages per client with 16 reserved control/recovery slots and 112 event slots;
  changing that baseline requires the documented approval and rationale rather than a silent tuning
  change;
- explicit lane ordering: a recovery snapshot establishes the new baseline before later events are
  applied, and events at or below an accepted snapshot revision are discarded as superseded rather
  than delivered after the snapshot.

The Bridge remains single-client. A reconnect receives fresh synchronization and never receives
queued events from the previous authenticated session.

#### 4.3 Production Progression Domains and Synchronization Kernel

Implement and test `character_xp` as `Sampled`/`Fast`/`Snapshot` and `character_level` as
`NativeEvent`/`Event`. The level domain begins with an authoritative snapshot and applies ordered
complete-state events. A level-up is represented by the new level state; consumers may derive the
transition without requiring a second event-only payload contract. Each production value must first
document its supported-runtime capture source, unavailable-value behavior, callback/thread boundary,
change-detection rule, and expected cost.

Future domains are not pulled into this phase, but their intended classification is now explicit:
health, stamina, and similar continuously changing values are likely sampled Snapshot domains;
quest completion and item-acquired/removed occurrences are candidates for native reliable Event
domains; and a current inventory view may be a Snapshot domain even if item-change Events are also
introduced later. Every future domain still requires its own runtime proof and protocol decision.

In the SDK synchronization kernel, prove snapshot baselines, Event-mode ordered apply,
duplicate/stale rejection, revision-gap detection, recovery buffering, snapshot supersession,
bounded recovery buffering, and recovery failure entering the existing bounded reconnect path.
This kernel remains reusable and internal until Stage 5 exposes its curated public API.

#### 4.4 Cross-Boundary Cutover and Cleanup

Run Bridge, SDK, .NET, canonical-fixture, and loopback integration scenarios together. Switch the
canonical writers to the redesigned contract, verify the SDK rejects incompatible `bridgeVersion`
values before normal traffic, remove old readers and migration-only fixtures, and close the stage
only after the final version-impact audit and Bridge release cutover succeed.

#### 4.5 Version-Impact Audit Foundation

Create the manually invoked version-audit skill and its repository documentation before Stage 4
closure. The skill reads the phase or bugfix diff, affected public exports, protocol/schema changes,
persistence formats, security/runtime behavior, tests, and current version ownership. It may update
the relevant Bridge, SDK, or Flutter version/changelog/compatibility files and prepare a commit
message, but it never commits.

At Phase 4 completion it audits the complete phase rather than each ordinary PR. A later bugfix may
invoke it independently; a contract-breaking bugfix must not be forced into a patch bump.

### Update mode

Each stateful domain declares exactly one canonical live-delivery mode, `UpdateMode`: `Snapshot` or
`Event`. A consumer does not choose between them per subscription; the domain's protocol definition
fixes it. Capture policy and rate class remain separate from this choice. `character_xp` is
`Sampled`/`Fast`/`Snapshot`: every delivered update is complete state and coalesced to latest-value.
`character_level` is `NativeEvent`/`Event`: it begins from an authoritative snapshot for initial
synchronization and reconnect/gap recovery, then applies ordered complete-state events. Event mode
is reliable within one authenticated session; reconnect starts from the current authoritative
snapshot and does not replay the previous session's queued events.

### Stateful domains versus ephemeral notifications

A stateful domain represents persistent, current state and participates in authoritative revisions,
snapshots, synchronization, and recovery, per the mechanics above. An ephemeral notification
represents that something happened rather than reconstructable current state. By being ephemeral, it
does not participate in a state domain's authoritative revision/snapshot/recovery model. Ephemeral
describes state/recovery semantics only; it does not imply that a notification is optional, unreliable,
droppable, or low-priority. Delivery, ordering, acknowledgement, and handling guarantees are defined
independently by that message's own protocol contract. `session_invalidated` (`roadmap/03`) remains
the existing example: ephemeral in this state-model sense, but security-significant and authoritative
when received.

### Explicit snapshot requests

A client may request an authoritative snapshot of a stateful domain independently of whether it
currently has a continuous subscription to that domain. This defines synchronization semantics only;
it does not require implementing future domains now.

### Bounded recovery buffering

Recovery buffering for an Event-mode domain is bounded. If the bound is exceeded, the SDK abandons
that buffered recovery attempt and obtains another authoritative snapshot rather than allowing
unbounded growth or partially trusting incomplete buffered state. The numerical capacity is an
implementation/profiling decision, consistent with this document's existing guidance to select
queue capacities from profiling rather than fixing speculative values.

### Event-mode proof

This phase must implement and prove the generic Event-mode synchronization machinery through focused
automated tests, using the production `character_level` domain as the first small Event-mode example.
Coverage includes initial synchronization from an authoritative snapshot, ordered apply,
duplicate/stale rejection, gap detection, recovery buffering, a later authoritative snapshot
superseding buffered events, bounded recovery buffering, and recovery-snapshot failure entering the
bounded reconnect path. No test-only public protocol domain is added.

### Completion criteria

Stage 4 is complete only when the redesigned contract is the sole supported contract, Bridge live
delivery is bounded and observable, `character_xp` and `character_level` meet their source and
synchronization contracts, the internal SDK kernel passes its recovery proof, all required
cross-boundary tests pass, and the phase-end version audit has completed.
