# Identity and transport security architecture

**Status:** S1 architecture contract with an unresolved initial-pairing gate. S2.2 reassessed the
Pasini–Vaudenay SAS-AKE construction, current Shortcake upstream, DovahLink's Host lifecycle, and
platform integration evidence. **S2.2 STOP — no production initial-pairing profile is selected and
S3 remains blocked.** The paper's commitment can be implemented faithfully in an isolated POC, but
Shortcake still omits its independent randomness; no proof justifies the proposed post-SAS identity,
transcript-MAC, and Client-PoP composition; the P-256 library change remains an open PR; retry
analysis lacks an accepted lifetime bound; and no canonical bytes, vectors, or demonstrated target
platform builds exist. This STOP applies to initial pairing; it does not reopen the separately
selected normal reconnect architecture of application-level fresh ECDSA P-256 proof-of-possession.
Its exact S7 protocol remains future work. This document does not describe behavior already
implemented. Until later migration PRs land, current behavior remains defined by
`protocol/schema/README.md`, `ai/context/protocol/security.md`, and the current SDK/Host
implementation. Sections below that describe cryptographic migration behavior remain proposals
only where they are not supported by a completed security gate.

The Host/client security migration must follow this contract. Cryptographic algorithms and libraries are not implementation details to guess later. S2.2 is the current feasibility and selection gate; it ended in STOP, so do not implement S3 or infer a protocol from this document's candidate descriptions.

## 1. Goals

- Give each DovahLink Host and Client a stable logical installation identity and a separate persistent cryptographic identity.
- Authenticate and encrypt connections with standard TLS 1.3 over WebSocket (`WSS`), using Host-key pinning rather than public certificate authorities.
- Use client proof-of-possession instead of a reusable bearer credential for trusted reconnects.
- Make pairing the user-authorized establishment of a Host-key ↔ Client-key relationship through an initial-pairing construction that passes the security gate. The construction is unresolved; balanced PAKE is no longer a mandatory selection.
- Make client persistence support zero or more Known Hosts without confusing local knowledge with the Host's live trust decision.
- Preserve distinct Host-authoritative trusted, revoked, blocked, and unknown outcomes.

## 2. Non-goals

S1 does not implement TLS, key generation or storage, PAKE, authentication, pairing changes, schema changes, multi-Host persistence, discovery expansion, UI, or key rotation. It does not select a PAKE algorithm, ciphersuite, or library. It does not add a public-CA dependency or custom cryptography.

LAN/mDNS discovery, Host selection UI, automatic Host ranking or failover, aliases, and session UI remain outside this contract's implementation scope.

## 3. Terms

- **Logical identity** is a stable DovahLink installation identifier such as `hostId` or `clientId`.
- **Cryptographic identity** is a persistent asymmetric key pair. Possession of its private key proves control of the corresponding public identity.
- **KnownHost** is a client-side record of a relationship previously established with a Host identity. It is not live trust state.
- **KnownDevice** is a Host-side record associating a Client identity with a public key and Host-authoritative trust state.
- **Candidate** is an endpoint suggested by discovery or manual input. A candidate has no trust by itself.
- **Provisional TLS** is encrypted TLS used only for initial pairing before the client has a pinned Host key. It does not authenticate the Host to the client and cannot carry ordinary trusted traffic.
- **Initial-pairing construction** is the cryptographic protocol that first binds a Host and Client identity. S2.1 selected no construction; S2.2 also ended without selecting one. Balanced PAKE was the S1 proposal and is not an implementation authorization or an active mandatory primitive.

## 4. Current implementation and target architecture

