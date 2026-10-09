# Stage 5 — Dart Client SDK Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./04-live-state-synchronization-foundation.md) · [Next stage](./05a-windows-sas-integration-validation.md)

## 5. Dart Client SDK Foundation

**Status:** Complete. The package scaffold, protocol/transport layer, and persistence boundary
(`clientId`, credential, `CONFIRMING` pairing-recovery state, behind a Windows DPAPI-backed
`IClientStorage`) were pulled forward to unblock Phase 3's client-side pairing recovery, per
`ai/context/sdk/persistence.md`. Delivery is decomposed into the public typed protocol boundary,
synchronization API, subscription/recovery lifecycle, Flutter middleware proof, and phase-end version
auditing.
Phases 5.1–5.5 are complete: the typed protocol/compatibility boundary, state synchronization API,
subscription/reconnect/session lifecycle, Flutter live-state proof, and full version-impact audit.
Phase 5.3 adds canonical complete-set subscription updates with Host reconciliation, SDK per-domain
intent, ordinary reconnect restoration, administrative dormancy until explicit recovery, and
intentional-disconnect cleanup.
Its complete-set subscription meaning is incompatible with released Host `0.4.0`'s additive behavior.
The repository's `0.5.0` version and dated changelog sections are internal records; no supported
Host/Client `0.5.0` package was publicly released. Earlier internal `0.5.0` builds are not
compatibility targets under the pre-release policy, and current Host/SDK source is aligned at
`0.5.0`/`0.5.x`. A `0.5.1` bump is not needed solely to distinguish an unpublished internal build.
PR #120 delivered the SDK-to-Redux pipeline, and PR #122 delivered the state-backed Session
Overview. Phase 5.4 finished player-facing number formatting and visual parity, recorded the
maintainer's runtime validation, and rebaselined the proof acceptance. Phase 5.5 completed the full
Stage 5 version-impact audit and acceptance review; Stage 5 is complete.
The app's current `features/connection/` area owns Host selection and navigation rather than a
separate protocol client. Phase 3.3 (`roadmap/03`) similarly pulled forward the single inbound SDK
receiver/router and initial per-operation retry-safety/session-requirement/timeout-class policy,
per `ai/context/sdk/architecture.md` and `ai/context/sdk/api-design.md`.

### Outcome

Dart applications can participate correctly in DovahLink — transport, Host-version compatibility
detection, authentication, pairing recovery, reconnect, session and authoritative-state identity,
revisions, subscriptions, snapshots, recovery, and reusable client persistence — without
implementing that behavior themselves, and the official Flutter application becomes the first
production consumer proving the supported SDK API is sufficient to build a complete client.

### Scope and behavior

- Complete `sdk/dart/dovahlink_client/` as a first-class repository ownership boundary alongside
  `app/`, `host/`, `adapter/`, `protocol/`, and `integration/`, per `ARCHITECTURE.md` and
  `ai/context/sdk/`.
- Complete the reusable Dart-side connection, compatibility, authentication, pairing, reconnect,
  session, revision, subscription, and recovery behavior in the SDK boundary rather than rebuilding
  it in Flutter. The current pairing/reconnect work remains pulled forward; Stage 5 adds the
  protocol and live-state behavior established by Stage 4.
- Establish the SDK's explicit supported Host-version range and its own persistence boundary
  (stable local `clientId`, client credential, pairing recovery state, reusable cache metadata),
  versioned and migration-owned by the SDK per `ai/context/sdk/persistence.md`.
- Expose one underlying client engine through a small simple API plus focused expert capability
  views (lifecycle, diagnostics, administration), per `ai/context/sdk/architecture.md` and
  `api-design.md`; do not build a second parallel service stack.
- Wire the official Flutter application through the SDK's public API and retire any parallel
  app-private protocol/client implementation; the app must not construct raw transport,
  compatibility, authentication, pairing, reconnect, revision, or subscription logic after this
  phase completes.
- Keep the SDK repository-internal and unpublished; publication, package stability guarantees, and
  a public release workflow remain a separate future decision.

### Phase breakdown

