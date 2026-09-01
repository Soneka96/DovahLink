# 4.2 Bridge Live Publication and Bounded Transport

**Status:** HISTORICAL / PAUSED REFERENCE

This plan is retained for its record of the Bridge live-publication and bounded-transport
decisions; it is not the active implementation track and does not by itself authorize continuing
Bridge production work. The active replacement is the standalone C# host and thin native adapter
migration owned by `host/PLAN.md`, which treats `bridge/` as frozen reference behavior until its own
Stage 7 conformance gate and Stage 8 cutover. For current status and direction, see:

- `ROADMAP.md` for product-stage order and status.
- `host/PLAN.md` for the active Host/Adapter replacement migration, its stages, and the disclaimer
  that owns implementation authority for that track.
- The phase package under `plans/` for the current phase's authorized scope (for example
  `plans/stage-4-host-client-boundary-and-pairing/` for Stage 4).

## Selected decisions

- The Bridge uses a bounded session registry from 4.2 onward. Its capacity is controlled by
  `bridge/security/constants.hpp`'s `kMaxConnectedClients`, which remains `1` for 4.2.
- Capture policy, cadence, authoritative state, and revisions are shared at Bridge/play-context
  scope. Each authenticated session has independent delivery state: session identity,
  capabilities, subscriptions, publisher binding, outbound queue, recovery barriers, and
  diagnostics.
- When no session is connected, authoritative state continues to update. Reliable Events are not
  retained across sessions; the next session receives fresh current Snapshots.
- 4.2 does not implement concurrent clients, fan-out to multiple sessions, LAN discovery, or
  multi-Bridge behavior. Stage 9 raises the session capacity and proves independent multi-client
  delivery using this same ownership shape.
- `0.3.2` is treated as historical/unreleased and unsupported. The redesigned contract does not
  preserve the old `0.3.x` wire contract as a compatibility target.

## Stage 1: Game-Thread Capture Components
- [x] Complete

**Scope:**
Add the Bridge-side capture components that will feed the live-publication pipeline. The components
define the game-thread capture boundary, trustworthy-value handoff, shared capture-policy registry,
cadence scheduler for sampled values, and explicit adapters for native-event captures. Production
registration and runtime wiring are completed in Stage 5.

**Acceptance criteria:**
- A shared capture-policy registry and cadence scheduler exists for sampled values.
- An explicit native-event adapter component exists alongside the sampled-capture scheduling
  components.
- Game callbacks capture trustworthy values and update owned stores without serializing JSON or
  touching WebSocket state.

**Not in scope:** production composition, building typed snapshots/events from captured values, or
delivering them over the transport (Stages 2, 3, and 5).

**Depends on:** None

**Notes:** Component foundation complete. The production callback, lifecycle, and worker handoff
must still satisfy Stage 5's integration gate.

## Stage 2: Worker-Owned Publisher Component
- [x] Complete

**Scope:**
Add the Bridge-side worker-owned publisher component. It builds typed snapshots/events from values
captured in Stage 1 and submits them toward the bounded outbound organization built in Stage 3.
Publication is serialized per state area so state changes and revisions have a deterministic order
even when capture callbacks arrive from different runtime sources. Replaceable Snapshot state uses
latest-value replacement; reliable Event state is delivered ordered and non-coalesced. Production
session wiring is completed in Stage 6, with live-boundary proof in Stage 7.

**Acceptance criteria:**
- One serialized publication path exists per state area, giving state changes and revisions a
  deterministic order even when capture callbacks arrive from different runtime sources.
- Replaceable Snapshot state uses latest-value replacement.
- Reliable Event state is delivered ordered and non-coalesced.

**Not in scope:** production session wiring, the outbound queue's bounds, lanes, and
slow-consumer/disconnect behavior (Stage 3 and Stage 6); capture itself (Stage 1).

**Depends on:** Stage 1

**Notes:** Component foundation complete. The real session must not be considered live until Stage 5
and Stage 6 prove worker ownership and full-duplex writer integration.

## Stage 3: Bounded Outbound Transport Component
- [x] Complete