| Area | Current implementation | Target architecture |
| --- | --- | --- |
| Host identity | Persistent `hostId`; mutable OS-derived `hostName`; both appear in `hello_ack`. | Retain those semantics and bind `hostId` to a pinned persistent Host key. |
| Client identity | Stable persisted `clientId`. | Retain it and bind it to a persistent Client key. |
| Endpoint | Routing information, separate from Host identity. | Routing information only; never a trust anchor. |
| Client Host persistence | One optional `PersistedClientState.knownHost`. | `knownHosts`, keyed by `hostId`, with an independent pin and endpoint per Host. |
| Trusted reconnect | `hello.auth` sends a persisted bearer `trusted_device_credential`. | TLS Host verification and Client proof-of-possession; no reusable bearer secret. |
| Pairing | Six-digit code eventually issues a reusable credential. | Cryptographic construction unresolved after S2.2 STOP. No SAS-AKE profile was selected; do not treat the six-digit Committed-SAS candidate as a PAKE secret, a trust token, or an implemented profile. |
| Transport | Current loopback WebSocket behavior is defined by `ai/context/protocol/security.md`. | WSS/TLS 1.3, pinned Host identity, and mutual cryptographic authentication for trusted clients. |

The current wire schema remains current until the migration changes it. Do not edit `protocol/schema/README.md` or fixtures to describe target messages as implemented during S1.

## 5. Host identity

### Logical identity

`hostId` is the stable, DovahLink-generated UUID for one logical Host installation. It is not a public-key fingerprint, Steam ID, hardware identifier, computer name, endpoint, process ID, `adapterInstanceId`, or `stateAuthorityId`.

It survives Skyrim, Host, and Windows restarts, machine rename, and endpoint/IP/port changes. It may survive an authorized cryptographic key rotation. A deliberate identity reset or reinstall creates a new `hostId`, unless a future supported identity backup/restore operation explicitly restores the old identity.

`hostName` is the mutable OS computer name, used as presentation metadata. It may change without changing `hostId` or the Host key.

### Cryptographic identity

The Host owns a persistent asymmetric key pair. Its private key never leaves the Host. The canonical public identity representation is DER-encoded SubjectPublicKeyInfo (SPKI); its fingerprint is `SHA-256(SPKI DER)` encoded as unpadded base64url. S2.1 separately selected this representation, independently of its initial-pairing STOP. A client's KnownHost can bind this identity to `hostId` only after a future passing pairing profile or authorized key rotation; this selection does not authorize production pinning behavior.

`hostId` and Host public key are deliberately separate. The ID names the logical installation; the key proves cryptographic control of it.

## 6. Client identity

`clientId` is the stable logical identity of one companion installation and persists across reconnects. `clientName` is presentation metadata resolved later in this order: user override, OS device name, then `This device`.

The Client owns a persistent asymmetric key pair, and its private key never leaves the Client. The canonical public identity representation is DER SPKI and its fingerprint is `SHA-256(SPKI DER)` encoded as unpadded base64url, as selected independently of the unresolved pairing protocol. The Host may bind the Client public identity to `clientId` in its KnownDevice record only after proof of possession and authorized pairing; this selection does not implement that binding. `clientId` names the logical installation; proof of the corresponding private key establishes possession of that identity.

## 7. Endpoint semantics and discovery

An endpoint answers only “where may this Host currently be reached?” It is never identity or trust. A Host may change address, port, or transport route without changing `hostId` or its key. Host name is also not identity.

Discovery remains untrusted candidate discovery. Loopback is current; LAN, mDNS, manual address, QR/deep-link, or other discovery may be added only in later approved work. Every source follows:

```text
discovery candidate
    → secure connection
    → cryptographic Host verification or an initial-pairing construction that passes the security gate
    → KnownHost lookup/update
```

Discovery means only that a DovahLink Host may be reachable at an endpoint. It never creates a KnownHost, pins a key, authorizes pairing, or authenticates a Host.

## 8. Client persistence and multiple Hosts

The target client state is conceptually:

```text
PersistedClientState
├── clientId
├── client cryptographic identity/reference
├── knownHosts keyed by hostId
│   ├── hostId
│   ├── lastKnownHostName
│   ├── pinned Host public key/certificate identity
│   └── lastKnownEndpoint
└── optional PendingPairingRecovery { hostId, phase }
```

The same Client identity and key may pair independently with Hosts AAA, BBB, and CCC. Each Host has its own pin, endpoint metadata, and pairing/recovery relationship.

