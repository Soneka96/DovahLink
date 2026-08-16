# SDK architecture

These conventions govern the Dart Client SDK (`sdk/dart/dovahlink_client/`, once it exists — see
`sdk/README.md` and `ROADMAP.md`'s Phase 5). Shared Dart-language rules live in
`ai/context/dart/dart-style.md`; do not duplicate them here.

## Dependency direction

```text
protocol/
   ↓
Dart SDK implementation
   ↓
Official Flutter app (and any other Dart consumer)
```

`protocol/` remains the sole canonical language-neutral Bridge/client contract. The SDK implements
that contract for Dart consumers; it is not a second protocol authority, and its convenient typed
client/domain models never become wire-contract authority. Bridge-version compatibility ownership
(the SDK's declared supported range, the compatibility bootstrap, contract-change assessment) is
defined by `ai/context/protocol/compatibility.md`; do not restate it here, and do not give the SDK
an independent historical protocol-generation model.

## Bridge versus SDK ownership

The Bridge remains authoritative for live Skyrim game state, authoritative revisions, the current
`playContextId`, server-side trusted-client records, revocation, trust administration, Bridge
capabilities, and server-side security decisions. The SDK is a client library: it must never become
authoritative over Skyrim or server-side trust.

Trust has two sides, and the SDK owns only its own:

```text
Bridge:            "These clients are trusted."
SDK on a client:    "This is my clientId and credential."
```

The SDK owns its local `clientId`, credential, pairing `CONFIRMING` recovery state, and other
client-side authentication persistence. It may expose typed APIs for Bridge trust-administration
capabilities (list/revoke/reset), but the authoritative mutation always happens on the Bridge; see
`ai/context/protocol/security.md` for the trust model itself.

## App independence

After the Dart Client SDK Foundation phase, the official app depends on the SDK's public API for
normal DovahLink communication. It must not construct a new parallel raw WebSocket implementation,
Bridge compatibility implementation, protocol decoder, authentication implementation, pairing
implementation, reconnect state machine, revision tracker, or subscription engine. If the app needs
behavior the SDK cannot provide, treat that as evidence the SDK needs to be extended, not as
justification for private app-side plumbing.

## One engine, multiple API views

The SDK has one underlying client engine/state machine. Do not create separate implementations
(for example `SimpleDovahLinkClientService` alongside `AdvancedDovahLinkClientService`) that each
establish their own transport connection, state store, subscriptions, session, or cache. Expose a
small high-level API plus focused expert capability interfaces (lifecycle, diagnostics,
administration, compatibility metadata) over the same underlying client instance. Do not pre-create
an empty capability interface for a hypothetical future need; add one when a real approved
capability exists. Avoid letting one unbounded "advanced" interface become a dumping ground.

## Public versus internal boundaries

The reusable client core must not depend on Flutter widgets, Redux, `GetIt`, navigation, or any
official-app presentation architecture — see `ai/context/sdk/api-design.md` for the curated public
API this boundary supports.

## Platform ports

Where reusable client behavior requires platform-specific facilities (secure credential storage,
cache/filesystem location, future local discovery, platform lifecycle integration), place them
behind explicit ports/interfaces rather than hardcoding one platform's APIs into the reusable client
model. Do not pre-create speculative adapters; add one when a real supported platform requires it.
An initial Windows implementation may use Windows-appropriate facilities without those APIs becoming
part of the reusable model; later Android/iOS implementations provide platform behavior without
rewriting connection, authentication, or state semantics.

## Feature and capability organization

Organize the SDK around the capabilities it actually implements (connection/session,
compatibility, pairing/trust, subscriptions/state, persistence, cache) rather than a generic
"services" layer. Each capability's internal implementation stays internal; only its curated public
surface is exported, per `ai/context/sdk/api-design.md`.
