# S2.1 Committed-SAS security feasibility result

**Status: STOP — S2.1 did not pass. S3 remains blocked.** This assessment was completed on
2026-09-27 against `origin/main` at `77b440e1`. No initial-pairing cryptographic profile is selected.
The reviewed SAS protocols examined are complete protocols with security properties tied to their
own message ordering, transcript, primitives, and transport assumptions. DovahLink's requested
profile would require selecting and composing those elements for DovahLink: commitment roles and
reveal ordering, new identity and ceremony binding, a six-digit derivation, key confirmation, and
application proof-of-possession. The sources do not specify or analyze that composition. Building it from
standard primitives and then showing C# and Dart agree would prove interoperability, not establish
that the new protocol is secure.

This is a STOP on the proposed Committed-SAS profile under the requested review bar. It is not a
claim that no future standard or fully specified reviewed construction can meet DovahLink's needs.

## Gate result

| Requirement | Finding | Result |
| --- | --- | --- |
| Established complete SAS construction | ZRTP and Bluetooth LE Secure Connections Numeric Comparison each define complete, protocol-specific ceremonies. Neither defines the requested DovahLink ceremony and identity model. | **FAIL** |
| Commit/reveal and grinding argument | ZRTP's one-sided HVI commitment and Bluetooth's confirm/random exchange each rely on their own roles and sequencing. The proposed two-sided ECDH-plus-nonce commitment is not a profile of either without changing its analyzed flow. | **FAIL** |
| Exact DovahLink transcript and identity binding | Neither reference defines the joint binding of DovahLink `hostId`, Host SPKI, `clientId`, Client SPKI, `ChallengeId`, security-fence generation, and DovahLink roles/domain. | **FAIL** |
| Six-digit unbiased SAS | ZRTP defines its own 20-bit Base32 or 16-bit word-list rendering; Bluetooth defines `g2` using AES-CMAC and reduction to six decimal digits. Replacing either with the other extraction or a new rejection-sampling rule changes the selected construction. | **FAIL** |
| Canonical public identity encoding | ECC SPKI has a standardized DER representation; SHA-256 SPKI fingerprints and unpadded base64url encoding have authoritative format references and were exercised in the Dart pinning POC. This choice is independent of the pairing protocol. | **PASS — independent subdecision** |
| Application Client PoP, durable Block principal, transaction-bound Skyrim authorization | These are DovahLink application semantics beyond either SAS profile. Their composition with a candidate ceremony has no reviewed construction or complete DovahLink state-machine proof. | **UNPROVEN** |
| Independent C# ↔ Dart ceremony vectors | Existing POCs do not implement the candidate ceremony. No SAS/transcript/ECDH/HKDF/confirmation vectors were produced. | **NOT RUN** |
| Production safety | No production Host, SDK, App, Adapter, protocol, or trust behavior changed. | **PASS** |

## Primary-construction comparison

| Construction | What the complete source specifies | Why it cannot be copied as the DovahLink profile without material changes |
| --- | --- | --- |
| **ZRTP, RFC 6189** | A media-path key agreement with fixed `Hello`, `Commit`, `DHPart1`, `DHPart2`, `Confirm1`, and `Confirm2` sequencing. The initiator commits with `hvi = hash(DHPart2 || responder Hello)` before the responder's DH public value is revealed; the responder checks that commitment after receiving DHPart2. The RFC explains that this constrains a MITM to one SAS guess. It defines P-256 as optional EC25, the full `total_hash` order, an HMAC-based KDF, SAS derivation, and confirmation messages. | The commitment is asymmetric and coupled to ZRTP's `Hello`/`DHPart` messages and role election. Its `Hello` carries a random 96-bit ZID and protocol capabilities, not DovahLink logical IDs and identity SPKIs. The RFC's SAS output is the top 20 bits rendered as Base32 or the top 16 bits rendered as words, not six decimal digits. Replacing its commitment, transcript contents, derivation, or confirmation exchange with the proposed DovahLink variants is a new protocol profile whose security is not established by RFC 6189. |
| **Bluetooth LE Secure Connections Numeric Comparison** | Bluetooth Core Security Manager first exchanges both P-256 public keys. It then exchanges `f4` confirm values: the initiator sends `Ca`, the responder sends `Cb` after receiving it; only after both confirms does the initiator reveal `Na`, followed by responder reveal `Nb`. The confirms commit to the nonces, not to the already-exchanged public keys. `g2` then computes the displayed six-digit value from public-key x-coordinates and nonces. Later `f6` DHKey checks use AES-CMAC and include nonces, role-associated IO capabilities, and Bluetooth addresses. | It is a Bluetooth link-layer ceremony. Its role/address/IO-capability inputs and pairing state machine are not DovahLink fields. Its nonce commitment does not commit the ephemeral public-key contributions proposed for DovahLink. Replacing its inputs/order with WSS application messages, SHA-256/HMAC, DovahLink IDs, SPKIs, challenge generations, and Skyrim authorization is not merely reusing `g2`; it creates a distinct protocol. Also, reducing a uniform 32-bit `g2` output modulo 1,000,000 is not exactly uniform: 967,296 decimal values have 4,295 preimages and 32,704 values have 4,294 preimages. |