A KnownHost means “this Client previously established a relationship with this Host identity.” It does not mean that Host currently trusts the Client. Do not persist `trusted`, `connected`, `offline`, `blocked`, or `revoked` as authoritative facts. Only the Host supplies current trust status.

### Legacy bearer state

The S1 plan for a transitional singleton bearer binding was superseded by the maintainer's S2.1
legacy rule. Do not add a singleton bearer migration service, dual-authentication mode, bearer
fallback, or compatibility negotiation. Existing bearer/PIN state is unreleased development state;
a later authorized cutover may invalidate it and require re-pairing. The current production baseline
remains unchanged while S2.2 is STOP.

There may be at most one active/persisted pairing recovery operation per Client installation. It must record its owning `hostId`; a pairing operation or recovery for AAA must never resume against BBB. Multiple concurrent pairing operations are not required.

`lastUsedHostId`, if introduced, is product selection preference state owned by the app, not SDK trust or authentication persistence. The SDK owns KnownHosts and connection/authentication semantics; the app owns which Host it presents as selected.

## 9. Host trust records and authority

The Host remains the only authority for live Client trust. A conceptual KnownDevice contains `clientId`, the Client public key/fingerprint, display name, trust status, and existing administrative metadata.

The Host's trusted, revoked, blocked, and unrecognized (unknown) outcomes remain typed and distinct. A Client key existing locally does not mean that the Host currently trusts it. A revoked Client receives a typed revoked result and may re-pair only as permitted by current Host policy. A blocked Client remains blocked and cannot bypass the block by presenting the same key or starting pairing. Do not collapse these results into diagnostic strings or client-inferred state.

## 10. TLS and initial trust bootstrap

### Trusted connections

The target public transport is WSS over TLS 1.3, implemented with maintained platform TLS libraries. Hosts use a self-generated certificate/key; the design does not require public CA certificates. The independently selected public identity representation is DER SPKI, fingerprinted as `SHA-256(SPKI DER)` in unpadded base64url. A KnownHost may pin that Host identity only after a future passing pairing profile binds it to `hostId`; endpoint and `hostName` do not participate in verification. The representation selection alone does not authorize production pin validation.

The old S1 certificate-based Client-authentication direction is superseded. The selected DovahLink v1 normal reconnect architecture is application-level Client proof-of-possession: establish TLS 1.3, verify the pinned Host identity, receive a fresh cryptographically random authentication challenge generated by the Host, sign a domain-separated transcript with the Client's persistent non-exportable ECDSA P-256 identity key, verify against the KnownDevice public key, consume the challenge, then apply Trusted / Revoked / Blocked / Unknown state. mTLS and TLS Client certificates are not selected for v1. S2.2's initial-pairing STOP does not reopen this architecture decision. S7 still owns the exact transcript bytes, domain/version constant, challenge size and entropy, lifetime, single-use semantics, signature encoding, replay behavior, canonical encoding, deterministic C#↔Dart vectors, and production platform key integration. Until S7 freezes those details, this is an approved architecture direction, not a production-ready protocol profile. A peer-supplied `clientId` or public key alone is never proof of possession.

Disable TLS 1.3 early data (0-RTT) and any resumption path that would omit fresh proof of the Client private key. A TLS ticket or other reusable value must not become a bearer substitute for Client identity. Every reconnect creates a fresh authenticated transport/session.

### Initial pairing

The first Host certificate is not yet pinned. A provisional TLS connection may therefore accept the candidate certificate only while the user explicitly starts initial pairing. This connection is encrypted but the certificate is not treated as an authenticated Host identity. It carries only restricted pairing/bootstrap traffic; it cannot read or publish normal Host state or establish a trusted session. A KnownHost key mismatch must abort before any pairing secret or ordinary application data is sent. It must not silently downgrade to provisional pairing.

