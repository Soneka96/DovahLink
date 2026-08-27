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
- If a bounded queue is full, never block the game thread: a replaceable Snapshot may replace an
  already-pending value for the same state area or be deferred, while a reliable Event must remain
  ordered and causes the slow client to be disconnected rather than being coalesced or dropped;
  record the outcome diagnostically.
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
- The outbound organization has one authoritative ordering point per state area for applying captured
  values and assigning revisions. A recovery snapshot establishes the new baseline before later
  stateful events are applied; events at or below an accepted snapshot revision are superseded. This
  does not convert an ephemeral notification into recoverable state.
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
