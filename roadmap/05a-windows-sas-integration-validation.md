# Stage 5A — Windows SAS Integration Validation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./05-dart-client-sdk-foundation.md) · [Next stage](./05b-android-secure-wifi-development-path.md)

## 5A. Windows SAS Integration Validation

**Status:** Planned. Phase 5A.0 (this rebaseline) is documentation only; no later phase is started.

This stage was split out of the former combined Stage 5A. It validates the separately developed,
experimental [`sas-pairing`](https://github.com/Soneka96/sas-pairing) library as a real DovahLink
consumer on Windows, inside the approved constrained loopback development environment, before
Android and non-loopback networking
([Stage 5B](./05b-android-secure-wifi-development-path.md)) add their own complexity.

This is a maintainer-prioritized, separately authorized workstream. It does not replace the normal
roadmap order: the next ordinary product-planning action remains the Stage 6 acceptance audit, and
this stage neither closes nor reopens any completed stage. Stage 5 stays complete. Target identity,
pairing, and transport authority is
[`ai/context/security/identity-and-transport.md`](../ai/context/security/identity-and-transport.md);
current runtime rules stay in `ai/context/protocol/security.md` until each phase below changes
them.

### Outcome

On Windows, against a Host on loopback, the maintainer can pair the official Flutter client with a
real Skyrim session through a real `sas-pairing` ceremony: the player compares a SAS value in the
app and in Skyrim, the Host asks the Skyrim player to authorize that exact attempt, and durable
trust exists only if the Host's own policy permits it. The path is proven end to end, with its
failure and cancellation behavior, without claiming network security.

### Security posture (read first)

- The S2.2 initial-pairing security decision ended **STOP** and remains STOP. This stage does not
  convert it to a pass and selects no production initial-pairing profile.
- `sas-pairing` is experimental and pre-alpha: not professionally audited, not formally verified,
  and not approved for production. Integrating it here is the maintainer's pre-alpha P10
  authorization (see the
  [initial-pairing security deviation](deviations/initial-pairing-security/README.md#p10-pre-alpha-integration-authorization)),
  not production-security approval. A human SAS match is bounded probabilistic evidence under
  `sas-pairing`'s stated assumptions, not proof that no attacker mediated the ceremony.
- Windows loopback is a constrained integration-validation environment. Success there is not
  evidence of hostile-network security. This stage authorizes no hostile-LAN pairing, no
  non-loopback listener or exposure, no Android production pairing, no completion of S3–S11, and no
  skipping of a future security review.
- The Host SAS foundation delivered by PR #121 is dormant. It stays dormant until Phase 5A.9, and
  only for Windows loopback.
- Completing this stage does not approve Stage 5B, Stage 22, or production LAN exposure.

### Status of what exists

| Category | Contents |
| --- | --- |
| Delivered runtime behavior | The six-digit pairing flow and bearer reconnect; Host-owned Known Device, Trusted/Revoked/Blocked, and Pair/Reject/Block administration (Stage 3); SDK-owned Known Hosts, discovery, and pairing recovery; Flutter pairing presentation. |
| Delivered dormant foundations (PR #121, not composed by the running Host) | Persistent non-exportable Host ECDSA P-256 identity key; Host Bootstrap and authority-scope encoders; the isolated `DovahLink.Host.PairingCeremony` library over the pinned `SasPairing` package; test-only peer. See the [Host architecture](../ai/context/host/architecture.md#dormant-sas-pairing-integration-foundation). |
| Approved future architecture | The exact-attempt Pending Pairing Approval separation and the SDK/Host/Adapter/Flutter ownership below, as selected in `identity-and-transport.md`. |
| Unimplemented planned behavior | Everything in Phases 5A.1–5A.11: SAS UI, interactive Host/Adapter prompts, pending approvals, the Client key, the SDK Initiator, the public contract, and activation. |
| Experimental security assumptions | `sas-pairing` and its P10 consumer integration are pre-alpha. |
| Unresolved production gates | S2.2 STOP; S4–S11; WSS/TLS; Host pinning; Client proof of possession; hostile-network threat model; independent review. |

### Ownership

```text
Skyrim
   ↕
Native SKSE Adapter
   ↕ Private typed IPC
C# Host
   ↕ Public DovahLink protocol
Dart SDK
   ↕ Typed API
Flutter application
```

- **Adapter** owns SKSE/CommonLib integration, Skyrim game-thread interaction, Skyrim-facing
  prompts and the player's selections, native game-state capture, and bounded private Host IPC. It
  owns no pairing policy, cryptography, trust administration, or durable Known Device state. It
  presents Host-directed interactions and returns the exact player decision.
- **C# Host** is the authoritative DovahLink application security boundary: session admission,
  pairing authorization, Known Devices, Trusted/Revoked/Blocked, persistent trust, pending pairing
  attempts, trust administration, and mapping generic SAS evidence to application authorization. A
  successful cryptographic ceremony never by itself grants durable trust.
- **Dart SDK** owns reusable client behavior: Client identity abstractions, Known Hosts, future Host
  pins, client-side SAS integration, transport and authentication, reconnect and recovery, security
  persistence semantics, and typed pairing states and outcomes.
- **Flutter** owns presentation, navigation, Redux presentation state, preferences, and calls to
  typed SDK operations. It is not the Host trust-administration interface and never gains Block,
  Unblock, Revoke, final Pair approval, Host-side Reject administration, or Known Device
  administration. It may display an authoritative blocked status received through the SDK. It must
  not create a separate security architecture.
- **`sas-pairing`** owns generic SAS construction, protocol profile, implementation, vectors, and
  security review only. DovahLink pairing authorization and trust policy never move into it.

### Intended player experience (planned, not implemented)

The numeric values below are illustrative, not test vectors or values to hardcode.

Flutter / Windows client:

```text
Preparing pairing...
        ↓
Compare pairing codes
4821 7394 1056

[Codes Match]
[Codes Don't Match]
        ↓
Waiting for approval in Skyrim...
        ↓
Connected / Rejected / Cancelled / Failed
```

The application only participates in the human SAS comparison. It does not authorize durable
pairing and offers no Block action.

Skyrim / Adapter, first the SAS comparison:

```text
Compare pairing code:
4821 7394 1056

[Match]
[Doesn't Match]
```

After successful ceremony evidence, the Host may request application authorization for one exact
pending attempt:

```text
Pair this device?

[Pair]
[Reject]
[Block]
```

The Adapter displays the request and reports the player's choice; the Host interprets it and applies
policy. Whether Block is offered, and what it persists, for an untrusted first-pair candidate is an
open design gate (Phase 5A.4).

### Authorization model

SAS evidence and authorization stay separate:

```text
Generic SAS ceremony
        ↓
Validated local ceremony result
        ↓
DovahLink pending pairing approval
        ↓
Skyrim player authorization
        ↓
Host application policy
        ↓
Durable trust, only if permitted
```

- A pending approval binds one specific attempt, including its relevant cryptographic
  identity/evidence and expiry. An old decision never authorizes a later attempt merely because the
  `clientId` is identical.
- Preserved distinctions: Trusted vs. Blocked; Revoked vs. Blocked; pairing vs. trusted reconnect;
  discovery candidate vs. Known Host; endpoint vs. Host identity; transport interruption vs.
  administrative invalidation; cryptographic Client identity vs. `clientId`.
- Existing Stage 3 pairing and recovery contracts do not change in this stage unless a phase below
  records an explicit, separately approved change.

### Design gate: Block before trust (unresolved)

Stage 3 defines Block as acting on an existing eligible Known Device record; an arbitrary,
never-known `clientId` cannot be blocked, and an `Unpaired` device is never eligible
([Stage 3](./03-local-device-pairing-and-reconnection.md)). The intended Skyrim UI may include Block
for a first-pair candidate, but its persistence semantics (whether it creates a Known Device record,
what identity it keys on given `clientId` is not cryptographic identity, how it is later
administered, and its abuse and storage bounds) are **not decided here**. Phase 5A.4 must obtain an
explicit maintainer decision before implementing Block on an untrusted candidate. Until then,
Block-before-trust is not specified, not implemented, and not assumed.

### Phase structure

Phases are decimal phases of this stage. Each is a focused, independently reviewable delivery slice,
delivered outward (Adapter/Host, then SDK, then Flutter) per the roadmap's planning rules, and each
needs an explicit maintainer instruction naming its scope before implementation. Names of fields and
messages below are conceptual and are not frozen protocol names.

| Phase | Layer | Summary |
| --- | --- | --- |
| 5A.0 | Docs | Roadmap and architecture rebaseline (this change) |
| 5A.1 | Flutter | SAS-ready pairing presentation |
| 5A.2 | Adapter | Skyrim interactive pairing UI foundation |
| 5A.3 | Host + Adapter | Typed interactive IPC |
| 5A.4 | Host | Pending pairing approval and exact-attempt policy |
| 5A.5 | SDK | Windows Client cryptographic identity |
| 5A.6 | `sas-pairing` + Host/SDK | P10 consumer API readiness |
| 5A.7 | Protocol + SDK + Host | DovahLink SAS public contract |
| 5A.8 | SDK | Real SAS Initiator integration |
| 5A.9 | Host | Activate Host SAS for Windows loopback |
| 5A.10 | Flutter | Connect real SAS pairing |
| 5A.11 | Cross-layer | Real Windows end-to-end validation |

Dependency order: 5A.1 is independent of the rest. 5A.2 precedes 5A.3. 5A.4 needs 5A.3 for the
Skyrim decision and the 5A.4 design gate. 5A.5 and 5A.6 are independent of each other and precede
5A.7–5A.8. 5A.7 needs 5A.4 and 5A.5. 5A.8 needs 5A.6 and 5A.7. 5A.9 needs 5A.3, 5A.4, and 5A.7.
5A.10 needs 5A.1, 5A.8, and 5A.9. 5A.11 needs all. The maintainer may reorder independent phases;
dependencies may not be skipped.

### Phase 5A.0 — Roadmap and architecture rebaseline

**Outcome:** Planning documentation separates Windows SAS integration validation (this stage) from
Android and secure Wi-Fi ([Stage 5B](./05b-android-secure-wifi-development-path.md)).
**Layer:** documentation only.
**Scope:** this stage file; the Stage 5B file; roadmap index and dependencies; forward-looking
references in the Host architecture, security, and deviation documents.
**Proof:** one canonical spec per stage; no broken links; only Markdown changed; historical S2.2
STOP text unchanged.
**Boundaries and non-goals:** no code, protocol, ABI, dependency, or runtime change; no activation of
dormant SAS components; no fixes for PR #121 review findings (those are a separate follow-up).

### Phase 5A.1 — Flutter SAS-ready pairing presentation

**Outcome:** The Flutter pairing journey can render the SAS comparison and waiting/outcome states,
without real SAS.
**Layer:** Flutter (presentation and Redux presentation state only).
**Dependencies:** none beyond the existing typed SDK pairing state surface.
**Scope:** screens and states for Preparing, Compare codes (value, Match, Doesn't Match), Waiting for
approval in Skyrim, and the Connected, Rejected, Cancelled, and Failed outcomes; the presentation
model that renders them. The current six-digit flow remains the functioning product path and is not
removed in this phase.
**Proof:** widget, view-model, and reducer-level tests driven by presentation fixtures. No test
constructs fake production cryptographic behavior or hardcodes a SAS value as a contract.
**Boundaries:** no pairing administration in the app; no Block, Unblock, Revoke, Pair, or Reject
control; no new security persistence; no SDK or protocol change.
**Non-goals:** real SAS, real backend wiring, removal of the six-digit UI.

### Phase 5A.2 — Skyrim interactive pairing UI foundation

**Outcome:** The Adapter can present a Host-directed interactive prompt to the player and capture the
player's selection, without any pairing logic.
**Layer:** Adapter (native).
**Dependencies:** none for the in-game mechanism; consumed by 5A.3.
**Scope:** a bounded, game-thread-safe mechanism to show a message with a small fixed set of choices
and report which one the player chose, for one prompt at a time, including cancellation and teardown
(loading, main menu, game exit). Prompt text and choices are supplied by the Host; the Adapter
interprets nothing.
**Proof:** native tests for lifecycle, single-prompt exclusivity, cancellation, expiry, and
teardown; a runtime check in Skyrim recorded by the maintainer.
**Boundaries:** no SAS cryptography, trust, or identity logic in the Adapter; a choice is never
retained after its prompt ends; an expired or superseded prompt cannot report a usable decision.
**Non-goals:** the IPC messages (5A.3), policy, SAS rendering rules.

### Phase 5A.3 — Host ↔ Adapter typed interactive IPC

**Outcome:** The Host can request an interaction from the Adapter and receive exactly one correlated
result, over the private IPC.
**Layer:** Host and Adapter. The contract is private; the public protocol is unchanged. Authority:
the "Host-to-adapter IPC contract" in
[`ai/context/host/architecture.md`](../ai/context/host/architecture.md) and
[`adapter-host-ipc/`](../adapter-host-ipc/README.md).
**Dependencies:** 5A.2.
**Scope:** typed request and response messages with correlation; bounded operation lifetime; game
thread safety; cancellation; connection-generation safety; stale-response rejection; defined
behavior on Adapter or Host restart; invalidation on loading screens and main-menu transitions.
**Proof:** cross-side fixtures and tests (as for the existing private IPC) for each lifecycle
case; stale, duplicate, late, wrong-generation, and unknown-correlation responses are rejected.
**Boundaries:** an expired or superseded prompt must not authorize another attempt; the transport
carries no key material or SAS secrets beyond what must be displayed; no SAS cryptography.
**Non-goals:** pending approval policy (5A.4), the public contract (5A.7).

### Phase 5A.4 — Host pending pairing approval and exact-attempt policy

**Outcome:** The Host holds a pending approval for one exact pairing attempt, obtains the Skyrim
player's decision for that attempt, and applies Host policy.
**Layer:** Host (the Adapter only displays and reports).
**Dependencies:** 5A.3. Authority for the intended separation:
`identity-and-transport.md` ("Selected DovahLink pairing authorization architecture").
**Scope:** a pending record that conceptually binds a Host-generated attempt identifier
(`pendingPairingId`), the `clientId`, the candidate Client cryptographic identity, a reference to
the validated ceremony evidence, display metadata, expiry, and authorization state; Pair, Reject,
and expiry/supersession transitions; one active attempt at a time consistent with the existing
single-client constraint. These are design inputs, not committed names or a wire schema.
**Design gate (blocks implementation of Block on a first-pair candidate):** the maintainer must
decide Block-before-trust semantics (see "Design gate: Block before trust" above). Reject and Pair
may proceed without that decision; Block on an untrusted candidate may not.
**Proof:** Host tests that a decision authorizes only its own attempt; that the same `clientId`
in a later attempt gets no authority from an earlier decision; that expiry, supersession,
disconnect, and restart leave no trust; that Pair creates trust only under Host policy.
**Boundaries:** trust authority stays in the Host; the generic library receives no authorization or
trust policy; no new trust on ceremony success alone.
**Non-goals:** SAS cryptography, public wire changes, Flutter authorization actions.

### Phase 5A.5 — Windows SDK Client cryptographic identity

**Outcome:** The SDK has a persistent Client ECDSA P-256 identity behind its platform/security
boundary on Windows.
**Layer:** Dart SDK (platform port plus Windows implementation).
**Dependencies:** none; consumed by 5A.7–5A.8. Authority: `identity-and-transport.md` §6, §14–§15
and `ai/context/sdk/persistence.md`.
**Scope:** a key abstraction (create, use to sign, expose public key as DER SubjectPublicKeyInfo
and fingerprint, identify by reference) with a protected, non-exportable Windows-backed
implementation preferred, consistent with the approved architecture. `clientId` stays separate from
key identity. Raw private-key bytes never enter the ordinary JSON client state; that state holds
only a key reference and public data. The port is designed so a future Android Keystore
implementation fits, without implementing Android.
**Required semantics (decided before implementation):** behavior on key loss, an inaccessible or
mismatched stored key (fail closed, never silent regeneration), deliberate reset, replacement, and
rotation, including their effect on existing Host trust.
**Proof:** SDK tests with an in-memory fake of the port; Windows tests for the real key behavior.
**Boundaries:** no Host-authoritative trust state in the client; no change to the reconnect
protocol.
**Non-goals:** Client proof of possession on reconnect (S7), Android, Host pinning (S5).

### Phase 5A.6 — `sas-pairing` P10 consumer API integration readiness

**Outcome:** It is known, from the actually pinned `sas-pairing` revision, whether a supported
outbound Initiator connection path exists, and the SDK integration has a clean dependency to build
on.
**Layer:** `sas-pairing` repository (generic), then Host/SDK pin updates.
**Dependencies:** none; gates 5A.8.
**Scope:** verify the current API against the pinned and the latest `sas-pairing` revision. PR #121
used a test byte relay because ABI v1 was listen-only ("no export connects out"), so do not assume
that this limitation still holds or still applies. If no supported outbound Initiator path exists,
plan a focused generic `sas-pairing` change (tracked and reviewed in that repository), rather than
embedding the relay workaround permanently in the DovahLink SDK. Also confirm the Dart package's
Windows-only, FFI, and library-distribution requirements for the SDK.
**Proof:** a written readiness result naming the verified revision and the chosen path; any needed
generic change landed and reviewed in `sas-pairing`, with its own vectors and tests.
**Boundaries:** generic SAS functionality belongs to `sas-pairing`; DovahLink authorization and trust
policy never migrate into it; a dependency bump is a separate maintainer-approved change.
**Non-goals:** DovahLink SDK integration code, Android support (a later `sas-pairing` milestone).

### Phase 5A.7 — DovahLink SAS public protocol / SDK contract

**Outcome:** The minimum public contract exists for a Client and Host to run one real pairing
attempt, and the SDK exposes it as typed states and outcomes.
**Layer:** `protocol/` (canonical contract), Host mapping, SDK typed API. This phase is the only one
that changes the public wire contract; it follows the protocol schema and versioning rules in
`protocol/schema/README.md`.
**Dependencies:** 5A.4 (attempt model), 5A.5 (Client identity).
**Scope:** define the minimum messages and states to begin an attempt, carry the information the
Client needs to run its side, report presentation state, report outcome, and cancel. Keep these
distinct and never conflate them: the DovahLink pairing attempt ID; the generic SAS run/ceremony
identifiers; identity inputs (`hostId`, `clientId`, key fingerprints); Bootstrap context; presentation
state; cryptographic completion evidence; and Host application authorization.
**Proof:** schema and fixtures validated by `tooling/validate_protocol_fixtures.py`; Host and SDK
conformance tests; compatibility handling per the documented policy.
**Boundaries:** no native `sas-pairing` handles or ceremony secrets in the public protocol; the
authorization decision is Host-internal and never client-supplied; no automatic durable trust.
**Non-goals:** activation (5A.9), the SDK ceremony implementation (5A.8), Flutter wiring.

### Phase 5A.8 — SDK real SAS Initiator integration

**Outcome:** The SDK runs the real Initiator side of a ceremony and exposes the SAS to its
consumers through the typed API.
**Layer:** Dart SDK. Flutter does not integrate `sas-pairing`.
**Dependencies:** 5A.6, 5A.7.
**Scope:** the SDK-owned adapter over the Dart `sas-pairing` package, using the path established in
5A.6; supplying the Client's own identity values to the Bootstrap; surfacing the presentation
state; carrying the user's Codes Match or Codes Don't Match decision into the ceremony for the exact
presented ceremony; mapping all outcomes (mismatch, cancel, timeout, peer failure, Bootstrap
mismatch) to typed results.
**Proof:** SDK tests against a real `sas-pairing` peer (or the Host test peer) on Windows,
including identity mismatch and invalid peer Bootstrap; the SDK never reports trust from ceremony
success.
**Boundaries:** the SDK holds no authorization authority; ceremony results are local evidence, not
trust; native resources are owned and released deterministically.
**Non-goals:** Host activation, Flutter changes, Android.

### Phase 5A.9 — Activate Host SAS for Windows loopback

**Outcome:** The running Host can run real `sas-pairing` ceremonies and connect their local results
to the pending-approval policy, on Windows loopback only.
**Layer:** Host.
**Dependencies:** 5A.3, 5A.4, 5A.7.
**Scope:** compose the Host identity key and the `DovahLink.Host.PairingCeremony` integration into
the running Host (adding the `DovahLink.Host` reference); the Host `sas-pairing` listener and driver
lifecycle bound to loopback; the Skyrim SAS comparison prompt and the MATCH/MISMATCH decision for the
exact presented ceremony; validating a local result into ceremony evidence; creating the pending
approval; shutdown, failure, and responsiveness behavior. This is the designated point where the
dormant PR #121 foundation becomes active.
**Proof:** Host tests with the real native library for the lifecycle, failure, and shutdown paths;
ordinary Host work stays responsive during a ceremony; the packaged Host contains the intended
assemblies only.
**Boundaries:** loopback only. No non-loopback listener, no LAN exposure, no hostile-network claim,
no bypass of the pending approval. Ceremony success never creates durable trust by itself. The
six-digit flow stays available.
**Non-goals:** Flutter wiring, removing the six-digit flow, network-facing activation.

### Phase 5A.10 — Connect real Flutter SAS pairing

**Outcome:** The Flutter pairing journey uses the real SDK SAS path.
**Layer:** Flutter (presentation over typed SDK operations).
**Dependencies:** 5A.1, 5A.8, 5A.9.
**Scope:** wire the 5A.1 presentation to the real SDK states and the Codes Match / Codes Don't Match
calls; show Waiting for approval in Skyrim and the terminal outcomes. The obsolete six-digit
user-interface behavior is removed only once the real replacement works and has been validated
(that is, after 5A.11 evidence, or by an explicit maintainer decision at the end of this phase).
**Proof:** app tests over SDK fakes plus a real run against a loopback Host.
**Boundaries:** no Block, Unblock, Revoke, final Pair approval, Host-side Reject, or Known Device
administration in the app; an authoritative blocked status from the SDK may be displayed.
**Non-goals:** any Host-side or protocol change, Android, new security persistence.

### Phase 5A.11 — Real Windows end-to-end validation

**Outcome:** The real application path is accepted, or its gaps are recorded.

```text
Skyrim
 ↕
Adapter
 ↕
C# Host
 ↕
sas-pairing
 ↕
Dart SDK
 ↕
Windows Flutter app
```

**Layer:** cross-layer acceptance; adds no new feature behavior.
**Dependencies:** 5A.1–5A.10.
**Scenario categories** (evidence is process-level tests where the path can be automated with
synthetic game input, plus a maintainer-recorded Skyrim runtime validation; no scenario is claimed
passing before it runs):

- legitimate comparison and completion;
- SAS mismatch;
- user cancellation, from the app and from Skyrim;
- exact-attempt approval, and that it applies to no other attempt;
- Reject, and Block per the decision made at the 5A.4 design gate;
- expired or stale decisions;
- Client or Host identity mismatch;
- Host or Adapter disconnect and restart;
- app closure;
- invalid or mismatched peer Bootstrap;
- game-context transitions (loading, main menu);
- no durable trust after any incomplete, failed, or rejected ceremony.

**Proof:** an acceptance record listing, for each category, the evidence (test names, runtime notes)
and any limitation. It states plainly that the environment is Windows loopback and that nothing here
approves production security, LAN exposure, or Stage 5B.
**Non-goals:** a large mechanical test matrix, performance work, Android, any non-loopback run.

### `sas-pairing` activation

Activating the dormant Host `sas-pairing` foundation is owned by Phase 5A.9, and completing the
product path by Phases 5A.10–5A.11, always within the Windows loopback boundary above. Until then the
existing six-digit flow and bearer reconnect remain the active product behavior, and the dormant
foundation does not satisfy any security gate.

### Explicit stage non-goals

- No Android, mobile code, or iOS work; see [Stage 5B](./05b-android-secure-wifi-development-path.md).
- No non-loopback listener, LAN exposure, discovery change, or automatic port selection.
- No WSS/TLS, Host pinning, or Client proof of possession on reconnect (S5–S7).
- No production-security claim, audit claim, or closing of the S2.2 STOP.
- No change to the Stage 3 Known Device model beyond what an explicitly approved decision at the
  5A.4 design gate records.
- No fixes for PR #121 review findings as part of this stage's documentation phases.

### Stage acceptance criteria

- Each phase's own proof criteria are met and recorded, and each phase landed as an independently
  reviewed change.
- The Phase 5A.11 acceptance record exists with its limitations stated.
- Trust authority remained in the Host; Flutter gained no pairing administration; `sas-pairing`
  gained no DovahLink policy.
- Every security statement in this document still holds: S2.2 STOP intact, `sas-pairing`
  experimental, no hostile-network claim, Block-before-trust either decided by the maintainer or
  still explicitly unresolved.

### Dependencies and boundaries

This stage consumes the identity and pairing semantics of Stages 2 and 3, the SDK platform-port and
persistence boundaries of Stage 5, and the PR #121 dormant foundation. It does not close Stage 5,
Stages 22–23, or any security-migration slice S3–S11. Stage 5B depends on the secure transport and
identity work it names and is not unblocked by this stage.
