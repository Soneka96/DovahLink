# Flutter app changelog

Notable changes to the Flutter client are recorded here, most recent first. Add entries to
`[Unreleased]` in the pull request that makes them, grouped under `Added`/`Changed`/`Fixed`/
`Removed`/`Security` (omit unused subsections). State the user-visible outcome in one concise
sentence; split unrelated outcomes into separate bullets.

Repository releases share root `VERSION`. When an app change is included in a release, its
`[Unreleased]` entries are promoted into a dated version section in that release PR. See
[`ai/context/common.md`](../ai/context/common.md)'s "Versioning" for the release workflow.

## [Unreleased]

### Added

- The app's shared interface now renders in the Frostbound, Dovah, or Hearth style; theme selection
  is not yet reachable through normal navigation.
- The selected theme is saved locally and restored after relaunch.

### Fixed

- Appearance storage failures no longer prevent startup; the default theme is used when
  preferences are unavailable.
- Interactive theme, connection, and action controls expose a single, exact screen-reader label.
- The shared theme tokens preserve every prototype surface color and the approved focus halo color.
- Themed buttons respect reduced-motion settings.

## [0.4.0] - 2026-09-23

### Fixed

- The app no longer keeps observing a stale connection status after its session is invalidated.
