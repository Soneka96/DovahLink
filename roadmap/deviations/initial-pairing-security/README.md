# Initial Pairing Security Investigation and Extraction

**Status:** Active as a DovahLink production-security gate; generic SAS research transferred to
[`Soneka96/sas-pairing`](https://github.com/Soneka96/sas-pairing).

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
[`Soneka96/sas-pairing`](https://github.com/Soneka96/sas-pairing). That project remains in research
and security-design work; no construction is claimed selected, proven, or production-ready.
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

Implementing the pending Host/Skyrim authorization boundary is not a prerequisite for ordinary Phase
5.4 product work. It may be scheduled in the separate connection/pairing convergence deviation; that
record owns its exact milestones and timing. The boundary must exist before DovahLink relies on it for
a production first-contact or non-loopback pairing path. Secure production pairing also remains
dependent on `sas-pairing` research producing an approved construction and on DovahLink's security
integration gate passing.

## Relationship to the main roadmap

Normal product progression resumes at the roadmap's current next phase, 5.4, for work that does not
depend on secure hostile-network first contact. Stage 5A secure Android/Wi-Fi development and
production LAN exposure remain gated. This security deviation remains active until an approved
generic profile and DovahLink's security integration gate pass. It does not change the normal stage
order, mark S3–S11 complete, or create a new roadmap stage.