The S1 balanced-PAKE bootstrap proposal is unresolved and was not replaced by Committed SAS. The S2.1 investigation found that Pasini–Vaudenay's three-move SAS-AKE construction might remain internally unchanged while later authenticated application data binds DovahLink identity fields, subject to source composition rules. S2.2 did not establish that composition's security argument or select the construction. Shortcake remains unaudited and pre-release, its P-256 suite is not in the release, and the DovahLink transcript, retry policy, and finalization ordering lack byte-level and independent-language evidence. Do not implement the ceremony, persist trust from a provisional TLS connection, or infer an attempt policy from the existing wrong-code counter.

Any future initial-pairing construction's authenticated transcript/key-confirmation context must bind all of the following:

- `hostId` and the Host public key/certificate fingerprint;
- `clientId` and the Client public key/certificate fingerprint;
- DovahLink protocol/domain-separation context;
- the one pairing challenge, session nonce, or equivalent fresh anti-confusion context.

The exact canonical encoding must be specified before implementation. Both endpoints must use their own actual identity/key values in the binding, not trust peer-supplied identity text alone. Authentication and key confirmation must fail if an intermediary substitutes either public key or changes the context. A transparent relay of unmodified messages must not authorize a different Host or Client identity. The candidate application composition described in `crypto-stack-selection.md` has not been frozen or proven interoperable, so S2.2 selects no construction proving these properties together.

No permanent trust may be committed before the selected construction's client proof-of-possession, user authorization, cryptographic confirmation, and durable finalization all succeed for one exact ceremony. The current production flow is unchanged. The finalization contract remains future work and cannot be activated before the security gate passes.

Any future profile must define whether pairing continues on the provisional connection or requires a new one, then bind subsequent normal authentication to the durable Host and Client keys. S2.2 did not decide this lifecycle. Do not infer the old PAKE reconnect ordering as selected.

SPAKE2 (RFC 9382) and CPace remain references from the prior S1 PAKE proposal, not selected bootstrap protocols. ZRTP (RFC 6189) and Bluetooth LE Secure Connections Numeric Comparison remain complete but protocol-specific SAS references. Generic SAS/AKE work, including Pasini–Vaudenay and Shortcake, was also assessed; the construction survives, but the implementation and profile gates do not. See `crypto-stack-selection.md` for the STOP analysis and evidence.

## 11. Trusted reconnect and hello

For Known Host AAA, the Client loads `hostId = AAA`, its pinned Host key, and AAA's last-known endpoint. It connects once to that endpoint. TLS proves the responder holds the pinned Host key before DovahLink application data is sent. A mismatch aborts the connection, discloses no Client secret, leaves KnownHost unchanged, and returns a typed Host-identity mismatch.

The selected normal reconnect architecture is application-level fresh-signature PoP on the same TLS 1.3 connection: verify the pinned Host identity; receive a fresh cryptographically random authentication challenge generated by the Host; have the Client sign a domain-separated transcript with its persistent non-exportable ECDSA P-256 identity key; verify against the KnownDevice public key; consume the challenge; then apply the current Host-owned trust state. mTLS and TLS Client certificates are not selected for v1. The exact transcript, domain/version, challenge size/entropy/lifetime and consumption semantics, optional Client nonce, signature encoding, canonical wire encoding, replay behavior, and deterministic C#↔Dart vectors remain S7 specification work; platform key integration is also future implementation work. S2.2's initial-pairing STOP does not change this architecture decision or select the concrete reconnect protocol. The Host must map proven Client-key possession and `clientId` to current Host-owned trust before granting normal session access.

Only after transport identity/authentication does `hello` negotiate DovahLink protocol/session semantics. Target `hello` may carry `clientId` and non-secret compatibility/bootstrap metadata; it does not carry a reusable credential or PAKE secret. `hello_ack` may carry `hostId`, `hostName`, `hostVersion`, and typed identity/trust interpretation. The current `hello.auth.method` values `trusted_device_credential` and `one_time_local_token` are current implementation details expected to change or disappear as their respective policies migrate. Do not change the canonical current schema during S1.

## 12. Pairing and recovery

