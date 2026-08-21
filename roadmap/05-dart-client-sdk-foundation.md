# Stage 5 — Dart Client SDK Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./04-live-state-synchronization-foundation.md) · [Next stage](./06-pc-second-screen-baseline.md)

## 5. Dart Client SDK Foundation

**Status:** Planned. The package scaffold, protocol/transport layer, and persistence boundary
(`clientId`, credential, `CONFIRMING` pairing-recovery state, behind a Windows DPAPI-backed
`ClientStorage`) were pulled forward to unblock Phase 3's client-side pairing recovery, per
`ai/context/sdk/persistence.md`. Bridge-version compatibility detection, reconnect, revisions,
subscriptions, snapshots, and retiring the app's separate `features/connection/` Redux protocol
code remain undone, so this phase is not complete. The single inbound SDK receiver/router and
initial per-operation retry-safety/session-requirement/timeout-class policy were similarly pulled
forward by Phase 3.3 (`roadmap/03`), per `ai/context/sdk/architecture.md` and
`ai/context/sdk/api-design.md`; this phase still owns extending both to the rest of the SDK's
operations and domains.

### Outcome

Dart applications can participate correctly in DovahLink — transport, Bridge-version compatibility
detection, authentication, pairing recovery, reconnect, session and authoritative-state identity,
revisions, subscriptions, snapshots, recovery, and reusable client persistence — without
implementing that behavior themselves, and the official Flutter application becomes the first
production consumer proving the supported SDK API is sufficient to build a complete client.

### Scope and behavior

- Create the real `sdk/dart/dovahlink_client/` package and establish it as a first-class repository
  ownership boundary alongside `app/`, `bridge/`, `protocol/`, and `integration/`, per
  `ARCHITECTURE.md` and `ai/context/sdk/`.
- Migrate the reusable Dart-side connection, compatibility, authentication, pairing, reconnect,
  session, revision, subscription, and recovery behavior already implemented directly in `app/` by
  Phases 2 through 4 into the SDK boundary, rather than redesigning approved semantics.
- Establish the SDK's explicit supported Bridge/mod-version range and its own persistence boundary
  (stable local `clientId`, client credential, pairing recovery state, reusable cache metadata),
  versioned and migration-owned by the SDK per `ai/context/sdk/persistence.md`.
- Expose one underlying client engine through a small simple API plus focused expert capability
  views (lifecycle, diagnostics, administration), per `ai/context/sdk/architecture.md` and
  `api-design.md`; do not build a second parallel service stack.
- Wire the official Flutter application through the SDK's public API and retire its parallel
  app-private protocol/client implementation in this same phase; the app must not construct raw
  transport, compatibility, authentication, pairing, reconnect, revision, or subscription logic
  after this phase completes.
- Keep the SDK repository-internal and unpublished; publication, package stability guarantees, and
  a public release workflow remain a separate future decision.

### Dependencies and boundaries

This phase depends on Phases 2, 3, and 4 and consumes their approved identity, pairing/reconnection,
and live-synchronization semantics rather than redesigning them. It does not implement Phase 9
concurrent-client delivery, Phase 10 multi-Bridge discovery, Phase 11 automatic connection/transport
selection, or Phase 22 secure LAN transport; when those phases are implemented, their Dart client
behavior extends the SDK rather than being built privately into the app again. The independent .NET
validation client remains a separate implementation of the canonical contract and does not consume,
wrap, or generate from the Dart SDK.

### Acceptance criteria

- The `sdk/dart/dovahlink_client/` package exists with a curated public API; internal transport,
  codec, persistence, compatibility, and state-machine types are not accidentally exported.
- The SDK declares an explicit supported Bridge/mod-version range rather than inferring compatibility
  from SemVer, and canonical contract changes are assessed against that declared range per
  `ai/context/protocol/compatibility.md`.
- The official Flutter application consumes the SDK exclusively for normal DovahLink communication;
  its parallel app-private protocol/client stack is retired in this phase, not left running alongside
  the SDK.
- One underlying client engine backs every exposed API view; there is no duplicate transport,
  session, or cache stack behind a "simple" and an "advanced" surface.
- SDK-owned persistence (client credential, pairing recovery state, cache metadata) is versioned and
  migration-owned by the SDK; the app never needs to understand or migrate that private schema.
- The independent .NET validator still passes the same canonical fixtures without depending on the
  Dart SDK.
- The SDK is not published outside the repository as part of this phase.
