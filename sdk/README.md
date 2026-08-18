# DovahLink SDK

This directory owns the reusable, supported client SDK implementations for the DovahLink protocol.
Product status and phase dependencies live in the root [ROADMAP.md](../ROADMAP.md); system
boundaries live in [ARCHITECTURE.md](../ARCHITECTURE.md); SDK-specific conventions live in
[`ai/context/sdk/`](../ai/context/sdk/).

## Purpose

The SDK implements what it means to be a correct DovahLink client for one language, so that
consumers do not need to implement transport, Bridge-version compatibility detection,
authentication, pairing recovery, reconnect, session and authoritative-state identity, revisions,
subscriptions, snapshots, recovery, or reusable client persistence themselves.

## Dependency direction

```text
Skyrim
   |
DovahLink Bridge / mod
   |
protocol/
   |
Dart Client SDK
   |
Official Flutter app
```

`protocol/` remains the sole canonical language-neutral Bridge/client contract; the SDK implements
that contract for Dart consumers and is not a second protocol authority. The official Flutter app is
the SDK's first production consumer, not a privileged one — see
[ARCHITECTURE.md](../ARCHITECTURE.md#sdk).

## Status

Partially implemented, pulled forward from `ROADMAP.md`'s Phase 5 ("Dart Client SDK Foundation")
ahead of that phase's formal start, because Phase 3 (Local Device Pairing and Reconnection) needed
the SDK's persistence boundary to avoid a larger later migration. The real package exists at:

```text
sdk/
  dart/
    dovahlink_client/
```

It currently provides the connect/hello/pairing/disconnect protocol client, proven against the real
bridge harness, plus SDK-owned `clientId`, credential, and `CONFIRMING` pairing-recovery persistence
behind the `ClientStorage` interface (a real Windows DPAPI-backed implementation ships today) --
see `ai/context/sdk/persistence.md`. The official Flutter app depends on it
(`dovahlink_client_sdk` in `app/pubspec.yaml`), but nothing in the app consumes it yet; the
production pairing UI is a later, separate build. Phase 5's remaining scope -- Bridge-version
compatibility detection, reconnect, revisions, subscriptions, snapshots, and retiring the app's
separate `features/connection/` Redux protocol code -- is undone, so this pull-forward does not
close Phase 5.

Until the pairing UI and the rest of Phase 5 land, the app-side Redux `features/connection/` code
documented in [`ai/context/flutter/`](../ai/context/flutter/) remains a separate, not-yet-retired
implementation for the identity and live-synchronization foundations already in progress; see
[`app/README.md`](../app/README.md).