### Evidence mapping

| DovahLink mechanism under consideration | Reference mechanism | DovahLink change | Does the reference establish the changed construction's security? |
| --- | --- | --- | --- |
| P-256 ephemeral ECDH | ZRTP EC25 (optional); Bluetooth LE Secure Connections P-256 | Make P-256 mandatory, run it over WSS, and bind DovahLink identities and challenge state | **No.** The primitive is standardized; its composition with new messages and context is not. |
| Commit/reveal to limit adaptive SAS grinding | ZRTP initiator HVI commitment; Bluetooth confirms commit to nonces after public-key exchange | Commit both ephemeral public keys and nonces before revealing either side's values | **No.** ZRTP commits one initiator DHPart2 before responder key reveal; Bluetooth commits nonces after both public keys are already public. Neither analyzes this new symmetric key-plus-nonce order. `SHA-256(ephemeralKey)` alone is not that proof. |
| Transcript-bound SAS | ZRTP hashes its fixed ZRTP transcript; Bluetooth `g2` consumes Bluetooth-specific public-key and nonce inputs | Include DovahLink version/domain, roles, logical IDs, Host/Client SPKIs, ephemeral keys, nonces, `ChallengeId`, and other generation context | **No.** Neither reference specifies these fields or their exact encoding/order. |
| Six-digit SAS | ZRTP 20-bit Base32 / 16-bit word-list; Bluetooth `g2` reduction | Produce exactly `000000`–`999999`, preserve leading zeroes, and meet the requested bias requirement | **No.** A new extraction/rejection rule needs its own profile and analysis. |
| Key confirmation | ZRTP Confirm1/Confirm2 and hash-chain MACs; Bluetooth `f6` DHKey checks | HMAC-SHA-256 with independent directional labels and ceremony-bound fields | **No.** Standard MACs do not prove the surrounding state machine is secure. |
| Client key proof during pairing | ZRTP optionally signs its SAS hash; Bluetooth confirms the DHKey | ECDSA P-256/SHA-256 signature over a pairing-only transcript binding `clientId`, Client SPKI, and exact ceremony | **No.** The pairing context and signature protocol are DovahLink additions. |
| Normal reconnect proof | TLS 1.3 certificate authentication proves possession of a certificate key in the TLS handshake | Application-level fresh Host challenge and Client ECDSA signature, with distinct authentication domain | **No.** TLS CertificateVerify is not this application-authentication transcript. |
| Provisional Host key possession | TLS 1.3 CertificateVerify signs the handshake transcript with the private key for the presented certificate | Treat its SPKI as the long-term Host identity and bind that SPKI and `hostId` into the SAS transcript | **Conditionally sufficient only for key possession.** The TLS proof does not make the presented certificate a trusted DovahLink Host. The required equality and SAS binding have not been implemented or proven. |
| `hostId`/Host-key and `clientId`/Client-key binding | ZRTP ZID and Bluetooth address fields are local to those protocols | Bind both logical IDs and both full cryptographic identities to one ceremony | **No.** Hashing or packing DovahLink values into an unrelated field is not specified by either source. |
| Public identity encoding and fingerprint | RFC 5480 ECC SPKI DER; RFC 7469 SHA-256 SPKI fingerprint; RFC 4648 base64url | Encode Host and Client public identities as DER SPKI; fingerprint as `SHA-256(SPKI DER)` using unpadded base64url | **Yes, independently.** This fixes representation only; it does not bind IDs, authenticate pairing, or establish trust. |
| Human Pair action and persistent Block | Existing Host owns trust state; existing pairing uses exact challenge IDs, display acknowledgements, expiry, cancellation, supersession, and a security-fence generation | Authorize and persist one exact cryptographic ceremony; key-backed unknown-client Block | **No.** Existing application safeguards are useful fit evidence, but do not bind a new ceremony to finalization. |

