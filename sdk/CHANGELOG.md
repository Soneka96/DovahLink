# Dart SDK changelog

Notable changes to supported client SDKs are recorded here, most recent first. Add entries to
`[Unreleased]` in the pull request that makes them, grouped under `Added`/`Changed`/`Fixed`/
`Removed`/`Security` (omit unused subsections). State the consumer-visible outcome in one concise
sentence; split unrelated outcomes into separate bullets.

Repository releases share root `VERSION`. When an SDK change is included in a release, its
`[Unreleased]` entries are promoted into a dated version section in that release PR. See
[`ai/context/common.md`](../ai/context/common.md)'s "Versioning" for the release workflow.

## [Unreleased]

### Added

- The SDK handshake result exposes the stable Host installation ID and current OS computer name.
- The Dart SDK discovers the local loopback Host with an isolated unpaired handshake and returns the responding peer's validated Host ID and name claims with its endpoint; discovery does not authenticate Host identity.
- DovahLinkConnectionException preserves an HTTP status when a peer rejects the WebSocket upgrade during discovery.

### Changed

- The Dart SDK exposes Host-reported pairing cooldowns and remaining wrong-code attempts as typed metadata.
- Windows DPAPI storage is available through a Windows-specific entry point, while the shared SDK entry point stays platform-neutral.

### Fixed

- Interrupted authentication consistently reports cancellation when storage or Host operations fail.
- Explicit disconnect cancels pending automatic reconnect retries and ignores recovery outcomes
  that arrive afterward.
- Explicit disconnect invalidates an ordinary-loss recovery handoff while teardown is still in
  progress.
- Explicit disconnect cancels authentication recovery before a rejected credential can trigger a
  second connection attempt.

## [0.5.0] - 2026-09-24

### Added

- The Dart SDK exposes typed per-domain subscription intent and sends complete desired state-area
  sets to the Host.
- The Dart SDK restores desired subscriptions after trusted recovery and clears intent on explicit
  disconnect.
- The Dart SDK exposes replayable typed synchronization streams for character XP, health, magicka,
  stamina, and level.
- The Dart SDK requires Host `0.5.x` for its complete-set subscription API and rejects released
  Host `0.4.0` before admitting a session.

### Fixed

- The Dart SDK retries temporarily unavailable state Snapshots without interrupting the session.
- The Dart SDK routes later baseline snapshots even when they reuse a completed request's
  correlation ID.
- The Dart SDK ignores in-flight recovery outcomes after their state area is unsubscribed.

## [0.4.0] - 2026-09-23

### Fixed

- The SDK stamps outgoing envelopes with the resolved `clientId` and fails fast when a required
  `clientId` cannot be resolved, instead of proceeding silently.
