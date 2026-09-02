# Roadmap

The DovahLink roadmap is the delivery specification for the product's ordered foundations and
features. It records intended outcomes, detailed phase behavior, boundaries, dependencies,
acceptance criteria, explicit non-goals, deferred work, and historical decisions. Phase status
records product order; implementation authority remains in AGENTS.md.

The roadmap is intentionally foundation-first: establish identity, protocol, security, and
synchronization semantics before expanding clients and player-facing features. The detailed
acceptance contract lives in the stage file for each phase. ARCHITECTURE.md owns reusable
cross-cutting architecture, protocol/schema/README.md owns the current wire contract, and
ai/context/protocol/security.md owns reusable transport and security constraints.

## Status legend

- **Complete** — delivered and retained as a historical record.
- **Active** — the current stage contains the next planned delivery phase.
- **Planned** — ordered future work.
- **Planned after read-only product validation** — intentionally held until the read-only product is
  validated.
- A phase may have implementation work pulled forward without changing its roadmap status or
  closing the phase.

## Numbering and history

- Completed phase numbers and acceptance meaning remain stable.
- Whole-number stages are the default ordering unit.
- Decimal phases stay with their parent stage when they are a focused, independently reviewable
  delivery slice; they do not normally become separate files.
- New phases require an explicit maintainer-approved scope and must not silently reorder dependencies.

## Planning and delivery rules

- Each phase is a coherent delivery specification and acceptance contract, not a summary checklist.
  Preserve exact user-visible behavior, state transitions, recovery rules, edge cases, security
  invariants, persistence requirements, ordering requirements, explicit non-goals, deferred work,
  and completion criteria in the stage file.
- Keep reusable implementation/design authority in its owning architecture, protocol, security,
  SDK, SKSE, Flutter, or other context document. Link to that authority instead of creating
  conflicting duplicate sources of truth.
- When a substantial capability crosses meaningful architectural boundaries, prefer delivering it
  outward in focused phases/PRs:

  ```text
  Core / Skyrim / Bridge
          ↓
  SDK / Client Integration
          ↓
  Flutter / UI Integration
  ```

  This is a default, not a mechanical rule. Create separate phases only when a layer contains
  meaningful independently reviewable behavior, architecture, or testing; trivial glue should stay
  with the surrounding phase. When a feature crosses meaningful boundaries, finish and validate
  each boundary independently before moving outward.
- For routine work on one phase, load ROADMAP.md, that phase's stage file, the next relevant
  stage file when rolling planning requires it, and only the relevant architecture, security,
  protocol, SDK, or other context documents. Repo-wide planning may load the full roadmap corpus.
- Keep root links and stage headings stable and predictable. Stage files link back to this index.

## Current position

- **Current stage:** Stage 3 — Local Device Pairing and Reconnection is complete; Stage 4 — Live
  State Synchronization Foundation is active.
- **Current phase:** Phase 4.1 — Typed Protocol Contract Redesign and Migration (**Complete**)
  delivered the typed per-message protocol contract (connection, pairing, state, error,
  invalidation, and control families), canonical cross-side fixtures, and the matching Bridge/SDK/
  .NET adapter updates, retiring the old aggregate `character` state area. Phase 4.2 — Bridge Live
  Publication and Bounded Transport is next; it will establish the shared-authority, capacity-one
  session-registry boundary that later multi-client delivery will extend.

The standalone host/adapter migration in `host/PLAN.md` is a parallel replacement track. Its
Stage 4 — Host Client Boundary and Pairing may be planned and implemented without changing the
product roadmap's Phase 4.2 status, but it does not close Phase 4.2, activate the replacement in
production, or authorize removal of `bridge/`. Those outcomes remain gated by `host/PLAN.md`'s
Stage 7 conformance gate and Stage 8 cutover.

## Ordered stages

