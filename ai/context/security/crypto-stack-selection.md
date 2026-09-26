# S2 cryptographic stack feasibility result

**Status: STOP — S2 did not pass.** This document records the feasibility work completed on
2026-09-26. No production cryptographic stack is selected, and S3–S11 must not start. The blocker is
the mandatory balanced PAKE: I found maintained implementations, but none meets the required review
and cross-platform acceptance bar for Windows Host plus Dart/Flutter clients on Windows, Android,
and iOS.

## Gate result

| Requirement | Finding | Result |
| --- | --- | --- |
| Standard/reviewed balanced PAKE | SPAKE2 has an RFC and suitable protocol properties, but the available implementations examined disclose that they have not received an independent security review. CPace remains an Internet-Draft and its current candidate implementation has an interoperability limitation. | **FAIL** |
| Windows C# ↔ Dart/Flutter interoperability | No acceptable maintained implementation with a supported C# and Dart API was found. A shared Rust/native wrapper would be possible in principle, but the candidate Rust PAKE implementations have not passed the review bar and no such DovahLink wrapper was tested. | **UNPROVEN** |
| Android and iOS use | The platform key APIs can perform asymmetric operations, but they do not supply a suitable balanced PAKE. The Dart SDK also has no existing native PAKE bridge. | **UNPROVEN** |
| Canonical vectors, wrong-secret and context-mismatch proof | No PAKE was selected, so the required cross-language proof was not run. | **NOT RUN** |
| Production safety | No production Host, SDK, App, protocol, or trust behavior changed. | **PASS** |

For this hard gate, I interpreted S1's term “reviewed” conservatively as requiring a published
independent implementation security review for the balanced PAKE. S1 does not say “third-party
audit” verbatim; the missing evidence means `pakery-spake2` is unproven against that acceptance bar,
not that the S1 text itself names a specific audit format. The maintainer can clarify this criterion
before a future S2 attempt.

## Other feasibility evidence (not final selections)

| Area | Evidence gathered | Gate status |
| --- | --- | --- |
| Host TLS/WSS | A .NET 9 Kestrel spike negotiated TLS 1.3 and exchanged a WebSocket message with the standard Dart `dart:io` client. | Demonstrated for the spike; production listener integration, resumption, and 0-RTT policy remain unverified. |
| Host key/certificate | The Windows software CNG KSP created a non-exportable ECDSA P-256 key. A self-signed certificate was renewed over the same key; the SPKI remained stable. | Demonstrated for the spike. This did not prove at-rest key ACLs or behavior under the packaged Host identity. |
| Host pin | The Dart spike extracted SPKI from the presented certificate and checked `SHA-256(SPKI DER)` using unpadded base64url. A match connected and a mismatch failed before WebSocket data. | Demonstrated using `asn1lib` 1.6.5 in the isolated POC; its production suitability was not accepted or established. |
| Client key operations | A software CNG P-256 key was closed/reopened, used to sign a fresh challenge, and verified from SPKI. Local checks rejected key export, reused challenge, and a proof against a new challenge. | Demonstrated by a local verifier simulation, not production Host admission. |
| Android/iOS key APIs | Android `AndroidKeyStore`/`KeyGenParameterSpec` supports non-exportable P-256 signing keys; Apple `SecKey` supports persistent Keychain and Secure Enclave P-256 keys. | Documentation evidence only. No Android device was connected, Android SDK licenses were incomplete, and this checkout has no Android Gradle app module or iOS target; Xcode is unavailable on this Windows host. |
| Client authentication | Fresh application challenge/signature over pinned TLS fits non-exportable key APIs. The Dart TLS certificate-key path accepts key bytes, not a native key-operation handle. | Promising direction only. The required full mTLS comparison and final choice were not completed. |
| Provisional TLS | The WSS POC accepts a presented certificate only when a supplied SPKI pin matches, with an empty trust store. | Does not prove a production-only explicit-pairing gate or mismatch-without-downgrade behavior. |
| Cross-language/native boundary | A Rust shared library could in principle be called by C# P/Invoke and Dart FFI. | Not selected or packaged; no cross-language PAKE vector was demonstrated. |
| Canonical encodings and dependency policy | The POCs used SPKI DER, SHA-256, unpadded base64url, and isolated lockfiles. | POC formats only; no final protocol encoding, dependency baseline, or update policy selected. |

