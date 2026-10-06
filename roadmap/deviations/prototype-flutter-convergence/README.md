# Prototype → Flutter Convergence

**Status:** Partial — the approved Connections → Discover → Pairing → Session Shell journey is
implemented and its documented SDK/app truth is projected into the UI. Pixel-level visual parity
remains unverified because Flutter screenshots were not available for comparison with the external
approved prototype. The final audit records split-stack local verification separately from historical
results. Broader historical connection/pairing slices remain paused for re-planning.

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
| 03 — Connection/pairing convergence | Historical slices remain paused; the separately approved current-journey UI pass is implemented, with screenshot audit pending | [Step index](03-connection-pairing-convergence/README.md) |
| 03.1–03.3 | Complete | Host identity, local discovery, and Known Host persistence |
| 03.4–03.8, 03.10 | Paused / re-planning required | Historical client identity, lifecycle, discovery/trust UI, pairing state/UI, and handoff |
| 03.9 | Re-planned current Settings/device-identity phase implemented; screenshot audit pending | [Settings / device name](03-connection-pairing-convergence/03.9-settings-device-name.md) |
| 04 — Final canonical audit | Partial — automated checks and state mapping are recorded; pixel comparison remains unverified | [Final audit](04-final-canonical-audit.md) |

## Current position