| Stage | Status | Detailed specification |
| --- | --- | --- |
| 0 | Complete | [Stage 0 — Foundation](roadmap/00-foundation.md) |
| 1 | Complete | [Stage 1 — Skyrim Bridge Foundation](roadmap/01-skyrim-bridge-foundation.md) |
| 2 | Complete | [Stage 2 — Bridge Identity and Authoritative State](roadmap/02-bridge-identity-and-authoritative-state.md) |
| 3 | Complete | [Stage 3 — Local Device Pairing and Reconnection](roadmap/03-local-device-pairing-and-reconnection.md) |
| 4 | Active. Phase 4.1 (typed protocol contract redesign and migration) is complete; Phase 4.2 (Bridge live publication and bounded transport) is next. | [Stage 4 — Live State Synchronization Foundation](roadmap/04-live-state-synchronization-foundation.md) |
| 5 | Planned. The package scaffold, protocol/transport layer, and persistence boundary are partially implemented and pulled forward. | [Stage 5 — Dart Client SDK Foundation](roadmap/05-dart-client-sdk-foundation.md) |
| 5A | Planned. Early Android and secure same-LAN development slice pulled forward from Stages 22–23; does not close those stages. | [Stage 5A — Android and Secure Wi-Fi Development Path](roadmap/05a-android-wifi-development-path.md) |
| 6 | Planned | [Stage 6 — PC / Second-Screen Baseline](roadmap/06-pc-second-screen-baseline.md) |
| 7 | Planned | [Stage 7 — Core UI Theme System](roadmap/07-core-ui-theme-system.md) |
| 8 | Planned | [Stage 8 — Live Player State](roadmap/08-live-player-state.md) |
| 9 | Planned. Extends the capacity-one session-registry boundary established by Stage 4.2. | [Stage 9 — Multi-Client Runtime Foundation](roadmap/09-multi-client-runtime-foundation.md) |
| 10 | Planned | [Stage 10 — Multi-Bridge and Local Discovery Foundation](roadmap/10-multi-bridge-and-local-discovery-foundation.md) |
| 11 | Planned | [Stage 11 — Automatic Connection and Transport Selection](roadmap/11-automatic-connection-and-transport-selection.md) |
| 12 | Planned | [Stage 12 — Mod Awareness](roadmap/12-mod-awareness.md) |
| 13 | Planned | [Stage 13 — Interactive Map Foundation](roadmap/13-interactive-map-foundation.md) |
| 14 | Planned | [Stage 14 — Map Asset and Worldspace System](roadmap/14-map-asset-and-worldspace-system.md) |
| 15 | Planned | [Stage 15 — Quests](roadmap/15-quests.md) |
| 16 | Planned | [Stage 16 — Navigation / Path Guidance](roadmap/16-navigation-path-guidance.md) |
| 17 | Planned | [Stage 17 — Inventory](roadmap/17-inventory.md) |
| 18 | Planned | [Stage 18 — Equipment](roadmap/18-equipment.md) |
| 19 | Planned | [Stage 19 — Magic, Spells, Shouts, and Powers](roadmap/19-magic-spells-shouts-and-powers.md) |
| 20 | Planned | [Stage 20 — Favorites and Hotkeys](roadmap/20-favorites-and-hotkeys.md) |
| 21 | Planned | [Stage 21 — Customizable Dashboard](roadmap/21-customizable-dashboard.md) |
| 22 | Planned | [Stage 22 — Secure LAN Transport and Network Discovery](roadmap/22-secure-lan-transport-and-network-discovery.md) |
| 23 | Planned | [Stage 23 — Mobile / Tablet Client](roadmap/23-mobile-tablet-client.md) |
| 24 | Planned | [Stage 24 — Item Knowledge and Search](roadmap/24-item-knowledge-and-search.md) |
| 25 | Planned | [Stage 25 — Legacy of the Dragonborn Integration](roadmap/25-legacy-of-the-dragonborn-integration.md) |
| 26 | Planned | [Stage 26 — Installed UI Detection](roadmap/26-installed-ui-detection.md) |
| 27 | Planned | [Stage 27 — Optional UI Mod Adapters](roadmap/27-optional-ui-mod-adapters.md) |
| 28 | Planned after read-only product validation | [Stage 28 — Safe Companion Authorization Foundation](roadmap/28-safe-companion-authorization-foundation.md) |
| 29 | Planned | [Stage 29 — Runtime Profiling and Advanced Bridge Hardening](roadmap/29-runtime-profiling-and-advanced-bridge-hardening.md) |
| 30 | Planned | [Stage 30 — CommonLib Dependency Maintenance Audit](roadmap/30-commonlib-dependency-maintenance-audit.md) |

