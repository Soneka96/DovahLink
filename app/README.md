# DovahLink client

This directory owns the Flutter desktop client. Product status and phase dependencies live in the
root [ROADMAP.md](../ROADMAP.md); system boundaries live in
[ARCHITECTURE.md](../ARCHITECTURE.md); Flutter-specific conventions live in
[`ai/context/flutter/`](../ai/context/flutter/).

Canonical messages and shared fixtures remain under [`protocol/`](../protocol/). Client data Models
and adapters consume that contract without redefining it.

## SDK integration

The app consumes the SDK's `hosts`, `connections`, `pairing`, and `currentHost` API groups from one
shared [`DovahLinkClient`](../sdk/README.md). The `features/connection/` area owns Host selection
and navigation while mirroring the SDK's Known Host and candidate streams. Candidate membership and
identity reconciliation stay in the SDK. Initial connection retries, bounded established-session
recovery, protocol handling, and live-state synchronization also remain SDK-owned.

The `features/live_state/` boundary consumes the SDK's eight public gameplay streams. SDK public
domain models remain canonical, and `StateSynchronization<T>` remains the canonical synchronization
truth. Redux carries those values unchanged without redefining or copying their domain fields.
Presentation-specific models may be derived later only when they add real UI semantics. Typed Redux
actions, reducers, selectors, and the Session Overview ViewModel carry those values to presentation.
The integration preserves synchronization status and authority/context metadata, nullable Location
facts, independent Supernatural Traits, Skyrim calendar fields, and the complete plural quest list.
The middleware requests desired state only after SDK trust is established and keeps observation tied
to the admitted session rather than the Session Shell route. Returning to Connections leaves the
admitted session and its state observation active. Ordinary reconnect and administrative recovery
remain SDK-owned; Flutter projects the SDK's status transitions and clears its live-state slice when
the session ends or is invalidated. The Phase 5.4 visible proof surface has not been delivered. Its
acceptance still includes XP, Vitals, and Level values; unavailable/stale/recovering states;
compatibility and connection lifecycle; and slow-consumer diagnostics, which the current public SDK
does not expose. PR #121 is the next intended task for prototype Overview convergence; Stage 8
remains planned.

The SDK pairing group also owns authentication, pending-confirmation recovery, and the
confirmation/credential-acknowledgement sequence; Flutter maps typed results for presentation.
Flutter conventions point to [`ai/context/sdk/`](../ai/context/sdk/) for SDK-owned protocol behavior
rather than duplicating it in the app.

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
