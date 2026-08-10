# SKSE bridge architecture

These conventions apply to the native Skyrim bridge. The bridge is a boundary adapter, not the place where the Flutter client or protocol becomes embedded.

## Technology boundary

- The native SKSE plugin is C++ code built for the supported Skyrim runtime through the approved SKSE/CommonLib toolchain.
- Keep the runtime choice, CommonLib version, compiler, and build system documented when the first bridge feature is started.
- Do not introduce Papyrus into the core bridge unless a capability genuinely requires Skyrim scripting.
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

### Plugin boundary

Owns SKSE loading, lifecycle, event registration, and runtime-specific integration. It delegates quickly and does not contain business rules or wire serialization.

### Game-state adapters

Read supported Skyrim state through the approved runtime API and convert it into DovahLink-owned values. They are the only layer allowed to depend directly on CommonLib or Skyrim runtime types.

### Application layer

Coordinates snapshots, updates, connection-facing capabilities, and read-only behavior. It must be testable without a running Skyrim process.

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

## Threading and callbacks

- Never perform blocking network, filesystem, or expensive serialization work inside a Skyrim callback or game-thread hook.
- Keep callback work small: capture the required value, enqueue work, and return.
- Copy captured values into owned, immutable work items before they cross a thread boundary.
- Make ownership and thread handoff explicit.
- Do not access game objects from a worker thread unless the approved runtime API explicitly permits it.
- If a bounded queue is full, never block the game thread: apply the documented latest-state coalescing or drop policy and record the loss.
- Shutdown must stop workers and close transport resources before the plugin unloads.

## Ownership and shutdown

- One application coordinator owns the callback registrations, work queues, worker threads, and transport lifecycle.
- Registration handles are released before the coordinator is destroyed.
- Shutdown first prevents new callback work, then drains or cancels queued work, stops workers, and finally closes transport resources.
- Queued work must not retain borrowed Skyrim objects, `BuildContext`-like runtime handles, or pointers whose lifetime is not owned by the queue.

## Failure semantics

- If plugin startup cannot establish a required runtime capability, disable the affected capability and log a clear reason; do not publish fabricated state.
- Queue overflow must produce an observable diagnostic and leave the next published state marked as potentially incomplete or recovered by a fresh snapshot.
- A transport disconnect marks the client unavailable and triggers the approved reconnect policy; it must not block game-state capture.
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
