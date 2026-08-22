# SDK testing

Read `ai/context/integration/testing.md` for the cross-side contract-test conventions this file
does not repeat, and `ai/context/dart/dart-style.md` for general Dart test style.

Apply `ai/context/dart/dart-style.md`'s shared test-organization rules to SDK tests.
- Tests for a collaborator's method belong with that source unit's mirrored test file. A service test
  may prove the service's recovery behavior through a collaborator, but it must not become the
  direct test suite for the collaborator's method.

## Test fixtures and ownership

- Keep SDK-owned fixtures below the package-local
  `sdk/dart/dovahlink_client/test/fixtures/` directory, with subfolders mirroring the owning
  production area below `lib/src/` (`protocol`, `persistence`, `internal`, and so on). For example,
  the typed envelope fixture belongs at `test/fixtures/protocol/envelope.fixture.dart` relative to
  the SDK package root. This is an in-memory unit-test fixture, not a canonical cross-side JSON
  fixture. Use descriptive `.fixture.dart` files; do not create one global fixture or constants
  file, and apply the shared Dart fixture-builder rules.

## Ownership after SDK migration

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

Use a fake transport for state-machine, compatibility, session, and recovery tests; a real local
connection check is required only when transport framing or platform networking changes, mirroring
`ai/context/integration/testing.md`'s end-to-end boundary. Do not depend on a running Skyrim process
for behavior that can be proven deterministically without Skyrim.

## The independent validator stays independent

The .NET validation client (`integration/DovahLinkValidationClient/`) must remain a separate,
hand-written implementation of the canonical contract. It must not consume, wrap, generate from, or
otherwise reuse the Dart SDK; its value is precisely that it can catch a Bridge bug, an SDK bug, or
an assumption accidentally shared only by the official Dart implementation.
