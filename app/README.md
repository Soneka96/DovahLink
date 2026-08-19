# DovahLink client

This directory owns the Flutter desktop client. Product status and phase dependencies live in the
root [ROADMAP.md](../ROADMAP.md); system boundaries live in
[ARCHITECTURE.md](../ARCHITECTURE.md); Flutter-specific conventions live in
[`ai/context/flutter/`](../ai/context/flutter/).

Canonical messages and shared fixtures remain under [`protocol/`](../protocol/). Client models and
adapters consume that contract without redefining it.

## SDK migration

Before `roadmap/05-dart-client-sdk-foundation.md`'s Phase 5 ("Dart Client SDK Foundation"), this directory owns its protocol and
client adapters directly — the identity, pairing, and live-synchronization foundations already in
progress are implemented here, following `ai/context/flutter/`. After that phase, this app consumes
[`sdk/dart/dovahlink_client/`](../sdk/README.md)'s public API for normal DovahLink communication
instead: transport, Bridge-version compatibility, authentication, pairing, reconnect, and revision
logic move to the SDK boundary. Flutter conventions point to
[`ai/context/sdk/`](../ai/context/sdk/) for that SDK-owned behavior rather than duplicating it here.

## Development checks

Run commands from this directory:

```powershell
flutter pub get
dart run build_runner build
flutter analyze
flutter test
```

Generated `.g.dart` files are committed beside their source models and are regenerated rather than
edited by hand.