## Major dependencies

- Stages 0–2 establish documentation, the client/protocol foundation, bridge connectivity, and
  identity/state ownership.
- Stage 3 depends on Stage 2 and establishes local pairing, durable trust, and trust-state recovery.
- Stage 4 depends on identity and pairing and now establishes the redesigned typed protocol contract,
  live Bridge delivery, the first production Snapshot and Event state domains, and the internal
  synchronization kernel. Phase 4.2 also establishes the shared-authority, capacity-one
  session-registry boundary: session delivery state is client-specific, while capture and revisions
  are shared. Its protocol migration may use temporary boundary compatibility during delivery, but
  the completed phase supports only the new contract.
- Stage 5 consumes Stage 4's stable contract and synchronization kernel to complete the reusable Dart
  client boundary, public subscription/recovery API, middleware-owned Flutter integration, and the
  minimal live-state proof surface. Its scaffold and persistence work may be pulled forward when
  required by earlier pairing/client work without closing the phase.
- Stage 5A deliberately pulls forward the smallest complete Android and secure same-LAN path needed
  for real-device development. It consumes the already-approved identity/pairing semantics and
  pulled-forward SDK ports, but does not close Stage 5 or replace the later generalized LAN/mobile
  stages.
- Stage 6 remains the desktop connected-client proof; Stages 6–8 validate the connected second-screen
  product while Stage 5A supplies early physical-device feedback before broader feature work.
- Stages 9–11 establish the generalized multi-client, local discovery, and automatic connection
  policy that the later LAN capability builds on. Stage 9 raises the Stage 4.2 session capacity and
  adds fan-out without replacing the shared-authority/session-delivery boundary; Stage 5A remains a
  deliberately narrow LAN slice for early client development.
- Stages 12–27 add read-only product capabilities and presentation adaptation in dependency order.
- Stage 22 generalizes and hardens the secure LAN/discovery slice first proven by Stage 5A; Stage 23
  completes the mobile/tablet client experience on top of that work.
- Stage 28 follows read-only validation and adds only authorization machinery, not gameplay mutation.
- Stages 29–30 are evidence-driven hardening and dependency maintenance.

## Deferred possibilities

These ideas require a demonstrated need and separate product, architecture, security, and validation
decisions before entering the ordered roadmap:

- Individual companion actions, including equipment, favorites or hotkeys, map markers, and fast travel.
- Arbitrary console or Papyrus execution through the bridge.
- Arbitrary map-selected destinations.
- A general plugin or extension platform.
- Internet or hosted remote connectivity.
- Accounts, cloud storage, or cross-device cloud synchronization.
- Broad compatibility promises for unsupported mods, UI themes, or Skyrim editions.
- A device-identity capability resistant to reinstall/identity reset, potentially including physical-
  device attestation, reconsidered alongside the LAN/mobile security phases (22–23); not committed
  scope for Phase 3.2's device-blocking work.
- Network-environment compatibility hardening for guest/client-isolated Wi-Fi, VPNs, mobile hotspots,
  routed or multi-subnet networks, IPv6-only environments, firewall edge cases, and discovery
  failures beyond the initial ordinary same-LAN development path.