The references justify their respective constructions only. A deterministic cross-language implementation
would be necessary for a viable profile, but would not substitute for the missing construction-level
security argument.

## Grinding, six-digit probability, and retry conclusion

RFC 6189 explicitly describes why the HVI commitment constrains a MitM to one SAS guess: the
initiator forms HVI over its complete DHPart2 (including its public contribution) and the responder's
Hello, sends that commitment first, receives the responder's DHPart1, then reveals DHPart2. The
responder verifies HVI before deriving keys. In the two-sided MitM case, the attacker must take the
initiator role on one leg and the responder role on the other; the sequence stops it from choosing
both leg contributions after seeing both peers' public values.

Bluetooth LE Secure Connections has a different order. Both ephemeral public keys are exchanged
first. The initiator then sends `Ca`; the responder sends `Cb` only after receiving `Ca`. The
initiator reveals `Na` only after receiving `Cb`, and the responder reveals `Nb` after receiving
`Na`; both verify their confirms and later exchange `f6` DHKey checks. Thus Bluetooth commits both
nonces before nonce reveal, but does not commit the ephemeral public keys. Its attack argument relies
on its fixed SMP state machine, `f4`/`g2`/`f6` inputs, Bluetooth roles, and address/IO-capability
context.

One possible DovahLink order would require both peers to commit to every SAS-influencing ephemeral
public key and nonce before either peer reveals. If commitments are binding and hiding, and every
input affecting the SAS is included, that order would stop a peer from changing its already-committed
contribution after a reveal. It would not, by itself, prove the full two-leg active-MITM bound, bind
all DovahLink identities, or constrain fresh attempts. If a contribution or SAS input remains
uncommitted when the other side reveals, an active peer can choose that value with information it
should not yet have. A ZRTP-shaped one-sided HVI order is another possible direction, but adopting it
requires preserving ZRTP's exact initiator/responder order and commitment inputs while defining
DovahLink's additional identity and application context. Neither branch was selected or reviewed as
a whole. Therefore the required question — whether a MitM can repeatedly choose an ephemeral
contribution after learning enough information to bias the SAS — cannot be answered “no” for the
proposed DovahLink ceremony. The RFC and Bluetooth security arguments cannot be transferred by
assertion.

For an ideal independent six-digit SAS, one active MitM ceremony has match probability `1/1,000,000`.
After `n` independently generated ceremonies, the cumulative probability is
`1 - (1 - 10^-6)^n`: five ceremonies are about `0.0005%` (about 1 in 200,000), and 100 are about
`0.01%` (about 1 in 10,000). With unlimited rapid restarts the probability tends to 1. The existing
five wrong-code attempts do not bound this attack: no six-digit value is submitted to the Host, and
each new ceremony may create a new chance. A policy would have to charge mismatch, Reject, cancellation,
disconnect, expiry, and rapid restart consistently, and resist reconnect/restart reset; changing the
subject ID must not reset a Host-wide resource/attempt bound. No retry policy is selected because no
ceremony and no verified transcript exists to define what counts as one attempt. This is another
unmet PASS condition, not a reason to adopt the old wrong-PIN counter mechanically.

## DovahLink fit and production evidence

The canonical public identity encoding is the independent S2.1 selection: use DER-encoded
SubjectPublicKeyInfo (SPKI) for Host and Client public identities, and represent a pin fingerprint
as `SHA-256(SPKI DER)` in unpadded base64url. RFC 5480 defines ECC SPKI; RFC 7469 specifies SHA-256
over DER SPKI for fingerprints; RFC 4648 defines the URL-safe alphabet and padding behavior. The
existing Dart POC extracted SPKI DER from a TLS certificate and accepted a matching SHA-256
base64url pin while rejecting a mismatch. This does not select a pairing transcript, make an
unknown Host trusted, or authorize production pin validation.

The current Host's `PairingCoordinator` already uses a distinct `ChallengeId`, expiry, cancellation,
supersession, an Adapter display acknowledgement, and the Host `SecurityFenceGeneration`. The
`TrustStore` advances that fence with trust mutations; `TrustAdminService` coordinates durable
Revoke/Block/Unblock/ResetTrust, session invalidation, and pairing cancellation; and the Adapter IPC
session has an attempt/session generation. Current Block is keyed by `ClientId`; `Unblock` returns a
blocked record to Unpaired, not Trusted; `ResetTrust` changes Trusted records to Revoked and advances
the security fence. These are application lifecycle boundaries a later design must preserve, but no
current component accepts a cryptographic ceremony identifier or proves `Pair(C)` through Client
PoP, Skyrim authorization, confirmation, and Host finalization. No production code was changed.