#### 5.1 SDK Typed Protocol and Host Compatibility Boundary

**Status:** Complete

Complete the Dart DTOs for the redesigned message families using generated structural
`fromJson`/`toJson` code plus handwritten semantic validation. Keep a small shared message header and
prevent raw JSON, transport types, and internal codecs from crossing the public export boundary.

The SDK reads `hello_ack.hostVersion`, applies the repository's pre-1.0 same-major/same-minor and
post-1.0 accepted-minor rules, and closes before capabilities or state traffic when the Host is
incompatible. Phase 5.3's complete-set subscription semantics make released Host `0.4.0`
incompatible with the public subscription API; the current `0.5.0` source includes that change.
Earlier internal builds are not compatibility targets under the pre-release policy. The SDK owns the
explanation; the Host only advertises its version and does not reject SDK versions.

#### 5.2 SDK State Synchronization API

**Status:** Complete

The initial per-scalar Vitals SDK streams were superseded before release by the Character data
architecture correction. The current API groups domain streams under `client.currentHost.character`
and exposes the coherent `character_vitals` state alongside independent XP and Level domains; the
original acceptance description below records Phase 5.2's delivery at the time.

Expose the Stage 4 synchronization kernel through curated typed models for the character Snapshot
state areas (`character_xp`, `character_health`, `character_magicka`, and `character_stamina`) and
`character_level` Event state. A state stream carries a typed value plus its domain
synchronization status (`notSubscribed`, `unavailable`, `synchronized`, `stale`, `recovering`, or
`failed`); global connection lifecycle remains a separate stream.

The SDK owns authoritative identity checks, revisions, duplicate/stale suppression, gap recovery,
bounded Event buffering, snapshot supersession, and recovery failure handling. Flutter receives
typed values and statuses rather than protocol envelopes.

#### 5.3 Subscription, Reconnect, and Session Lifecycle

Provide explicit SDK per-domain subscription intent and lifecycle operations for the middleware.
The existing `subscribe.stateAreas` field represents the complete desired set for that connection;
`stateAreas: []` removes all active areas. Each Host update replaces its active set, removes omitted
or rejected areas, and stops future publication for them. The SDK returns rejected domains, applies
only Host-accepted areas, resets removed state streams to `notSubscribed`, and ignores late messages
for areas no longer active.

A trusted session starts the desired subscriptions. An intentional SDK disconnect clears desired
intent and closes the session. Ordinary transport loss preserves intent, exposes the reconnecting
lifecycle, and restores the desired set after trusted session admission; successful explicit pairing
also restores it. Each accepted area receives a fresh Snapshot baseline before its state is
synchronized. Administrative invalidation clears the active state gate but leaves intent dormant
until explicit user authentication or pairing recovery succeeds.

The SDK's single inbound receiver remains the only raw transport reader. Correlated replies,
unsolicited state messages, session invalidation, protocol violations, and late messages from older
connection generations remain separated by the existing request/session architecture.

#### 5.4 Flutter Middleware and Minimal Live-State Proof

Add middleware-owned SDK stream wiring. The UI does not call SDK streams or protocol operations:

```text
trusted connection
    -> middleware subscribes to SDK state
    -> stream values/statuses become Redux actions
    -> reducers update AppState
    -> UI reads AppState
```

The connected second-screen product presents current XP and Level in the Session header,
whole-number Health, Magicka, and Stamina values in the Character panel, and precise ratio bars in
Current Status. The maximum remains synchronized in the coherent Vitals Snapshot and feeds each
ratio; it is not a separate player-facing Overview number. Stale, recovering, and unavailable values
keep truthful visual treatment. Compatibility failures and connection lifecycle appear at the
connection or error boundary when they require user understanding or action. Bounded slow-client
behavior remains covered by Host transport tests and the Host-local abnormal-end contract; normal
Overview does not show developer diagnostics. This phase does not introduce dashboard customization,
discovery, or mobile presentation work. All six Vitals fields share the `character_vitals` domain's
availability, synchronization status, and revision; do not model them as separate state areas or add
a second composed resource view.

