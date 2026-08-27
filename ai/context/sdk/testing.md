# SDK testing

Read `ai/context/integration/testing.md` for the cross-side contract-test conventions this file
does not repeat, and `ai/context/dart/dart-style.md` for general Dart test style.

Apply `ai/context/dart/dart-style.md`'s shared test-organization rules to SDK tests.
- Tests for a collaborator's method belong with that source unit's mirrored test file. A service test
  may prove the service's recovery behavior through a collaborator, but it must not become the
  direct test suite for the collaborator's method.
- A mocktail mock declaration targets the `I`-prefixed contract type, per `ai/context/dart/dart-
  style.md`'s interface-naming convention: `class MockClientStorage extends Mock implements
  IClientStorage {}`, not the removed unprefixed name.

## Test fixtures and ownership

- Keep SDK-owned typed fixtures below the package-local
  `sdk/dart/dovahlink_client/test/fixtures/` directory. The package's discoverable catalog is
  `test/fixtures/fixtures.dart`, with named builders such as `Fixtures.buildEnvelope(...)` and
  `Fixtures.buildPersistedClientState(...)` grouped by the owning production area (`Request`,
  `Protocol`, `Persistence`, `Internal`, and so on). This catalog is test-only in-memory
  construction; canonical cross-side JSON fixtures remain in `protocol/fixtures/`.
- A representative DTO/value construction lives in exactly one catalog builder. Every other test
  file calls the relevant named catalog builder, such as `Fixtures.buildPendingOperation(...)` or
  `Fixtures.buildRequestPolicy(...)`, rather than duplicating construction inline or creating a
  private builder. When one fixture's default value needs another fixture (`PendingOperation`'s
  default `policy` needs `RequestPolicy`'s representative shape), compose the other builder via a
  nullable parameter and `??`, because fixture-builder calls are not `const`-eligible.
- A test-local helper may compose a catalog builder and override only the fields that define its
  scenario, such as a reply envelope with a particular message type or correlation ID. It must not
  re-derive the catalog builder's representative defaults.
- A DTO/value type used through a fixture builder needs real `==`/`hashCode` (see the hand-written
  pattern on `PersistedClientState`) if any test ever compares two instances for equality —
  `const` literals canonicalize to the same instance and can silently stand in for a missing
  equality override; a fixture builder's non-`const` calls cannot, and will surface the gap as a
  failing `verify()`/`expect()` the moment inline literals are replaced with fixture calls.

## Test ownership boundaries

SDK tests own: canonical contract encoding/decoding, semantic validation, Bridge compatibility
checks, session identity, Bridge identity, play-context identity, revision handling, stale
suppression, subscription state, snapshot recovery, reconnect, late-message handling, pairing
client recovery, SDK persistence, and cache correctness.

Phase 3.3 also requires SDK/client coverage for continuous observation of long-lived connection
loss, distinction between ordinary transport failure and `session_invalidated(reason)`, typed
`revoked`/`blocked`/`trustReset`/`factoryReset` state, reason-specific credential cleanup,
`clientId` preservation, manual Retry recovery, and the absence of automatic re-pair or uncontrolled
reconnect loops. Official Flutter tests own the intentionally identical presentation of those four
administrative reasons; third-party SDK consumers may test their precise diagnostic exposure.

App tests own: SDK state mapped into Redux/application state, application state mapped into UI,
product recovery presentation, user-facing error presentation, compatibility-error presentation,
loading/stale/unavailable UI, and navigation/interaction behavior.

Do not maintain the same Dart client correctness test suite independently inside both `app/` and
`sdk/`. Canonical cross-side fixtures remain owned by `protocol/fixtures/`; SDK contract tests
consume them rather than redefining equivalent examples.

## Transport fidelity

Use a controllable, thread-safe fake transport for state-machine, compatibility, session, and
recovery tests; a real local connection check is required only when transport framing or platform
networking changes, mirroring `ai/context/integration/testing.md`'s end-to-end boundary. Do not
depend on a running Skyrim process for behavior that can be proven deterministically without Skyrim.

## The independent validator stays independent

The .NET validation client (`integration/DovahLinkValidationClient/`) must remain a separate,
hand-written implementation of the canonical contract. It must not consume, wrap, generate from, or
otherwise reuse the Dart SDK; its value is precisely that it can catch a Bridge bug, an SDK bug, or
an assumption accidentally shared only by the official Dart implementation.

## Service test boundaries

Contract migrations and consumer-test migrations land together. Consumer tests remain black-box
tests of the consumer's reaction; the dependency's own test file owns its internal behavior. The
real object graph is reserved for the explicit composition-root integration test.

Every constructor dependency of the class under test is covered by a contract double. Use a
mocktail mock (`class MockX extends Mock implements X {}`) for synchronous, stateless
interaction-only dependencies; use a controllable thread-safe fake when timing, lifetime,
cross-thread access, synchronization, or mutable state is part of the behavior under test. This
applies uniformly to every class this package tests, not only the seven
Services: `SessionService`'s test mocks `SessionState`, `LifecycleOperationQueue`, and
`ConnectionTeardownCoordinator`; `RequestServiceImpl`'s test mocks `PendingOperationBookkeeping`,
`PendingOperationTransmitter`, and `MessageRouter`; `ConnectionTeardownCoordinator`'s own test mocks
`LifecycleOperationQueue`, in turn. A class's own test file is the only place that class's real
behavior runs; every consumer treats it as a black box and verifies via `verify()`/`captureAny()`
that the right call happened with the right arguments — never by composing the real object and
observing the outcome it produces. This holds even where the dependency is a small, mechanical,
zero-constructor-dependency object like `LifecycleOperationQueue` (just a real FIFO sequencer): its
own behavior is already proven once, in its own test file, and re-proving that same behavior through
every consumer's test is exactly the duplication this rule exists to eliminate. The one place real,
composed objects are deliberately used together is `DovahLinkClient`'s composition-root assembly
integration test (see `ai/context/sdk/architecture.md`'s composition root) — that is where genuine
end-to-end call-sequencing guarantees (for example `ConnectionTeardownCoordinator`'s queued-call
deduplication, composed with a real `LifecycleOperationQueue`) get their one, deliberate, real-object
proof; no individual Service or collaborator's own unit test re-derives it.

A consumer's test suite proves its own reaction to a dependency's contract — success, each documented
failure mode, each retry/terminal classification the dependency's typed result or exception exposes,
and (for a mocked collaborator) that the right method was called with the right arguments — and must
not become a second test suite for that dependency's own internal branches, which stay owned by the
dependency's own test file. For example: `ReconnectServiceImpl`'s tests mock `ISessionService` and
`AuthenticationService` and prove reconnect's own reaction (continue vs. stop, attempt/deadline
bookkeeping) to each classification `AuthenticationService.hello()` can produce, without re-proving
how `AuthenticationServiceImpl` itself decodes or classifies a rejected `hello`.
`AuthenticationServiceImpl`'s tests mock `ISessionService`, `ISessionAdmissionService`, `RequestService`,
and `IClientStorage`. `SessionAdmissionService`'s and `SessionTrustService`'s tests mock
`SessionState` and the Service dependencies each one actually declares. Do not introduce a Service
interface solely because mocking a dependency is convenient — `mocktail`'s pattern already makes
mocking a concrete class' single interface a one-line cost regardless of that interface's size, so
mock-boilerplate reduction is never, by itself, sufficient justification for a new interface in this
codebase; every Service interface exists because of a genuine architectural reason documented in
`ai/context/sdk/architecture.md`.

## Teardown deduplication and connection-guard coverage

Two behaviors need their own explicit tests, not just whatever coverage happens to exist elsewhere:

- `SessionService`'s `onTeardown` callback must fire exactly once per real, non-stale teardown,
  and never fire for a duplicate signal belonging to an already-torn-down generation (for example a
  transport's `onError` and `onDone` both firing for one dead connection). Under "Service test
  boundaries"' full mock isolation, `SessionService`'s own test proves only that it calls
  `ConnectionTeardownCoordinator.tearDown` with the right arguments for each reactive signal;
  `ConnectionTeardownCoordinator`'s own generation-check dedup logic is proven in its own test file;
  the full, real, composed guarantee (a real coordinator over a real queue actually deduplicating a
  queued-behind call) is proven once, deliberately, at `DovahLinkClient`'s composition-root assembly
  integration test (`test/dovahlink_client_test.dart`'s "Behavior composition-root teardown
  deduplication" group) — never re-derived at any individual Service's own mocked-everything
  unit-test level.
- `RequestServiceImpl.sendAndAwait`'s `connectionState` guard must fail a request issued before any
  `connect()` call immediately and synchronously with a typed `DovahLinkConnectionException`,
  without registering or transmitting anything.