The existing isolated POCs were reused and rerun:

| Existing experiment | Result | What it does not prove |
| --- | --- | --- |
| .NET Kestrel TLS 1.3 / Dart WSS | Passed: WSS exchange used TLS 1.3. | Production listener policy, resumption disablement, 0-RTT disablement, or SAS security. |
| Dart SPKI pin extraction | Passed: matching SPKI accepted; mismatch rejected before application data. | Production pin validation, logical `hostId` binding, or a complete initial pairing protocol. |
| Windows CNG P-256 key and certificate | Passed: software CNG private key export rejected; renewed certificate retained SPKI. | Packaged Host key ACLs, hardware key assurance, or production identity lifecycle. |
| CNG Client challenge/signature POC | Passed: fresh signature verified; reused challenge and old signature for a new challenge rejected. | Cross-language authentication transcript, pairing PoP, persistent challenge consumption, or production admission. |

The Client signature POC signs a POC-only UTF-8 string. It is not a canonical transcript. None of the
existing POCs implements commitment/reveal, ECDH ceremony binding, SAS extraction, HKDF key separation,
directional confirmation, or C#↔Dart vectors. `pubspec.yaml` and POC files remain unchanged.

## Security invariants retained or deferred

The STOP does not weaken these target invariants. They remain constraints for any future passing
profile; this PR makes no production claim that they are newly implemented:

- Endpoint, hostname, display name, `hostId`, and `clientId` are not substitutes for cryptographic
  Host/Client identity. Both logical ID and actual key identity must eventually be bound.
- Discovery is not authentication. A known Host-key mismatch must abort and must never downgrade to
  provisional pairing. A same Host/key at a new endpoint may be verified; endpoint reuse grants no
  inherited trust.
- Long-term Host and Client identity keys must not be reused as ephemeral ECDH keys. Private keys
  remain with their owner. No production keys are added by this PR.
- Host remains the trust authority. Adapter presents Skyrim decisions but does not own trust;
  Flutter presents the Client flow but does not own Host trust. Trusted, Revoked, Blocked, Unknown,
  Unblock, ResetTrust, and Host identity reset stay distinct operations.
- A future Skyrim Pair decision must authorize one exact ceremony only: current ChallengeId, keys,
  transcript, expiry, cancellation/supersession state, and current security-fence generation. A
  stale callback or generic success cannot authorize another ceremony. The existing generation and
  challenge guards are evidence to preserve, not proof for the new protocol.
- Normal reconnect must not use a reusable bearer credential. Multi-Host remains native to the
  Client design. An unknown-client Block principal must be a proven Client-key fingerprint; claimed
  IDs, names, IPs, and endpoints are metadata or routing only.
- TLS target remains TLS 1.3 WSS with resumption and 0-RTT disabled and fresh Client proof on each
  full connection. LAN remains restricted and is not enabled by this PR.
- The development-token path survives only if later adversarial review proves it is explicitly
  local/development scoped and cannot admit a normal remote Client, bypass Client PoP, or operate on
  a future LAN path.

## Required scenarios and disposition

| Scenario group | Required disposition for this assessment |
| --- | --- |
| Normal pairing; active MITM; transparent relay; substituted Host key; substituted Client key; fake `hostId`; fake `clientId`; fake display name | No pairing is authorized under S2.1. A later profile must bind actual IDs and keys; display name stays metadata. Provisional TLS proves possession of the certificate key only, not the claimed Host identity. |
| Commitment mismatch; contribution grinding; nonce replay; ephemeral-key replay; whole-ceremony replay; role reflection; version/domain/field mutation | No DovahLink message order or canonical bytes exist to verify. The ZRTP and Bluetooth defenses apply only to their defined messages and inputs; they cannot be assumed for a hybrid. |
| SAS mismatch; repeated SAS attempts; duplicate Pair; stale Pair; Pair after ResetTrust/fence change; Pair after expiry/supersession; Reject; cancellation; Client/Host/Adapter disconnect | The existing challenge/display/cancellation/fence checks remain production behavior but do not prove a future ceremony-bound finalization. S3 is blocked; no SAS authorization reaches production. |
| Correct Client reconnect proof; wrong key; replayed signature/challenge; expired/reused challenge; same ID with different key; same key with different ID; Host pin mismatch | The CNG POC demonstrates only local C# signing and verification for its test transcript. No normal-auth transcript is selected or proven. Host pin mismatch must remain a hard abort in the future design. |
| Block with fake `clientId` and attacker-owned key; same blocked key with new ID/name; known ID with unexpected key; entirely new key and ID | No key-backed Block behavior exists in production. A later design must use the proven Client-key fingerprint as unknown-subject principal; IDs and names remain metadata. A new key and ID cannot be linked cryptographically to the old installation. |

