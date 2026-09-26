# DovahLink client

This directory owns the Flutter desktop client. Product status and phase dependencies live in the
root [ROADMAP.md](../ROADMAP.md); system boundaries live in
[ARCHITECTURE.md](../ARCHITECTURE.md); Flutter-specific conventions live in
[`ai/context/flutter/`](../ai/context/flutter/).

Canonical messages and shared fixtures remain under [`protocol/`](../protocol/). Client data Models
and adapters consume that contract without redefining it.

## SDK integration

The pairing feature already uses [`sdk/dart/dovahlink_client/`](../sdk/README.md)'s public API for
transport, authentication, pairing, pairing recovery, and bounded reconnect. The
`features/connection/` area currently owns Host selection and navigation; it does not implement
live-state synchronization. Phase 5.1 delivered the SDK's Host-version compatibility checks. The
remaining Stage 5 phases add state synchronization and subscription/recovery APIs, then wire
live-state streams through Flutter middleware. Flutter
conventions point to [`ai/context/sdk/`](../ai/context/sdk/) for SDK-owned protocol behavior rather
than duplicating it in the app.

## Development checks

Run commands from this directory:

```powershell
flutter pub get
dart run build_runner build
flutter analyze
flutter test
```

Generated `.g.dart` files are committed beside their source data Models and are regenerated rather than
edited by hand.

## Windows shutdown check

Start DovahLink, connect to a Host, and close the window with X or Alt+F4. The client stops pairing
retry work and immediately starts disconnecting an SDK client that was already created. The app
waits up to three seconds for cleanup; if that budget expires, the Windows runner resumes close
processing after its five-second native timeout. After cleanup, Flutter and its plugins receive the
close message before native window destruction. No reconnect attempts or shutdown errors should
follow, no WebSocket/use-after-close errors should appear, and no DovahLink-owned Dart process
should remain.
The DovahLink Host may remain running while Skyrim is open because the Host belongs to the
Skyrim/adapter lifecycle.
