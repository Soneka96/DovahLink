# DovahLink SDK

This directory owns the reusable, supported client SDK implementations for the DovahLink protocol.
Product status and phase dependencies live in the root [ROADMAP.md](../ROADMAP.md); system
boundaries live in [ARCHITECTURE.md](../ARCHITECTURE.md); SDK-specific conventions live in
[`ai/context/sdk/`](../ai/context/sdk/).

## Purpose

The SDK implements what it means to be a correct DovahLink client for one language, so that
consumers do not need to implement transport, Host-version compatibility detection,
authentication, pairing recovery, reconnect, session and authoritative-state identity, revisions,
subscriptions, snapshots, recovery, or reusable client persistence themselves.

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
DPAPI-backed implementation ships today) -- see `ai/context/sdk/persistence.md`. The official
Flutter app depends on it (`dovahlink_client_sdk` in `app/pubspec.yaml`) and already uses its public
client for pairing and authentication through `PairingRemoteDataSource`.

The SDK supports Host releases in the `0.4.x` range and rejects older or newer Host versions during
`hello`, before admitting a session. It still has no public state synchronization API: Stage 5 owns
the SDK's typed state models, revisions, subscriptions, snapshot/recovery lifecycle, and restoring
desired subscriptions after reconnect. The app's `features/connection/` code currently handles Host
selection and navigation; Stage 5 wires live state through the SDK and Flutter middleware. This
work does not close Stage 5.

The app's `features/connection/` area remains responsible for Host selection and navigation. It is
not a parallel protocol implementation. See [`app/README.md`](../app/README.md) for the current
division between app presentation and SDK-owned communication.
