# S2.1 Committed-SAS security feasibility result

**Status: STOP — S2.1 did not pass. S3 remains blocked.** This reassessment was completed on
2026-09-27 against current primary sources. No initial-pairing profile is selected. The Pasini–
Vaudenay three-move SAS-AKE construction survives at the paper level. Shortcake follows its message
flow, but its commitment omits an independent random value required by the paper's concrete
random-oracle commitment scheme. No complete DovahLink profile passes: the
Shortcake/KEM substitution and post-AKE identity/PoP composition lack a complete assumption-to-proof
mapping; Shortcake is an unaudited pre-release; its released ready suite is not DovahLink's P-256
direction; Shortcake's current commitment omits the independent random value in the paper's ROM
instantiation; its P-256 suite is still an unreviewed pull request; and no frozen canonical wire
encoding or independent C#↔Dart vectors exist. This STOP is about proof/commitment composition,
implementation assurance, and profile/interoperability evidence, not a finding that the
Pasini–Vaudenay construction is unsuitable.

The previous STOP assessment was incomplete. It considered ZRTP and Bluetooth Numeric Comparison
but missed general SAS-AKE constructions and the possibility of authenticating DovahLink application
identity data only after successful human SAS confirmation. The construction and composition analysis
below corrects that record. It does not authorize implementation or weaken any S2.1 acceptance gate.

## Gate result

| Requirement | Finding | Result |
| --- | --- | --- |
| Reviewed SAS-AKE construction | Pasini–Vaudenay defines an optimal three-move SAS-AKE in the random-oracle model by composing a two-move key agreement with a three-move SAS cross-authentication protocol. | **CONSTRUCTION SURVIVES** |
| Shortcake protocol mapping | Current `main` follows the message flow, SAS expression, transcript key derivation, and human-confirm-before-key-use rule. Its commitment omits the paper's independent random commitment value; one reflection check also differs from the paper's participant-identity check. | **FLOW MATCH; COMMITMENT DEVIATION UNPROVEN** |
| Sequential DovahLink identity binding | Laur–Pasini's composition restrictions require outputs remain unused until all parties accept, fresh per-instance randomness, distinct roles/identities, and unique OOB-to-instance binding. They do not alone prove the proposed HKDF/MAC/signature composition or its unknown-key-share properties. | **UNPROVEN COMPOSITION BLOCKER** |
| Exact SAS profile | The source permits a 30-bit prefix and a one-to-one six-symbol Base32 mapping. Crockford's 32-symbol alphabet is a plausible human-oriented candidate; font/accessibility rendering and exact profile bytes are not frozen. | **NOT SELECTED** |
| Standard key-establishment profile | RFC 9180 DHKEM(P-256, HKDF-SHA256) is a normal, specified KEM instantiation for the construction, not a custom P-256 KEM. Shortcake's implementation of it is only in open PR #35. | **TECHNICALLY PLAUSIBLE; NOT RELEASED/REVIEWED** |
| Production implementation assurance | Shortcake `0.1.0-pre.4` says it has not been audited. Repository CI does not demonstrate Windows, Android, or iOS builds; Rust FFI and packaging have not been proven here. | **FAIL** |
| Independent C#↔Dart interoperability | No DovahLink vectors or independent implementations exist. Two clients calling one Rust library through FFI would prove ABI/wire use, not independent C#↔Dart crypto. | **NOT RUN / NOT SATISFIED** |
| Existing production safety | No production Host, SDK, App, Adapter, protocol, trust, or network behavior changed. | **PASS** |

## Construction review

### Pasini–Vaudenay and Shortcake

