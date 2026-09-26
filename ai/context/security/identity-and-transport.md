# Identity and transport security architecture

**Status:** S1 target architecture contract. This document is authoritative for the future security migration. It does not describe behavior already implemented. Until later migration PRs land, current behavior remains defined by `protocol/schema/README.md`, `ai/context/protocol/security.md`, and the current SDK/Host implementation.

The Host/client security migration must follow this contract. The selected cryptographic algorithms and libraries are not implementation details to guess later; S2 below is a required feasibility and selection gate.

## 1. Goals

- Give each DovahLink Host and Client a stable logical installation identity and a separate persistent cryptographic identity.
- Authenticate and encrypt connections with standard TLS 1.3 over WebSocket (`WSS`), using Host-key pinning rather than public certificate authorities.
- Use client proof-of-possession instead of a reusable bearer credential for trusted reconnects.
- Make pairing the user-authorized establishment of a Host-key ↔ Client-key relationship, including a balanced PAKE bootstrap for the first connection.
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
- **Balanced PAKE** is a reviewed password-authenticated key exchange for two parties that both know the same short-lived pairing secret. It is the mandatory initial trust-bootstrap primitive; its algorithm and implementation are selected in S2.

## 4. Current implementation and target architecture

| Area | Current implementation | Target architecture |
| --- | --- | --- |
| Host identity | Persistent `hostId`; mutable OS-derived `hostName`; both appear in `hello_ack`. | Retain those semantics and bind `hostId` to a pinned persistent Host key. |
| Client identity | Stable persisted `clientId`. | Retain it and bind it to a persistent Client key. |
| Endpoint | Routing information, separate from Host identity. | Routing information only; never a trust anchor. |
| Client Host persistence | One optional `PersistedClientState.knownHost`. | `knownHosts`, keyed by `hostId`, with an independent pin and endpoint per Host. |
| Trusted reconnect | `hello.auth` sends a persisted bearer `trusted_device_credential`. | TLS Host verification and Client proof-of-possession; no reusable bearer secret. |
| Pairing | Six-digit code eventually issues a reusable credential. | Six-digit code is a temporary shared PAKE secret that establishes the public-key binding. |
| Transport | Current loopback WebSocket behavior is defined by `ai/context/protocol/security.md`. | WSS/TLS 1.3, pinned Host identity, and mutual cryptographic authentication for trusted clients. |

The current wire schema remains current until the migration changes it. Do not edit `protocol/schema/README.md` or fixtures to describe target messages as implemented during S1.

## 5. Host identity

### Logical identity

`hostId` is the stable, DovahLink-generated UUID for one logical Host installation. It is not a public-key fingerprint, Steam ID, hardware identifier, computer name, endpoint, process ID, `adapterInstanceId`, or `stateAuthorityId`.

It survives Skyrim, Host, and Windows restarts, machine rename, and endpoint/IP/port changes. It may survive an authorized cryptographic key rotation. A deliberate identity reset or reinstall creates a new `hostId`, unless a future supported identity backup/restore operation explicitly restores the old identity.

`hostName` is the mutable OS computer name, used as presentation metadata. It may change without changing `hostId` or the Host key.

### Cryptographic identity

The Host owns a persistent asymmetric key pair. Its private key never leaves the Host. The corresponding public key, presented by the TLS certificate or an equivalent standard representation, is the cryptographic Host identity. A client's KnownHost binds that identity to `hostId` only after successful initial pairing or authorized key rotation.

`hostId` and Host public key are deliberately separate. The ID names the logical installation; the key proves cryptographic control of it.

## 6. Client identity

`clientId` is the stable logical identity of one companion installation and persists across reconnects. `clientName` is presentation metadata resolved later in this order: user override, OS device name, then `This device`.

The Client owns a persistent asymmetric key pair, and its private key never leaves the Client. The Host binds the Client public identity to `clientId` in its KnownDevice record. `clientId` names the logical installation; proof of the corresponding private key establishes possession of that identity.

## 7. Endpoint semantics and discovery