Steps 03.1–03.3 completed in PRs [#97](https://github.com/Soneka96/DovahLink/pull/97),
[#98](https://github.com/Soneka96/DovahLink/pull/98), and
[#99](https://github.com/Soneka96/DovahLink/pull/99). The attempted follow-up in
[PR #100](https://github.com/Soneka96/DovahLink/pull/100) was closed after exposing a credential
identity-check race. That security detour is recorded separately in the
[Initial Pairing Security Investigation and Extraction](../initial-pairing-security/README.md);
generic SAS research continues in `Soneka96/sas-pairing`.

Known Host lifecycle integration established SDK-owned observation and its app projection without
resuming historical slices 03.4–03.10. Those historical slices remain paused for re-planning against
current DovahLink security architecture; this branch's separately approved current-journey UI pass
is recorded below and does not mark those historical slices complete. Ordinary product work that
does not depend on hostile-network first contact may continue from Phase 5.4. Stage 5A and production
LAN pairing remain gated.

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

## Current approved journey pass — implementation complete, visual audit partial

The current pass implements Connections → Discover → Pairing → real trusted-session handoff → a
minimal Session Shell. It retains the prototype as the layout, copy, interaction, and responsive
authority while using SDK/Redux state for connection and pairing truth.

| Surface | Structure | Interaction | Visual comparison |
| --- | --- | --- | --- |
| Connections | Implemented for durable Known Hosts, checking/online/offline/connected/reconnecting/repair states, and the offline dialog. | Online selects; Offline and transient states do not start authentication; Pair again is gated by the saved SDK hint and Online availability. | Prototype metrics, card treatments, and copy are implemented. Screenshot comparison is unverified. |
| Discover | Implemented for searching, available, empty, failed, candidate checking, and embedded pairing. | Real search/auth outcomes drive transitions; dismissing cleans up the owned lifecycle; trusted candidates enter the Session Shell only after SDK admission. | Prototype modal, candidate, status, and empty-state treatments are implemented. Screenshot comparison is unverified. |
| Pairing | Implemented for code entry, redisplay, cooldown, confirming, success, failure, blocked, and repair states exposed by current typed app state. | Host-reported attempts, expiry, terminal outcomes, redisplay result, and retry timing drive the controls and copy. | Prototype spacing, copy, countdown emphasis, redisplay placement, and responsive metrics are implemented. Screenshot comparison is unverified. |
| Session Shell | Opens on the state-backed Overview, with prototype tabs and placeholders for Map, Quests, Inventory, and Character. | Entry requires the exact Known Host to be connected, including direct route navigation. Back preserves the admitted connection; reconnecting/reauthenticating keeps the shell open, and matching administrative invalidation returns to Connections. | Host status and the current character summary use real projections. The two-row header, active tab rule, page geometry, theme metrics, and placeholder cards follow the prototype. Notifications remains prototype-only. Screenshot comparison remains unverified. |

### Typed projection coverage

| State | SDK truth available | Current app behavior |
| --- | --- | --- |
| Wrong-code attempts | The SDK reports remaining attempts for counted invalid outcomes. | The client preserves and displays the reported count; it does not calculate or hard-code attempts. |
| Redisplay success versus cooldown | Typed `renotified`/`cooldown` outcomes and Host retry timing are available. | Redux preserves the distinction and timing; the button shows the prototype-aligned result and cooldown. |
| Terminal pairing outcomes | Typed expired, invalid, pacing-limited, attempt-limit, pending-not-found, and invalidated outcomes are available. | App-owned messages and outcome-specific expired/attempt-limit actions are rendered from typed state. |
| Known Host repair requirement | The SDK persists and emits the `pairingRequired` hint from real credential rejection or administrative invalidation. | The app maps the hint and shows Pair again only for Online Known Hosts; Offline remains informational. |

No missing SDK-to-Flutter projection was found for these approved, implemented states. This is not a
claim of full pixel parity: the full-app screenshot comparison could not be captured in this
workspace, so visual parity remains unverified and the final canonical audit stays partial.

## Canonical Discovery / Connections UI convergence — implemented, visual comparison unverified

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

The available state pairs “Local Host found.” with the prototype's green status dot. The empty state
says “No other Skyrim PCs found.” without referring to SDK candidate reconciliation or asserting
that a filtered Known Host is unreachable.

### Session Shell handoff — implemented

The maintainer approved a minimal Session Shell in this convergence pass. It opens after a real
trusted-session event and the selected Known Host's SDK session projection reports `connected`.
Online availability, candidate discovery, opening Pairing, and trust without admitted connection are
not sufficient. The router also rejects direct entry unless that exact route Host is connected. The
shell receives the Host ID from the route and resolves the real Host context. The Overview is its
default page; the other four prototype destinations remain placeholders and add no gameplay feature
or pairing policy.

Back returns to Connections without disconnecting or removing trust. This follows the existing
trusted-flow disposal contract: `PairingDisposedAction(wasTrusted: true)` preserves the admitted
connection. The new Back action changes navigation only. The SDK's typed Known Host invalidation
event returns the shell to Connections only when its Host ID matches the current route; reconnecting
and reauthenticating leave it open. The Notifications control remains visible for prototype parity
but has no functionality; its implementation belongs to a future feature phase and is marked TODO
in the screen.

### Session Overview convergence — implemented; screenshot comparison pending

The canonical `dist/index.html` `.session-top`, `.game-nav`, `.game-tab`, `.session-content`,
`.game-page`, `.page-intro`, `.overview-grid`, `.hero-panel`, `.side-stack`, `.panel`, `.quest`,
`.bars`, and `.placeholder-grid` rules define the implemented shell, Overview, and placeholder
composition. `assets/themes.css` defines each theme's tab treatment, materials, and responsive
changes. The Overview consumes the existing public SDK synchronization values through Redux selectors
and its ViewModel; it adds no backend or domain state.

The Session header shows the real Known Host name and status plus the available character name and
level. Current XP appears beside Level as `Level X (Y XP)` only when both values exist; it is not
converted into a progress percentage. The Overview context line uses the character, one most-specific
meaningful place, and Skyrim calendar time, omitting unavailable segments. The character hero uses
the actual identity/race/level and independent supernatural facts. Vitals retain raw current/max
values and use bounded ratios only for visual fills. Quest content follows the complete plural
tracked-quest contract: zero uses “No path is marked.”, one uses its real title/objective where
available, and multiple uses “Multiple paths remain open.” No arbitrary current quest is selected.

Overview status is visual: stale/failed values retain their content with reduced emphasis, recovering
values receive a quiet theme treatment, and unavailable values remain dormant without fake zeroes or
technical status copy. The header uses the same stale/recovering precedence for its retained character
summary. The Map, Quests, Inventory, and Character tabs use the prototype's placeholder
copy and card structure only; they do not claim implemented gameplay features. XP remains header
content rather than an Overview progress widget, as required by the production SDK semantics.

The permanent immersive second-screen copy rule is recorded in
[`ai/context/flutter/architecture.md`](../../../ai/context/flutter/architecture.md). Flutter
structural and interaction tests cover each theme and supported test size; exact screenshot parity
remains pending until rendered images are compared.

### Known Host “Pair again” / repair projection — implemented

The SDK's per-Host `pairingRequired` hint is set only from a real Host credential rejection
(`revoked` or `unrecognized`) or a typed administrative invalidation (`revoked`, `trust_reset`, or
`factory_reset`) for an admitted Known Host session. A `blocked` response clears the hint and does
not permit Pair again. The hint is a persisted last-known UI cue, not current trust or authorization;
an Offline Host can make it stale. The root action re-authenticates through the SDK, and the Host's
current response decides whether pairing can proceed. Offline, Unknown, discovery, endpoint
matching, and generic failures never set it.

The client SDK stores and emits the hint with Known Host state and the app maps that state to the
Online-only Pair again action. No Host or protocol change was needed.

This hint and its SDK/app projection were implemented in the preceding approved connection/lifecycle
work in this branch. The current convergence pass does not infer repair from availability, endpoint
matching, or a generic failure.

The final prototype artifact is outside this repository. Its CSS and markup were used for code-level
comparison; no Flutter screenshots were captured here, so exact pixel parity of the candidate cards,
pairing states, and Session Shell remains unverified.

### Pairing presentation projection — current typed states represented

The SDK and Known Host projections carry the approved journey's pairing states into Redux. Flutter
renders them without inventing policy, attempt counts, cooldowns, expiry, or trust.

| Prototype state | SDK / app truth | Current presentation |
| --- | --- | --- |
| Wrong-code attempts remaining | Host-reported `attemptsRemaining` is typed on counted invalid outcomes. | Rendered in code-entry copy; the app does not decrement or hard-code a maximum. |
| Redisplay success versus cooldown | Typed `renotified`/`cooldown` outcomes carry Host retry timing. | Redux preserves the distinction; the quiet redisplay action renders the result and availability countdown. |
| Expired, attempt-limit, blocked, and repair states | Terminal outcomes, credential rejection, and per-Host repair hints are typed in SDK/app state. | Outcome-specific copy/actions, blocked state, and repair flow render without inferring trust. |

No remaining SDK-to-Flutter projection blocker was identified for these approved states. This does
not verify exact visual parity; screenshot comparison remains pending.

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