Initial pairing must bind `hostId` + Host public identity to `clientId` + Client public identity through a construction that passes a future security gate. S2.2 selected none. The Host remains authoritative for trust; the Client never infers current Host trust from a local KnownHost record.

Pairing recovery is Host-scoped. A pending record names its owning `hostId` and phase. If pairing with AAA is interrupted, connecting to BBB cannot continue, cancel, complete, or overwrite AAA's recovery state. At most one recovery operation is required per Client installation; multiple simultaneous pairings are not a target requirement.

## 13. Revocation, blocking, and reset

The Host checks KnownDevice status on each authenticated reconnect. A locally held Client key cannot override a revoked or blocked Host decision. The SDK exposes typed trust results. Blocking remains distinct from revocation: blocked Clients cannot authenticate or re-pair until Host administration changes that status; revoked Clients follow the existing Host re-pair policy. An unknown Client remains unknown until authorized pairing succeeds.

Trust reset, revoke, block, or forget does not silently change `hostId` or the Host key. A deliberate Host identity reset creates a new logical Host identity and key relationship; reusing the same name or endpoint does not make it the old Host.

## 14. Key storage and ownership

The Host private key is protected by Windows DPAPI or suitable Windows cryptographic key storage and is never written in plaintext when avoidable. Client private keys use platform secure storage: Windows DPAPI/appropriate Windows cryptographic storage, Android Keystore, and iOS Keychain/Secure Enclave where appropriate.

The reusable SDK depends on key-storage and cryptographic-operation ports. It does not contain platform-specific filesystem/security code. The platform layer owns OS key APIs and OS device/computer-name lookup. The Host owns its key persistence implementation. The app owns presentation and user name overrides; it never handles private keys, PAKE internals, bearer secrets, or protocol security rules. Flutter is not a parallel security implementation.

## 15. Key rotation

Keep logical and cryptographic identities separate so rotation is possible:

- An authorized Host rotation requires H1 to cryptographically authorize H2 while binding both keys to `hostId`. KnownHosts update the pinned key while retaining `hostId`; a name or endpoint change is not a rotation.
- An authorized Client rotation requires the old Client key to cryptographically authorize its replacement while binding both keys to `clientId`. The Host updates that KnownDevice's public-key binding without changing its trust authority.
- Rotation authorization cannot clear `blocked` or `revoked` status. A blocked or revoked Client does not regain trust by rotating its key.
- If the old key is lost and cannot authorize its replacement, re-pairing or a separately approved recovery procedure is required. Never accept a new key solely because the logical ID is unchanged.

The exact rotation message and persistence protocol are future implementation decisions; S1 requires only that the architecture permit authorized rotation and fail closed without it.

## 16. Host reinstall and identity reset

If a Host reset changes `hostId` from AAA to BBB, the Client treats BBB as a different Host even when `hostName`, endpoint, machine, and Skyrim installation are unchanged. If only the Host key rotates under authorization from the previous key, `hostId` may remain AAA. If the Host private key is lost while its `hostId` file survives, clients must not trust the Host by `hostId` alone; require re-pairing or an explicitly supported authenticated recovery/restore procedure.

## 17. Migration from bearer credentials

Current state is a singleton KnownHost record, a persisted trusted-device bearer credential, a `hello` containing `trusted_device_credential` and `auth.token`, and pairing that issues then acknowledges that credential. S1 documents a future replacement only; it does not patch `AuthenticationService`, `PairingService`, persistence, Host startup, or wire behavior.

PR #100's experimental sequence — identify a Host on one connection, disconnect, reconnect to the same endpoint, then send that Host's bearer credential — is rejected. Endpoint ownership can change between the two connections, so a different Host can receive the credential after the first connection verified the expected `hostId`. More reconnects or another `AuthenticationService` guard do not close that TOCTOU gap. The replacement verifies the pinned Host key and proves Client-key possession on one cryptographically bound transport. Preserve the useful regression cases from that experiment: a wrong Host does not mutate KnownHost; endpoint/name changes do not change Host identity; and Host mismatch remains typed.