**Scope:**
Build and test the bounded outbound organization that the publisher (Stage 2) submits typed
snapshots/events into. The component reserves capacity for recovery/control traffic separately from
event traffic, and is explicitly bounded in both message count and encoded-byte size. Lane ordering,
Snapshot replacement, Event reliability, slow-consumer handling, and session-scoped queue ownership
are component contracts. Production session creation, reconnect behavior, and live transport proof
are completed in Stage 6 and Stage 7.

**Acceptance criteria:**
- Reserved capacity exists for recovery/control traffic, separate from event traffic.
- Explicit queue bounds exist in both message count and encoded-byte size, with the current
  security baseline of 128 outbound messages per client, 16 reserved control/recovery slots, and
  112 data slots; changing the baseline requires documented approval and rationale.
- Lane ordering is explicit: a recovery snapshot establishes the new baseline before later events
  are applied, and events at or below an accepted snapshot revision are discarded as superseded
  rather than delivered after the snapshot.
- Slow-consumer diagnostics and disconnect behavior are explicit.
- The queue is session-scoped so a new queue instance can provide fresh synchronization without
  inheriting queued Events from a previous session.

**Not in scope:** production session creation and reconnect proof (Stage 6 and Stage 7), or
instrumentation/metrics emission (Stage 4 and Stage 7).

**Depends on:** Stage 2

**Notes:** Component foundation complete. Stage 6 must prove that the queue is bounded and correctly
owned by the real authenticated session.

## Stage 4: Publication Instrumentation Components
- [x] Complete

**Scope:**
Add instrumentation components for the capture, publisher, and bounded transport paths built in
Stages 1-3 so their behavior can be observed before tuning thresholds. The component foundation
covers capture timing, queue depth, coalescing, dequeue latency, recovery, and disconnects.
Production enqueue-latency measurement and diagnostic wiring are completed in Stage 6, then proven
in Stage 7.

**Acceptance criteria:**
- Instrumentation components exist for capture timing, queue depth, coalescing, dequeue latency,
  recovery, and disconnects.

**Not in scope:** production diagnostic composition or using the instrumentation to set or tune the
queue-bound baseline — that remains a documented, separately approved change per Stage 3.

**Depends on:** Stage 1, Stage 2, Stage 3

**Notes:** Component foundation complete. Stages 6 and 7 must prove that every required production
path emits the documented signals.

## Stage 5: Production Capture and Lifecycle Composition
- [ ] Complete

**Scope:**
Connect the capture-policy registry, cadence scheduler, native-event adapters, authoritative
state-area ordering, worker handoff, bounded registered-area policy, and publication components to
the real coordinator and plugin lifecycle. This stage establishes the production ownership and
thread boundaries before the session writer is replaced.

**Acceptance criteria:**
- Before production implementation proceeds, the design is recorded in the owning documents:
  runtime, callback, worker, and shutdown decisions in `ai/context/skse/architecture.md`;
  transport limits and recovery/security decisions in `ai/context/protocol/security.md`; and
  implementation-specific ownership and size-accounting details in the owning Bridge source
  documentation. The decisions are not left only in `PLAN.md` or implementation comments.
- The design record explicitly defines queue-item ownership, encoded-byte accounting, scheduler
  alignment, missed-tick behavior, the per-state-area ordering point, the approved game-thread
  tick source, loading behavior, shutdown behavior, the fixed registered-area bound, one Snapshot
  slot and one dirty marker per registered Snapshot area, the Normal/Heavy threshold, the Heavy
  four-slot policy, control-lane priority, per-area recovery barriers, the encoded-byte budget,
  and failed-cutover behavior when a required consumer is unavailable.
- The design record names the maintainer-approved numeric values for the 16 reserved
  control/recovery slots, 108 Normal data slots, 4 Heavy data slots, encoded-byte budget, and
  Normal/Heavy classification threshold, with profiling and approval rationale.
- The production plugin constructs and injects the stable capture-policy registry, cadence
  scheduler, bounded registered-state-area policy, authoritative state/revision dependencies,
  shared publisher, active-session publication router, publication diagnostics, and a per-session
  composition factory through explicit project-owned contracts. The per-session factory creates
  only the session delivery record, bounded outbound organization, and writer after authentication,
  binding them to that session's socket and `sessionId`; it attaches that record to the shared
  publisher through the router.
