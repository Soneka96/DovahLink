# DovahLink SDK

This directory owns the reusable, supported client SDK implementations for the DovahLink protocol.
Product status and phase dependencies live in the root [ROADMAP.md](../ROADMAP.md); system
boundaries live in [ARCHITECTURE.md](../ARCHITECTURE.md); SDK-specific conventions live in
[`ai/context/sdk/`](../ai/context/sdk/).

## Purpose

The SDK implements what it means to be a correct DovahLink client for one language, so that
consumers do not need to implement transport, Host-version compatibility detection,
authentication, pairing recovery, reconnect, session and authoritative-state identity, revisions,
subscriptions, snapshots, recovery, or reusable client persistence themselves. The SDK also
provides local Host discovery through its loopback-only public endpoint.

## Dependency direction

```text
Skyrim
   |
DovahLink Host / Adapter
   |
protocol/
   |
Dart Client SDK
   |
Official Flutter app
```

`protocol/` remains the sole canonical language-neutral Host/client contract; the SDK implements
that contract for Dart consumers and is not a second protocol authority. The official Flutter app is
the SDK's first production consumer, not a privileged one — see
[ARCHITECTURE.md](../ARCHITECTURE.md#sdk).

## Status

Partially implemented, pulled forward from `roadmap/05-dart-client-sdk-foundation.md`'s Phase 5 ("Dart Client SDK Foundation")
ahead of that phase's formal start, because Phase 3 (Local Device Pairing and Reconnection), documented in `roadmap/03-local-device-pairing-and-reconnection.md`, needed
the SDK's persistence boundary to avoid a larger later migration. The real package exists at:

```text
sdk/
  dart/
    dovahlink_client/
```

It currently provides the connect/hello/pairing/disconnect protocol client and bounded automatic
reconnection after ordinary transport loss, plus SDK-owned `clientId`, credential, and
`CONFIRMING` pairing-recovery persistence behind the `IClientStorage` interface (a real Windows
DPAPI-backed implementation ships today through the Windows-specific
`dovahlink_client_windows.dart` entry point) -- see `ai/context/sdk/persistence.md`. The official
Flutter app depends on it (`dovahlink_client_sdk` in `app/pubspec.yaml`) and already uses its public
client for pairing and authentication through `PairingRemoteDataSource`. The public SDK also probes
the local loopback endpoint with an isolated unpaired handshake and returns the responding peer's
protocol-validated `hostId`/`hostName` claims and current endpoint. Discovery does not authenticate
Host identity, prove the peer owns a previously trusted identity, or discover Hosts over LAN or
mDNS.

The app selects storage at its composition boundary. Windows uses DPAPI; other platforms currently
use an explicit unsupported-storage boundary, and pairing stays unavailable until secure storage is
implemented for that platform.

The SDK supports Host releases in the `0.5.x` range and rejects older or newer Host versions during
`hello`, before admitting a session. Released Host `0.4.0` used additive subscription updates and is
incompatible with the Phase 5.3 complete-set subscription API. The repository release is `0.5.0`.
Subscribe and unsubscribe calls update local desired intent before Host synchronization, so a failed
request does not necessarily roll back the change; retained intent may be synchronized on a later
trusted session, while intentional disconnect clears it.
Phase 5.2 is complete: the public client exposes replayable typed
state streams for XP, health, magicka, stamina, and level, backed by the SDK's state models,
revision tracking, Snapshot handling, and level Event handling. Phase 5.3 is complete: callers have
typed per-domain subscription intent, and the SDK restores the desired set after trusted recovery
while keeping it dormant after administrative invalidation. Phase 5.4 wires SDK streams through
Flutter middleware; Phase 5.5 audits version impact and closes Stage 5.

The app's `features/connection/` area remains responsible for Host selection and navigation. The
SDK supplies local discovery and protocol communication; the app owns selection and presentation.
See [`app/README.md`](../app/README.md) for the current division between app presentation and
SDK-owned communication.
