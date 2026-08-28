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
injects one instance each of `CadenceScheduler`, `CapturePolicyRegistry`,
`ActivePlayContextProvider`, `RegisteredStateAreaPolicy`, `ActiveSessionPublicationRouter`,
`StatePublisher`, `CommonLibCaptureQueueDiagnostics`, `CaptureDispatchWorker`,
`CadenceTickDriver`, and `SessionPublicationFactory`, as function-local statics in the same
dependency-ordered style already used for pairing, trust, and session components. `CadenceTickDriver`
uses the policy registry as a gate: a due key must have a complete sampled policy before it can
produce a capture work item. 4.2 registers no sampled domain, because the first real capture policy
and its value reader belong to Phase 4.3. There is no process-lifetime revision tracker: revisions belong to whichever
`PlayContext` a capture is pinned against, reached through `ActivePlayContextProvider`, so a new
save/new game always starts from a fresh, empty revision sequence rather than inheriting the previous
context's. 4.2 registers zero state areas through `RegisteredStateAreaPolicy`; its fixed bound exists
so the mechanism -- and every consumer that depends on "the registered-area count is fixed and
bounded" -- is real and tested before a later phase fills it with production character domains.
`RegisteredStateAreaPolicy` has a real caller in this composition: `ActivePlayContextLevelSink`'s
native-event gate below.

`SessionPublicationFactory` still depends on the concrete `ActiveSessionPublicationRouter`, not an
interface, for a reason recorded in that class's own doc comment
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
inside `StatePublisher` is unaffected by whether a session is attached, matching "When no session is
connected, authoritative state continues to update" above.

**The per-state-area ordering point.** `CaptureDispatchWorker::Dispatch` is the single per-state-area
ordering point for applying a captured value, determining change, and assigning the authoritative
revision -- for every capture, whether it arrived from a native-event or sampled source. A dequeued
`CaptureWorkItem` carries the specific `PlayContext` it was pinned against at capture time (not
re-read at dispatch time) and an `applyAndBuildIfChanged` closure. `Dispatch` first discards the item
if that pinned context is no longer `ActivePlayContextProvider::CurrentPlayContext()` (including a
transition to no active context at all); otherwise it invokes the closure against the pinned
context, discarding again if it reports no change. A changed result reaches
`StatePublisher::PublishCapture`, which -- under one per-state-area lock shared with every other
publish for that area -- decides whether to honor the requested Event mode or, when the area has no
revision baseline yet within the pinned context's own tracker, establish one as a Snapshot instead
using the identical data (valid because an Event is itself defined as complete post-change state, the
same shape a Snapshot carries, not a delta), stamps the pinned context's own identity onto the built
envelope's `playContextId`, and checks a `stillCurrent` predicate -- which re-queries
`ActivePlayContextProvider` live -- as the last condition immediately before the envelope reaches the
sink. No second ordering mechanism exists outside this path; a worker never re-derives revision or
change-detection logic of its own.

**Staleness is minimized, not eliminated.** The `stillCurrent` check narrows the window between "was
this context still active" and "the publication reached the sink" to the duration of one predicate
call, but it is not perfectly atomic with the sink handoff: a context transition landing in that
last instant can still let a stale publication reach `ActiveSessionPublicationRouter`, correctly
labeled with the context it actually came from. Closing that window completely would require holding
a lock shared with `PlayContextLifecycle`'s own transition path across the sink call, which would let
a worker-thread publish stall a game-thread save/load -- a worse outcome than the residual race. Full
elimination is the responsibility of whichever component checks `playContextId` against its own
current context at send time; that component is Stage 6's authenticated-session writer, not yet
built. This is a named, accepted Stage 5 limitation, not a solved problem.

