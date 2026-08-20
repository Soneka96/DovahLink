# Changelog

All notable changes to the DovahLink Bridge are documented here, most recent first. This is the
developer-facing record of what changed and why; the shorter, player-facing summary posted with
each Nexus Mods file upload is derived from these entries but is not identical to them.

A release is cut by building the versioned ZIP with `tooling/BridgeBuilder` and uploading it to
Nexus Mods manually; see [`tooling/BridgeBuilder/README.md`](tooling/BridgeBuilder/README.md).
This file is updated in the same change that bumps `bridge/vcpkg.json`'s `version-string` and flips
the corresponding `ROADMAP.md` phase to Complete.

## [0.3.1] - 2026-08-19

### Added

- Countdown display showing remaining time on an active pairing code (`roadmap/03-local-device-pairing-and-reconnection.md`, Phase 3.1, "Live Pairing Challenge UX").
- "Show code again" operation to redisplay the pairing code in-game without generating a new challenge.
- Cancellation operation to explicitly end an in-progress pairing attempt and return to idle state.
- Automatic code redisplay and rate-limiting on wrong-code attempts: at most one validation per second, at most 5 wrong codes before cancellation.
- Grace period on disconnect: 10 seconds to reconnect and resume an active challenge without losing code/expiry; longer disconnects cleanly cancel and free the slot.
- Pending-credential expiry: issued credentials not acknowledged within 5 minutes are destroyed, returning pairing to idle.

## [0.3.0] - 2026-08-18

### Added

- Local device pairing: short-lived six-digit code displayed in Skyrim, one-time validation, and atomic trust bootstrap (`roadmap/03-local-device-pairing-and-reconnection.md`, Phase 3, "Local Device Pairing and Reconnection").
- Persistent per-user trust: successful pairing binds a strong credential to a `clientId`, survives Skyrim/Bridge/Windows restarts, and is scoped to the Windows user profile rather than the modpack or `bridgeInstanceId`.
- Recoverable pairing semantics: client persists credential and recovery state before final confirmation; recovery from `confirming` state reuses the existing credential and treats an `already_trusted` outcome as success.
- Trust administration: dedicated trust-store abstraction with list/revoke/reset operations reusable across console commands, Flutter UI, and developer tooling.
- Immediate revocation: revoking a trusted client disconnects its active sessions, invalidates its credential, and rejects reconnection with a specific `revoked` outcome.
- Device administration: each trusted client receives a five-digit `shortId` (stable across all trust state transitions) and optional `displayName`, never used for authentication or authorization.
- WebSocket-native connection liveness: Ping/Pong and bounded idle timeout replace guessing or invented application heartbeat.
- Automatic reconnection: paired clients reconnect without a new code, in a fresh session with fresh `sessionId`, after transport loss, restart, or Bridge restart.

## [0.2.0] - 2026-08-15

### Fixed

- The bridge and client now correctly detect a new authoritative state identity after a bridge
  restart, so cached character state from a previous bridge lifetime can no longer be presented as
  current (`roadmap/02-bridge-identity-and-authoritative-state.md`, Phase 2, "Bridge Identity and Authoritative State Foundation").
- Revisions now advance only when authoritative state actually changes; repeated unchanged snapshot
  requests reuse the existing revision instead of manufacturing a new one.
- A client reconnect now preserves the authoritative revision it left off at, instead of resetting
  it. This is about revision continuity across a reconnect, not the separate one-time-token
  limitation below, which still requires a bridge restart before a second successful session.

### Changed

- Replaced the independent protocol-generation (`protocolVersion`) negotiation model with
  Bridge-version compatibility: `hello_ack` now reports `bridgeVersion` directly, and clients check
  it against their own declared supported range instead of negotiating a shared version with the
  bridge. See [`ai/context/protocol/compatibility.md`](ai/context/protocol/compatibility.md).

## [0.1.0] - 2026-08-12

### Added

- First public release: local, authenticated connection between Skyrim and one external client.
- Read-only character state exposing the player's current level.
- Explicit handling for unsupported Skyrim/SKSE runtimes and failed connections.
- A Vortex-ready installation package.

### Known limitations

- Reconnecting a client that already completed one successful session requires restarting the
  bridge; the one-time bootstrap token is single-use for the bridge's lifetime. Planned fix:
  `roadmap/03-local-device-pairing-and-reconnection.md`, Phase 3, "Local Device Pairing and Reconnection".
