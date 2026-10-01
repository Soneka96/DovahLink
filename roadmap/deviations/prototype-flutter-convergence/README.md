# Prototype → Flutter Convergence

**Status:** Active — design and visual foundation complete; connection/pairing convergence partial;
remaining historical steps require re-planning. Local Host discovery and Known Host lifecycle
integration are established; canonical discovery / Connections UI convergence follows them.

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

The current `feature/known-host-lifecycle-discovery-integration` branch establishes SDK-owned Known
Host observation and its app projection without resuming historical slices 03.4–03.10. Those
remaining connection/pairing steps must still be reconciled with current DovahLink security
architecture before implementation. Ordinary product work that does not depend on hostile-network
first contact may continue from Phase 5.4. Stage 5A and production LAN pairing remain gated.

The follow-up `feature/known-host-availability` work adds SDK-owned runtime availability and its
logic-only app projection. It remains separate from the paused Connections UI convergence: widgets
do not display availability in that change. This state reports only the SDK's current reachability
evidence and remains separate from durable Known Host metadata, connection lifecycle, and trust.

## Local Host discovery foundation — established

The local discovery foundation established the production-quality application contract:

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

## Known Host lifecycle + discovery integration — current branch

The SDK owns complete persisted client-state mutations and exposes the complete Host-ID-keyed Known
Hosts collection through `client.hosts.loadKnownHosts()` and `client.hosts.knownHostsChanges`. Each stream
event is a complete immutable snapshot, published only after storage succeeds. The state owner
preserves pairing's atomic Host-scoped credential, recovery-state, and Known Host write. Pairing,
trusted-session metadata refresh, credential removal, and failed pairing recovery continue to use
SDK-owned lifecycle rules.

The app mirrors that state through this boundary:

```text
SDK persisted state -> client.hosts.knownHostsChanges -> ConnectionMiddleware -> HostMapper
  -> ConnectionKnownHostsChangedAction -> ConnectionState.knownHosts -> ViewModel / UI
```

Redux stores app `Host` values only. Its Known Hosts field is the latest complete projection emitted
by the SDK; pairing actions and discovery success do not create or update it. Middleware starts the
subscription with the store and cancels it at app shutdown. The app-wide composition root owns the
single SDK client used by Connection and Pairing. When secure storage is unsupported, Known Hosts
observation still subscribes; the SDK reports an initial storage error and keeps the subscriber
attached, while pairing support remains unavailable.

Known Hosts are durable SDK-owned client relationships, not a claim of current trust. Discovery
candidates are currently reachable, untrusted routing candidates whose claimed `hostId` does not
authenticate them. The selected Host is app-owned presentation and routing state. Current trust is
Host/SDK runtime state and is not persisted as Known Host membership.

Known Host availability is a separate SDK-owned, non-persisted runtime projection with exactly three
states: `unknown`, `online`, and `offline`. Persisted Hosts begin `unknown` at SDK startup. Successful
Known Host authentication or pairing that creates or updates a durable Host during an active session
reports `online`. An ordinary transport loss retains the previous state while bounded reconnect
runs; successful recovery reports `online`. Reconnect exhaustion reports `offline` only when typed
failures establish connection or transport reachability failure. Identity mismatch, compatibility
failure, protocol response or rejection (including retryable protocol errors that exhaust the
budget), and other semantic termination report `unknown`; a responsive endpoint identifying as
another Host also leaves the expected Known Host `unknown`. An actual explicit Known Host
transport-connect failure reports `offline`. Deliberate client disconnect returns the current Known
Host to `unknown`; administrative invalidation preserves its previous availability. Discovery and
candidate authentication do not affect Known Host availability, and availability does not represent
trust or pairing state. TTL and other future liveness policy belongs inside the SDK availability
owner; no such timer or discovery-based signal exists now.

Discovery remains a separate command/result that returns reachable candidates. Discovery claims,
including `hostId`, do not refresh Known Host metadata, establish trust, bypass pairing, or authorize
credential disclosure.

If a later Connections UI correlates a reachable candidate with the saved Host, that is local
product/routing behavior; it is not identity verification. A Known Host remains known while offline,
revoked, blocked, unrecognized, or in need of repair; forgetting it requires explicit remove/forget
behavior. A Known Host record does not represent live trust. The SDK's saved Host after restart is
its initial stream value; Flutter does not load and subscribe separately or reconcile a startup
race.

## After that — Canonical Discovery / Connections UI Convergence

The next UI work uses the SDK-owned Known Host projection alongside discovery candidates/status,
selected Host, and real connection/pairing state. It will reproduce the approved prototype's
structure, copy, interactions, and responsive presentation, add no fake delays, and should need
little or no discovery/SDK architecture change. Historical slice 03.6 remains paused and is not
marked complete by this work.

## Future — production discovery mechanism

The final transport is not designed yet. The app-facing discovery contract and Redux/ViewModel states
are expected to stay stable when a separately designed production mechanism replaces the current
loopback probe beneath the SDK. This split keeps today's known local endpoint replaceable while
allowing the better-understood product interaction to converge independently.

Today's path is candidate → connect → current loopback development pairing → successful association
→ Known Host. If an approved SAS profile preserves the current human interaction, pairing
implementation changes should be able to stay behind the SDK boundary and use the same Flutter app.
If SAS eventually needs materially different human interaction, Flutter pairing presentation may
change while SDK-owned Host, session, and trust lifecycle remains reusable. No SAS profile or UX is
selected or production-ready today. Production LAN discovery, mDNS/DNS-SD, secure first contact,
WSS/TLS migration, and non-loopback pairing remain gated by the security requirements and integration
evidence.

## Related deviation

- [Initial Pairing Security Investigation and Extraction](../initial-pairing-security/README.md) is
  a separate top-level deviation, not a child of this one.

## Resume rule

Resume a paused convergence step only after its product intent has been reconciled with current SDK,
Host, and security contracts. The connection/pairing step index owns that re-planning; this README
does not invent new milestones or change the main roadmap's stage order.
