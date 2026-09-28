# Prototype → Flutter Convergence

**Status:** Active — design and visual foundation complete; connection/pairing convergence partial;
remaining historical steps require re-planning. The Local Host discovery foundation is a separate
approved scope. Known Host lifecycle integration is next; canonical discovery / Connections UI
convergence follows it.

## Why this deviation exists

The production Flutter app needed to become mechanically faithful to the approved landscape
prototype outside the normal ordered roadmap. Work began with design and visual foundations, then
expanded into connection, Host identity, discovery, Known Host persistence, pairing, and lifecycle
integration. The prototype governs presentation and interaction; SDK/domain/protocol contracts
govern actual application semantics.

## Canonical authority

The convergence history referred to the design as **DovahLink Prototype v17**. The current Flutter
architecture names the final approved visual source `DovahLink-Prototype-final`; use that current
authority for future work. The prototype governs composition, responsive behavior, interactions,
connection cards, pairing and Settings presentation, Host selection, and navigation/handoff. It does
not authorize the UI to invent connectivity, trust, or pairing outcomes. See
[`ai/context/flutter/architecture.md`](../../../ai/context/flutter/architecture.md) and the
[DovahLink security architecture](../../../ai/context/security/identity-and-transport.md).

## Progress

| Deviation step | Status | Detail |
| --- | --- | --- |
| 01 — Design contract | Complete | [Design contract](01-design-contract.md) · [PR #90](https://github.com/Soneka96/DovahLink/pull/90) |
| 02 — Visual/material foundation | Complete | [Visual foundation](02-visual-material-foundation.md) · [PR #93](https://github.com/Soneka96/DovahLink/pull/93) · [PR #94](https://github.com/Soneka96/DovahLink/pull/94) |
| 03 — Connection/pairing convergence | Partial | [Step index](03-connection-pairing-convergence/README.md) |
| 03.1–03.3 | Complete | Host identity, local discovery, and Known Host persistence |
| 03.4–03.10 | Paused / re-planning required | Client identity, lifecycle, discovery/trust UI, pairing state/UI, Settings, and handoff |
| 04 — Final canonical audit | Paused | [Final audit](04-final-canonical-audit.md) |

## Current position

Steps 03.1–03.3 completed in PRs [#97](https://github.com/Soneka96/DovahLink/pull/97),
[#98](https://github.com/Soneka96/DovahLink/pull/98), and
[#99](https://github.com/Soneka96/DovahLink/pull/99). The attempted follow-up in
[PR #100](https://github.com/Soneka96/DovahLink/pull/100) was closed after exposing a credential
identity-check race. That security detour is recorded separately in the
[Initial Pairing Security Investigation and Extraction](../initial-pairing-security/README.md);
generic SAS research continues in `Soneka96/sas-pairing`.

Remaining connection/pairing steps must be reconciled with current DovahLink security architecture
before implementation. Ordinary product work that does not depend on hostile-network first contact
may continue from Phase 5.4. Stage 5A and production LAN pairing remain gated.

## Local Host discovery foundation — current branch

This branch establishes the production-quality local discovery application contract:

```text
SDK local probe -> ConnectionMiddleware -> typed discovery actions/state -> Redux -> ViewModel
```

The middleware calls the SDK discovery contract directly. The SDK checks the current known loopback
endpoint with its real `hello` and protocol validation. Redux exposes `idle`, `discovering`,
`available`, `empty`, and `failed`, plus discovered candidates, the selected Host, and an
app-owned semantic failure reason. No forwarding use case, repository, datasource, or service is
added for the SDK-owned operation.

The temporary app candidate keeps the fixed display name **Local Host** and the discovered endpoint
for routing. Peer-asserted Host ID and name are not candidate identity, trust, authentication, or
persisted Known Host metadata. Endpoint means location only. The selected Host continues into the
existing authentication/pairing flow, which owns the real outcome.

The current screen retains just enough temporary presentation to exercise these states: a Discover
action, visible search/candidate/empty/failure feedback, and selection into the existing flow. This
is foundation-state presentation, not the final canonical discovery UI. It does not claim exact
visual, modal, trust-check, transition, or connection-card parity, and it adds no simulated delay.

At the Redux boundary, middleware maps SDK discovery exceptions to the app-owned
`ConnectionFailureReason`; Redux carries that reason, not the exception or display text. The enum's
`message` getter owns the centralized user-facing failure copy, and widgets render it without SDK
exception switches. Operation-specific converters such as `fromDiscoveryError` are named for the
application operation, not an SDK exception type. Add another converter only when another operation
needs one; do not create one method per SDK exception or a global enum for every possible DovahLink
failure. Localization can replace the centralized copy when the app adopts localization.

## Next PR — Known Host Lifecycle + Discovery Integration

The SDK already persists Known Host metadata, publicly loads it through
`DovahLinkClient.loadKnownHost()`, and records it during successful pairing according to its existing
persistence rules. Discovery uses isolated transient storage and does not persist a Known Host. The
next PR integrates these existing behaviors end to end: load the saved Host at startup, keep Known
Hosts separate from discovery candidates, persist association through the existing SDK flow, and
show the saved Host after restart.

For the current localhost-only route, an already-associated local Host should not reappear as a new
candidate. Treat this as local product/routing correlation, not identity verification: discovery
claims such as `hostId` remain untrusted. The UI must not depend on how this correlation is done.
A Known Host remains known while offline, revoked, blocked, unrecognized, or in need of repair;
forgetting it requires explicit remove/forget behavior. A Known Host record does not represent live
trust. This PR integrates and exercises the existing SDK persistence; it does not invent Known Host
persistence from scratch.

## After that — Canonical Discovery / Connections UI Convergence

The canonical UI convergence follows Known Host lifecycle integration so the final UI can be built
and tested against real saved/discovered Host behavior rather than temporary assumptions. It will
reproduce the approved prototype's structure, copy, interactions, and responsive presentation using
real Known Hosts, discovery candidates/status, selected Host, and existing connection/pairing state.
It owns presentation and handoff, adds no fake delays, and should need little or no discovery/SDK
architecture change. Historical slice 03.6 remains paused and is not marked complete by this work.

## Future — production discovery mechanism

The final transport is not designed yet. The app-facing discovery contract and Redux/ViewModel states
are expected to stay stable when a separately designed production mechanism replaces the current
loopback probe beneath the SDK. This split keeps today's known local endpoint replaceable while
allowing the better-understood product interaction to converge independently.

Today's path is candidate → connect → current loopback development pairing → successful association
→ Known Host. Later, a reviewed and approved SAS/secure bootstrap may replace that initial pairing
ceremony, followed by Pair / Reject / Block and successful association. Replacing the initial pairing
ceremony later with SAS should not require rebuilding discovery, Known Host lifecycle, or the
Connections UI; it also leaves saved Host presentation, reconnect, and offline/repair presentation in
place. No SAS profile is selected or production-ready today. Production LAN discovery, mDNS/DNS-SD,
secure first contact, WSS/TLS migration, and non-loopback pairing remain gated by the security
requirements and integration evidence.

## Related deviation

- [Initial Pairing Security Investigation and Extraction](../initial-pairing-security/README.md) is
  a separate top-level deviation, not a child of this one.

## Resume rule

Resume a paused convergence step only after its product intent has been reconciled with current SDK,
Host, and security contracts. The connection/pairing step index owns that re-planning; this README
does not invent new milestones or change the main roadmap's stage order.
