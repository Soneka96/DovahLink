# Changelog

All notable changes to the DovahLink Bridge are documented here, most recent first. This is the
developer-facing record of what changed and why; the shorter, player-facing summary posted with
each Nexus Mods file upload is derived from these entries but is not identical to them.

A release is cut by building the versioned ZIP with `tooling/BridgeBuilder` and uploading it to
Nexus Mods manually; see [`tooling/BridgeBuilder/README.md`](tooling/BridgeBuilder/README.md).
This file is updated in the same change that bumps `bridge/vcpkg.json`'s `version-string` and flips
the corresponding `ROADMAP.md` phase to Complete.

## [0.2.0] - 2026-08-15

### Fixed

- The bridge and client now correctly detect a new authoritative state identity after a bridge
  restart, so cached character state from a previous bridge lifetime can no longer be presented as
  current (`ROADMAP.md` Phase 2, "Bridge Identity and Authoritative State Foundation").
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
  `ROADMAP.md` Phase 3, "Local Device Pairing and Reconnection".
