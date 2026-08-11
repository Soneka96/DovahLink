# Protocol compatibility

Protocol compatibility is part of the product contract. A message that works only because both implementations were changed together is not a stable protocol.

The canonical v1 schema is `protocol/schema/README.md`. This file defines how that schema evolves; it does not replace the schema.

## Versioning

- Version negotiation follows the current sequence in `protocol/schema/README.md`.
- Keep protocol version separate from app version, bridge version, Skyrim runtime version, and transport version.
- Change the protocol version only when the canonical wire contract changes. Client-only screens,
  state, tests, and internal boundaries do not change it.
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

The current recovery sequence and error codes belong to `protocol/schema/README.md`. Compatible
changes preserve session isolation, prevent missing state from appearing current, retain safe
duplicate handling, and reject invalid messages before game logic or presentation state.

## Change process

Before changing a message:

1. State the compatibility impact.
2. Update the canonical protocol documentation and fixtures.
3. Update both adapters and their contract tests in the same feature branch.
4. Test an older compatible message against the new reader when compatibility is promised.
