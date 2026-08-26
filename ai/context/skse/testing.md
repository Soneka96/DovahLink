# SKSE bridge testing

## Test layers

- Test game-state conversion with plain representative values; do not require a running Skyrim process.
- Test application state transitions with fake game adapters and a fake transport.
- Test protocol mapping against canonical fixtures shared with the client boundary.
- Test transport behavior with a local deterministic test double before using real sockets.
- Reserve manual in-game checks for runtime hooks, event timing, loading behavior, and compatibility that cannot be represented in unit tests.

Use explicit seams for runtime-dependent code: a fake callback registry for registration and unregistration, a fake queue or controllable executor for handoff, and a fake transport for connection lifecycle. Lifecycle tests must not need Skyrim to prove ordering.

## Service interaction tests

- A service or orchestrator test uses mocks for its behavior-bearing dependencies and verifies the
  service's own calls, arguments, failure handling, and contractually important ordering.
- A stateful domain test uses the real domain class and proves its state transitions, persistence,
  atomicity rules, and invariants. It may use a stateful fake for an external boundary when that
  fake provides meaningful deterministic behavior.
- Protocol, DTO, and value tests use the real DTOs and serialization or validation code. Do not
  mock the object or codec whose behavior the test is intended to prove.
- Small composition or integration tests use real implementations to prove that the production
  graph is wired correctly; they do not duplicate every collaborator's unit tests.
- When a consumer changes to depend on a new contract, update its isolated consumer test in the
  same implementation step. Use strict mocks for synchronous consumers; use controllable,
  thread-safe fakes for worker, callback, transport, and lifetime consumers. Keep real composition
  tests separately when they prove production wiring.

Use FakeIt for synchronous interaction tests. It passed the repository proof of concept through the
real MSVC, C++23, Catch2, and vcpkg path, including const methods, reference, `std::string_view`,
and `std::optional` arguments. FakeIt is not thread-safe and must not be called from production
worker, callback, transport, or lifetime threads.

GoogleMock is approved as a narrow, test-only exception for contract calls that must cross a
test-controlled worker/session thread. Its use must be proven through the real MSVC, C++23, CMake,
vcpkg, and Catch2 target, and every custom action or captured test state remains the test author's
responsibility to synchronize. Do not migrate synchronous tests to GoogleMock or create a second
general-purpose production mocking policy. Project-owned interface inheritance is prohibited and
is not a framework capability to validate.

The Bridge pilot selected FakeIt `2.5.0` through the pinned vcpkg baseline for synchronous
interaction tests. The Catch2 configuration passed the supported MSVC/C++23 build, `const`
methods, `std::optional` and `std::string_view` arguments, exact call counts, sequence verification,
unexpected-call diagnostics, and Catch2 failure reporting. FakeIt remains test-only: its mocks are
not thread-safe and do not support multiple or virtual inheritance, so worker, callback, transport,
and other concurrency/lifetime tests continue to use controllable stateful fakes. The
ConnectionSession contract test separately proved GoogleMock `1.18.0` through `GTest::gmock` on the
supported MSVC/C++23 target; this exception is limited to cross-thread contract calls and does not
replace FakeIt for synchronous tests.

Unexpected interactions must be detectable by default. Verify ordering only when it is part of the
service contract; do not impose global ordering on incidental calls. Keep framework syntax visible
in the test when that makes the interaction being proved clearer.

Shared mock setup, reusable mock aliases, and common test values belong in module-level test-support
headers when repetition justifies them. Follow the existing `<module>_test_support.hpp` shape and
do not create a repository-wide `MockEverything` header or hide every expectation behind a private
mini-framework. Keep stateful fakes for persistence failures, deterministic stores, clocks,
queues, barriers, runtime adapters, and other tests where the fake's behavior is the subject.

## Test data construction

- Build representative test values through the type's own aggregate/designated-initializer syntax
  (`CharacterSnapshot snapshot{.level = 12};`) when its default member initializers already supply
  sensible values for every other field. This is the default: most game-state and application value
  types in this codebase are plain aggregates for exactly this reason.
- When a representative value is needed in more than one test file, or the type is not a plain
  aggregate (it has invariants, computed state, or a non-trivial constructor), extract a
  `Build<Type>` free function with default parameter values instead of duplicating the value inline.
  Place it in that module's own test-support header (for example a new
  `<module>/<module>_test_support.hpp`), mirroring `bridge/protocol/fixture_test_support.hpp`'s role
  as the protocol module's test-only helper header.
- Do not introduce a mutable global or static test value; each call constructs a fresh instance.

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