These results do not make S2 a pass. The balanced-PAKE gate is mandatory, and the remaining S2
areas were not promoted to concrete production selections after that gate failed.

## Candidate review

| Candidate | Baseline, license, and maintenance | Security/review evidence | Platform and interoperability fit | Decision |
| --- | --- | --- | --- | --- |
| **SPAKE2 (RFC 9382)** | RFC 9382, published September 2023; informational RFC, not Standards Track. | The RFC defines transcript-bound key derivation, associated data, and explicit mutual key confirmation. The RFC alone does not approve a specific implementation. | Balanced PAKE and relevant to six-digit codes, but requires an implementation with matching semantics and vectors. | Protocol candidate only; no library selected. |
| **RustCrypto `spake2`** | Crate `spake2` 0.4.0; Apache-2.0/MIT; maintained within RustCrypto's PAKE repository. | The repository says its PAKE crates have no formal cryptographic/security review and no effort was made to blind operations or erase secrets. The SPAKE2 crate supports only its Ed25519 group; an open issue reports its transcript encoding differs from RFC 9382. | Rust only; a C ABI could be added, but that would not resolve review or RFC-conformance concerns. No C#, Dart, Android, or iOS integration proof. | **Reject.** Review and interoperability risks fail the gate. |
| **`pakery-spake2`** | Version 0.3.1, released 2026-09-09; dual Apache-2.0/MIT; actively developed in the `pakery` Rust workspace. | Advertises RFC 9382 vectors, constant-time comparisons, zeroization, explicit key confirmation, and no unsafe Rust. The upstream workspace says it has not been independently audited. It is a young 0.x API. | Best technical candidate found. It exposes identities/AAD and confirmation outputs. A shared Rust library could serve C# P/Invoke and Dart FFI, but DovahLink has not built or packaged that boundary for Windows, Android ABIs, or iOS. | **Do not select yet.** Independent review and cross-platform FFI proof are missing. |
| **CPace / `pakery-cpace`** | `pakery-cpace` 0.6.0, released 2026-09-22; Apache-2.0/MIT. Current specification is `draft-irtf-cfrg-cpace-21`, still an active I-D and awaiting RFC Editor assignment. | The crate reports draft-vector coverage for its Ristretto255 suite, but the workspace has no independent audit. Its documented P-256 suite is intentionally not the draft suite and has no conformant peer. | Rust only. The Ristretto suite still needs accepted review and C#/Dart/mobile FFI proof; the P-256 suite fails conformant interoperability. | **Reject for now.** Draft maturity, audit, and packaging gaps fail the gate. |
| **BoringSSL SPAKE2 API** | BoringSSL is actively maintained by Google, but consumers vendor a matching revision; its API/ABI is explicitly unstable. | The public API documents the older `draft-irtf-cfrg-spake2-02` variant, not RFC 9382. | C API could be called from C# and Dart FFI, but building and updating it for Windows, Android, and iOS is a separate supply-chain/packaging burden. Its protocol version does not establish RFC 9382 interoperability. | **Reject.** Wrong protocol baseline and unstable shared-library boundary. |
| **Bouncy Castle J-PAKE** | Bouncy Castle maintains C# and Java implementations; the protocol is RFC 8236. | J-PAKE is a balanced PAKE. RFC 8236 recommends explicit key confirmation. | No maintained Dart/iOS implementation was identified. Separate C# and Java implementations do not cover a reusable Dart SDK and iOS without another implementation or a native library. | **Reject for this target.** The all-platform implementation path is absent. |
| **SPAKE2-Java** | Java implementation; LGPL-3.0; no tagged releases were listed when checked. | Its upstream README explicitly calls it unaudited. | Android-focused and BoringSSL-compatible; not a shared C#/Dart/iOS implementation. | **Reject.** Review, release, license, and platform gaps. |
| **Python `spake2`** | MIT-licensed pure Python package; older implementation. | Upstream explicitly says it is not constant-time and warns about timing measurement. | Not a practical embedded C# Host plus Flutter/Dart mobile boundary. | **Reject.** Timing and platform fit fail the gate. |
| **SRP / OPAQUE / SPAKE2+** | Implementations exist in multiple ecosystems. | SRP is not a fit selected solely by familiarity; OPAQUE and SPAKE2+ are augmented PAKEs. | Cross-platform availability cannot override S1's balanced-PAKE requirement. | **Reject under the current S1 contract.** |