An endpoint answers only “where may this Host currently be reached?” It is never identity or trust. A Host may change address, port, or transport route without changing `hostId` or its key. Host name is also not identity.

Discovery remains untrusted candidate discovery. Loopback is current; LAN, mDNS, manual address, QR/deep-link, or other discovery may be added only in later approved work. Every source follows:

```text
discovery candidate
    → secure connection
    → cryptographic Host verification or explicit initial PAKE pairing
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

### Transitional bearer ownership in S5

S5 may introduce the `knownHosts` collection before bearer credentials are removed, but it does not activate multi-Host trusted authentication. Until S9 cutover, the existing bearer trust relationship remains associated with exactly one Host. Migration from the singleton state is conceptually:

```text
OLD:
  knownHost = AAA
  credential = credential-A

TRANSITIONAL:
  knownHosts:
    AAA
  legacy bearer binding:
    hostId = AAA
    credential = credential-A

FINAL:
  knownHosts:
    AAA
    BBB
    CCC
  bearer credentials: none
```

The transitional binding may use another repository-consistent representation, but it must explicitly associate the old credential with AAA. A multi-Host store must never contain one global bearer credential with an unknown owner. Other KnownHost entries cannot authenticate through that legacy credential. S9 handles existing development trust data by an approved migration or deliberate invalidation and re-pairing policy; S10 removes the transitional binding with the bearer path.

There may be at most one active/persisted pairing recovery operation per Client installation. It must record its owning `hostId`; a pairing operation or recovery for AAA must never resume against BBB. Multiple concurrent pairing operations are not required.

`lastUsedHostId`, if introduced, is product selection preference state owned by the app, not SDK trust or authentication persistence. The SDK owns KnownHosts and connection/authentication semantics; the app owns which Host it presents as selected.

## 9. Host trust records and authority

The Host remains the only authority for live Client trust. A conceptual KnownDevice contains `clientId`, the Client public key/fingerprint, display name, trust status, and existing administrative metadata.

The Host's trusted, revoked, blocked, and unrecognized (unknown) outcomes remain typed and distinct. A Client key existing locally does not mean that the Host currently trusts it. A revoked Client receives a typed revoked result and may re-pair only as permitted by current Host policy. A blocked Client remains blocked and cannot bypass the block by presenting the same key or starting pairing. Do not collapse these results into diagnostic strings or client-inferred state.

## 10. TLS and initial trust bootstrap

### Trusted connections

The target public transport is WSS over TLS 1.3, implemented with maintained platform TLS libraries. Hosts use a self-generated certificate/key; the design does not require public CA certificates. A KnownHost pins the Host public-key identity (for example, an SPKI fingerprint) associated with its `hostId`; endpoint and `hostName` do not participate in verification.

Trusted reconnect uses certificate-based client authentication as well as Host-key pinning. The Client verifies the pinned Host key during the TLS handshake before sending DovahLink application data. The Host verifies proof of possession of the Client key and maps its public identity plus `clientId` to a KnownDevice. A syntactically valid but unrecognized Client certificate may be admitted only into the restricted initial-pairing path; it is not trusted merely because TLS completed.

Disable TLS 1.3 early data (0-RTT) and any resumption path that would omit fresh proof of the Client private key. A TLS ticket or other reusable value must not become a bearer substitute for Client identity. Every reconnect creates a fresh authenticated transport/session.

### Initial pairing

The first Host certificate is not yet pinned. A provisional TLS connection may therefore accept the candidate certificate only while the user explicitly starts initial pairing. This connection is encrypted but the certificate is not treated as an authenticated Host identity. It carries only restricted pairing/bootstrap traffic; it cannot read or publish normal Host state or establish a trusted session. A KnownHost key mismatch must abort before any pairing secret or ordinary application data is sent. It must not silently downgrade to provisional pairing.

The six-digit code is a short-lived shared secret: the Host generates it and displays it in Skyrim, and the user enters it in the Client. The two endpoints run a reviewed **balanced PAKE** with that code. The code is never sent as a reusable token, is never stored as permanent trust material, and is destroyed with all temporary PAKE material after the ceremony. The current short lifetime, single-use, and attempt-limit policy remains. A failed PAKE or key-confirmation attempt counts against that pairing attempt policy.

The PAKE's authenticated transcript/key-confirmation context must bind all of the following:

- `hostId` and the Host public key/certificate fingerprint;
- `clientId` and the Client public key/certificate fingerprint;
- DovahLink protocol/domain-separation context;
- the one pairing challenge, session nonce, or equivalent fresh anti-confusion context.

The exact canonical encoding is specified before implementation. Both endpoints must use their own actual identity/key values in the binding, not trust peer-supplied identity text alone. Mutual PAKE key confirmation must fail if an intermediary substitutes either public key or changes the context. A transparent relay of unmodified PAKE messages does not authorize a different Host or Client identity.

No permanent trust is committed before successful mutual key confirmation. On success, the Host stores the KnownDevice binding and the Client stores the KnownHost binding. The PAKE protocol response and persistence/recovery ordering are finalized in S8; neither side may report pairing complete before its own durable binding succeeds.

After the pairing binding is committed, close the provisional connection. Establish a **new** normal WSS/TLS 1.3 connection. The Client verifies the newly pinned Host key, and the Host verifies the Client key against the KnownDevice record. Only then does ordinary DovahLink session negotiation continue. PAKE is not used on normal reconnects.

SPAKE2 (RFC 9382) is a relevant published balanced-PAKE reference. CPace is also relevant; its specification and library maturity must be rechecked in S2. Neither is selected by S1. Do not select the augmented SPAKE2+ merely because it is a PAKE, and do not implement PAKE arithmetic in DovahLink.

## 11. Trusted reconnect and hello

For Known Host AAA, the Client loads `hostId = AAA`, its pinned Host key, and AAA's last-known endpoint. It connects once to that endpoint. TLS proves the responder holds the pinned Host key before DovahLink application data is sent. A mismatch aborts the connection, discloses no Client secret, leaves KnownHost unchanged, and returns a typed Host-identity mismatch.

On that same authenticated TLS connection, certificate-based client authentication proves possession of the Client private key. The Host maps `clientId` and the authenticated public key to its current KnownDevice, checks current trust, and returns typed trusted/revoked/blocked/unrecognized semantics. There is no “verify socket A, disconnect, then authenticate socket B” sequence.

Only after transport identity/authentication does `hello` negotiate DovahLink protocol/session semantics. Target `hello` may carry `clientId` and non-secret compatibility/bootstrap metadata; it does not carry a reusable credential or PAKE secret. `hello_ack` may carry `hostId`, `hostName`, `hostVersion`, and typed identity/trust interpretation. The current `hello.auth.method` values `trusted_device_credential` and `one_time_local_token` are current implementation details expected to change or disappear as their respective policies migrate. Do not change the canonical current schema during S1.

## 12. Pairing and recovery

Initial pairing binds `hostId` + Host public identity to `clientId` + Client public identity through the six-digit balanced-PAKE ceremony. The Host records the KnownDevice and remains authoritative for trust; the Client records its KnownHost and never infers current Host trust from that record.

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

The target stores multiple KnownHosts and a persistent Client key, pins each Host's cryptographic identity, binds public keys during PAKE pairing, authenticates normal reconnects with TLS proof-of-possession, and removes reusable bearer credentials from normal authentication. Current wire fields remain current until their migration PR lands.

DovahLink has no supported public release that requires compatibility with unshipped protocol generations. Follow `ai/context/common.md`'s pre-release compatibility policy: update the baseline cleanly instead of adding legacy protocol negotiation or a compatibility shim. Preserve local development data only where a narrow, safe migration is straightforward; do not build a general backward-compatibility layer for unreleased behavior.

## 18. Ownership boundaries

| Area | Owns |
| --- | --- |
| SDK | `clientId`, Client cryptographic identity abstraction, KnownHosts and pins, per-Host endpoints, auth semantics, typed trust results, reconnect mechanics, pairing recovery, protocol compatibility, Host identity verification. |
| Host | `hostId`, Host cryptographic identity, KnownDevice trust store, trusted/revoked/blocked/unknown truth, pairing authority, and server-side authorization. |
| Platform | Private-key persistence and OS cryptographic APIs, plus OS device/computer-name lookup. |
| App | UI, current screen/modal, selected/preferred Host presentation, user Client-name override, themes, and interaction flow. |

The SDK owns reusable security behavior; Flutter calls the SDK and never implements TLS verification, PAKE, key handling, or trust decisions independently.

## 19. Frozen implementation PR sequence

Each slice is one focused implementation PR and must remain comfortably below the repository's 100-file hard limit. If planning shows a slice approaching 70 changed files, split it before implementation.

Every security migration PR must leave the merged baseline internally coherent. New authentication paths are introduced before activation; activation occurs atomically at cutover; obsolete paths are deleted only after cutover.

| Slice | Scope |
| --- | --- |
| **S1** | Security + identity architecture contract — this document. |
| **S2** | Cryptographic stack feasibility and primitive selection. Prove the full target is implementable with acceptable standard/platform APIs and maintained libraries before S3 creates production keys. S2 may stop the sequence if it cannot establish a safe implementation path. |
| **S3** | Persistent Host cryptographic identity and protected Host private-key storage. |
| **S4** | Persistent Client cryptographic identity abstraction and platform key storage. |
| **S5** | Multi-Host persistence: `knownHosts` keyed by `hostId`, each pinned Host identity, endpoint and name metadata, Host-scoped pairing recovery, and migration from singleton `knownHost`. Keep legacy bearer trust bound to exactly one `hostId`; multi-Host bearer authentication is not active. |
| **S6** | WSS/TLS 1.3 transport, normal Host-key verification, and provisional initial-pair plumbing. Provisional unknown-certificate acceptance remains unreachable or feature-gated from production pairing until PAKE exists and S9 activates it. Disable early-data/resumption paths that would bypass fresh proof. |
| **S7** | Client certificate proof-of-possession and Host mapping of public identity plus `clientId` to KnownDevice. |
| **S8** | Implement the balanced-PAKE/public-key pairing path alongside the old production path. Retain short-lived, single-use, attempt-limited code policy and commit only after mutual confirmation. Do not remove or deactivate bearer credential issuance/reconnect behavior from the active product path in this slice. |
| **S9** | Atomic production cutover and full integration: activate PAKE pairing/key binding, pinned Host verification, Client proof-of-possession, and SDK KnownHost/reconnect/discovery integration. Migrate existing development trust data or intentionally invalidate it for re-pairing according to the approved migration policy. After S9, the product operates on the new cryptographic identity model; the old path is no longer active. |
| **S10** | Delete obsolete bearer machinery after cutover: pairing credential issuance/ack semantics, `trusted_device_credential` reconnect, obsolete auth token fields/messages, storage fields, tests/docs/fixtures, and canonical protocol pieces. |
| **S11** | Security/adversarial regression audit across Host, SDK, transport, persistence, pairing, recovery, and protocol boundaries. |

### S2 feasibility gate

S2 evaluates the whole cryptographic stack, not only the PAKE:

- **Transport:** TLS 1.3 and WSS support; self-generated Host certificates; SPKI/public-key pinning; custom certificate verification; client-certificate authentication/mTLS or the selected equivalent; restricted handling of unknown Client certificates during bootstrap; session resumption; disabling 0-RTT; ensuring tickets cannot become bearer substitutes; and fresh proof requirements.
- **Host and Windows:** C#/.NET TLS and WSS capabilities; certificate/key generation; private-key storage integration; and DPAPI, Windows CNG, or appropriate Windows cryptographic APIs.
- **Dart/Flutter Client:** Dart TLS/WebSocket capabilities; custom Host-pin verification; Client certificate/private-key integration; and whether non-exportable platform keys can be used directly.
- **Android and iOS:** Android Keystore and non-exportable key support; Keychain/Secure Enclave where appropriate; and compatibility of those keys with TLS client authentication.
- **Balanced PAKE:** maintained/reviewed implementation; mutual key confirmation; authenticated transcript/application-context binding; deterministic cross-language vectors; offline-dictionary resistance appropriate to the selected PAKE; licensing; maintenance status; and implementation maturity.
- **Cross-language/native boundary:** C# Host interoperability with Dart/Flutter, vector compatibility, Windows plus future Android/iOS support, and whether one shared native crypto/FFI boundary is safer than separate stacks.
- **Security and supply chain:** no custom cryptography; maintained libraries; vulnerability/update process; licensing; platform coverage; and a version-pinning strategy.

S2 must confirm a safe end-to-end implementation path before S3 creates production keys. It may stop S3–S11 if required platform APIs or acceptable maintained libraries cannot satisfy the approved architecture. S2 does not preselect SPAKE2, CPace, or a library.

### S6 provisional-TLS activation guard

Provisional TLS certificate acceptance is implementation plumbing only until the balanced-PAKE bootstrap is available. It must remain unreachable or feature-gated from normal production pairing. TLS encryption without PAKE does not establish first trust. A user must never reach “accept unknown Host certificate → pair/trust” before PAKE binding is implemented; production activation happens only at S9 cutover.

After S11, resume UI convergence milestone 3.4, Companion Device Identity. This security sequence is a prerequisite gate, not permission to implement that UI in these PRs.

## 20. Security invariants

1. Endpoint alone, `hostName` alone, and Steam ID alone never identify a Host.
2. A different Host at a known endpoint cannot inherit the old Host relationship.
3. A Client private key never leaves the Client; a Host private key never leaves the Host.
4. Normal reconnect requires no reusable bearer secret.
5. Discovery and provisional TLS encryption alone establish no trust.
6. A KnownHost record does not mean the Host currently trusts the Client.
7. Only the Host determines current trusted, revoked, blocked, and unknown state.
8. Pairing is the user-authorized operation that initially binds the Host and Client cryptographic identities through balanced PAKE.
9. PAKE binds the actual Host and Client keys, logical IDs, domain/protocol context, and fresh pairing context; no identity may be substituted.
10. Host verification and Client proof-of-possession occur on one cryptographically bound normal transport/session; never verify one socket and authenticate another.
11. Endpoint or `hostName` changes do not change Host identity.
12. Authorized key rotation may retain `hostId` or `clientId`; lost keys require re-pair/recovery, not blind ID trust.
13. A Host identity reset creates a new logical Host unless explicitly restored through a supported authenticated backup/restore mechanism.
14. Multi-Host data is keyed by stable `hostId`; pairing recovery is scoped to its Host.
15. Blocked, revoked, and unrecognized/unknown remain typed and distinct; Client-local keys do not override Host status.
16. The Flutter app never handles private keys, permanent bearer secrets, PAKE internals, or protocol security policy.

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
| **L.** Discovery returns a fake candidate endpoint. | No trust is written. Normal TLS pin verification or explicit initial PAKE pairing must succeed first. |
| **M.** An active intermediary terminates provisional TLS during first pairing. | TLS encryption alone is not accepted as Host authentication. The balanced PAKE must authenticate the identities/context; substitution fails mutual key confirmation. The provisional certificate is never pinned merely because it completed TLS. |
| **N.** PAKE messages or a pairing code are replayed against another attempt/session. | Single-use code, bounded lifetime, attempt limits, and fresh bound pairing context reject reuse; failure consumes the applicable attempt. |

## Reference material

- [RFC 9382: SPAKE2, a Password-Authenticated Key Exchange](https://www.rfc-editor.org/rfc/rfc9382.html) is a published balanced-PAKE reference; S2 still evaluates its fit and maintained implementations.
- [CPace, a balanced composable PAKE](https://datatracker.ietf.org/doc/draft-irtf-cfrg-cpace/) is a relevant alternative. At the time of S1 it is an active Internet-Draft; S2 must re-check publication and implementation maturity.
- [RFC 8446: TLS 1.3](https://www.rfc-editor.org/rfc/rfc8446.html) defines the transport handshake. TLS supplies transport encryption and certificate proof; DovahLink pinning and the initial PAKE ceremony supply the application trust relationship.
