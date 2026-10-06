# Host and Adapter changelog

Notable changes to the C# Host and native Skyrim Adapter are recorded here, most recent first. Add
entries to `[Unreleased]` in the pull request that makes them, grouped under
`Added`/`Changed`/`Fixed`/`Removed`/`Security` (omit unused subsections). State the outcome in one
concise sentence; split unrelated outcomes into separate bullets.

The Host/Adapter package is versioned by root `VERSION`. A release promotes its accumulated
`[Unreleased]` entries into a dated version section and leaves a fresh empty `[Unreleased]` section
at the top, in the same release PR that synchronizes the version literals. This file is the source
for the player-facing summary posted with each Nexus Mods package. See
[`ai/context/common.md`](../ai/context/common.md)'s "Versioning" for the release workflow and
[`tooling/DovahLinkBuilder/README.md`](../tooling/DovahLinkBuilder/README.md) for packaging.

## [Unreleased]

### Added

- Host publishes the complete, localized tracked quest and current objective collection as synchronized Snapshot state.
- Host publishes Character Identity and Supernatural Traits as independently synchronized Snapshot state areas.
- The connection handshake now exposes the stable Host installation ID and current OS computer name.
- The loopback listener exposes a bounded, sessionless Host metadata probe for discovery and presence checks.

### Changed

- Tracked-quest Snapshot collection now returns unavailable after five seconds so a delayed page sequence cannot keep resynchronization pending.
- Adapter logs now suppress routine capture and successful setup messages while retaining one startup status and 30-second rate-limited capacity warnings.
- Host publishes current and effective maximum Health, Magicka, and Stamina as one coherent
  `character_vitals` snapshot instead of three scalar state areas.
- Pairing outcomes now expose Host-calculated attempts remaining and the committed renotify cooldown.
- Raw public connection capacity is bounded separately from authenticated session capacity.

### Fixed

- Play-context and authority boundaries now purge queued state and send generic revision-zero unavailable baselines before new-identity state.
- Host state timestamps now use the canonical UTC RFC 3339 wire format accepted by the Dart SDK.
- State Snapshots and Events now retain the authority identity captured with each publication if authority rotates during encoding.

## [0.5.0] - 2026-09-24

### Changed

- Host subscription updates now replace the active state-area set and stop publishing removed
  areas; this contract requires Host `0.5.x` because released Host `0.4.0` applied updates
  additively.

### Fixed

- Host now terminates a pending snapshot request after its state area is unsubscribed.

## [0.4.0] - 2026-09-23

### Added

- A local development setup guide and prerequisite checker that reports missing CI tools with
  install and verification guidance.
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

- Local CI uses its selected Python and exact pinned clang-format executable, and reports
  incompatible Visual Studio copies.
- Trust-admin list-scope vocabulary is now known/trusted/blocked consistently across console
  admin, Host, and Adapter.
- `tooling/DovahLinkBuilder` now packages the Host/Adapter distribution instead of the retired
  Bridge ZIP.
- Host composition now resolves every service through dependency injection instead of manual
  object-graph assembly in `Program.cs`.
- The Adapter's CommonLibSSE-NG dependency now tracks `alandtse/CommonLibSSE-NG`
  (GPL-3.0-or-later) instead of the `CharmedBaryon/CommonLibSSE` fork, to pick up its 1.7.x offset
  and Address Library V5 fixes.
- The Adapter now targets Steam Skyrim Special Edition `1.7.104` with SKSE64 `2.3.1`, replacing
  the previous exact-match target of `1.6.1170` / SKSE `2.2.6`.

### Fixed

- The Adapter embeds its formatting and logging libraries so SKSE can load it without locating
  separate `fmt` and `spdlog` DLLs.
- Local CI now falls back to standard locations and `PATH` when tool-path overrides are unset.
- The Builder now discovers non-standard Visual Studio installations and uses that installation's
  CMake and Ninja executables for Adapter builds.
- The Builder now uses its saved Skyrim install path to locate the Papyrus compiler, with
  environment and standard Steam paths as fallbacks.

### Removed

- The native Bridge (`bridge/`) and its CI/tooling wiring, superseded by the Host/Adapter
  implementation.
