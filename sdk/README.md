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

Stage 5 — Dart Client SDK Foundation is complete. The package and its persistence boundary were
pulled forward before the phase's formal start because Phase 3 (Local Device Pairing and
Reconnection), documented in `roadmap/03-local-device-pairing-and-reconnection.md`, needed them to
avoid a larger later migration. The real package exists at:

```text
sdk/
  dart/
    dovahlink_client/
```

It provides one client engine through four grouped views: `client.hosts`, `client.connections`,
`client.pairing`, and `client.currentHost`. These are facades over the same session, services,
storage, and mutable state; they do not create independent clients. The SDK also owns `clientId`,
Host-scoped credentials, pairing recovery, bounded established-session recovery, and plural Known
Host persistence behind `IClientStorage` (including Windows DPAPI through the
`dovahlink_client_windows.dart` entry point); see `ai/context/sdk/persistence.md`. The official
Flutter app consumes the same public API through `dovahlink_client_sdk`.

Local discovery is exposed by `client.pairing.discoverHosts()` and `client.pairing.candidates`.
Candidates are SDK-owned, runtime-only, and reconciled against committed Known Hosts. A discovered
Host ID is an unauthenticated claim. Candidate authentication uses the selected endpoint and never
uses the claim to select Known Host credentials, which remain SDK-owned. Discovery is loopback-only;
candidates are never persisted or discovered over LAN or mDNS.

The SDK owns the three-second initial connection retry policy. It remains separate from bounded
recovery after an established session loses transport. `client.pairing` sequences authentication
with pending-pairing recovery, and `confirmCode()` persists the credential and completes Host
acknowledgement as one SDK operation. Flutter maps typed results and failures into presentation; it
does not sequence protocol operations or own retry policy.

The app selects storage at its composition boundary. Windows uses DPAPI; other platforms currently
use an explicit unsupported-storage boundary, and pairing stays unavailable until secure storage is
implemented for that platform.

Current SDK source declares Host `0.5.x` and rejects older or newer Host versions during `hello`,
before admitting a session. Host `0.5.0` is an internal version; no supported Host/Client `0.5.0`
package was publicly released. Earlier internal builds that pass the `0.5.x` version check are not
compatibility targets under the repository's pre-release policy, and the current Host/SDK source pair
implements the current public SDK surface. Released Host `0.4.0` remains incompatible with the
Phase 5.3 complete-set subscription API.
Subscribe and unsubscribe calls update local desired intent before Host synchronization, so a failed
request does not necessarily roll back the change; retained intent may be synchronized on a later
trusted session, while intentional disconnect clears it.
Phase 5.2 is complete: `client.currentHost.character` exposes replayable typed streams for coherent
Vitals, XP, and Level domains, backed by the SDK's state models, revision tracking, Snapshot
handling, and Level Event handling. Phase 5.3 is complete: callers have
typed per-domain subscription intent, and the SDK restores the desired set after trusted recovery
while keeping it dormant after administrative invalidation. Phase 5.4 wires SDK streams through
Flutter middleware; Phase 5.5 completed the version-impact audit and closed Stage 5.

The app's `features/connection/` area remains responsible for Host selection, navigation, and
presentation. It mirrors Known Host and candidate state from the same persistent SDK client; the SDK
owns candidate membership and reconciliation.
See [`app/README.md`](../app/README.md) for the current division between app presentation and
SDK-owned communication.