Pasini–Vaudenay's PKC 2006 paper defines the three-move SAS-based key-agreement profile in two
layers: a two-move key-agreement protocol is run over the untrusted channel, and the protocol's
messages are authenticated by its three-move SAS cross-authentication construction. The optimized
construction is proven in the random-oracle model. The paper also gives a generic-commitment variant
with one extra move. The three-move proof has a concrete multi-instance bound; it is not an unlimited
`2^-rho` guarantee. See the [paper's protocol and theorem](https://infoscience.epfl.ch/server/api/core/bitstreams/a66b46e4-4da7-4013-aae9-4c63abd06756/content), especially Sections 4–6.

Shortcake's current `main` source implements the following byte-level operations (this describes the
current implementation, not the paper's exact commitment construction or a DovahLink wire profile):

1. Initiator generates a KEM key pair `(dk, ek)` and a fresh 32-byte nonce `R_A`; sends
   `MessageOne = (ek, c)`, where
   `c = SHA3-256("shortcake-commitment-v1" || I2OSP8(|ek|) || ek || I2OSP8(32) || R_A)`.
2. Responder encapsulates to `ek`, obtaining `(ct, kem_ss)`, generates fresh 32-byte `R_B`, and
   sends `MessageTwo = (ct, R_B)`.
3. Initiator decapsulates `ct`, computes the SAS, and sends `MessageThree = R_A`.
4. Responder verifies `c` against `(ek, R_A)` before computing its output. Both derive the same
   session key as
   `SHA3-256("shortcake-session-key-v1" || len8(ek) || ek || len8(ct) || ct || len8(R_B) || R_B || len8(R_A) || R_A || len8(kem_ss) || kem_ss)`.

The raw SAS is
`R_B XOR SHA3-256("shortcake-sas-v1" || len8(R_A) || R_A || len8(ct) || ct)`.
The shared `hash_fields` routine uses eight-byte big-endian length prefixes (`I2OSP(len, 8)`) for
each field. The code uses a random 256-bit initiator nonce as the commitment opening/key and as the
SAS hash key; the responder's independent 256-bit nonce masks the hash output. The KEM secret and
all ordered public transcript values feed the session-key derivation.

The mapping to the paper is:

| Pasini–Vaudenay value | Shortcake value | Classification |
| --- | --- | --- |
| Initiator message `m_A` | KEM encapsulation key `ek` | Underlying key-agreement message |
| Committed key `K` | Initiator nonce `R_A` | Faithful commitment/SAS-key role |
| Responder message `m_B` | KEM ciphertext `ct` | Underlying key-agreement message |
| Responder random `R` | Responder nonce `R_B` | Faithful SAS mask |
| Commitment/opening | Shortcake computes domain-separated SHA3-256 over length-prefixed `ek` and `R_A`; opening reveals `R_A` | **Security-relevant deviation:** the PV random-oracle commitment also samples an independent `ell_e`-bit value `e` and commits to `(e,R_A,tag)`. Shortcake has no independent `e`; its code is not the paper's analyzed instantiation by inspection. A separate hiding/binding/non-malleability reduction or a faithful implementation is required. |
| `R XOR h_K(m_B)` | `R_B XOR SHA3-256(domain, R_A, ct)` | Faithful keyed-hash SAS form; SHA3-256 is modeled as the random oracle |
| Underlying two-move KA output | KEM secret combined with full ordered transcript under a separate hash label | Modern key-agreement/KDF profile choice; the exact KEM must satisfy the underlying KA assumptions |
| Paper's `Alice != Bob` reflection guard | Shortcake rejects byte-equal `ek` and `ct` | **Not equivalent by inspection.** DovahLink must enforce distinct, role-typed Host and Client identities in its outer authenticated transcript; do not treat the library check as that proof. |
| OOB SAS confirmation before key use | `ProtocolOutput` exposes the session key through a separate consuming method and documents that it must not be used before the SAS matches | API warning/state boundary; DovahLink must enforce the ordering in its caller |
| Protocol state/errors | Consuming `start`/`finish` transitions; typed encapsulation, decapsulation, commitment, and reflection errors; zeroization on drop | Faithful implementation mechanics, but message cloning/replay and untrusted serialized state remain caller/wire-profile responsibilities |

The PV random-oracle commitment described in Section 2.3 is `c = H(e, K, m)`: it samples an
independent `ell_e`-bit random value `e`, commits to the SAS key `K` under tag/message `m`, and opens
with `(e,K)`. Shortcake's current `c = H(domain, ek, R_A)` has no independent `e`; `R_A` is the
committed SAS key. A faithful candidate profile would instead commit to `(e,R_A,ek)`, reveal both
`e` and `R_A` in move three, and bind `e` in the session-key transcript. Using SHA3-256 as the random
oracle and adding domain-separated, length-prefixed fields are profile/encoding choices; removing
`e` is a protocol-security change. A separate reduction might justify the current construction under
its high-entropy nonce, but none of the reviewed sources provides that proof.

The source uses a generic KEM abstraction; it is not tied to X-Wing by the protocol proof. The current
ready suite is X-Wing (X25519 + ML-KEM-768) with SHA3-256. RFC 9180's
[DHKEM(P-256, HKDF-SHA256)](https://www.rfc-editor.org/rfc/rfc9180.html) is a standard two-message
KEM candidate with a fresh recipient key and shared secret. It matches the shape of the generic
two-message key-agreement input to Pasini–Vaudenay, so using the standard DHKEM is a ciphersuite
candidate rather than inventing P-256 encapsulation mathematics. The generic protocol interface alone
does not prove that a specific KEM plus Shortcake's transcript hash meets the underlying AKA security
assumptions; that reduction must be checked before the substitution can count as faithful.
Shortcake's [open PR #35](https://github.com/facebook/shortcake/pull/35) proposes that exact suite,
including RFC vectors; it is not in `main` or the released crate and has no recorded reviewer. Do not
assume it has passed the production bar. Long-term ECDSA P-256 identity keys remain separate from
ephemeral DHKEM keys.

The implementation's fixed hash labels are domain-separated by operation, but do not encode a suite
identifier or DovahLink version. A DovahLink profile would need to pin one suite/version rather than
negotiate a downgradeable choice, frame its exact wire version, and bind the selected suite identifier
in the later authenticated application transcript. Adding DovahLink identities or version fields to
Shortcake's commitment/SAS inputs would be a protocol modification and is not proposed.

### Shortcake maturity and integration

As of this review, [Shortcake's README](https://github.com/facebook/shortcake/blob/main/README.md)
identifies version `0.1.0-pre.4` and says the implementation has not been audited. Its release is
marked prerelease. The repository was created in January 2026, has two named core authors, and the
current main head is [`db73640`](https://github.com/facebook/shortcake/commit/db73640a5531b5266bd1094d72e947ef22295cc1), published in August 2026. Its license is dual MIT/Apache-2.0. The crate depends on `digest`, `subtle`, `zeroize`, and `rand_core`, with optional `serde`, `getrandom`, and X-Wing features; the X-Wing feature depends on pre-release
`x-wing` and SHA3 crates. CI runs on Linux and cross-builds `wasm32-unknown-unknown` and
`thumbv6m-none-eabi`; it does not demonstrate Windows, Android, or iOS support. The current example
uses `postcard`, but neither the Rust types nor serde derive freezes a DovahLink wire protocol.

The August 2026 [PR #37](https://github.com/facebook/shortcake/pull/37) fixed stale SAS/hash
documentation and added coverage for rejecting a deserialized responder state with no shared secret.
The required guard had already been added; this was a test-coverage gap, not evidence of a known
cryptographic break. The May 2026 P-256 suite PR has no recorded review. These facts reinforce the
authors' explicit unaudited warning; Meta ownership does not substitute for review. No independent
security audit or platform assurance evidence was found in the reviewed primary sources.

Rust can in principle be built as a C ABI library for C# P/Invoke and Dart FFI. That alone would not
prove target support, memory ownership, panic containment, zeroization across the ABI, reproducible
packaging, or Windows/Android/iOS buildability. No static/dynamic library packaging, Android ABI,
iOS framework, or host-native ABI POC exists in this repository. Nor would one shared Rust implementation called by
both clients satisfy the original independent C#↔Dart implementation/vector criterion. Shortcake is
therefore a useful protocol reference, not an acceptable production dependency on current evidence.

## Composition and DovahLink mapping

The DovahLink fields should not be added to Shortcake's SAS formula or commitment. The source result
supports an authenticated session key, and the user-aided composition analysis by
[Laur and Pasini](https://secu.famillepasini.ch/files/publications/LaurPasini09-IJSN.pdf) gives the
conditions needed when protocols are used in a larger setting: fresh randomness per instance, no use
of outputs before all parties accept, distinct identities, and an authenticated OOB comparison that
uniquely identifies one protocol instance. Its multi-instance bound also grows with the number of
instances. These conditions support considering the following **candidate** sequence, subject to a
faithful commitment implementation, a separate composition proof, and independent implementation
evidence:

```text
Faithful Pasini–Vaudenay three-move SAS-AKE profile (not current Shortcake commitment code)
  → both sides complete and the user confirms the six-character SAS
  → derive a fresh application-authentication key with HKDF and a DovahLink-specific label
  → MAC one canonical, role-tagged transcript containing domain/version, hostId, Host SPKI DER,
    clientId, Client SPKI DER, ChallengeId, security-fence generation, and ceremony context
  → Client signs a separate pairing-PoP domain plus that transcript with its long-term ECDSA P-256 key
  → Host checks its TLS CertificateVerify key equals the transcript Host SPKI and authorizes only
    the current, unexpired, uncancelled, unsuperseded ceremony
```

This would be sequential application authentication after human-approved AKE output; it does not
modify the internal SAS construction. The output key must not be used before both sides accept the
SAS. The fresh exporter key must be separated from the AKE output. Both role-ordered MAC confirmations
and the Client signature must cover the same canonical transcript, and Host finalization must still
be gated by the exact current `ChallengeId` and security generation. Laur–Pasini's composition
restrictions are necessary usage rules, not proof of this specific key-exporter/MAC/Client-PoP design.
A security argument must reduce transcript substitution and unknown-key-share attempts to the AKE key
security, HKDF key separation, and MAC/signature authenticity assumptions. That argument has not been
supplied. A TLS 1.3 `CertificateVerify` proves
possession of the presented certificate's private key; it does not make an unknown Host trusted. The
transcript must bind the exact certificate SPKI and logical `hostId`. This proposed composition has
not been written as a fully specified interoperable profile or tested; matching KDF/MAC vectors alone
would not prove the source AKE's assumptions.

The source does not need to mention Skyrim UI or `TrustStore`: those remain DovahLink transaction and
authorization rules outside the cryptographic construction. The Host must persist unknown-client
Block by the Client key fingerprint only after verifying Client PoP; claimed `clientId` remains
metadata. Normal reconnect architecture is separately selected: application-level fresh ECDSA P-256
PoP after Host pin verification. Its exact S7 transcript and challenge protocol remain unspecified and
unimplemented; the S2.1 initial-pairing STOP does not reopen that architecture decision.

## Six-character SAS candidate and retry analysis

Shortcake exposes 256 raw SAS bits. The Pasini–Vaudenay construction parameterizes the output length
`rho`; taking the first 30 bits is a prefix extraction, not modulo reduction. A candidate exact
display profile is Crockford Base32's 32-symbol alphabet:

```text
0123456789ABCDEFGHJKMNPQRSTVWXYZ
```

Read the first 30 SAS bits in network bit order as six consecutive 5-bit values and map each value
directly to that alphabet. This is a bijection: no modulo bias, discarded value, or zero special
case; render exactly six uppercase characters and preserve leading `0`s. Crockford omits visually
confusable `I`, `L`, `O`, and `U`. This remains a candidate profile: Skyrim font rendering, screen
reader/accessibility behavior, contrast, and the final canonical display format have not been
validated, so S2.1 does not select it.

For the paper's analyzed commitment with one ideal 30-bit SAS comparison, a single active MITM
succeeds with probability `2^-30` (about 1 in 1,073,741,824). The Pasini–Vaudenay theorem's
multi-instance bound is approximately
`Q(Q-1)/2 × (2^-rho + epsilon_commit + epsilon_hash)` for `Q` protocol instances, with the two
construction-specific error terms from its random-oracle commitment and keyed-hash assumptions. Thus
do not multiply only by the number of completed Pair decisions and call that the whole theorem. Five
sequential ceremonies create ten role instances (`Q=10`), giving a leading bound of
`45 × 2^-30 ≈ 4.19×10^-8`, plus the proof error terms. With five ceremonies per day for 365 days,
`Q=3650` and the theorem's leading bound is about `6.20×10^-3` (0.62%). This quadratic growth shows
why a daily throttle alone is not a lifetime bound. These numeric theorem bounds are conditional on
the analyzed commitment construction; they do not certify Shortcake's current commitment without a
separate proof.

A candidate Host policy is: one active ceremony per Host; persist a Host-global attempt counter;
allow at most five started ceremonies in a rolling 24-hour window; charge mismatch, Pair/Reject,
cancellation, disconnect, expiry, timeout, reconnect, and restart identically; do not reset for a new
`clientId`, Client key, app process, Host process, or ordinary trust mutation. Enforce expiry and
supersession against the same persisted ceremony. This bounds rapid online grinding to at most five
ceremonies per Host per window, but cumulative multi-window risk still grows and must be accepted
explicitly against the source theorem before selection. No production rate limiter is implemented.

## Other required candidates

| Candidate | Security result and DovahLink fit |
| --- | --- |
| Laur–Asokan–Nyberg MA-3 | Their [ePrint 2005/424](https://eprint.iacr.org/2005/424.pdf) has Alice commit to a random key and send `(m_A,c)`, Bob return `(m_B,r_B)`, and Alice open; both compare an `ell`-bit keyed-hash result over the authenticated data. Its proof needs the exact order, commitment non-malleability (or an explicit instantiation proof), and hash-combiner properties; ordinary hiding/binding alone are insufficient. The paper discusses practical commitment/hash instantiations and explains composition with a key-agreement transcript, but MA-3 is not itself Shortcake's three-move SAS-AKE profile and does not justify replacing PV steps piecemeal. |
| Laur–Nyberg MANA IV / MA-DH | The [CANS 2006 paper](https://kodu.ut.ee/~swen/publications/articles/laur-nyberg-2006.pdf) gives MANA IV's commit-key, reveal-key, and two-way OOB hash check; MA-DH replaces the committed random key with an ephemeral DH public value, then derives the DH key after the same authenticated OOB check. Its proof needs hiding, binding and non-malleable commitments, almost-regular/almost-universal hashing (strong universality for key agreement), and DDH for key secrecy. It requires fresh values, no output use before acceptance, and no concurrent instances for the same pair. The authors specifically warn that a casual PV/DH fusion using a plain `H(g^a)` commitment does not inherit their proof. Shortcake instead claims the PV three-move generic-AKA profile, but its KEM and commitment instantiation still require exact premise checking. |
| Pasini–Vaudenay SAS-AKE | The three-move random-oracle construction is the strongest fit found: its proof directly treats a generic two-move key agreement plus SAS cross-authentication. Its generic standard-model alternative costs another move. It supports a candidate application composition after OOB acceptance under explicit composition restrictions; it does not itself specify DovahLink IDs, Client PoP, durable authorization, or a DovahLink wire encoding. |
| Laur–Pasini systematization | [User-aided data authentication](https://secu.famillepasini.ch/files/publications/LaurPasini09-IJSN.pdf) organizes MA/SAS protocols and composition conditions. Its key-use restriction is directly relevant: outputs are not used before all parties accept, and the OOB action must identify one unique instance. Concurrent risk grows with the number of role instances. It is a survey/systematization, not an alternate concrete DovahLink profile. |
| TLS-SAS draft | [draft-miers-tls-sas-00](https://datatracker.ietf.org/doc/draft-miers-tls-sas/) is expired and never became an RFC. It defines a TLS 1.2 extension/new-handshake-message coin flip and derives SAS bits from TLS certificate fingerprints plus the shared coin flip. It is not a TLS 1.3 profile, requires TLS-stack handshake changes unavailable through ordinary .NET/Dart WebSocket APIs, and does not bind DovahLink's application ceremony. It is not suitable for the fixed TLS 1.3/WSS direction. |
| ZRTP / Bluetooth Numeric Comparison | Their complete constructions remain protocol-specific. Their commitments, SAS functions, key confirmation, and endpoint context cannot be transplanted into a different flow. The prior assessment of those two candidates remains valid; it was incomplete only because it omitted generic SAS-AKE work. |

## Security-critical mechanism mapping

| Mechanism | Reviewed source | Candidate DovahLink mapping | Change type and status |
| --- | --- | --- | --- |
| Three-move SAS-AKE, roles, contribution order | Pasini–Vaudenay PKC 2006, Sections 4–6; Shortcake current `initiator.rs`/`responder.rs` | Fix one Host/Client role assignment and one KEM suite; use its three in-band messages and one human comparison | **Faithful message-flow candidate**; current commitment deviates, and the specific KEM premises are unverified |
| Commitment and nonce reveal | Pasini–Vaudenay Fig. 4 and Section 2.3 random-oracle commitment; Shortcake `commitment.rs` | Faithful profile commits to `(e,R_A,ek)`, receives `(ct,R_B)`, then reveals `(e,R_A)`; current Shortcake commits only to `(ek,R_A)` and reveals only `R_A` | **Current code has an unproven security-relevant omission**; adding `e` follows the analyzed scheme but changes Shortcake's current messages |
| Session key and SAS derivation | Pasini–Vaudenay cross-authentication output; Shortcake `sas.rs` | Fixed RFC 9180 DHKEM P-256 candidate, SHA-256 profile; candidate 30-bit SAS prefix | **KEM/hash profile choice**; underlying KA and KDF assumptions still require a reduction |
| Host logical and cryptographic identity | PV AKE identity/session model; TLS 1.3 `CertificateVerify` for key possession | After human confirmation, authenticate `hostId` and exact TLS certificate SPKI DER in a role-tagged application transcript | **Authenticated application data**; source does not itself bind these DovahLink fields |
| Client logical and cryptographic identity | PV AKE identity/session model; no pairing-signature mechanism in Shortcake | After SAS confirmation, MAC `clientId` and Client SPKI DER; verify distinct-domain ECDSA P-256 pairing PoP | **Application-authentication addition**; composition proof not supplied |
| Challenge, security generation, Skyrim Pair decision | DovahLink Host transaction/fence state, not the SAS-AKE paper | Bind exact `ChallengeId` and fence generation in the application transcript; Host accepts only the current ceremony and commits trust after PoP | **Authenticated application context plus local authorization**; not an internal SAS input |
| Human-readable display | Pasini–Vaudenay `rho`-bit OOB output | Prefix 30 SAS bits and map each 5-bit group to Crockford Base32 | **Bijective display encoding candidate**; not selected pending UI/accessibility review |
| Replay and retry bound | PV multi-instance theorem; Laur–Pasini composition restrictions | Unique active ceremony, one active Host transaction, global persisted attempt accounting | **Application retry policy**; proposed rate cap is not a lifetime bound |
| Normal reconnect PoP | Frozen DovahLink architecture decision, separate from initial SAS-AKE | After TLS 1.3 Host pin verification, Host issues a fresh random challenge; Client signs a domain-separated transcript with persistent non-exportable ECDSA P-256; Host verifies the KnownDevice key, consumes the challenge, then applies trust state | **Architecture selected**; exact S7 bytes, challenge rules, replay behavior, and vectors remain unspecified |
| Persistent Block principal | DovahLink trust-authority rule, not SAS-AKE | Block an unknown Client by proven Client-key fingerprint after PoP; keep claimed `clientId` as metadata | **Application trust rule**; not selected or implemented |
| Message serialization | Shortcake Rust message types and `postcard` example | Define versioned canonical binary wire bytes and deterministic cross-language vectors | **Unspecified encoding/profile**; no wire format can be inferred from serde derives |

## DovahLink identity and application requirements

No source construction binds all DovahLink fields inside its SAS calculation. The candidate composition
would bind them after successful SAS acceptance in one length-prefixed, role-tagged transcript with a
versioned DovahLink domain. At minimum it must bind `hostId`, exact Host SPKI DER, `clientId`, exact
Client SPKI DER, `ChallengeId`, security-fence generation, and the selected pairing domain. The
endpoint and display name stay metadata. A canonical transcript MAC binds application identities to
the AKE key; a distinct pairing-PoP signature proves Client private-key possession. These are
authenticated application data and application proof, not changes to Shortcake's commitment or SAS.

The exact transcript encoding, signature input, MAC schedule, confirmation ordering, retry-state
storage, and final transaction are not specified at byte level. Laur–Nyberg's warning that a careless
PV/DH fusion can invalidate an argument is an additional reason to require a specific composition
reduction rather than infer safety from matching protocol shapes. There are no deterministic
C#↔Dart vectors for success, mutation, reflection, replay, stale ceremony, wrong key/ID, or wrong
challenge. A shared Rust FFI implementation would not satisfy the independent-language criterion by
itself. Therefore the application composition remains a candidate, not an approved profile.

## Existing production evidence and boundaries

The previously completed .NET TLS 1.3/WSS, Dart SPKI pinning, Windows CNG key, and Client challenge
signature POCs are historical evidence for those separate primitives only. They do not validate
Shortcake, the selected DHKEM ciphersuite, a SAS profile, an application transcript, or cross-language
interoperability. No new POC or production code was added in this step.

The existing Host challenge, expiry, cancellation, supersession, Adapter acknowledgement, security
fence, trust administration, and IPC generations remain application lifecycle protections. They
must authorize one exact cryptographic ceremony before durable trust. The approved unknown-client
Block principal remains the proven Client-key fingerprint, with claimed IDs and names as metadata.
TLS remains WSS over TLS 1.3, resumption and 0-RTT disabled, and a known Host pin mismatch remains a
hard abort. LAN stays restricted; no legacy bearer/PIN migration, fallback, or compatibility path is
approved. These production behaviors were not changed.

## Disposition

**S2.1 STOP — S3 remains blocked.** The Pasini–Vaudenay construction itself survives, and Shortcake is
a close implementation reference. The exact production profile does not pass because the specific
Shortcake/KEM substitution and post-AKE application binding lack a complete assumption-to-proof
mapping; its only concrete library is pre-release and explicitly unaudited; the released suite is not
the intended P-256 profile; the P-256 implementation has no recorded review and is not released;
target-platform support/FFI is unverified; and DovahLink has no frozen canonical wire/application
transcript or independent C#↔Dart vectors. These are composition, implementation-assurance,
platform, and interoperability blockers. No production profile or SAS alphabet is selected. Step 2
is not authorized by this STOP; S3 and later work remain blocked. Reopen only on direct maintainer
approval of a renewed feasibility step that addresses these classified blockers without weakening
the acceptance criteria. This initial-pairing STOP does not reopen the separately selected normal
reconnect architecture of application-level fresh ECDSA P-256 Client PoP; S7 still owns its concrete
protocol specification and implementation.

## Primary references

- [Pasini and Vaudenay, SAS-Based Authenticated Key Agreement (PKC 2006)](https://infoscience.epfl.ch/server/api/core/bitstreams/a66b46e4-4da7-4013-aae9-4c63abd06756/content).
- [Laur, Asokan, and Nyberg, Efficient Mutual Data Authentication Using Manually Authenticated Strings (ePrint 2005/424)](https://eprint.iacr.org/2005/424.pdf).
- [Laur and Nyberg, Efficient Mutual Data Authentication Using Manually Authenticated Strings (CANS 2006)](https://kodu.ut.ee/~swen/publications/articles/laur-nyberg-2006.pdf).
- [Laur and Pasini, User-aided Data Authentication (2009)](https://secu.famillepasini.ch/files/publications/LaurPasini09-IJSN.pdf).
- Shortcake [README](https://github.com/facebook/shortcake/blob/main/README.md), [Cargo.toml](https://github.com/facebook/shortcake/blob/main/Cargo.toml), [SAS implementation](https://github.com/facebook/shortcake/blob/main/src/sas.rs), [commitment](https://github.com/facebook/shortcake/blob/main/src/commitment.rs), [initiator](https://github.com/facebook/shortcake/blob/main/src/initiator.rs), [responder](https://github.com/facebook/shortcake/blob/main/src/responder.rs), [ciphersuite](https://github.com/facebook/shortcake/blob/main/src/ciphersuite.rs), and [CI workflow](https://github.com/facebook/shortcake/blob/main/.github/workflows/main.yml).
- [RFC 9180 — HPKE](https://www.rfc-editor.org/rfc/rfc9180.html); [Shortcake DHKEM P-256/P-384 PR #35](https://github.com/facebook/shortcake/pull/35); [Shortcake state-deserialization test PR #37](https://github.com/facebook/shortcake/pull/37).
- [TLS-SAS expired draft](https://datatracker.ietf.org/doc/draft-miers-tls-sas/); [RFC 6189 — ZRTP](https://www.rfc-editor.org/rfc/rfc6189.html); [Bluetooth Core Security Manager Specification](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Core_v6.3/out/en/host/security-manager-specification.html).
- [RFC 5480 — ECC SPKI](https://www.rfc-editor.org/rfc/rfc5480.html), [RFC 7469 — SPKI fingerprints](https://www.rfc-editor.org/rfc/rfc7469.html), [RFC 4648 — Base-N encodings](https://www.rfc-editor.org/rfc/rfc4648.html), and [RFC 8446 — TLS 1.3](https://www.rfc-editor.org/rfc/rfc8446.html).