The strongest current candidate is `pakery-spake2`, but its missing independent review leaves it
unproven against the acceptance criterion stated above. Treating published RFC test-vector coverage
as equivalent to an implementation security review would weaken that criterion. No custom PAKE
arithmetic or wrapper around an unreviewed implementation was added. POC C was therefore not run.

## Existing pairing attempt bound

The Host currently allows five wrong pairing-code attempts, expires the challenge after five minutes,
and paces confirmations at one per second (`host/DovahLink.Host/Constants.cs`). A six-digit decimal
code has 1,000,000 values, including leading zeroes. Five independent online guesses have probability
`5 / 1,000,000 = 0.0005%` (1 in 200,000) of guessing one issued code. SPAKE2's RFC describes one
online password guess per protocol execution and no offline dictionary check; the existing Host
attempt limit must remain even if a future implementation is selected. This calculation is not a
substitute for a working PAKE proof.

## Required disposition

S1's balanced-PAKE requirement remains unsatisfied. The safe disposition is to keep the sequence
stopped at S2 and leave `identity-and-transport.md`, `ROADMAP.md`, and production behavior unchanged.
The next safe attempt requires either:

1. an independently reviewed, maintained balanced-PAKE implementation with cross-language vectors
   and a demonstrated Windows, Android, and iOS integration path; or
2. a separate maintainer-approved S1 revision that explicitly changes the PAKE requirement and then
   passes a fresh security review.

No acceptable substitute has been evidenced here. Do not start S3 or proceed by using a custom,
unaudited, augmented, or TLS-only pairing construction.

## Evidence

- [RFC 9382 — SPAKE2](https://www.rfc-editor.org/rfc/rfc9382.html)
- [RustCrypto PAKEs security warning](https://github.com/RustCrypto/PAKEs#security)
- [RustCrypto SPAKE2 implementation warning](https://github.com/RustCrypto/PAKEs/tree/master/spake2)
- [RustCrypto issue: transcript encoding and RFC 9382](https://github.com/RustCrypto/PAKEs/issues/186)
- [`pakery-spake2` 0.3.1 documentation](https://docs.rs/crate/pakery-spake2/0.3.1)
- [`pakery-cpace` documentation and suite limitation](https://docs.rs/crate/pakery-cpace/0.6.0)
- [CPace draft 21 status](https://datatracker.ietf.org/doc/draft-irtf-cfrg-cpace/)
- [BoringSSL SPAKE2 API](https://github.com/google/boringssl/blob/main/include/openssl/curve25519.h)
- [BoringSSL API/ABI compatibility policy](https://github.com/google/boringssl/blob/main/PORTING.md)
- [RFC 8236 — J-PAKE](https://www.rfc-editor.org/info/rfc8236/)
- [Bouncy Castle supported PAKE APIs](https://github.com/bcgit/bc-java/blob/main/docs/specifications.html)
- [SPAKE2-Java security/license status](https://github.com/MuntashirAkon/spake2-java)
- [Python SPAKE2 timing warning](https://github.com/warner/python-spake2#security)
