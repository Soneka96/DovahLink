# Stage 5 — Dart Client SDK Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./04-live-state-synchronization-foundation.md) · [Next stage](./05a-android-wifi-development-path.md)

## 5. Dart Client SDK Foundation

**Status:** Planned. The package scaffold, protocol/transport layer, and persistence boundary
(`clientId`, credential, `CONFIRMING` pairing-recovery state, behind a Windows DPAPI-backed
`ClientStorage`) were pulled forward to unblock Phase 3's client-side pairing recovery, per
`ai/context/sdk/persistence.md`. Delivery is decomposed into the public typed protocol boundary,
synchronization API, subscription/recovery lifecycle, Flutter middleware proof, and phase-end version
auditing.
Bridge-version compatibility detection, revisions, subscriptions, snapshots, recovery, and retiring
the app's separate `features/connection/` Redux protocol code remain undone, so this phase is not
complete. The single inbound SDK receiver/router and initial per-operation retry-safety/session-
requirement/timeout-class policy were similarly pulled forward by Phase 3.3 (`roadmap/03`), per
`ai/context/sdk/architecture.md` and `ai/context/sdk/api-design.md`.

### Outcome

Dart applications can participate correctly in DovahLink — transport, Bridge-version compatibility
detection, authentication, pairing recovery, reconnect, session and authoritative-state identity,
revisions, subscriptions, snapshots, recovery, and reusable client persistence — without
implementing that behavior themselves, and the official Flutter application becomes the first
production consumer proving the supported SDK API is sufficient to build a complete client.

### Scope and behavior

- Complete `sdk/dart/dovahlink_client/` as a first-class repository ownership boundary alongside
  `app/`, `bridge/`, `protocol/`, and `integration/`, per `ARCHITECTURE.md` and `ai/context/sdk/`.
- Complete the reusable Dart-side connection, compatibility, authentication, pairing, reconnect,
  session, revision, subscription, and recovery behavior in the SDK boundary rather than rebuilding
  it in Flutter. The current pairing/reconnect work remains pulled forward; Stage 5 adds the
  protocol and live-state behavior established by Stage 4.
- Establish the SDK's explicit supported Bridge-version range and its own persistence boundary
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

#### 5.1 SDK Typed Protocol and Bridge Compatibility Boundary

Complete the Dart DTOs for the redesigned message families using generated structural
`fromJson`/`toJson` code plus handwritten semantic validation. Keep a small shared message header and
prevent raw JSON, transport types, and internal codecs from crossing the public export boundary.

The SDK reads `hello_ack.bridgeVersion`, applies the repository's pre-1.0 same-major/same-minor and
post-1.0 accepted-minor rules, and closes before capabilities or state traffic when the Bridge is
incompatible. The SDK owns the explanation; the Bridge only advertises its version and does not
reject SDK versions.

#### 5.2 SDK State Synchronization API

Expose the Stage 4 synchronization kernel through curated typed models for `character_xp` Snapshot
state and `character_level` Event state. A state stream carries a typed value plus its domain
synchronization status (`notSubscribed`, `unavailable`, `synchronized`, `stale`, `recovering`, or
`failed`); global connection lifecycle remains a separate stream.

The SDK owns authoritative identity checks, revisions, duplicate/stale suppression, gap recovery,
bounded Event buffering, snapshot supersession, and recovery failure handling. Flutter receives
typed values and statuses rather than protocol envelopes.

#### 5.3 Subscription, Reconnect, and Session Lifecycle

Provide explicit SDK subscription intent and lifecycle operations for the middleware. A trusted
connection starts the desired state subscriptions; an intentional disconnect removes them. Ordinary
transport loss keeps the desired intent, exposes reconnecting/recovery state, and restores the
remote subscriptions after authentication followed by fresh snapshots. Administrative invalidation
leaves desired subscriptions dormant until explicit user-initiated recovery.

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

The proof surface remains intentionally small: it shows the current XP and level values, unavailable
state, stale/recovering state, compatibility failure, connection lifecycle, and slow-consumer
diagnostics without introducing the later theme system, dashboard customization, discovery, or
mobile presentation work.

#### 5.5 Version-Impact Audit and Stage 5 Closure

Reuse the manually invoked version-audit skill established by Phase 4.5 and extend its ownership map
for the SDK and Flutter application. The skill reads the Stage 5 or bugfix diff, affected public
exports, protocol/schema changes, persistence formats, security/runtime behavior, tests, and current
version ownership. It may update the relevant Bridge, SDK, or Flutter version/changelog/compatibility
files and prepare a commit message, but it never commits.

At Stage 5 completion it audits the complete stage rather than each ordinary PR. A later bugfix may
invoke it independently; a contract-breaking bugfix must not be forced into a patch bump.

### Dependencies and boundaries

This phase depends on Phases 2, 3, and 4 and consumes their approved identity, pairing/reconnection,
protocol, and live-synchronization semantics rather than redesigning them. The separate Stage 5A
development slice consumes the SDK's pulled-forward platform-port and transport boundaries but does
not close this phase. Stage 5 itself does not implement Phase 9 concurrent-client delivery, Phase 10
multi-Bridge discovery, Phase 11 automatic connection/transport selection, or the generalized Stage
22 secure LAN transport; when those phases are implemented, their Dart client behavior extends the
SDK rather than being built privately into the app again. The independent .NET validation client
remains a separate implementation of the canonical contract and does not consume, wrap, or generate
from the Dart SDK.

### Acceptance criteria

- The `sdk/dart/dovahlink_client/` package exists with a curated public API; internal transport,
  codec, persistence, compatibility, and state-machine types are not accidentally exported.
- The SDK declares an explicit supported Bridge-version range rather than inferring compatibility
  from generic SemVer rules, applies the repository's pre-1.0 and post-1.0 comparison policy, and
  assesses canonical contract changes against that declared range per
  `ai/context/protocol/compatibility.md`.
- The official Flutter application consumes the SDK exclusively for normal DovahLink communication;
  its parallel app-private protocol/client stack is retired in this phase, not left running
  alongside the SDK.
- One underlying client engine backs every exposed API view; there is no duplicate transport,
  session, or cache stack behind a "simple" and an "advanced" surface.
- SDK-owned persistence (client credential, pairing recovery state, cache metadata) is versioned and
  migration-owned by the SDK; the app never needs to understand or migrate that private schema.
- The independent .NET validator still passes the same canonical fixtures without depending on the
  Dart SDK.
- Middleware owns SDK state-stream subscriptions and translates typed values/statuses into Redux
  actions; widgets and screens do not consume SDK streams directly.
- The minimal live-state proof demonstrates Snapshot and Event domains, revision-gap recovery,
  ordinary reconnect restoration, administrative-invalidation dormancy, and incompatible-Bridge
  handling.
- The manually invoked version-audit skill completes the phase's version/changelog/compatibility
  review without committing changes.
- The SDK is not published outside the repository as part of this phase.