**Native-event capture.** `CommonLibLevelIncreaseSink::ProcessEvent` (game-state layer) invokes
`LevelIncreaseHandler::HandleLevelIncrease`, which calls `IActivePlayContextLevelSink::BeginCapture`
*before* reading the runtime accessor and uses only the context it returns for the rest of the
attempt -- one atomic snapshot serves as both the "should I capture" guard and the pinned capture
target, so no accessor read happens while loading or before an authoritative play context exists, and
no later step can silently observe a different context than the one the guard checked.
`ActivePlayContextLevelSink::OnLevelCaptured` receives that pinned context and, only when
`RegisteredStateAreaPolicy::IsRegistered` reports its state-area key registered, builds an owned
`CaptureWorkItem` -- whose closure applies the value to the pinned context's `CharacterStateStore` and
reports whether it changed -- and calls `CaptureDispatchWorker::TryEnqueue`, the identical handoff
sampled capture uses. Applying to `CharacterStateStore` now happens only through this worker-owned
closure, not directly on the game thread: an unregistered area's `CharacterStateStore` is never
touched at all, which matches its current status as "read by no production consumer yet." 4.2 never
registers `"character_level"` (this native event's documented state-area identity), so the worker
handoff stays unreachable in production; the mechanism is proven in tests through a private fixture
area instead, so proving it does not require registering `character_level`'s real Phase 4.3 wire
contract ahead of that phase.

**Capture-queue rejection.** `CaptureDispatchWorker::TryEnqueue` reports a capacity rejection through
`ICaptureQueueDiagnostics`, called after its internal queue mutex releases so the report can never
delay the worker's own next lock acquisition. The report is mode-aware: a rejected Snapshot-mode
capture is logged as a routine warning, since the next sample tick recaptures current state anyway,
but a rejected Event-mode capture is logged as an error, since it represents a state transition
nothing will re-capture later -- and because applying a captured value now happens only inside the
worker's ordering point, a rejected item never updates its play context's authoritative store either.
**This diagnoses the loss; it does not prevent or recover it.** Reliable native-event delivery under
sustained capture-queue pressure is not guaranteed by Stage 5's design, and this criterion is not
claimed complete. A queue-full rejection is currently unreachable in production (4.2 registers zero
state areas), so this gap has no production consequence today, but the decision must be revisited
once Phase 4.3 registers a real Event-mode domain and its actual load is known.

`CadenceTickDriver` is the approved game-thread tick source for sampled capture. It owns a dedicated
background thread that wakes at a fixed 100 ms interval -- finer than the fastest `RateClass::kFast`
capture period -- and, when no previously marshaled tick is still pending, calls
`SKSE::TaskInterface::AddTask` to marshal a due-key check onto the game thread. The 100 ms figure
bounds only how often the driver attempts that submission, not when the check actually executes:
`AddTask` queues the task on SKSE's own task queue, drained at SKSE's own pace, and the driver holds
off submitting a new tick at all while the previous one remains pending, so a slow-draining queue (a
stalled or hitching game) pushes the next due-key check later than one interval rather than queuing a
second one behind it. Once it runs, the marshaled task pins the active play context once, before
checking `ICadenceScheduler::DueKeys(now)`, skipping the whole due-key check when no context is
active, and reuses that same pinned context -- the same guard-then-pin snapshot native-event capture
uses -- for every due key found in that tick, handing each to `CaptureDispatchWorker`. This was
chosen over hooking the engine's own update loop:
`SKSE::TaskInterface` is SKSE's own supported mechanism for safely running code on the game thread and
requires no new engine hook, memory patch, or Address Library offset, keeping with this document's
general preference against engine hooking (the `bAchievementCompat` patch documented in
`bridge/README.md` remains the narrow precedent for when a hook is genuinely unavoidable). Only the
due-key evaluation and any resulting capture read happen on the game thread, through the marshaled
task; the interval timing itself runs on an ordinary background thread. `CadenceTickDriver` depends
on SKSE's task interface only through an injected `ITaskMarshaller` port, so it remains testable
without SKSE.

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