- The session registry is collection-shaped and bounded by `kMaxConnectedClients` from
  `bridge/security/constants.hpp`; its approved 4.2 value is `1`. The limit is an admission policy,
  not a reason to store one global queue, subscription set, recovery barrier set, or publisher
  binding.
- Shared authoritative state and revisions remain valid when no session is connected. The active
  session router delivers current Snapshot state to a new session, while reliable Event delivery
  is limited to the authenticated session that owns its queue and is never replayed across sessions.
- The approved game-thread callback is registered only after the runtime is ready, skips capture
  while loading or when no play context is active, and is unregistered before coordinator
  shutdown completes.
- The sampled-capture callback uses the shared scheduler, skips missed instants without a
  catch-up burst, captures only bounded validated owned values, and hands those values to worker
  processing without deferring a runtime read.
- Native-event callbacks use explicit adapters and the same owned-value handoff boundary.
- Applying a captured value, determining change, assigning the authoritative revision, and
  creating the publication intent occur in one per-state-area ordering point, even when captures
  arrive from different runtime sources.
- Availability transitions are represented as authoritative state changes: available-to-
  unavailable and unavailable-to-available advance the area revision once, while equivalent
  repeated unavailable captures do not. Capture failures never become plausible values.
- Game-thread callbacks perform only approved runtime reads, validation, owned-value capture, and
  handoff. They do not serialize JSON, encode envelopes, access WebSocket state, or perform
  worker-side runtime reads.
- Worker code performs publication construction, JSON encoding, size classification, and queue
  admission.
- Every new behavior-bearing consumer uses its project-owned contract and constructor injection;
  the publisher does not depend on a concrete revision-tracker implementation.

**Not in scope:** replacing the authenticated session writer or proving live socket delivery;
those belong to Stage 6 and Stage 7. Registering the five production character domains and
defining their exact domain data contracts belongs to Phase 4.3.

**Depends on:** Stage 1, Stage 2, Stage 3, Stage 4

**Notes:** If the approved runtime callback, lifecycle boundary, registered-area policy, or
worker handoff is not available, leave canonical writers unchanged and do not advance this stage.

The capture-side redesign in this branch is recorded in `ai/context/skse/architecture.md`'s
"Production capture and lifecycle composition" and `bridge/README.md`'s matching section: an atomic
guard-then-pin capture lease, `CaptureWorkItem` pinned to the specific play context it was captured
against, `CaptureDispatchWorker::Dispatch` as the real single per-state-area ordering point
(apply/change-detect via the item's own closure, then `StatePublisher::PublishCapture`'s atomic
baseline decision and revision assignment), per-play-context revision ownership replacing the prior
process-lifetime tracker, and mode-aware capture-queue-rejection diagnostics. This stage is **not**
marked complete; four acceptance criteria are known unmet, not silently resolved:

- The approved 2 MiB encoded-byte budget and 4 KiB Normal/Heavy threshold are
  documented as provisional values but have not been profiled against a real
  character-domain payload; that profiling remains deferred until Phase 4.3.
- `SessionPublicationFactory` is constructed and injected but has no caller (Stage 6 scope).
- Reliable native-Event loss under capture-queue pressure is diagnosed but not prevented or
  recovered; the recovery contract is undecided pending a real Phase 4.3 domain and its actual load.
- Stale-context publication is minimized (a last-instant freshness check plus a captured
  `playContextId` stamp) but not eliminated at the Bridge; full elimination needs a send-time check
  against the live session's own current context, which belongs to Stage 6's authenticated-session
  writer.

The maintainer should review the full acceptance-criteria list above against the current
implementation before flipping this stage's box; this note does not itself constitute that review.

## Stage 6: Full-Duplex Session and Bounded Queue Integration
- [ ] Complete

**Scope:**
Replace the authenticated session's single-operation writer path with one serialized full-duplex
read/write lifecycle. Create a fresh bounded outbound organization for each authenticated session,
connect the production publisher and control responses to it, and enforce the queue's storage,
ordering, recovery, failure, and lifetime contracts without blocking game capture or allowing
unsafe concurrent WebSocket writes.