The S1 target proposed multiple KnownHosts, a persistent Client key, Host-key pinning, PAKE pairing, certificate-based Client authentication, and removal of reusable bearer credentials. The certificate-based Client-authentication direction has been superseded by the selected application-level fresh ECDSA P-256 PoP architecture for v1; its exact S7 protocol remains unspecified. S2.2 did not select an initial-pairing profile. No legacy migration or dual-authentication machinery is approved; when a future passing initial-pairing profile reaches its authorized cutover, unreleased development trust may be invalidated and require re-pairing. Current wire fields remain current until their migration PR lands.

DovahLink has no supported public release that requires compatibility with unshipped protocol generations. Follow `ai/context/common.md`'s pre-release compatibility policy: update the baseline cleanly instead of adding legacy protocol negotiation or a compatibility shim. Existing unreleased bearer/PIN development state may be invalidated and require re-pairing; do not add migration machinery to preserve it.

## 18. Ownership boundaries

| Area | Owns |
| --- | --- |
| SDK | `clientId`, Client cryptographic identity abstraction, KnownHosts and pins, per-Host endpoints, auth semantics, typed trust results, reconnect mechanics, pairing recovery, protocol compatibility, Host identity verification. |
| Host | `hostId`, Host cryptographic identity, KnownDevice trust store, trusted/revoked/blocked/unknown truth, pairing authority, and server-side authorization. |
| Platform | Private-key persistence and OS cryptographic APIs, plus OS device/computer-name lookup. |
| App | UI, current screen/modal, selected/preferred Host presentation, user Client-name override, themes, and interaction flow. |

The SDK owns reusable security behavior; Flutter calls the SDK and never implements TLS verification, unselected pairing cryptography, key handling, or trust decisions independently.

## 19. Frozen implementation PR sequence

Each slice is one focused implementation PR and must remain comfortably below the repository's 100-file hard limit. If planning shows a slice approaching 70 changed files, split it before implementation.

Every security migration PR must leave the merged baseline internally coherent. New authentication paths are introduced before activation; activation occurs atomically at cutover; obsolete paths are deleted only after cutover.

| Slice | Scope |
| --- | --- |
| **S1** | Security + identity architecture contract — this document. |
| **S2** | Original cryptographic feasibility gate; stopped because no acceptable balanced-PAKE implementation path was established. Superseded for investigation by S2.1, but its implementation slices remain blocked. |
| **S2.1** | Committed-SAS feasibility and standards review. Historical STOP; superseded by S2.2 assessment. See `crypto-stack-selection.md`. |
| **S2.2** | Pasini–Vaudenay SAS-AKE production-profile feasibility. **STOP — post-SAS application composition is unproven; no production profile selected; S3 remains blocked.** See `crypto-stack-selection.md`. |
| **S3** | Persistent Host cryptographic identity and protected Host private-key storage. |
| **S4** | Persistent Client cryptographic identity abstraction and platform key storage. |
| **S5** | Multi-Host persistence: `knownHosts` keyed by `hostId`, each pinned Host identity, endpoint and name metadata, and Host-scoped pairing recovery. The old singleton bearer format is unreleased development state; do not add compatibility or migration machinery to preserve it. A later approved cutover may require reset and re-pairing. |
| **S6** | WSS/TLS 1.3 transport, normal Host-key verification, and provisional initial-pair plumbing, only after the profile passes. Disable resumption and 0-RTT. |
| **S7** | Specify and implement the selected application-level fresh ECDSA P-256 Client PoP architecture: exact transcript/encoding, challenge and consumption rules, replay behavior, independent C#↔Dart vectors, and Host mapping of proven public identity plus `clientId` to KnownDevice. This does not select mTLS. |
| **S8** | Implement the selected initial-pairing construction and key binding, only after a future complete profile and retry policy pass review. |
| **S9** | After a future initial-pairing profile passes and S7's reconnect protocol is specified, atomically activate initial pairing plus the selected application-level Client PoP architecture with Host pinning and SDK KnownHost/reconnect integration. Unreleased development trust may be intentionally invalidated for re-pairing; do not build bearer compatibility, fallback, or migration machinery. |
| **S10** | Remove any remaining obsolete bearer pairing/authentication behavior as part of the approved cutover. This is not permission to retain dual security modes or defer deletion for unreleased development state. |
| **S11** | Security/adversarial regression audit across Host, SDK, transport, persistence, pairing, recovery, and protocol boundaries. |

