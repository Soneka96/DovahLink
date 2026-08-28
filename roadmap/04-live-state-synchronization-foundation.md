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

### Architecture model

Stage 4 treats capture, authoritative state, and client publication as related but distinct layers:

- A **capture unit** is one value or event acquired from Skyrim. Its `CapturePolicy` is either
  `NativeEvent` or `Sampled`.
- A sampled capture unit declares a `RateClass`: `Fast`, `Medium`, or `Slow`. The rate controls the
  maximum frequency at which DovahLink checks Skyrim, not the frequency of revisions or network
  messages.
- An **authoritative state area** owns the current DovahLink value, availability, play-context
  identity, and revision sequence. It is the ordering point for applying captured values and
  assigning revisions.
- A stateful publication area declares one canonical `UpdateMode`: `Snapshot` or `Event`. A
  `Snapshot` is replaceable latest state; an `Event` is an ordered complete post-change state that
  advances from a known revision baseline.
- A **publication unit** is the client-facing representation produced from an authoritative state
  area. Capture units, state areas, and publication units may be different sizes: several captured
  values may contribute to a future composed view, while one captured event may update one focused
  state area.
- `CapturePolicy`, `RateClass`, and `UpdateMode` are Bridge-side policy metadata and are not new
  wire fields by themselves. The wire exposes registered `stateArea` contracts through the existing
  typed state messages.

The normal flow is:

```text
Skyrim event or shared sampler
        ↓
small owned captured value
        ↓
state-area ordering point and change detection
        ↓
authoritative revision
        ↓
Snapshot or Event publication
        ↓
bounded session delivery
```

These policies are orthogonal in meaning, but not every combination is valid. In particular,
reliable Event mode requires a runtime source that cannot miss semantically required occurrences;
sampling is Event-compatible only when that property is proved for the specific value. Otherwise the
value is a Snapshot domain.

### Scope and behavior

- Replace the Phase 1 request/response polling loop with full-duplex asynchronous delivery. The
  `character_xp` state area is Snapshot mode: every update is complete current XP state and
  replaceable under pressure. The `character_level` state area is Event mode: an initial snapshot
  establishes the current level and a later level change is an ordered `state_event` whose data is
  the complete post-change level state. Correlated request/response messages remain part of the
  protocol and are not removed.
- Apply the shared capture, scheduling, and ordering model defined above. Recurring capture is driven
  by an approved game-thread callback or hook; workers never defer a Skyrim read. The callback
  synchronously copies a small, validated, DovahLink-owned value, and unchanged sampled values stop
  before they create a revision, publication, queue entry, serialization work, or WebSocket traffic.
- Use the completed 4.1 typed message-family contract rather than reopening its design. Each message
  has one canonical JSON shape and a dedicated DTO/codec boundary; the remaining phase work registers
  and publishes the typed state-area data carried by those messages.
- Prefer native events for values that require reliable occurrence delivery, and sample only where no
  trustworthy event exists or where the product only needs current state.
- The Bridge must publish unsolicited replaceable state only on authoritative change.
- Always deliver initial, recovery, and explicitly requested snapshots, even when the state is unchanged;
  these snapshots reuse the current authoritative revision.
- A subscriber receives complete post-change state rather than a patch; unchanged unsolicited
  replaceable state produces no traffic.
- Separate replaceable state, ordered reliable state events, recovery/control traffic, and ephemeral
  notifications; each category follows its own contract.
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
- one authoritative ordering point per state area for applying captured values and assigning revisions
  when capture callbacks arrive from different runtime sources; serialization and network writing
  remain worker-owned after that point;
- latest-value replacement for replaceable Snapshot state;
- ordered, non-coalesced delivery for reliable Event state;
- reserved capacity for recovery/control traffic;
- a bounded data organization in which replaceable Snapshot entries are keyed by state area while
  reliable Event entries remain FIFO; Snapshot pressure may replace or defer an unsolicited value,
  but Event pressure closes the slow client rather than dropping an Event;
- explicit slow-consumer diagnostics and disconnect behavior;
- instrumentation for capture timing, depth, coalescing, enqueue/dequeue latency, recovery, and
  disconnects;
- the current security baseline of 128 outbound messages per client with 16 reserved control/recovery
  slots and 112 data slots. The implementation must also establish a bounded encoded-byte budget
  before production use; its numeric value is a profiling and approval decision, not an accidental
  consequence of the individual 1 MiB frame limit;
- explicit lane ordering: a recovery snapshot establishes the new baseline before later stateful
  events are applied, and stateful events at or below an accepted snapshot revision are discarded as
  superseded rather than delivered after the snapshot. This supersession rule does not apply to
  ephemeral notifications, whose delivery contract is independent of state snapshots.

