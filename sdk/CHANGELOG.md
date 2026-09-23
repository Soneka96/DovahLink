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

- The Dart SDK exposes replayable typed synchronization streams for character XP, health, magicka,
  stamina, and level.
- The Dart SDK rejects Host versions outside its declared `0.4.x` compatibility range before
  admitting a session.

## [0.4.0] - 2026-09-23

### Fixed

- The SDK stamps outgoing envelopes with the resolved `clientId` and fails fast when a required
  `clientId` cannot be resolved, instead of proceeding silently.
