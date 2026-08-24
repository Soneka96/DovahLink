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
  State Synchronization Foundation is next, not yet started.
- **Current phase:** Phase 3.3 — Client Trust-State Integration (**Complete**) closed out Stage 3,
  finishing the SDK/client reaction to Phase 3.2's Bridge-side trust administration: continuous
  invalidation observation, typed revoked/blocked/trustReset/factoryReset state, reason-specific
  credential cleanup across both the explicit re-authentication path and bounded automatic
  reconnect, clientId preservation, and no automatic re-pair or uncontrolled reconnect loop.

## Ordered stages

| Stage | Status | Detailed specification |
| --- | --- | --- |
| 0 | Complete | [Stage 0 — Foundation](roadmap/00-foundation.md) |
| 1 | Complete | [Stage 1 — Skyrim Bridge Foundation](roadmap/01-skyrim-bridge-foundation.md) |
| 2 | Complete | [Stage 2 — Bridge Identity and Authoritative State](roadmap/02-bridge-identity-and-authoritative-state.md) |
| 3 | Complete | [Stage 3 — Local Device Pairing and Reconnection](roadmap/03-local-device-pairing-and-reconnection.md) |
| 4 | Planned | [Stage 4 — Live State Synchronization Foundation](roadmap/04-live-state-synchronization-foundation.md) |
| 5 | Planned. The package scaffold, protocol/transport layer, and persistence boundary are partially implemented and pulled forward. | [Stage 5 — Dart Client SDK Foundation](roadmap/05-dart-client-sdk-foundation.md) |
| 6 | Planned | [Stage 6 — PC / Second-Screen Baseline](roadmap/06-pc-second-screen-baseline.md) |
| 7 | Planned | [Stage 7 — Core UI Theme System](roadmap/07-core-ui-theme-system.md) |
| 8 | Planned | [Stage 8 — Live Player State](roadmap/08-live-player-state.md) |
| 9 | Planned | [Stage 9 — Multi-Client Runtime Foundation](roadmap/09-multi-client-runtime-foundation.md) |
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
  synchronization kernel. Its protocol migration may use temporary boundary compatibility during
  delivery, but the completed phase supports only the new contract.
- Stage 5 consumes Stage 4's stable contract and synchronization kernel to complete the reusable Dart
  client boundary, public subscription/recovery API, middleware-owned Flutter integration, and the
  minimal live-state proof surface. Its scaffold and persistence work may be pulled forward when
  required by earlier pairing/client work without closing the phase.
- Stages 6–8 validate the connected second-screen product before multi-client, discovery, LAN/mobile,
  knowledge, theming, and action foundations expand the product.
- Stages 9–11 establish multi-client, local discovery, and automatic connection policy before LAN.
- Stages 12–27 add read-only product capabilities and presentation adaptation in dependency order.
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
