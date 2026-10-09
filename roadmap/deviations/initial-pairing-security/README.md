# Initial Pairing Security Investigation and Extraction

**Status:** Active as a DovahLink production-security gate; generic SAS research transferred to
[`Soneka96/sas-pairing`](https://github.com/Soneka96/sas-pairing). Experimental, pre-alpha
integration of that project is authorized for the named P10 work only (see
[P10 pre-alpha integration authorization](#p10-pre-alpha-integration-authorization)); the
production-security gate remains closed.

## Starting point

DovahLink's current six-digit code supports its constrained loopback development model. It does not
establish secure first contact over an attacker-controlled LAN, so it could not be extended into a
production network-security claim. Initial-pairing security work was pulled ahead of normal roadmap
progression to resolve that gap.

At the time, the main roadmap had Stage 5 active, Phases 5.1–5.3 complete, and Phase 5.4 next. This
deviation did not renumber or complete roadmap stages.

## Investigation and outcome

DovahLink first formalized its identity and trust architecture, then investigated cryptographic
feasibility through S2, S2.1, and S2.2. S2.2 ended **STOP**: no production initial-pairing profile
was selected. Production secure first contact and security-dependent non-loopback exposure remain
blocked. This is not a claim that the current six-digit flow is secure for hostile-network pairing.
The deviation changed architecture and gating documentation; it did not implement a pairing protocol,
change runtime behavior, or alter the wire schema.

The detailed decisions and evidence remain in the authoritative
[identity and transport security architecture](../../../ai/context/security/identity-and-transport.md)
and [S2.2 feasibility record](../../../ai/context/security/crypto-stack-selection.md). This
deviation does not duplicate their cryptographic analysis.

## Extraction and ownership

The investigation exposed a reusable problem: human-authenticated device pairing with a Short
Authentication String is not specific to Skyrim. Generic SAS construction, cryptographic assumptions,
human comparison rules, proof-required transcript and retry constraints, canonical profiles,
vectors, generic implementation, and security review moved to
[`Soneka96/sas-pairing`](https://github.com/Soneka96/sas-pairing). That project has since built an
experimental, pre-alpha implementation of one candidate profile (a Rust core behind a frozen native
ABI v1, with Dart and .NET wrappers); it is not claimed proven, professionally audited, formally
verified, or production-ready.
Separating the generic proof keeps its protocol/security surface reusable and allows independent
implementation and external review without requiring reviewers to understand Skyrim or DovahLink.

DovahLink remains an intended consumer and owns application identity, authorization, trust decisions,
and product integration. See the [DovahLink security architecture](../../../ai/context/security/identity-and-transport.md)
for the detailed SDK, Host, Platform, and App ownership boundaries.

## Selected authorization architecture and timing

DovahLink has selected the application-level separation: bootstrap or SAS evidence applies to one
pairing attempt and feeds a DovahLink-owned pending authorization decision; the Skyrim user chooses
Pair, Reject, or Block; durable application trust may be established only after Pair. The pending
decision must be tied to the exact attempt, not `clientId` alone. This is selected architecture,
while runtime implementation is deferred. The exact persistence semantics of Block before completed
trust remain undecided.

Implementing the pending Host/Skyrim authorization boundary was not a prerequisite for Phase 5.4's
ordinary product work or Phase 5.5's closure of Stage 5, both now complete; neither depended on
hostile-network first contact. The authorization boundary may be scheduled in the
separate connection/pairing convergence deviation; that record owns its exact milestones and timing.
It must exist before DovahLink relies on it for production first-contact or non-loopback pairing.
Secure production pairing also remains dependent on `sas-pairing` research producing an approved
construction and on DovahLink's security integration gate passing.

## P10 pre-alpha integration authorization

S2.2 remains a valid historical STOP for DovahLink's own candidate analysis. It is not retroactively
converted to a pass, and no new DovahLink feasibility gate has passed.

Since S2.2, `sas-pairing` completed its experimental core, froze its native ABI v1, built Dart and
.NET wrappers, defined the DovahLink consumer boundary, froze the DovahLink Bootstrap mapping, and
audited DovahLink's current authentication (its P1–P9, P10.1, and P10.2). The maintainer has
explicitly authorized experimental, pre-alpha DovahLink integration against that separately
developed profile under `sas-pairing` P10.

This authorization opens only the named work:

- **S3 Host cryptographic identity:** a persistent, non-exportable Windows ECDSA P-256 Host key with
  its DER SubjectPublicKeyInfo and fingerprint, with fail-closed handling of a lost or inaccessible
  provisioned key.
- **Dormant Host `sas-pairing` integration foundation:** the Host's Bootstrap and authority-scope
  encoding, a pinned `sas-pairing` dependency, and an isolated infrastructure adapter over the
  public .NET package, exercised by tests only. The running Host does not compose it, binds no
  `sas-pairing` listener, runs no pairing ceremony, and writes no trust from it.

It does not authorize the Client key (S4), Host pinning (S5), WSS/TLS (S6), Client proof of
possession (S7), the initial-pairing cutover (S8–S10), the security audit (S11), LAN exposure, or
Android work. Those remain incomplete, and each needs its own explicitly approved increment.

Both named items are implemented as dormant foundations; see the
[Host architecture](../../../ai/context/host/architecture.md#dormant-sas-pairing-integration-foundation).
Tests run them against the real pinned `sas-pairing` native library. A real ceremony between the
Host integration and a separate test peer process reaches a Host local result whose peer Bootstrap
equals the expected Client frame, and changing any one field of that frame is rejected. One Host
installation's authority scope has exactly one owner across processes, and the integration's owner
thread leaves ordinary Host work responsive. Nothing in the running Host uses either foundation.

DovahLink's production-security gate remains **closed**. No hostile-LAN or production
secure-pairing claim is made. Stage 5A and production LAN exposure remain gated. `sas-pairing`
itself remains experimental and pre-alpha, not professionally audited or formally verified. A human
SAS match is bounded probabilistic evidence under `sas-pairing`'s stated assumptions, not a
deterministic proof that no attacker mediated the ceremony.

The current six-digit flow and bearer reconnect remain the running product behavior. Activating
`sas-pairing` in the product (the Host listener and driver lifecycle, the interactive Skyrim SAS
prompt, Flutter SAS rendering and comparison, human MATCH/MISMATCH decisions, and handing a result to
DovahLink's pairing authorization) belongs to
[Stage 5A](../../05a-android-wifi-development-path.md#sas-pairing-activation).

## Relationship to the main roadmap

Normal product progression continued independently of this detour: Phase 5.5 — Version-Impact Audit
and Stage 5 Closure is complete, so Stage 5 is closed, and the next normal product-planning task is
the Stage 6 acceptance audit. None of that depends on secure hostile-network first contact. Stage 5A secure Android/Wi-Fi development
and production LAN exposure remain gated. This security deviation remains active until an approved
generic profile and DovahLink's security integration gate pass. It does not change the normal stage
order, mark S3–S11 complete, or create a new roadmap stage. The P10 authorization above pulls the S3
Host identity foundation forward without completing S3's production activation or any later slice.
