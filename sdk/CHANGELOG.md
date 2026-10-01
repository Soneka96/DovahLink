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

- The SDK checks restored Known Host presence on startup and refreshes it while the client remains open, separately from session connection state.
- `DovahLinkClient.knownHostsChanges` emits the complete persisted Known Hosts view after commits.
- The SDK keeps each Host's current bearer credential and pending pairing recovery scoped to that Host ID.
- Known Host authentication resolves its endpoint and credential inside the SDK from `DovahLinkHostId`.
- The SDK handshake result exposes the stable Host installation ID and current OS computer name.
- The Dart SDK discovers the local loopback Host through a bounded sessionless metadata probe; its Host ID claim remains unauthenticated.
- The SDK exposes the shared Host presence probe for local discovery and Known Host reachability.
- `DovahLinkClient.discoverHosts()` and `candidateHostsChanges` expose SDK-reconciled, runtime-only candidates.
- `DovahLinkKnownHostState` exposes the exact Known Host session lifecycle separately from reachability.
- `DovahLinkClient.close()` stops background presence monitoring and releases SDK-owned subscriptions.
- DovahLinkConnectionException preserves an HTTP status when a peer rejects the metadata probe.

### Changed

- Initial candidate and Known Host connection failures now retry in the SDK every three seconds, independently from bounded established-session recovery; the previous Offline presentation is preserved.
- Discovery reconciles claims with committed Known Hosts by normalized Host ID; candidates are never persisted.
- Pairing credentials no longer leave the SDK API, and candidate authentication never selects a Known Host credential.
- Persisted client state moves to format 3; unreleased singleton bearer state requires pairing again.
- The Dart SDK exposes Host-reported pairing cooldowns and remaining wrong-code attempts as typed metadata.
- Windows DPAPI storage is available through a Windows-specific entry point, while the shared SDK entry point stays platform-neutral.

### Fixed

- Pending discovery checks terminal shutdown after storage and probe awaits, preventing late
  subscriptions, probes, and candidate updates.
- Pairing commits remove a Host from candidates, and stale discovery results cannot restore it.
- `DovahLinkClient.close()` starts session teardown alongside monitor cleanup and continues after independent cleanup failures.
- Deliberate disconnect preserves Known Host reachability evidence while ending its session.
- Periodic Known Host refresh keeps the previous availability while its probe is pending.
- Host UUID casing is canonicalized across persisted Known Hosts, pairing recovery, and authentication.
- Pending pairing recovery fails closed before session admission when `hello_ack` reports a
  different Known Host, and automatic reconnect treats that identity mismatch as terminal.
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
