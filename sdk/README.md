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

Planned. This directory currently contains only this README; no Dart package exists yet. The real
package is created when `ROADMAP.md`'s Phase 5, "Dart Client SDK Foundation," begins, at:

```text
sdk/
  dart/
    dovahlink_client/
```

Until that phase, the app-side Dart client documented in
[`ai/context/flutter/`](../ai/context/flutter/) remains the active Dart consumer for the identity,
pairing, and live-synchronization foundations already in progress; see
[`app/README.md`](../app/README.md).