### S2/S2.1/S2.2 feasibility gate

The original S2 evaluated the whole cryptographic stack, not only the PAKE. S2.1 first assessed ZRTP and Bluetooth Numeric Comparison, then reassessed generic SAS constructions and Shortcake. S2.2 checked the Pasini–Vaudenay construction against the whole-profile requirements below. The construction survives at paper level, but no production profile passed:

- **Transport:** TLS 1.3 and WSS; Host SPKI pinning; provisional Host key possession; resumption and 0-RTT policy; fresh Client proof.
- **Host and Windows:** C#/.NET TLS and WSS capabilities; certificate/key generation; private-key storage integration; and DPAPI, Windows CNG, or appropriate Windows cryptographic APIs.
- **Dart/Flutter Client:** Dart TLS/WebSocket capabilities, Host pinning, and platform key operations for application-level signatures.
- **Android and iOS:** Android Keystore and non-exportable key support; Keychain/Secure Enclave where appropriate; and compatibility with application-level signatures.
- **Initial pairing:** complete reviewed SAS construction, commitment/reveal ordering and grinding resistance, exact transcript and identities, unbiased SAS, key confirmation, Client PoP, durable authorization, and bounded repeated attempts.
- **Cross-language/native boundary:** C# Host interoperability with Dart/Flutter, vector compatibility, Windows plus future Android/iOS support, and whether one shared native crypto/FFI boundary is safer than separate stacks.
- **Security and supply chain:** no custom cryptography; maintained libraries; vulnerability/update process; licensing; platform coverage; and a version-pinning strategy.

S2.2 did not confirm a safe end-to-end profile. The blockers are the unproven mapping from the chosen KEM to the paper's key-agreement assumptions, the unproven post-SAS DovahLink identity/PoP composition, lack of an accepted lifetime retry bound, Shortcake's explicit unaudited prerelease status and open P-256 change, unverified target packaging, and missing canonical profile bytes and interoperability evidence—not a finding that the Pasini–Vaudenay construction is unsuitable. S3–S11 remain blocked. A renewed feasibility step must address these gaps and establish all required interoperability evidence before any production identity or authentication work begins. Standard primitives or matching vectors alone are insufficient.

### S6 provisional-TLS activation guard

Provisional TLS certificate acceptance is not trust. TLS 1.3 `CertificateVerify` proves possession of the private key for the presented certificate, but an unknown certificate is not yet a trusted Host identity. A future profile may rely on that proof only if the certificate SPKI is the Host identity and the exact SPKI plus `hostId` is bound by the selected initial-pairing construction. S2.2 selected no such construction. Keep provisional acceptance unreachable from production pairing.

After S11, resume UI convergence milestone 3.4, Companion Device Identity. This security sequence is a prerequisite gate, not permission to implement that UI in these PRs.

## 20. Security invariants

1. Endpoint alone, `hostName` alone, and Steam ID alone never identify a Host.
2. A different Host at a known endpoint cannot inherit the old Host relationship.
3. A Client private key never leaves the Client; a Host private key never leaves the Host.
4. Normal reconnect requires no reusable bearer secret.
5. Discovery and provisional TLS encryption alone establish no trust.
6. A KnownHost record does not mean the Host currently trusts the Client.
7. Only the Host determines current trusted, revoked, blocked, and unknown state.
8. Pairing is the user-authorized operation that initially binds the Host and Client cryptographic identities; the required cryptographic construction remains unresolved.
9. Any selected construction must bind the actual Host and Client keys, logical IDs, domain/protocol context, and fresh pairing context; no identity may be substituted.
10. Host verification and Client proof-of-possession occur on one cryptographically bound normal transport/session; never verify one socket and authenticate another.
11. Endpoint or `hostName` changes do not change Host identity.
12. Authorized key rotation may retain `hostId` or `clientId`; lost keys require re-pair/recovery, not blind ID trust.
13. A Host identity reset creates a new logical Host unless explicitly restored through a supported authenticated backup/restore mechanism.
14. Multi-Host data is keyed by stable `hostId`; pairing recovery is scoped to its Host.
15. Blocked, revoked, and unrecognized/unknown remain typed and distinct; Client-local keys do not override Host status.
16. The Flutter app never handles private keys, permanent bearer secrets, unselected pairing internals, or protocol security policy.

