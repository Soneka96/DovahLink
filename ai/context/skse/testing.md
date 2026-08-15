# SKSE bridge testing

## Test layers

- Test game-state conversion with plain representative values; do not require a running Skyrim process.
- Test application state transitions with fake game adapters and a fake transport.
- Test protocol mapping against canonical fixtures shared with the client boundary.
- Test transport behavior with a local deterministic test double before using real sockets.
- Reserve manual in-game checks for runtime hooks, event timing, loading behavior, and compatibility that cannot be represented in unit tests.

Use explicit seams for runtime-dependent code: a fake callback registry for registration and unregistration, a fake queue or controllable executor for handoff, and a fake transport for connection lifecycle. Lifecycle tests must not need Skyrim to prove ordering.

## Required behavior

Cover:

- plugin startup and clean shutdown
- unsupported runtime handling
- missing or unavailable game values
- callback-to-queue handoff
- owned snapshot creation at the callback boundary
- queue-full behavior without blocking the callback
- latest-state coalescing and forced fresh-snapshot recovery after queue loss
- next-game-callback recovery capture after queue loss without worker-side runtime reads
- registration and unregistration ordering
- each SKSE runtime interface with a call-once registration contract (for example `SKSE::MessagingInterface::RegisterListener`, which SKSE allows exactly one call to per plugin and which fails both registrations silently on a second call) has exactly one call site; verify this structurally, since the failure only surfaces inside a running SKSE process
- callbacks that enter during shutdown and callbacks already in flight at unregister time
- worker shutdown while work is pending
- worker, callback, and transport-completion exceptions are contained
- transport completions arriving after cancellation are ignored safely
- transport completions already running during shutdown are drained or rejected by a generation guard
- transport disconnect and reconnect
- slow-client or full-queue behavior
- reserved recovery/control capacity while the event lane is full
- critical outbound message handling when the control lane is full
- worker failure transitions to unavailable and recovery/reconnect behavior
- worker restart requires a fresh snapshot before publication resumes
- stale or out-of-order state updates
- malformed, incompatible, and unknown protocol messages

## Test boundaries

- Do not test CommonLib or Skyrim internals as if they were DovahLink code.
- Do not use a real game process for tests that only verify mapping or application logic.
- Do not rely on timing sleeps to prove concurrency; use controllable fakes, barriers, or explicit state transitions.
- Keep protocol fixtures language-neutral so Flutter and SKSE can consume the same examples.
- Assert that no borrowed runtime object crosses into queued work or worker-owned state.
- Assert that queued work contains captured values only and never a deferred game/runtime read.
- Assert that hooks cannot reach protocol serialization or transport directly; test through the coordinator seam.

## Manual verification

When a change touches runtime integration, record the Skyrim runtime, SKSE version, load order conditions, reproduction steps, expected result, and observed result. A manual check does not replace automated tests for the code that can be tested without Skyrim.

If a manual verification pass turns up a genuine SKSE or engine quirk rather than a project-specific bug, also record it in `runtime-quirks.md`.
