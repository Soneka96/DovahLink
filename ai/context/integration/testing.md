# Integration testing

Integration tests prove that the SKSE bridge and Flutter client agree on the canonical protocol. They do not replace unit tests inside either side.

## Shared fixtures

- Keep language-neutral protocol fixtures in the protocol area, not inside only the Flutter or SKSE test tree.
- Include valid snapshots, valid events, unavailable values, malformed messages, unknown optional fields, and stale revisions.
- When Stage 4 registers production state areas, assert their capability advertisement, exact domain
  data shape, update mode, revision behavior, and unavailable-value representation from shared
  Bridge/SDK/.NET fixtures.
- Expected decoded values are asserted in the consuming test rather than duplicated in fixture metadata.
- Shared fixtures are the source of truth for cross-side contract tests; client- or bridge-only fixtures must not redefine them.

## Contract tests

- SKSE adapter tests prove native response values serialize to the expected canonical messages.
- Flutter adapter tests prove canonical messages decode into the expected client models.
- Both sides must test the same fixture, not equivalent hand-written examples.
- Assert exact field values, units, enum meanings, revisions, and unavailable-data behavior.

## Connection scenarios

Cover at least:

- compatibility bootstrap
- capability exchange
- registered state-area capabilities and rejection of unknown state areas
- snapshot delivery
- ordered event delivery
- shared sampled-capture cadence, including aligned due times and missed-tick handling
- unchanged sampled values producing no revision, publication, serialization, or network traffic
- Snapshot latest-value replacement and Event FIFO preservation under outbound pressure
- Normal/Heavy publication classification, four-slot Heavy capacity, and Heavy Snapshot/Event
  behavior across the wire using a deterministic larger structured publication fixture rather than
  a production inventory, map, or asset domain
- Snapshot dirty-marker retry after pressure and bounded queue-byte accounting
- reliable Event overflow explicitly disconnecting the slow client without blocking game capture
- duplicate and out-of-order events
- reconnect and snapshot recovery
- reconnect after an unhealthy Event session receiving only the current authoritative snapshot, with
  no replay of the previous session's queued events
- an incompatible Bridge/client version during the compatibility bootstrap
- unsupported capability
- malformed message
- oversized frame
- unauthenticated or unauthorized peer
- replayed message and stale session
- rate-limited repeated violations
- invalid token, expired token, reused token, and duplicate message ID
- redaction of tokens, credentials, paths, and raw infrastructure exceptions from errors and logs
- nesting-depth, string-length, array-length, object-member, idle-timeout, outbound-queue, and session-message limits
- concurrent token-consumption attempts and concurrent proof-client attempts
- failed-token throttling and repeated-violation connection closure
- transport disconnect while state is changing
- revision-gap detection entering recovery, and the domain not exposing state as synchronized until an authoritative recovery snapshot arrives
- recovery buffering of events that arrive while a recovery snapshot is in flight, including a later snapshot superseding already-buffered events at or below its revision
- recovery ordering proving that stateful events after the accepted snapshot are applied in order and
  ephemeral notifications are not incorrectly treated as snapshot-recoverable state
- failed compatibility or domain-readiness checks leaving canonical writers on the old contract rather
  than performing a partial cutover
- bounded recovery buffering: abandoning a buffered recovery attempt and requesting a fresh snapshot when the bound is exceeded, rather than growing unbounded
- a recovery snapshot request that times out or fails, causing the connection to be treated as unhealthy and recovery to proceed through the applicable bounded reconnect behavior followed by fresh synchronization

## End-to-end boundary

Use a deterministic fake transport first. A real local connection check is required when transport framing or platform networking changes, but it should not be the only proof of protocol correctness.

The first real connection check must prove loopback-only binding and rejection of a non-loopback peer. LAN checks are not a substitute for pairing and authenticated-encryption tests when LAN support is eventually proposed.

Do not use a running Skyrim process for tests that only verify protocol mapping, client decoding, or application state transitions.

## Real-harness disconnect/reconnect timing

A single-session Bridge process (`kMaxConnectedClients == 1`) releases its previous session slot
asynchronously, across the process boundary, after processing a client's socket teardown. A
client-side `disconnect()`/transport `close()` future only waits for that side's own teardown, not
for the Bridge to finish releasing the slot.

- A real-harness test that disconnects and then immediately reconnects to the *same* Bridge process
  must not treat completion of the client-side `disconnect()` future as proof the Bridge has
  released the previous session slot. Doing so races the Bridge's asynchronous release: if the new
  `hello()` arrives first, admission sees the slot still occupied and can return `unauthorized`.
- The Skyrim-independent test harness (`bridge/harness/dovahlink_bridge_harness.cpp`) prints an
  observable `SESSION_RELEASED <clientId>` stdout line the instant a connection's own teardown
  actually releases its session slot (`ISessionReleaseNotificationSink`,
  `bridge/application/connection_session.hpp`). Such a test must wait for that line -- or another
  deterministic release-synchronization mechanism -- before reconnecting, rather than an arbitrary
  fixed sleep or a bounded blind retry against a timing-dependent rejection.
- This line is scoped to a connection's own organic teardown only. `revoke`/`block`/`trust_reset`/
  `factory_reset` invalidate an active session directly (`ActiveSessionController`, on the harness's
  main command thread) before force-closing its socket, strictly before the connection's own worker
  thread notices and unwinds; `ConnectionSession` checks `ISessionManager::IsValidForConnection`
  immediately before releasing so it never re-announces a slot an administrative caller already
  released. Do not expect `SESSION_RELEASED` after those commands -- their own existing response
  line (`REVOKED <clientId>`, `BLOCKED <clientId>`, and so on) remains the sole, synchronous signal,
  and both a real Bridge session and this harness's own command loop write to the same output
  stream, so a second, uncoordinated line racing it would have no defined order.
- This is a test-harness lifecycle race, not a production condition: do not broaden production
  `unauthorized` handling to be globally retryable to work around it, and do not treat the real
  Skyrim plugin as having an equivalent signal -- its own composition root wires a no-op
  implementation of the same interface, since gameplay has no use for it. This section also does not
  apply when a test disposes the previous Bridge process and starts a fresh one before reconnecting
  -- a new process has no prior session slot to race.