**Acceptance criteria:**
- The authenticated session permits one outstanding read and one outstanding write on the
  WebSocket stream, with no second concurrent write and no blocking write mixed with the async
  publication writer.
- After authentication succeeds, capabilities, subscription acknowledgements, snapshot-request
  responses, errors, control messages, state Snapshots, and state Events all use the same
  serialized outbound writer. `ConnectionSession` has no direct post-auth write that can race
  publication delivery.
- `MessageDispatcher`, the subscription handler, and the snapshot-request handler route accepted
  state requests through the session's registered-area policy, publisher, and outbound queue.
  Accepted subscriptions enqueue initial Snapshots, accepted snapshot requests enqueue the current
  Snapshot, and unknown areas are rejected before any queue, barrier, Snapshot-slot, or dirty-marker
  state is allocated.
- A fresh outbound queue is created for every authenticated session and is discarded on session
  close. Its pending traffic and recovery barriers never cross into a reconnecting session.
- The active-session router can attach and detach session records without replacing authoritative
  state or revision ownership; 4.2 admits only one record, while Stage 9 will raise the same
  capacity and fan out shared publications.
- Queue capacity is exactly 128 messages per client: 16 reserved control/recovery slots, 108
  Normal data slots, and 4 Heavy data slots, with an independently enforced encoded-byte budget.
  The approved
  Normal/Heavy threshold is applied only after worker-side encoding and before queue admission.
- The reserved control/recovery lane carries initial, recovery, and explicitly requested
  Snapshots, acknowledgements, errors, and recovery/control messages ahead of data-lane traffic.
  Reserved-lane overflow disconnects the client rather than silently dropping the message. Reserved
  lane bytes do not consume the independently enforced data-lane byte budget.
- Normal and Heavy data lanes both support keyed replaceable Snapshot entries and ordered Event
  FIFOs. Snapshot pressure replaces the latest value or retains one dirty marker per registered
  area; it never creates an unbounded pending-value structure or a catch-up burst.
- There is one pending Snapshot slot per registered Snapshot area across both data lanes. Moving
  or replacing that slot updates lane and byte accounting without creating a second slot.
- A reliable Event is never coalesced or silently dropped. If message or byte capacity prevents
  admission, the client is disconnected and authoritative state and revisions remain valid.
- Every initial, recovery, or explicitly requested Snapshot establishes its per-area baseline at
  revision `R` in the single serialized outbound order. Events at or below `R` are superseded;
  later Events are released only after the Snapshot is ordered. Ephemeral notifications remain
  outside this supersession rule. The client does not need a Snapshot acknowledgement; ordered
  writes and each Event's `baseRevision` provide the apply rule.
- Unknown state areas cannot allocate queue, barrier, Snapshot-slot, or dirty-marker state. The
  registered-area count is fixed and bounded before a session can publish state.
- The existing one-connected-client limit remains enforced at admission; Stage 6 does not broaden
  the Bridge to multi-client publication. The limit is read from
  `bridge/security/constants.hpp`'s `kMaxConnectedClients`, not encoded as a singleton delivery
  architecture.
- Heavy structured live data remains subject to the frame limit and is not used for maps, assets,
  artwork, or other heavy resources excluded from the live stream.
- A rejected unsolicited Snapshot remains recoverable from the authoritative store; a rejected
  reliable Event ends the session rather than rolling back the store or revision.
- No external socket callback is invoked while the queue mutex is held, including synchronous
  failure callbacks. A failed or cancelled completion cannot access destroyed queue state because
  an independently owned lifetime token or equivalent in-flight completion barrier protects it.
- Queue shutdown, transport cancellation, and late completions are idempotent and safe; worker,
  callback, and transport-completion exceptions are contained and cannot escape their thread or
  callback boundary.

**Not in scope:** registering the five production character domains or defining their exact domain
data contracts; those belong to Phase 4.3. The internal proof fixture used in Stage 7 must not be
advertised as a public protocol state area.

