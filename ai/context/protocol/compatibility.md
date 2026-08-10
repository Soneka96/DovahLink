# Protocol compatibility

Protocol compatibility is part of the product contract. A message that works only because both implementations were changed together is not a stable protocol.

The canonical v1 schema is `protocol/schema/README.md`. This file defines how that schema evolves; it does not replace the schema.

## Versioning

- Every connection negotiates a protocol version through `hello` and `hello_ack` before state messages are exchanged. Pre-negotiation messages use envelope version `0`; later messages use the selected version.
- Keep protocol version separate from app version, bridge version, Skyrim runtime version, and transport version.
- Additive optional fields should preserve compatibility when their absence has a defined meaning.
- Changing the meaning or type of an existing field requires a new version or message shape.
- Never silently reinterpret an old field to mean something new.

## Unknown data

- Readers ignore unknown optional fields in the envelope and payload when the negotiated schema permits forward-compatible extension. Writers do not send unregistered fields in v1; unknown-field fixtures model a future compatible sender.
- Readers reject unknown required fields or incompatible message versions clearly.
- Writers must not send a field until the negotiated version and capabilities permit it.
- Generated or hand-written adapters must preserve fields they do not own when round-tripping is required.

## Capabilities

- Capabilities describe registered state areas, not arbitrary implementation details or unregistered client features.
- A missing capability means the client must remain usable without that feature.
- Capability negotiation must happen before optional state begins.
- Do not infer capabilities from a version number when the feature can vary independently. The v1 capability registry is defined in `protocol/schema/README.md`.

## Recovery

- On reconnect, the client receives a new `sessionId`, completes `hello` and capability negotiation, requests or receives a fresh snapshot for each subscribed state area, applies the snapshot, and only then accepts subsequent events.
- Messages from an older `sessionId` are discarded even if their revision is higher.
- A missing event must not leave the client permanently believing stale data is current.
- Duplicate events must be harmless where practical; otherwise the contract must expose the identity needed for safe deduplication.
- Events at or below the current revision are ignored as duplicate or stale; only a higher revision with the wrong `baseRevision` triggers recovery.
- Malformed, unsupported, or unauthenticated messages are rejected at the boundary and never reach game logic or presentation state.

## Reconnect policy

- Reconnect attempts use bounded exponential backoff with jitter and a maximum delay of 30 seconds.
- A successful negotiated connection resets the backoff.
- Reconnect must not block game-state capture or plugin shutdown.
- Queued state is not replayed blindly across a disconnect; recovery uses a fresh snapshot unless the protocol explicitly guarantees replay coverage.

## Change process

Before changing a message:

1. State the compatibility impact.
2. Update the canonical protocol documentation and fixtures.
3. Update both adapters and their contract tests in the same feature branch.
4. Test an older compatible message against the new reader when compatibility is promised.
