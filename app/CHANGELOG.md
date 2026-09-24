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

- The app's shared interface now renders in the Frostbound, Dovah, or Hearth style.
- The Connections screen has an appearance button in its header that opens the theme picker.
- The Connections screen shows a Discover Skyrim button, disabled until Host discovery exists.
- The selected theme is saved locally and restored after relaunch.
- Selecting a Host on the Connections screen opens a themed pairing dialog for that Host, showing
  each pairing step: connecting, waiting for Skyrim, requesting a code, entering it, confirming,
  and success or failure with the reason.
- Pairing code entry shows one box per digit of the 6-digit code, and Pair stays disabled until the
  code is complete.
- The pairing dialog's title follows the step: the Host name while pairing, Skyrim isn’t running
  while the Host is unreachable, Pairing required when trust changed, and Connected once paired.
- When a Host's trust was removed or is no longer recognized, the pairing dialog explains why and
  asks you to confirm Pair again before requesting a new code.

### Changed

- The start screen is now the branded Connections screen, listing each Host as a themed card, in
  place of the plain Host list.
- Pairing connects to the Host you selected instead of always the default Host.
- Dialogs use the prototype's darker backdrop, header rule, and size limits, with roomier padding
  and larger pairing content on taller windows and tighter spacing on short ones.
- Pairing asks Skyrim for the code as soon as you select a Host, instead of waiting for you to
  request it.
- Pairing no longer asks for a device name.
- The Connections screen follows the prototype's narrow and short-window layouts: tighter margins,
  header, title, and cards, no detail column below 900 px wide, and no footer note below 620 px
  tall.
- Connection cards use each theme's own padding, height, icon tile, and bevel.

### Fixed

- Blocked devices no longer see an option to pair again until an administrator unblocks them.
- Host code-rejection messages clear after the player edits the pairing code.
- Rapid theme changes persist in selection order so an older preset cannot replace the latest one.
- Dialogs dismiss when the user taps their backdrop.
- Appearance storage failures no longer prevent startup; the default theme is used when
  preferences are unavailable.
- Interactive theme, connection, and action controls expose a single, exact screen-reader label.
- The shared theme tokens preserve every prototype surface color and the approved focus halo color.
- Themed buttons respect reduced-motion settings.
- Dialogs use each theme's own backdrop color and blur, and dialog and button padding no longer
  change with the theme.
- Hearth panels, dialogs, and primary buttons use the prototype's corner radii.
- The wordmark's LINK half, its tagline, offline connection state, and pairing icon tiles use each
  theme's own colors.

### Removed

- The standalone pairing screen and its route; pairing now happens in a dialog on Connections.

## [0.4.0] - 2026-09-23

### Fixed

- The app no longer keeps observing a stale connection status after its session is invalidated.