## 21. Adversarial scenarios

| Scenario | Required result |
| --- | --- |
| **A.** Endpoint X used to reach AAA; BBB now listens there. | TLS key mismatch aborts before Client authentication or secret disclosure. AAA's KnownHost is unchanged; BBB is not treated as AAA. |
| **B.** AAA changes IP. | Discovery/routing may find the new endpoint; pinned-key verification recognizes AAA independently of address. |
| **C.** AAA changes OS hostname. | Same `hostId` and pinned key; refresh `lastKnownHostName` as display metadata after verification. |
| **D.** A process claims `hostId = AAA` but presents key XXX instead of pinned HHH. | Reject with typed identity mismatch; do not update KnownHost or downgrade to provisional pairing. |
| **E.** AAA revokes Client CCC, which still owns its private key. | TLS proves key possession, then Host returns typed revoked status; local key does not override it. |
| **F.** AAA blocks CCC. | Reject as blocked; presenting the same key or starting pairing cannot bypass the block. |
| **G.** Client key is lost. | Existing Host trust cannot be proven. Re-pair or an explicitly approved future recovery feature is required. |
| **H.** Host key is lost but `hostId` file survives. | Clients reject an unrecognized key; `hostId` alone is not enough. Re-pair or authenticated recovery is required. |
| **I.** AAA rotates H1 to H2 with authorization from H1. | Clients accept the authorized transition, update the pin, and may retain `hostId = AAA`. |
| **J.** Pairing with AAA is interrupted; Client connects to BBB. | BBB cannot resume or mutate AAA's recovery record; recovery remains explicitly scoped to AAA. |
| **K.** AAA, BBB, and CCC independently trust the same Client ID/key. | Each Host's KnownDevice record is independent; each endpoint has its own KnownHost pin and metadata. |
| **L.** Discovery returns a fake candidate endpoint. | No trust is written. Normal pinned-key verification or an initial-pairing construction that passes the security gate must succeed first. |
| **M.** An active intermediary terminates provisional TLS during first pairing. | TLS encryption and certificate possession alone do not authenticate a trusted Host. S2.2 selected no construction to bind the presented SPKI and `hostId`; pairing must remain unavailable. |
| **N.** Candidate SAS messages or a six-digit display are replayed or restarted. | No DovahLink profile or attempt policy was selected. The old wrong-code counter does not bound independent SAS restarts; see `crypto-stack-selection.md`. |

## Reference material

- [RFC 9382: SPAKE2](https://www.rfc-editor.org/rfc/rfc9382.html) and [CPace](https://datatracker.ietf.org/doc/draft-irtf-cfrg-cpace/) are historical references for the S1 balanced-PAKE proposal; neither is selected.
- [RFC 6189: ZRTP](https://www.rfc-editor.org/rfc/rfc6189.html) defines a complete SAS-based media key agreement whose commitment and SAS behavior were assessed in the historical S2.1 review; it is not a selected DovahLink protocol.
- [Bluetooth Core Security Manager Specification](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Core_v6.3/out/en/host/security-manager-specification.html) defines LE Secure Connections Numeric Comparison; it is a security-design reference only, not a DovahLink transport or selected protocol.
- [RFC 8446: TLS 1.3](https://www.rfc-editor.org/rfc/rfc8446.html) defines TLS certificate private-key possession and handshake authentication; TLS does not make an unknown certificate a trusted DovahLink Host identity.
