# Historical project changelog

This frozen combined project history is preserved through release `0.4.0`. New changes are recorded in
the component changelogs: [Flutter app](app/CHANGELOG.md), [Dart SDK](sdk/CHANGELOG.md), and
[Host/Adapter](host/CHANGELOG.md). The Host/Adapter changelog is the source for the shorter,
player-facing summary posted with each Nexus Mods file upload.

No new entries are added here. Keep the historical release sections below unchanged.

### Added

- The Dart SDK rejects Host versions outside its declared `0.4.x` compatibility range before
  admitting a session.
- Flutter client visual foundation adds the Frostbound, Dovah, and Hearth presets with shared
  themed components and locally persisted appearance state; the picker is not yet exposed through
  normal app navigation.

### Fixed

- Themed buttons respect reduced-motion settings, and failed appearance writes are reported as errors.

## [0.4.0] - 2026-09-23

### Added

- A local development setup guide and prerequisite checker that reports missing CI tools with install and verification guidance.
- Standalone C# Host process and thin native Adapter, replacing the native Bridge as DovahLink's
  production implementation.
- Host-owned public client boundary: WebSocket admission, session registry, and pairing continue on
  the Host after the migration.
- Private, bounded IPC channel between the Adapter and Host carrying pairing, trust administration,
  and Skyrim-facing notifications.
- Host-owned state subscriptions with baseline snapshots, bounded delivery, play-context recovery,
  and validated real Skyrim capture for health, magicka, stamina, XP, and level.
- Reserved control and data outbound lanes so state publication cannot starve control traffic.
- Dual licensing: the repository root and every component except `adapter/` are now licensed under
  the PolyForm Noncommercial License 1.0.0 (`LICENSE`); `adapter/` is licensed separately under
  GPL-3.0-or-later (`adapter/LICENSE`) because it links CommonLibSSE-NG.

### Changed

- Local CI uses its selected Python and exact pinned clang-format executable, and reports incompatible Visual Studio copies.
- Trust-admin list-scope vocabulary is now known/trusted/blocked consistently across console admin,
  Host, and Adapter.
- `tooling/DovahLinkBuilder` now packages the Host/Adapter distribution instead of the retired
  Bridge ZIP.
- Host composition now resolves every service through dependency injection instead of manual
  object-graph assembly in `Program.cs`.
- The Adapter's CommonLibSSE-NG dependency now tracks `alandtse/CommonLibSSE-NG` (GPL-3.0-or-later)
  instead of the `CharmedBaryon/CommonLibSSE` fork, to pick up its 1.7.x offset and Address Library
  V5 fixes.
- The Adapter now targets Steam Skyrim Special Edition `1.7.104` with SKSE64 `2.3.1`, replacing the
  previous exact-match target of `1.6.1170` / SKSE `2.2.6`.

### Fixed

- The Adapter embeds its formatting and logging libraries so SKSE can load it without locating separate `fmt` and `spdlog` DLLs.
- Local CI now falls back to standard locations and `PATH` when tool-path overrides are unset.
- The Builder now discovers non-standard Visual Studio installations and uses that installation's
  CMake and Ninja executables for Adapter builds.
- The Builder now uses its saved Skyrim install path to locate the Papyrus compiler, with environment
  and standard Steam paths as fallbacks.
- The SDK now stamps outgoing envelopes with the resolved `clientId` and fails fast when a required
  `clientId` cannot be resolved, instead of proceeding silently.
- The app no longer keeps observing a stale connection status after its session is invalidated.

### Removed

- The native Bridge (`bridge/`) and its CI/tooling wiring, superseded by the Host/Adapter
  implementation.

## [0.3.3] - 2026-08-24

### Added

- Typed per-message protocol DTOs (structural JSON serialization plus handwritten semantic
  validation) for every connection, pairing, capabilities, subscription-control, state, error, and
  invalidation message, replacing raw hand-rolled JSON handling across the Bridge, Dart SDK, and
  .NET validation client.
- Canonical cross-side fixtures for every redesigned message family.

### Removed

- The `character` state area (level/health/magicka/stamina bundled into one nullable aggregate).
  Every `subscribe`/`snapshot_request` is now explicitly rejected and both endpoints' `capabilities`
  lists are empty until a future phase registers a real state domain.

### Fixed

- Pairing status, pairing outcome, and error payloads now validate their full field-presence and
  value-vocabulary rules on decode instead of trusting individual fields in isolation.
- State-event and state-snapshot payloads enforce strict UTC timestamps and reject negative or
  backwards revisions.
- Protocol revisions are validated against the contract's 64-bit range, and capability instances and
  protocol identifier lists are validated before encoding.

## [0.3.2] - 2026-08-20

### Added

- Unified Known Device administration with stable identities, state-aware listing, rename, revoke,
  block, unblock, forget, Reset Trust, and confirmation-gated Factory Reset operations.
- Canonical `session_invalidated` events for administrative session termination, with typed reasons
  for revoke, block, trust reset, and factory reset.

### Fixed

- Administrative invalidation now takes effect immediately and disconnects affected sessions without
  making event delivery a security dependency.
- Developer-token sessions remain outside Known Device block, revoke, and reset-trust semantics while
  Factory Reset still terminates active sessions as required.

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

- First development baseline: local, authenticated connection between Skyrim and one external
  client. This historical baseline was not a supported public release.
- Read-only character state exposing the player's current level.
- Explicit handling for unsupported Skyrim/SKSE runtimes and failed connections.
- A Vortex-ready installation package.

### Known limitations

- Reconnecting a client that already completed one successful session requires restarting the
  bridge; the one-time bootstrap token is single-use for the bridge's lifetime. Planned fix:
  `roadmap/03-local-device-pairing-and-reconnection.md`, Phase 3, "Local Device Pairing and Reconnection".
