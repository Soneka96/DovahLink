# SKSE bridge architecture

These conventions apply to the native Skyrim bridge. The bridge is a boundary adapter, not the place where the Flutter client or protocol becomes embedded.

## Technology boundary

- The native SKSE plugin is C++ code built for the supported Skyrim runtime through the approved SKSE/CommonLib toolchain.
- Keep the runtime choice, CommonLib version, compiler, and build system documented when the first bridge feature is started.
- Do not introduce Papyrus into the core bridge unless a capability genuinely requires Skyrim
  scripting. The trust-administration console adapter
  (`ai/context/protocol/security.md`'s "Trust administration surface") is the approved exception: a
  small Papyrus glue script living outside `bridge/`, calling native functions the plugin registers.
  The Papyrus surface carries no application/business logic of its own -- only forwarding and output
  formatting.
- Do not copy game-runtime types into DovahLink protocol or client code.

## Internal shape

```text
SKSE plugin entry points / hooks
                ↓
        game-state adapters
                ↓
        DovahLink application
                ↓
       protocol message mapping
                ↓
             transport
```

The allowed dependency edges are:

```text
plugin entry / hooks → coordinator
plugin entry / hooks → callback registry
callback registry → game-state adapter
game-state adapter → Skyrim/CommonLib runtime
coordinator → application state
application state → protocol adapter
protocol adapter → canonical protocol schema
coordinator → transport
transport → protocol messages
```

No reverse edge is allowed. In particular, hooks and game-state adapters must not call transport or serialize protocol messages, and the application coordinator must not depend on CommonLib types.

### Plugin boundary

Owns SKSE loading, lifecycle, event registration, and runtime-specific integration. Hooks may synchronously capture through the game-state adapter and enqueue an owned application work item; they must not defer a runtime read to a worker. They must not read runtime objects directly, contain business rules, serialize wire messages, or call transport.

### Game-state adapters

Read supported Skyrim state through the approved runtime API and convert it into DovahLink-owned values. They are the only layer allowed to depend directly on CommonLib or Skyrim runtime types.

### Application layer

Coordinates snapshots, updates, connection-facing capabilities, and read-only behavior. It must be testable without a running Skyrim process.

Lifecycle transition coordination belongs in the application layer: one play-context lifecycle
aggregate owns the lifecycle state, play-context identity, and publication or invalidation of the
active play context as one synchronized state machine. Runtime adapters decode Skyrim/SKSE callbacks
and delegate the resulting application event; they do not own a second lifecycle transition state
machine.

Behavior-bearing application services own one coherent responsibility and coordinate through
explicit, DovahLink-owned capability ports supplied by constructor injection. They must not
construct behavior-bearing collaborators internally or depend on concrete runtime classes merely
because those classes are available.

Domain state machines own their state transitions and invariants. Orchestration services own
coordination across application, protocol, persistence, and transport boundaries; do not make a
state machine imitate an orchestration service or move its invariants into a port.

### Session and publication ownership

The Bridge uses a bounded collection of authenticated session records, with capacity controlled by
`bridge/security/constants.hpp`'s `kMaxConnectedClients`. The approved 4.2 value remains `1`; this
preserves the single-client product boundary while keeping session delivery state collection-shaped
for the later multi-client phase. Each session record owns its socket writer, capabilities,
subscriptions, outbound queue, recovery barriers, and diagnostics.

Capture policy, cadence, authoritative state, and revisions are shared at Bridge/play-context scope.
When no session is connected, capture and authoritative-state updates continue; reliable Events are
not retained for a later session, and the next session starts from fresh current Snapshots. Raising
the capacity later must add fan-out and independent client recovery, not create a second authority or
duplicate equivalent Skyrim reads.

### Production capture and lifecycle composition

The production plugin composition root (`bridge/plugin/dovahlink_bridge_plugin.cpp`) constructs and
injects one instance each of `CapturePolicyRegistry`, `CadenceScheduler`, `RevisionTracker`,
`RegisteredStateAreaPolicy`, `ActiveSessionPublicationRouter`, `StatePublisher`,
`CaptureDispatchWorker`, `CadenceTickDriver`, and `SessionPublicationFactory`, as function-local
statics in the same dependency-ordered style already used for pairing, trust, and session
components. 4.2 registers zero state areas through `RegisteredStateAreaPolicy`; its fixed bound
exists so the mechanism -- and every consumer that depends on "the registered-area count is fixed
and bounded" -- is real and tested before a later phase fills it with production character domains.
`RegisteredStateAreaPolicy` has a real caller in this composition: `ActivePlayContextLevelSink`'s
native-event gate below. `CapturePolicyRegistry` does not -- nothing in 4.2 has a capture policy to
classify, since classifying a policy presupposes a registered domain and Phase 4.3 is what supplies
the first one. This is a deliberately narrower state than "constructed and injected" implies for
`CapturePolicyRegistry` specifically; it is not yet passed to any consumer's constructor.

`StatePublisher` depends on `IRevisionTracker&`, not the concrete `RevisionTracker`, reached through
two ordinary (non-template) virtual methods, `CommitSnapshotEnvelopeIfBuilt`/
`CommitEventEnvelopeIfBuilt`, that `RevisionTracker` implements as one-line forwards into its
existing, untouched `CommitSnapshotIfBuilt`/`CommitEventIfBuilt` templates (`RevisionTracker`'s own
doc comment in `bridge/application/revision_tracker.hpp` has the full reasoning: those two templates
cannot themselves be virtual, since C++ forbids a template `virtual` method). `SessionPublicationFactory`
still depends on the concrete `ActiveSessionPublicationRouter`, not an interface, for a different
reason recorded in that class's own doc comment
(`bridge/application/active_session_publication_router.hpp`): `Attach`/`Detach` are ordinary methods
with no template constraint, but `ai/context/skse/cpp-style.md`'s "a C++ behavior-bearing
implementation implements exactly one DovahLink-owned interface" rule blocks adding them to
`IOutboundPublicationSink`, which `ActiveSessionPublicationRouter` already shares with
`BoundedOutboundQueue` and two test-only sinks -- widening that shared interface would wrongly
obligate all of them to implement session-attachment behavior they don't have.

`ActiveSessionPublicationRouter` is `StatePublisher`'s only `IOutboundPublicationSink`. It has no
session attached in 4.2's production graph: the per-session factory that would attach one is
constructed and injected but not yet called from any real connection path, since that call site
belongs to the full-duplex session integration that replaces the authenticated session writer. Until
then, every publication `StatePublisher` builds is dropped at the router rather than queued -- the
same behavior as any other moment when no session is connected. Authoritative revision assignment
inside `StatePublisher`/`RevisionTracker` is unaffected by whether a session is attached, matching
"When no session is connected, authoritative state continues to update" above.

`CaptureDispatchWorker` owns the boundary between a game-thread capture and worker-side publication
building: a game-thread callback copies an already-validated owned value into a bounded work item and
enqueues it; the worker thread dequeues it and calls `IStatePublisher`. This is the single
per-state-area ordering point for applying a captured value, determining change, and assigning the
authoritative revision -- realized by `StatePublisher`'s existing per-state-area publication gate,
which every `CaptureDispatchWorker` call reaches through that one contract. No second ordering
mechanism exists outside that gate; a worker never re-derives revision or change-detection logic of
its own.

Native-event capture uses the same domain-independent gate and worker handoff as sampled capture,
not a separate boundary. `CommonLibLevelIncreaseSink::ProcessEvent` (game-state layer) invokes
`LevelIncreaseHandler::HandleLevelIncrease`, which checks `IActivePlayContextLevelSink::IsCaptureActive`
*before* reading the runtime accessor -- the guard runs ahead of the read, not only ahead of routing
the already-read result, so no accessor read happens while loading or before an authoritative play
context exists. Once read, the captured value reaches `ActivePlayContextLevelSink::OnLevelCaptured`
(application layer), which always applies it to the play context's `CharacterStateStore` (a
per-context cache predating this worker pipeline, read by no production consumer yet) and, only when
`RegisteredStateAreaPolicy::IsRegistered` reports its state-area key registered, also builds an owned
`CaptureWorkItem` and calls `CaptureDispatchWorker::TryEnqueue` -- the identical handoff sampled
capture uses. 4.2 never registers that key (`"character_level"`, the identity
`roadmap/04-live-state-synchronization-foundation.md` already documents for it), so the worker handoff
stays unreachable in production; the mechanism is proven in tests through a private fixture area
instead, so proving it does not require registering `character_level`'s real Phase 4.3 wire contract
ahead of that phase.

