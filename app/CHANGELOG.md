# Flutter app changelog

Notable changes to the Flutter client are recorded here, most recent first. Add entries to
`[Unreleased]` in the pull request that makes them, grouped under `Added`/`Changed`/`Fixed`/
`Removed`/`Security` (omit unused subsections). State the user-visible outcome in one concise
sentence; split unrelated outcomes into separate bullets.

Repository releases share root `VERSION`. When an app change is included in a release, its
`[Unreleased]` entries are promoted into a dated version section in that release PR. See
[`ai/context/common.md`](../ai/context/common.md)'s "Versioning" for the release workflow.

## [Unreleased]

## [0.4.0] - 2026-09-23

### Fixed

- The app no longer keeps observing a stale connection status after its session is invalidated.
