# Protocol compatibility

Protocol compatibility is part of the product contract. A message that works only because both implementations were changed together is not a stable protocol.

The canonical schema is `protocol/schema/README.md`. This file defines how that schema evolves and how compatibility with it is identified; it does not replace the schema.

## Compatibility model

- The current wire contract has no independent runtime protocol-generation number. Compatibility is identified by the DovahLink Bridge/mod release version against the supported Bridge-version range a client or SDK explicitly declares.
- A supported range is deliberate, not inferred. Sharing a version prefix (for example `0.8.x`) does not by itself mean compatible; a contract-breaking change forces a compatibility review regardless of how the release number changed.
- Skyrim runtime, SKSE, and CommonLib compatibility belong to the bridge alone. A Skyrim/SKSE update that leaves the bridge/client wire contract unchanged requires no client compatibility change.
- SDK version, official app version, Bridge version, and roadmap phase are independent numbers. None is derived from another.
- Once the Dart SDK exists, every SDK release declares an explicit supported Bridge-version range (for example a minimum and a maximum). Until then, the app-side Dart client documented in `ai/context/flutter/` follows this same policy.

## Compatibility bootstrap

A client must learn which Bridge release it has connected to before depending on the normal contract:

```text
transport established
    -> minimal stable bootstrap
    -> Bridge exposes its release version
    -> client/SDK checks its declared supported range
    -> compatible? yes: normal DovahLink communication
                    no:  explicit incompatibility failure and disconnect
```

The bootstrap stays intentionally small: its purpose is compatibility detection, not general protocol negotiation. Do not evolve it into its own versioned protocol stack (no `BootstrapProtocolV1`/`V2`); if its representation ever needs a breaking redesign, that is a deliberate compatibility decision made from the requirements that exist at that time, not solved speculatively here.

## Incompatible combinations

An incompatible Bridge/client pairing fails explicitly. It must not partially parse normal traffic, silently continue, guess compatibility, fall back to the closest contract, or present stale or default data as current.

Where safely knowable, distinguish a Bridge older than the supported range from a Bridge newer than it, so the product can tell the user which component needs updating. The SDK/client owns the structured meaning of the incompatibility; the product owns the user-facing wording.

## Contract changes

Before changing a message:

1. State the compatibility impact: does the currently declared supported client/SDK range still understand this Bridge release correctly?
2. Update the canonical protocol documentation and fixtures.
3. Update both adapters and their contract tests in the same feature branch.
4. If the change stays compatible, record that the existing supported range remains valid. If it does not, update the client/SDK's declared range, its implementation, fixtures, tests, and documentation together, and confirm an old unsupported client/SDK rejects the new Bridge release cleanly.

## Unknown data

- Readers ignore unknown optional fields in the envelope and payload when the current schema permits forward-compatible extension.
- Readers reject unknown required fields or an incompatible Bridge/client combination clearly.
- Writers must not send a field until the current schema and negotiated capabilities permit it.
- Generated or hand-written adapters must preserve fields they do not own when round-tripping is required.

## Capabilities

- Capabilities describe registered state areas, not arbitrary implementation details or unregistered client features.
- A missing capability means the client must remain usable without that feature.
- Capability negotiation must happen before optional state begins.
- Do not infer capabilities from a Bridge release number when the feature can vary independently. The canonical capability registry is defined in `protocol/schema/README.md`.

## Recovery

The current recovery sequence and error codes belong to `protocol/schema/README.md`. Compatible changes preserve session isolation, prevent missing state from appearing current, retain safe duplicate handling, and reject invalid messages before game logic or presentation state.

## Future multi-contract support

Simultaneous support for more than one historical contract generation is deferred, not prohibited. Revisit it only when a concrete requirement exists — established third-party clients, independently distributed components that cannot update together, a public backwards-compatibility guarantee, or a comparable constraint — and design that system from the requirements in force at that time.