`CadenceTickDriver` is the approved game-thread tick source for sampled capture. It owns a dedicated
background thread that wakes at a fixed 100 ms interval -- finer than the fastest `RateClass::kFast`
capture period, so a due key is never delayed by more than one tick -- and calls
`SKSE::TaskInterface::AddTask` to marshal execution onto the game thread; the marshaled task calls
`ICadenceScheduler::DueKeys(now)` and hands each due key to `CaptureDispatchWorker`. This was chosen
over hooking the engine's own update loop: `SKSE::TaskInterface` is SKSE's own supported mechanism for
safely running code on the game thread and requires no new engine hook, memory patch, or Address
Library offset, keeping with this document's general preference against engine hooking (the
`bAchievementCompat` patch documented in `bridge/README.md` remains the narrow precedent for when a
hook is genuinely unavoidable). Only the due-key evaluation and any resulting capture read happen on
the game thread, through the marshaled task; the interval timing itself runs on an ordinary background
thread. `CadenceTickDriver` depends on SKSE's task interface only through an injected `ITaskMarshaller`
port, so it remains testable without SKSE.

Both `CaptureDispatchWorker` and `CadenceTickDriver` start after `kDataLoaded`, alongside the existing
coordinator start, and stop and join before `Coordinator::Shutdown` completes its callback and worker
teardown, per "Ownership and shutdown" above. Neither is a required runtime capability whose absence
should fail plugin load; per "Failure semantics," if the capture-policy registry, scheduler,
registered-area policy, or worker handoff described here is not available, canonical writers remain
unchanged and production composition does not advance.