## Rejected assumptions and remaining direction

- “P-256 + SHA-256 + HKDF + HMAC is a protocol” is rejected. It names primitives, not an analyzed composition.
- `SHA-256(ephemeralKey)` does not by itself establish a commit/reveal order or stop adaptive contribution choice.
- Copying Bluetooth `g2()` does not import Bluetooth Numeric Comparison's complete authentication stage. Bluetooth exchanges public keys before committing to the nonces, and its exact six-digit reduction has a small, specified nonuniformity.
- Copying ZRTP's HVI while changing its message roles, transcript inputs, SAS representation, or confirmation flow does not preserve its one-guess argument automatically.
- A TLS certificate accepted provisionally is not a trusted Host identity. TLS 1.3 `CertificateVerify` proves possession of the presented certificate's private key and binds it to the TLS handshake. It can establish the long-term Host key only if the certificate SPKI is that identity and the exact SPKI plus `hostId` is bound into a separately valid pairing transcript. An additional Host signature is not intrinsically required under those conditions; those conditions are not a selected DovahLink profile.
- Normal authentication must not use mTLS or a reusable bearer credential under the maintainer's current candidate direction. A future application-level Client signature needs a separate, exact reconnect domain and fresh single-use challenge. This direction is not a completed profile.
- TLS resumption and 0-RTT are not acceptable substitutes for fresh Client proof. TLS 1.3 specifies weaker 0-RTT replay guarantees; a later implementation must use full TLS 1.3 WSS handshakes, with resumption and early data disabled as requested.
- LAN remains disabled. No discovery, listener, firewall, or network exposure change is part of S2.1.
- Old development bearer/PIN state is not a compatibility requirement. No migration or dual-authentication machinery was added.
- The loopback development-token path remains a separate audit requirement: it may survive only if adversarial review proves local/development scoping and that it cannot admit or bypass PoP for normal remote Clients or future LAN paths.

## Disposition

**S2.1 STOP — S3 remains blocked.** The candidate cannot be promoted without inventing or materially
improvising the cryptographic protocol composition. No exact byte-level profile, deterministic SAS
vectors, C#↔Dart proof, or final retry policy is selected. Do not start S3 or implement production
security behavior. Reopen this gate only when an established complete construction can meet the full
DovahLink identity, ceremony, authorization, and interoperability requirements without changing its
security argument, or when a new complete reviewed standard/profile becomes available and is directly
approved for investigation.

## Primary references

- [RFC 6189 — ZRTP: Media Path Key Agreement for Unicast Secure RTP](https://www.rfc-editor.org/rfc/rfc6189.html), especially Sections 3.1, 4.4.1.1–4.4.1.4, 4.5.1–4.5.2, 4.6, 5.1.5, 5.1.6, and 7.2.
- [Bluetooth Core Specification 6.3, Part H — Security Manager Specification](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Core_v6.3/out/en/host/security-manager-specification.html), especially the LE Secure Connections `f4`, `f6`, and `g2` functions, public-key exchange, Numeric Comparison, and DHKey checks.
- [RFC 5480 — ECC Subject Public Key Information](https://www.rfc-editor.org/rfc/rfc5480.html), [RFC 7469 — Public Key Pinning Extension for HTTP](https://www.rfc-editor.org/rfc/rfc7469.html), and [RFC 4648 — Base-N Encodings](https://www.rfc-editor.org/rfc/rfc4648.html) support the independent SPKI/fingerprint encoding selection.
- [RFC 8446 — TLS 1.3](https://www.rfc-editor.org/rfc/rfc8446.html), Sections 4.4.3–4.4.4 and 8. It specifies certificate private-key possession in `CertificateVerify`, handshake key confirmation, and the replay limitations of 0-RTT.