The bounded organization has two physical lanes and three logical delivery categories. The reserved
control/recovery lane carries initial, recovery, and explicitly requested snapshots, acknowledgements,
errors, and recovery/control messages. The bounded data lane carries a finite keyed set of pending
unsolicited Snapshot values plus an ordered Event FIFO. There is one pending Snapshot slot per
registered Snapshot state area, and those slots are included in the 112-message data capacity;
replacing a slot never grows the queue. The registered-area count is fixed and bounded before a
session can publish state, and unknown client-requested areas never allocate queue state. If an
unsolicited Snapshot cannot enter because data capacity is occupied, the authoritative store retains
the latest value and sets a bounded per-area dirty marker; the latest value is admitted when a data
slot or byte budget becomes available, without issuing a catch-up burst. If a reliable Event cannot
enter because of message or byte capacity, the client is disconnected rather than losing the Event.
The encoded-byte budget is bounded independently of the message count.

Every initial or recovery snapshot establishes a per-state-area delivery barrier. The publisher
captures the authoritative baseline at revision `R`, queues that snapshot in the reserved lane, holds
later stateful Events for that area, and releases only Events newer than `R` after the snapshot has
entered the single serialized outbound order. The client does not need a snapshot acknowledgement:
ordered session writes and each Event's `baseRevision` provide the apply rule. If the session closes,
the barrier and its queue are discarded; the next session starts from a fresh snapshot.

Applying a captured value, determining whether it changed, assigning its authoritative revision, and
creating its publication intent occur at the state area's ordering point. Queue admission happens
after that point: a rejected unsolicited Snapshot remains recoverable from the store, while a rejected
reliable Event ends the session. The authoritative store and revision are not rolled back because a
client could not consume the publication.

Availability is part of authoritative state. A contract-defined transition from available to
unavailable, or from unavailable to available, is a state change and advances the area revision;
repeated equivalent unavailable samples do not. A capture failure is not converted into a plausible
value: the domain contract decides whether it is an unavailable state, a diagnostic with no state
change, or a capability failure.

The Bridge remains single-client. A reconnect receives fresh synchronization and never receives
queued events from the previous authenticated session.

Before 4.2 production implementation begins, the design must record the queue item's ownership and
size accounting, the monotonic scheduler's missed-tick behavior, the per-state-area ordering point,
the approved game-thread tick source and loading/shutdown behavior, the bounded registered-area and
Snapshot-slot policy, the control/data lane priority rules, the per-area recovery barrier, and the
encoded-byte budget. The design must also prove that a failed cutover or unavailable required
consumer leaves canonical writers unchanged. These are implementation gates, not new protocol fields.

#### 4.3 Production Progression Domains and Synchronization Kernel

Implement and test `character_xp` as `Sampled`/`Fast`/`Snapshot` and `character_level` as
`NativeEvent`/`Event`. The level domain begins with an authoritative snapshot and applies ordered
complete-state events. A level-up is represented by the new level state; consumers may derive the
transition without requiring a second event-only payload contract. Each production value must first
document its supported-runtime capture source, unavailable-value behavior, callback/thread boundary,
change-detection rule, and expected cost. Tests must also prove the shared scheduler's aligned
cadence, missed-tick behavior, optional staggering seam, unchanged-sample short-circuit, and the
mode-specific queue behavior before future domains are added.

Before either domain is published, Stage 4.3 registers its canonical state area in the protocol
schema and capabilities, defines the exact `data` shape and domain codec/validator, and adds the
shared Bridge/SDK/.NET fixtures. A domain contract records its capture unit, capture policy, rate
class when sampled, update mode, authoritative revision behavior, unavailable-value behavior,
initial/recovery snapshot behavior, and whether it is stateful or ephemeral. The existing generic
state envelope remains reusable, but production domain data must not stay an unvalidated arbitrary
JSON map once the area is registered. The domain contract also identifies whether a change is
reconstructable current state or a one-time occurrence; only the former may be recovered or
superseded by a state snapshot.

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

Run the cutover in this order:

1. Complete the registered domain data contracts, readers, codecs, and canonical fixtures.
2. Verify Bridge, SDK, .NET, and loopback consumers understand the redesigned contract while
   existing writers remain unchanged.
3. If any required consumer, validator, fixture, or runtime capture contract is not ready, leave
   canonical writers unchanged and stop the cutover with an actionable diagnostic.
4. Switch canonical Bridge and SDK writers only after every required consumer understands the new
   shapes.
5. Verify the SDK rejects an incompatible `bridgeVersion` immediately after `hello_ack`, before
   capabilities, subscriptions, snapshots, or Events.
6. Run the complete Bridge, SDK, .NET, fixture, recovery, reconnect, and slow-client scenarios.
7. Remove migration-only readers and fixtures, leaving no permanent dual protocol implementation.
8. Hand the complete phase diff to 4.5 for the version-impact audit and release cutover.

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
snapshot and does not replay the previous session's queued events. These delivery modes apply to
stateful domains; ephemeral notifications define their own ordering, acknowledgement, and retry
contract and are not made recoverable by a state snapshot.

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
synchronization contracts, the capture-policy and scheduler invariants are proven without worker-side
runtime reads, the mode-specific queue and per-area recovery-barrier behavior is proven, all required
registered domain data and cross-boundary tests pass, migration-only readers and fixtures are gone,
and the phase-end version audit has completed.