### Protocol mapping

Converts application values to the canonical protocol contract. It must not expose C++ runtime objects or make the Flutter client depend on native implementation details.

### Transport

Owns connection lifecycle, framing, encoding, reconnect behavior, and outbound queues. It must not read Skyrim memory or call game APIs.

## Dependency rules

- Plugin entry points may depend on the application coordinator and runtime integration, but they must not contain application policy.
- Game-state adapters are the only bridge components that may depend directly on CommonLib or Skyrim runtime types.
- Application code depends on DovahLink-owned interfaces and values, never on CommonLib types.
- Protocol mapping depends on DovahLink-owned application values, never on game objects.
- Transport depends on protocol messages and transport abstractions, never on game adapters or Skyrim APIs.

### Application ports and ownership

- Define one narrow interface for every behavior-bearing type, including a leaf type with one caller
  or no current collaborator. Do not add interfaces for DTOs, value objects, enums, or pure
  functions; those data-only or function-only types remain exempt.
- A port contains only the capability required by its consumer; it must not mechanically mirror a
  larger concrete class. Use the existing `I`-prefix convention for true injectable C++ ports.
- Put each DovahLink port beside its one concrete implementation in the implementation's owning
  production header by default. The narrowly defined CommonLib target dependency-wall exception
  documented in `ai/context/skse/cpp-style.md` applies to `IBridgeCallbackRegistry` and
  `BridgeCallbackRegistry`. Do not place unrelated public types in that header; structs, result
  types, and values own their own files. Every C++ port uses the `I<ClassName>` name matching its
  concrete implementation. A class may inherit from one required CommonLib/Skyrim framework base
  in a runtime adapter; that framework base is not a DovahLink port.
- Testability changes must preserve the dependency edges above. Application ports must use
  DovahLink-owned values and must never expose CommonLib or Skyrim runtime types.

## Threading and callbacks

- Never perform blocking network, filesystem, or expensive serialization work inside a Skyrim callback or game-thread hook.
- Keep callback work small: capture the required value, enqueue work, and return.
- Copy captured values into owned, immutable work items before they cross a thread boundary.
- Make ownership and thread handoff explicit.
- Capture must be bounded, validated, and non-blocking. Unbounded scans, waits, network calls, and uncontrolled allocation are forbidden in callbacks.
- Do not access game objects from a worker thread unless the approved runtime API explicitly permits it.
- Recurring sampled capture may be grouped behind one coordinator-owned scheduler with per-value
  due times and rate classes. The scheduler must be driven by an approved game-thread callback or
  hook; a worker may process captured values but must never request a deferred Skyrim read. Late
  ticks skip missed samples rather than issuing an unbounded catch-up burst.
