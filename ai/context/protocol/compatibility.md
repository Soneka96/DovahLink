# Protocol compatibility

Protocol compatibility is part of the product contract. A message that works only because both implementations were changed together is not a stable protocol.

The canonical schema is `protocol/schema/README.md`. This file defines how that schema evolves and how compatibility with it is identified; it does not replace the schema.

## Compatibility model

- The current wire contract has no independent runtime protocol-generation number. Compatibility is identified by the Host's own release version against the supported Host-version range a client or SDK explicitly declares, per
  `plans/documentation-and-composition-normalization/01.3a-public-vocabulary-and-identity-semantics.md`
  Section A. Source of truth stays the repository's single `VERSION` file
  (`ai/context/common.md`'s Versioning section); the wire field is `hostVersion`.
- A supported range is deliberate, not inferred. Sharing a version prefix (for example `0.8.x`) does not by itself mean compatible; a contract-breaking change forces a compatibility review regardless of how the release number changed.
- Skyrim runtime, SKSE, and CommonLib compatibility belong to the Adapter alone (`ai/context/adapter/architecture.md`'s "Ownership"), not to the Host's public compatibility boundary. A Skyrim/SKSE update that leaves the Host/client wire contract unchanged requires no client compatibility change.
- SDK version, official app version, Host version, and roadmap phase are independent numbers. None is derived from another.
- Once the Dart SDK exists, every SDK release declares an explicit supported Host-version range (for example a minimum and a maximum). Until then, the app-side Dart client documented in `ai/context/flutter/` follows this same policy.
- The repository release is `0.5.0`, and the Dart SDK declares Host `0.5.x` as its supported range.
  Released Host `0.4.0` used additive `subscribe.stateAreas` updates, while the Phase 5.3 contract
  replaces the complete active set; the public SDK subscription API therefore does not support
  released Host `0.4.0`. Before `1.0.0`, the
  major and minor must match and the patch is ignored; after `1.0.0`, the major must match, a Host
  minor above the SDK's accepted minor is rejected, and the patch is ignored. The SDK checks
  `hello_ack.hostVersion` before session admission and closes on an incompatible Host.
- If an SDK version declares support for a Host-version range, every public API that SDK version exposes must work across that entire declared range; a range must not silently exclude a public feature (for example "supports Host 0.5-0.8, but Inventory requires 0.7+"). A new public SDK feature that requires a newer Host contract raises that SDK version's declared minimum instead of narrowing which features work within the existing declared range. Encountering the declared-supported Host range without full support for the declared public surface is a contract/programming defect to fix, not a condition for the SDK to hide behind runtime feature negotiation.
- SDK↔Host protocol compatibility is determined by the SDK's declared supported Host range, defined above. Runtime Skyrim/mod feature availability is represented separately by capabilities/mod-awareness where applicable, per the Capabilities section below. Runtime capability negotiation must not be used to hide an unsupported protocol version, and protocol-version compatibility does not imply that every runtime Skyrim capability is present.

## Compatibility bootstrap

A client must learn which Host release it has connected to before depending on the normal contract:

```text
transport established
    -> minimal stable bootstrap
    -> Host exposes its release version
    -> client/SDK checks its declared supported range
    -> compatible? yes: normal DovahLink communication
                    no:  explicit incompatibility failure and disconnect
```

The bootstrap stays intentionally small: its purpose is compatibility detection, not general protocol negotiation. Do not evolve it into its own versioned protocol stack (no `BootstrapProtocolV1`/`V2`); if its representation ever needs a breaking redesign, that is a deliberate compatibility decision made from the requirements that exist at that time, not solved speculatively here.

The bootstrap's own canonical message types and error codes are closed typed vocabulary. Decode a
valid `hello_ack`, validate its Host version immediately, and do not accept capabilities or normal
traffic until that check succeeds. An unrecognized wire value is malformed protocol input; a known
Host version outside the declared range is an explicit incompatibility failure.

## Incompatible combinations

An incompatible Host/client pairing fails explicitly. It must not partially parse normal traffic, silently continue, guess compatibility, fall back to the closest contract, or present stale or default data as current.

Where safely knowable, distinguish a Host older than the supported range from a Host newer than it, so the product can tell the user which component needs updating. The SDK/client owns the structured meaning of the incompatibility; the product owns the user-facing wording.

## Contract changes

Before changing a message:

1. State the compatibility impact: does the currently declared supported client/SDK range still understand this Host release correctly?
2. Update the canonical protocol documentation and fixtures.
3. Update both adapters and their contract tests in the same feature branch.
4. If the change stays compatible, record that the existing supported range remains valid. If it does not, update the client/SDK's declared range, its implementation, fixtures, tests, and documentation together, and confirm an old unsupported client/SDK rejects the new Host release cleanly.

The current pre-release `0.5.x` contract adds required `hostId` and `hostName` fields to
`hello_ack`. The Host and Dart SDK sources are updated together in the same feature branch, so the
current supported range remains `0.5.x`. Earlier unreleased `0.5.x` builds are not compatibility
targets under `ai/context/common.md`'s pre-release policy; do not add nullable legacy fields or a
compatibility fallback for them. The repository version remains release-managed and is not bumped
in this feature change.

The Phase 5.3 meaning of `subscribe.stateAreas` is complete-set replacement. Released Host `0.4.0`
treated successive requests additively, so it cannot satisfy the Phase 5.3 public SDK subscription
API. The next compatible Host line is `0.5.x`; do not use capability negotiation or a second wire
implementation to imply compatibility with `0.4.x`.

## Release compatibility review

At each SDK/Host release, review the SDK↔Host compatibility range: the minimum supported
Host, the maximum supported Host, the relevant versions where protocol behavior changed since
the last review, the full public SDK API surface, incompatible-old behavior, incompatible-new
behavior, and user/developer guidance. Do not release with a compatibility claim that the full SDK
surface does not satisfy. This is a release-cadence full-surface audit, additional to and distinct
from the per-message review above — the per-message checklist assesses one contract change at a
time, while this review confirms the declared range still holds across everything the SDK has
shipped since the range was last validated.

## Unknown data

- Readers ignore unknown optional fields in the envelope and payload when the current schema permits forward-compatible extension.
- Readers reject unknown required fields or an incompatible Host/client combination clearly.
- Writers must not send a field until the current schema and negotiated capabilities permit it.
- Generated or hand-written adapters must preserve fields they do not own when round-tripping is required.

## Capabilities

- Capabilities describe registered state areas, not arbitrary implementation details or unregistered client features.
- A missing capability means the client must remain usable without that feature.
- Capability negotiation must happen before optional state begins.
- Do not infer capabilities from a Host release number when the feature can vary independently. The canonical capability registry is defined in `protocol/schema/README.md`.

## Recovery

The current recovery sequence and error codes belong to `protocol/schema/README.md`. Compatible changes preserve session isolation, prevent missing state from appearing current, retain safe duplicate handling, and reject invalid messages before game logic or presentation state.

## Future multi-contract support

Simultaneous support for more than one historical contract generation is deferred, not prohibited. Revisit it only when a concrete requirement exists — established third-party clients, independently distributed components that cannot update together, a public backwards-compatibility guarantee, or a comparable constraint — and design that system from the requirements in force at that time.

## Public state-authority continuity identifier

The public protocol exposes a state-authority continuity identifier, per
`plans/documentation-and-composition-normalization/01.3a-public-vocabulary-and-identity-semantics.md`
Section C, implemented by Concept `01.3c`. It is deliberately not a public counterpart
of the host-observed `adapterInstanceId` -- see the Decision below for why the two are independent
axes, not aliases. `ARCHITECTURE.md`'s "Runtime and identity model" fixes the four private identity
lifetimes; this decision adds a fifth, public-only concept without changing any of them.

**Decision:** yes. The wire field is `stateAuthorityId`, identifying a Host
authoritative-state *continuity epoch*: it changes whenever cached revisions from
before an event are no longer safely comparable to revisions after it, and only then --
rotated the instant the Host detects the break, not later when the recovery from it
succeeds. It is deliberately not `adapterInstanceId` itself, and not 1:1 with it -- a
Host restart rotates it at startup; an Adapter/IPC connection loss rotates it the
moment loss is detected, per `ai/context/host/architecture.md`'s "Adapter loss"
behavior, regardless of whether that loss later resolves via the same
`adapterInstanceId` reconnecting or a new one binding. One continuity break produces
exactly one rotation, at detection -- a new `adapterInstanceId`, when it follows, is a
consequence of a rotation that already happened, not an independent trigger of its
own. `01.3a`'s Section C records the full lifecycle (creation point, stability across
reconnects, null-after-startup answer, exact wire presence, comparison semantics);
`01.3c` implements it.

This was a hard gate, not a soft preference: `state_snapshot` and `state_event` publication was not
permitted to go live until `01.3c` implemented this field with real values, which it now does. A
state revision's identity is scoped to `(stateAuthorityId, playContextId, stateArea)` (renamed from
its earlier tuple form -- see `01.3a` Section D), and clients rely on the
authority component to reject state from an earlier authoritative-store lifetime; publishing live
state before that component had a real, decided value would have let a client silently accept state
from the wrong lifetime. Do not substitute `adapterInstanceId`, an OS process ID, a port, a path, or
an owner-lifetime ID for it -- none of them identify the same thing this field identifies, per
`ARCHITECTURE.md`'s "Runtime and identity model" and `01.3a` Section C's reasoning.