**Depends on:** Stage 5

**Notes:** Do not claim full-duplex completion while any post-auth path still calls the old direct
writer or while the queue can outlive its completion barrier.

## Stage 7: Live-Boundary Verification and 4.2 Closeout
- [ ] Complete

**Scope:**
Prove the Stage 5 and Stage 6 production composition at the live Bridge boundary, including
slow-client behavior, recovery ordering, reconnect isolation, diagnostics, and failed cutover.
Use a private application-level registered-area fixture and generic state envelopes for these
tests; do not add a public test-only protocol state area or pull production character domains
forward from Phase 4.3. This stage does not switch canonical cross-boundary writers or remove
migration-only readers; those actions belong to roadmap Phase 4.4.

**Acceptance criteria:**
- A deterministic bounded Heavy-lane fixture uses a larger structured publication and proves
  threshold classification, four-slot accounting, Heavy Snapshot replacement, and reliable Heavy
  Event overflow without introducing inventory, maps, assets, artwork, or other heavy resources.
- Composition tests use the real production graph and prove unsolicited delivery while the
  authenticated session read is blocked.
- Loopback tests prove, using the private fixture, initial synchronization, explicitly requested
  Snapshot delivery, recovery ordering, Snapshot replacement, dirty-marker retry, reliable Event
  FIFO ordering, Event overflow disconnect, reserved-lane priority, and no delivery of superseded
  Events after a recovery Snapshot.
- Reconnect tests prove a new session receives fresh synchronization, receives a new session
  identity, and never receives queued Events or recovery barriers from the previous session.
- Capacity tests prove a second client is still rejected in 4.2 and that rejecting it does not
  create a second authority, duplicate a Skyrim read, or alter the admitted session's delivery
  state.
- Capture tests prove approved callback timing, aligned cadence, missed-tick skipping, loading
  and shutdown behavior, owned-value handoff, unchanged-sample short-circuit, and no worker-side
  runtime reads.
- Ordering tests prove capture, change detection, authoritative-store update, revision assignment,
  and publication-intent creation occur in the documented per-state-area order, including
  available/unavailable transitions and repeated equivalent unavailable values.
- Failure tests prove synchronous send-failure callbacks, cancelled completions, late
  completions, queue destruction, worker failure, callback failure, transport failure, and
  shutdown do not deadlock, access destroyed state, lose authoritative revisions, or escape their
  containment boundary.
- Diagnostics are emitted and asserted for capture timing, queue depth, coalescing, enqueue
  latency, dequeue latency, recovery, Event overflow, reserved-lane overflow, send failure, and
  disconnect.
- A failed-cutover guard proves that an unavailable Bridge, SDK, .NET, fixture, or runtime capture
  consumer leaves canonical writers unchanged and produces an actionable diagnostic. Stage 7 does
  not switch canonical writers or remove migration-only readers and fixtures; roadmap Phase 4.4
  performs that cutover only after its complete consumer matrix passes.
- Worker failure transitions the coordinator to `unavailable`, stops state from being presented as
  current, emits the controlled diagnostic/error required by the existing failure contract, and
  requires the approved recovery or reconnect path. Any worker restart requires a fresh Snapshot
  before publication resumes.
- The final verification runs the complete Bridge unit/composition suite, the independent .NET
  loopback suite, protocol-fixture validation, formatting/consistency checks, and the documented
  manual runtime check for callback timing, loading, shutdown, and transport behavior. Production
  character-domain live delivery remains a Phase 4.3 manual/runtime check.
- Stage 7 remains incomplete until every acceptance criterion in Stages 5, 6, and 7 passes. A
  passing isolated component test cannot close 4.2.

**Not in scope:** the five production character-state contracts, their runtime sources, their
canonical state-area registration, and the SDK synchronization kernel; those belong to Phase 4.3.

**Depends on:** Stage 5, Stage 6

**Notes:** This is the only stage that may be marked complete after the production composition is
observable at the live boundary. After it passes, the Bridge work is handed to roadmap Phase 4.4
for cross-boundary cutover and cleanup, then to the separate 4.5 version-impact audit; this plan
does not perform either action.