- If a bounded queue is full, never block the game thread: a replaceable Snapshot may replace an
  already-pending value for the same state area or be deferred behind one bounded dirty marker per
  registered state area, while a reliable Event must remain ordered and causes the slow client to be
  disconnected rather than being coalesced or dropped; record the outcome diagnostically.
- Shutdown must stop workers and close transport resources before the plugin unloads.

## Ownership and shutdown

- One application coordinator owns the callback registrations, work queues, worker threads, and transport lifecycle.
- Registration handles are released before the coordinator is destroyed.
- Shutdown first marks the coordinator stopping so new callbacks return without touching destroyed state, then unregisters callbacks, waits for callbacks already in flight to leave, drains or cancels queued work, stops and joins workers, cancels transport completions, waits for completions already running to leave or rejects them through an independently owned lifetime token and generation guard, and finally closes transport resources. The lifetime token outlives every callback that can inspect it.
- Queued work must not retain borrowed Skyrim objects, `BuildContext`-like runtime handles, or pointers whose lifetime is not owned by the queue.
- Callback registration and in-flight tracking must remain alive until the unregister-and-wait barrier completes.

## Failure semantics

- If plugin startup cannot establish a required runtime capability, disable the affected capability and log a clear reason; do not publish fabricated state.
- Queue overflow must produce an observable diagnostic. Snapshot loss marks the affected state area
  incomplete until a fresh authoritative snapshot is captured; reliable Event overflow disconnects
  the slow client and ends that session rather than silently losing an Event.
- The v1 queue policy is mode-aware: replaceable Snapshot values are latest-value-wins per state
  area, while reliable Event values are ordered FIFO and are never coalesced or dropped. Initial,
  recovery, and explicitly requested snapshots, acknowledgements, errors, and recovery messages use
  the reserved control/recovery capacity and are never silently dropped or allowed to block the game
  thread. Unsolicited Snapshot values use the bounded data capacity and may be replaced or deferred.
  If reserved control/recovery capacity is full, the client is marked unavailable and the connection
  is closed.
- Within the current 128-message outbound bound, the 16 reserved control/recovery slots and the 112
  data slots are further organized as 108 Normal data slots and 4 Heavy data slots. A worker assigns
  Normal or Heavy after measuring the encoded publication; this classification changes storage only,
  not the Snapshot/Event reliability rule.
- The outbound organization has one authoritative ordering point per state area for applying captured
  values and assigning revisions. A recovery snapshot establishes the new baseline in the single
  serialized outbound order before later stateful events are applied; events at or below an accepted
  snapshot revision are superseded. This does not convert an ephemeral notification into recoverable
  state.
- When Snapshot loss marks a state area for recovery, the next eligible game callback synchronously
  captures a fresh owned value through the game-state adapter and places it in the reserved recovery
  lane; workers never request or perform a deferred runtime read.
- After reliable Event overflow or any resulting transport disconnect, the authoritative store and
  revision remain valid, but the session queue is discarded and the next authenticated session
  begins with fresh synchronization rather than replaying the old queue.
- A transport disconnect marks the client unavailable and triggers the approved reconnect policy; it must not block game-state capture.
- If a worker exits unexpectedly, the coordinator enters `unavailable`, stops publishing state as current, reports a controlled `internal_error`, and either restarts the worker through an approved policy or requires a clean reconnect. A restarted worker must receive a fresh snapshot before publication resumes on the existing session.
- Malformed or incompatible messages are rejected at the protocol boundary and never reach game APIs.

## Runtime compatibility

- Make the supported Skyrim runtime(s) an explicit project decision.
- Keep runtime-specific code behind a small adapter boundary.
- Do not scatter runtime-version checks through application or protocol code.
- Reject unsupported runtimes clearly during plugin initialization.

## Architectural non-goals

- No remote gameplay actions in the first bridge.
- No hosted backend or account system in the native plugin.
- No generic event framework before one real state flow requires it.
- No shared C++/Dart implementation layer; share the protocol contract only.
