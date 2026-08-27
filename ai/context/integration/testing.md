# Integration testing

Integration tests prove that the SKSE bridge and Flutter client agree on the canonical protocol. They do not replace unit tests inside either side.

## Shared fixtures

- Keep language-neutral protocol fixtures in the protocol area, not inside only the Flutter or SKSE test tree.
- Include valid snapshots, valid events, unavailable values, malformed messages, unknown optional fields, and stale revisions.
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
- snapshot delivery
- ordered event delivery
- shared sampled-capture cadence, including aligned due times and missed-tick handling
- unchanged sampled values producing no revision, publication, serialization, or network traffic
- Snapshot latest-value replacement and Event FIFO preservation under outbound pressure
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
- bounded recovery buffering: abandoning a buffered recovery attempt and requesting a fresh snapshot when the bound is exceeded, rather than growing unbounded
- a recovery snapshot request that times out or fails, causing the connection to be treated as unhealthy and recovery to proceed through the applicable bounded reconnect behavior followed by fresh synchronization

## End-to-end boundary

Use a deterministic fake transport first. A real local connection check is required when transport framing or platform networking changes, but it should not be the only proof of protocol correctness.

The first real connection check must prove loopback-only binding and rejection of a non-loopback peer. LAN checks are not a substitute for pairing and authenticated-encryption tests when LAN support is eventually proposed.

Do not use a running Skyrim process for tests that only verify protocol mapping, client decoding, or application state transitions.
