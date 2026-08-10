# Integration testing

Integration tests prove that the SKSE bridge and Flutter client agree on the canonical protocol. They do not replace unit tests inside either side.

## Shared fixtures

- Keep language-neutral protocol fixtures in the protocol area, not inside only the Flutter or SKSE test tree.
- Include valid snapshots, valid events, unavailable values, malformed messages, unknown optional fields, incompatible versions, and stale revisions.
- Every fixture must contain the protocol version required to decode it; expected decoded values are asserted in the consuming test rather than duplicated in fixture metadata.
- Shared fixtures are the source of truth for cross-side contract tests; client- or bridge-only fixtures must not redefine them.

## Contract tests

- SKSE adapter tests prove native response values serialize to the expected canonical messages.
- Flutter adapter tests prove canonical messages decode into the expected client models.
- Both sides must test the same fixture, not equivalent hand-written examples.
- Assert exact field values, units, enum meanings, revisions, and unavailable-data behavior.

## Connection scenarios

Cover at least:

- initial negotiation
- capability exchange
- snapshot delivery
- ordered event delivery
- duplicate and out-of-order events
- reconnect and snapshot recovery
- protocol-version mismatch
- unsupported capability
- malformed message
- transport disconnect while state is changing

## End-to-end boundary

Use a deterministic fake transport first. A real local connection check is required when transport framing or platform networking changes, but it should not be the only proof of protocol correctness.

Do not use a running Skyrim process for tests that only verify protocol mapping, client decoding, or application state transitions.
