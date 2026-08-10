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
- registration and unregistration ordering
- worker shutdown while work is pending
- transport disconnect and reconnect
- slow-client or full-queue behavior
- stale or out-of-order state updates
- malformed, incompatible, and unknown protocol messages

## Test boundaries

- Do not test CommonLib or Skyrim internals as if they were DovahLink code.
- Do not use a real game process for tests that only verify mapping or application logic.
- Do not rely on timing sleeps to prove concurrency; use controllable fakes, barriers, or explicit state transitions.
- Keep protocol fixtures language-neutral so Flutter and SKSE can consume the same examples.
- Assert that no borrowed runtime object crosses into queued work or worker-owned state.

## Manual verification

When a change touches runtime integration, record the Skyrim runtime, SKSE version, load order conditions, reproduction steps, expected result, and observed result. A manual check does not replace automated tests for the code that can be tested without Skyrim.