##### App-state integration delivery

PR #120 implements the SDK-to-Redux pipeline for the eight currently available Overview domains:
`character_vitals`, `character_xp`, `character_level`, `character_identity`,
`character_supernatural_traits`, `player_location`, `game_time`, and `tracked_quests`. The focused
`features/live_state/` boundary owns typed Redux actions, reducers, selectors, and the
`SessionOverviewViewModel`. It consumes the public SDK gameplay streams and passes SDK public domain
models and `StateSynchronization<T>` directly through Redux without app-owned duplicate domain
models or field redefinitions. `LiveStateMiddleware` owns the public SDK stream listeners and
expresses all required subscription areas only after SDK trust is established. It remains active
when the Session Shell route returns to Connections; routes do not own subscription lifetime. The
middleware keeps those listeners through ordinary reconnect so the SDK can restore desired intent
and publish stale, recovering, and fresh-baseline statuses. It cancels gameplay listeners and resets
the Redux slice on disconnection or administrative invalidation.

The integration preserves each domain's status, authority/context IDs, and revision alongside its
typed value. It retains all independent Supernatural Traits predicates, all Location facts, Skyrim
calendar fields, and the complete tracked-quest collection with all objective instances. Empty
tracked quests and unavailable quest state remain distinct. PR #122 consumes this state through the
typed ViewModel; Stage 8 remains open for its broader live-player-state acceptance.

##### Phase 5.4 acceptance review

