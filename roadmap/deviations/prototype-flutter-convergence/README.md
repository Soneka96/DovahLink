# Prototype → Flutter Convergence

**Status:** Active — design/material foundations are complete; canonical Connections / Discover
convergence still has documented Session Shell and Known Host repair-state gaps. Broader historical
connection/pairing slices remain paused for re-planning. Local Host discovery, Known Host lifecycle,
and runtime availability foundations are established.

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

Known Host lifecycle integration established SDK-owned observation and its app projection without
resuming historical slices 03.4–03.10. Those remaining connection/pairing steps must still be
reconciled with current DovahLink security architecture before implementation. Ordinary product
work that does not depend on hostile-network first contact may continue from Phase 5.4. Stage 5A
and production LAN pairing remain gated.

The Known Host availability foundation adds SDK-owned runtime reachability and its app projection.
It remains separate from durable Known Host metadata, connection lifecycle, and trust.

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

The temporary inline discovery presentation has been replaced by the canonical Discover Skyrim
modal. The main Connections list now contains only SDK-mapped Known Hosts; ephemeral candidates stay
in the modal. Searching, available, empty, and failed states use the real Redux projection and add
no simulated delay.

At the Redux boundary, middleware maps SDK discovery exceptions to the app-owned
`ConnectionFailureReason`; Redux carries that reason, not the exception or display text. The enum's
`message` getter owns the centralized user-facing failure copy, and widgets render it without SDK
exception switches. Operation-specific converters such as `fromDiscoveryError` are named for the
application operation, not an SDK exception type. Add another converter only when another operation
needs one; do not create one method per SDK exception or a global enum for every possible DovahLink
failure. Localization can replace the centralized copy when the app adopts localization.

## Known Host lifecycle + discovery integration — established

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

## Canonical Discovery / Connections UI Convergence — partial

Connections presents durable Known Hosts separately from ephemeral discovery candidates. An Online
Known Host is selectable and starts SDK authentication. Offline, Connected, Reconnecting, Checking,
and Unknown cards do not start authentication; Unknown is neutral reachability evidence, not Offline
or “Not connected.” Reconnecting is session recovery, not ordinary availability. An Offline card
may open the prototype's informational “Skyrim isn’t running” dialog; that action explains the real
offline state without selecting the Host or starting authentication.

Redux discovery status drives searching, available, empty, and failed feedback. A real candidate
result shows “Local Host found.” followed by AVAILABLE and the candidate card. Selecting it starts
the SDK authentication lifecycle and shows “Checking trusted connection…” while that lifecycle is
connecting. An already trusted outcome closes Discover without reopening Pairing. An unpaired or
failed outcome transitions to the existing pairing section within the same modal route. No fake
delay or Flutter-owned pairing policy is used. Discovery alone does not add a Known Host.

### Session Shell handoff — approved, implementation pending

The maintainer approved a minimal Session Shell in this convergence pass. Navigate there only after
the existing real trusted-session transition: successful Known Host authentication or pairing
confirmation. Online availability, candidate discovery, and opening Pairing are not sufficient.
The shell uses the currently selected Host and contains only prototype-compatible session chrome
and an empty body; it adds no gameplay data or pairing policy.

Back returns to Connections without disconnecting or removing trust. This follows the existing
trusted-flow disposal contract: `PairingDisposedAction(wasTrusted: true)` preserves the admitted
connection. Until the route is implemented, the current Connected presentation remains on
Connections. No SDK or protocol change is part of this handoff.

### Known Host “Pair again” / repair projection — approved, implementation pending

The maintainer approved a per-Host SDK `pairingRequired` hint for the prototype's root-card action.
Set it only from a real Host credential rejection (`revoked` or `unrecognized`) or a typed
administrative invalidation (`revoked`, `trust_reset`, or `factory_reset`) for an admitted Known Host
session. A `blocked` response clears the hint and does not permit Pair again. The hint is a persisted
last-known UI cue, not current trust or authorization; an Offline Host can make it stale. The root
action must re-authenticate through the SDK, and the Host's current response decides whether pairing
can proceed. Offline, Unknown, discovery, endpoint matching, and generic failures never set it.

The existing Host protocol already carries the required typed reasons, so this projection does not
change Host behavior or protocol meaning.

The committed Connections/Discover UI slice needed no SDK/state contract change or discovery
persistence. The separately approved Known Host Pair again hint does require the SDK projection and
storage change described above.

The current prototype artifact is not stored in the repository; exact pixel comparison of the
candidate's nearby-card radius and hover outline remains subject to review against the approved
`DovahLink-Prototype-final` reference.

### Pairing presentation projection gaps

These states remain unrenderable as exact prototype states until the app projection preserves the
available typed information. This convergence pass does not change the SDK contract or invent local
pairing policy.

| Prototype state | SDK truth | Information lost before Redux | Current Redux representation | Smallest future change |
| --- | --- | --- | --- | --- |
| Wrong-code attempts remaining | `DovahLinkPairingException` exposes Host-reported `attemptsRemaining` for counted invalid outcomes. | `PairingRemoteDataSource` converts it to `PairingRetriableFailure(message)` without the count. | `PairingPhase.awaitingCode` plus an error string; no count. | Carry the optional Host count through the app failure, action, and pairing state. No SDK change is needed. |
| Successful redisplay versus cooldown | `PairingRenotifyResult` exposes `renotified` or `cooldown`; both outcomes carry `retryAfterSeconds`. | The data source maps both to the same nullable `int`, and middleware interprets every non-null value as cooldown. | Pending flag and next-available deadline only; a successful redisplay is presented as cooldown. | Preserve the typed renotify status with its retry interval through the app use case and Redux state. No SDK change is needed. |
| Distinct terminal pairing outcomes | `DovahLinkPairingException` exposes the typed `PairingOutcome` and applicable retry metadata. | The data source maps outcomes to user-safe messages, then discards the typed outcome and retry metadata. Some outcomes retain distinct copy. | Terminal states share `PairingPhase.failed` and an error string; the error copy distinguishes several outcomes, but Redux has no typed outcome. | Carry the typed outcome through the app failure, action, and state so presentation can select a truthful state. No SDK change is needed. |
| Durable Known Host repair requirement | Authentication and `session_invalidated` expose typed Host reasons. | The SDK Known Host projection drops those reasons after the current operation. | `ConnectionState.knownHosts` contains Host metadata and availability; the active `PairingState` may contain a temporary rejection reason. | Persist and expose the non-authoritative `pairingRequired` hint in SDK Known Host state, then map it into the root card. No Host or protocol change is needed. |

Two visual behaviors intentionally follow production state: the candidate appears after the real
discovery result instead of alongside the prototype's artificial search presentation, and the
prototype's 900ms/300ms timers are omitted. Empty and failed states extend the prototype's modal
style with safe copy and a Search again action. Historical slice 03.6 remains paused and is not
marked complete by this convergence slice.

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