- **COMPLETE — State integration:** middleware owns trusted-session subscriptions, forwards public
  SDK models and synchronization values without duplicate domains, retains listeners through
  ordinary reconnect, and cancels/resets on disconnection or administrative invalidation (PR #120).
- **COMPLETE — Player-facing values:** XP, Level, and live Vitals are present in the Session
  experience. Current Health, Magicka, and Stamina display as whole numbers; effective maximums and
  ratios remain precise in synchronized state. This closeout fixes the remaining decimal-formatting
  defect.
- **COMPLETE — Synchronization and lifecycle:** stale, recovering, and unavailable gameplay values
  retain truthful presentation; connected and disconnected states reflect the real session lifecycle.
  Disconnection cancels gameplay observation and resets the live-state slice rather than preserving
  values as current. Compatibility failures surface at the connection/error boundary where they can
  inform user action.
- **COMPLETE — Slow-client behavior:** bounded outbound behavior and overflow termination remain
  Host-owned and covered by Host transport tests. The Host-local abnormal-end contract stays
  internal; no slow-client diagnostic appears on the normal Overview.
- **OBSOLETE / REPLANNED — Separate maximum-number display:** the current product shows current
  values and percentages; the effective maximum feeds the ratio and is not a separate Overview
  number.
- **OBSOLETE / REPLANNED — Visible slow-client diagnostics:** queue and overflow correctness remains
  testable at the Host transport boundary. The player-facing UI exposes a warning only if a real,
  actionable condition requires one.
- **COMPLETE — Runtime validation:** the maintainer confirmed initial connection and state
  population, hot restart without changing Skyrim state, repopulation, reconnect, tracked quests, and
  general Overview live-state behavior. No unresolved runtime blocker remains in the supplied
  validation.

Phase 5.4 is **Complete**. It did not complete Stage 5 or Stage 8; Phase 5.5 has now completed
the Stage 5 audit and closeout.

#### 5.5 Version-Impact Audit and Stage 5 Closure

Reuse the manually invoked version-audit skill established by Phase 4.5 and extend its ownership map
for the SDK and Flutter application. The skill reads the Stage 5 or bugfix diff, affected public
exports, protocol/schema changes, persistence formats, security/runtime behavior, tests, and current
version ownership. It may update the relevant Host, SDK, or Flutter version/changelog/compatibility
files and prepare a commit message, but it never commits.

At Stage 5 completion it audits the complete stage rather than each ordinary PR. A later bugfix may
invoke it independently; a contract-breaking bugfix must not be forced into a patch bump.

**Status:** Complete

##### Stage 5 version-impact audit record

_Note: this record predates the 5A/5B split. Its references to Stage 5A mean the then-combined Android and secure-LAN security track, now [Stage 5B](./05b-android-secure-wifi-development-path.md); the audit scope and conclusions are unchanged._

1. **Audited range:** `75b23938` (the `0.4.0` release merge and Stage 5.1's parent) through
   `20b01f51` (current `main`, containing PR #127). The first Stage 5 implementation merge is
   `b4172153` (PR #79, Phase 5.1). The audit includes merged Stage 5 SDK, Host/protocol, Flutter,
   and pulled-forward client work; it excludes unmerged branches and keeps the separate Stage 5A
   security track out of scope.
2. **Comparison baseline:** `75b23938`, the last `main` snapshot before the first Stage 5
   implementation merge. It contains the completed Stage 4 / Phase 4.5 baseline and the `0.4.0`
   release. Phase 4.5's audit record identifies `0.3.2` as the earlier Stage 4 contract baseline;
   that does not change the Stage 5 comparison point.
3. **Affected components:** Host subscription behavior and later synchronized state; Adapter
   capture for pulled-forward state-domain work; canonical protocol schema and fixtures; the Dart
   SDK API, compatibility, persistence, synchronization, and tests; Flutter's SDK integration and
   presentation; and repository consistency tooling. Supported Skyrim/SKSE/CommonLib minimums do
   not change. The Stage 5A security profile is outside this audit.
4. **Protocol impact:** **BREAKING CONTRACT CHANGE** across the complete Stage 5 range. Phase 5.3
   changed `subscribe.stateAreas` to complete-set replacement, making Host `0.4.0` incompatible. The
   repository's `0.5.0` version and dated changelog sections are internal records; no supported
   Host/Client `0.5.0` package was publicly released. Later internal work added required Host
   identity fields and new state domains, and consolidated Vitals. The canonical schema, fixtures,
   Host, SDK, and app are aligned. Earlier unreleased builds are not compatibility targets under the
   documented pre-release policy.
5. **Persistence impact:** SDK-owned client state writes format v4. Its decoder migrates v3 while
   preserving client identity, Known Hosts, credentials, and valid recovery state; formats v1/v2
   are invalidated under the pre-release policy. Unknown future formats fail closed. No separate
   reusable cache format exists or changed during Stage 5.
6. **Security and runtime impact:** the SDK owns client authentication, pairing recovery, and
   loopback-only local Host discovery; a discovered Host identity remains an unauthenticated claim
   and does not grant trust. Host trust and authorization remain Host-owned. Stage 5 adds no LAN,
   TLS/WSS, or hostile-network first-pairing guarantee, and does not change supported
   Skyrim/SKSE/CommonLib minimums. Stage 5A remains separate and blocked by its existing security
   gate.
7. **Compatibility impact:** the SDK explicitly supports Host `0.5.x`, rejects older/newer or
   malformed versions before session admission, and has tests for accepted patches, old/new Hosts,
   and malformed versions. Host `0.5.0` is an internal version; no supported Host/Client `0.5.0`
   package was publicly released. Earlier internal builds that pass the `0.5.x` check are not
   compatibility targets under the documented pre-release policy. The current Host/SDK source pair
   provides the full SDK surface at the declared range. Compatibility obligations begin with the
   first supported public release.
8. **Version ownership and recommendation:** root `VERSION` (`0.5.0`) owns the Host/Adapter
   packaged version. The SDK package (`0.1.0`) and Flutter app (`0.1.0+2`) are repository-internal
   and unpublished. No version literal changes on this phase branch. Do not bump to `0.5.1` solely
   to distinguish the unpublished internal `0.5.0`; version synchronization remains part of the
   dedicated release workflow.
9. **Changelog impact:** the Host/Adapter, SDK, and app `[Unreleased]` sections already record
   their distinct Stage 5 and pulled-forward outcomes. The internal Host `0.5.0` section records the
   complete-set subscription change. No duplicate entry or additional Phase 5.5 consumer change
   warrants a changelog edit. `CHANGELOG.md` remains the frozen archive through `0.4.0`.
10. **Independent .NET validator:** **OBSOLETE / SUPERSEDED.** The former independent validator
    and its native-plugin end-to-end scenarios were intentionally removed in Stage 3A.2 when the
    retired Bridge was deleted. `integration/README.md` and the Phase 4.5 audit record document
    that removal and the current Host/SDK/fixture/process-level validation path. No replacement
    validator is introduced by Stage 5.
11. **Unresolved blockers:** none for Stage 5 closure under the release workflow. Host `0.5.0` is
    an internal version, not a supported public release, and earlier internal builds are not
    compatibility targets. Version synchronization remains a dedicated release step after phase
    work merges; an unreleased version may wait without blocking phase closure. The SDK remains
    unpublished (`publish_to: none`); Stage 5A, secure LAN, production SAS pairing, Host pinning, and
    WSS/TLS remain separate work.

### Dependencies and boundaries

This phase depends on Phases 2, 3, and 4 and consumes their approved identity, pairing/reconnection,
protocol, and live-synchronization semantics rather than redesigning them. The separate Stage 5A and Stage 5B
development slices consume the SDK's pulled-forward platform-port and transport boundaries but does
not close this phase. Stage 5 itself does not implement Phase 9 concurrent-client delivery, Phase 10
multi-instance discovery, Phase 11 automatic connection/transport selection, or the generalized Stage
22 secure LAN transport; when those phases are implemented, their Dart client behavior extends the
SDK rather than being built privately into the app again. The formerly planned independent .NET
validation client was retired with the legacy Bridge in Stage 3A.2; see the Phase 5.5 audit record.

### Acceptance criteria

1. **COMPLETE — Curated SDK package:** `sdk/dart/dovahlink_client/` exists with a curated public
   API. Internal transport, codec, compatibility, persistence implementation, and state-machine
   types are not exported; the public storage port and state value are deliberate injection types.
2. **COMPLETE — Host compatibility implementation:** the SDK declares and tests its explicit
   Host-version range and applies the documented pre-1.0 and post-1.0 policy without generic SemVer
   inference. Current source literals are `0.5.0`/`0.5.x`; earlier internal builds are not
   compatibility targets because no supported Host/Client `0.5.0` package was publicly released.
3. **COMPLETE — Flutter boundary:** the official app uses the SDK for normal DovahLink communication
   and has no parallel app-private protocol/client stack.
4. **COMPLETE — One client engine:** all grouped API views share one underlying client composition;
   no second transport, session, or cache stack exists.
5. **COMPLETE — SDK persistence:** persisted client state is SDK-owned and versioned, v3 migration
   is tested, and the app neither owns nor migrates the private persisted schema. No reusable cache
   format exists to version.
6. **OBSOLETE / SUPERSEDED — Independent .NET validator:** the validator was intentionally removed
   in Stage 3A.2 with its sole legacy Bridge target. Current Host, Dart SDK, canonical fixture, and
   Host/Adapter process tests provide the active contract evidence; Stage 5 does not recreate the
   retired tool.
7. **COMPLETE — Middleware boundary:** middleware owns SDK state-stream subscriptions and dispatches
   typed values/statuses to Redux; screens and widgets read app state.
8. **COMPLETE — Subscription transitions:** canonical fixtures plus Host and SDK tests cover adding,
   removing, clearing, rejection, and stopping publication for removed areas.
9. **COMPLETE — Minimal live-state and recovery proof:** tests cover coherent Vitals Snapshot,
   independent XP Snapshot, Level Events, revision-gap recovery, reconnect restoration,
   administrative-invalidation dormancy, and incompatible Host handling. The Phase 5.4 record also
   captures the maintainer's live runtime validation.
10. **COMPLETE — Version audit:** this Phase 5.5 record audits the complete Stage 5 range,
    ownership, compatibility, persistence, protocol, changelogs, and release follow-up without
    committing changes.
11. **COMPLETE — Repository-internal SDK:** both SDK and app package manifests specify
    `publish_to: none`; no SDK publication occurred.
