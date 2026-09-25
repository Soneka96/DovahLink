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

- Add reusable layered theme materials, atmosphere recipes, and their rendering primitives.
- Connection cards rise and take the raised material when hovered, and wear each theme's
  decoration: Frostbound's fracture lines and available edge, Dovah's ember-to-ice link line, and
  a faint corner outline on both.
- Appearance picker cards show each theme's name, summary, and materials, and a badge on the
  active theme, in a three-column grid that rises on hover.
- The header sigil is dressed per theme: muted with a faint glow in Frostbound, a wider glow in
  Dovah, and warmed onto a pale disc in Hearth.
- The app names the design's fonts (Inter for text, and per theme Arial Narrow, Georgia, or their
  fallbacks for headings). No font is bundled, so the typeface shown depends on the fonts
  installed on the computer.

### Changed

- Custom Dovah controls suppress Material splash and state overlays.
- Themed material textures reuse rasterized tile images across repaints.
- Preset theme endpoints are built together at startup and reused during later rebuilds.
- Panels, cards, buttons, and icon tiles use each theme's layered material texture: scratched
  iron in Frostbound, forged steel in Dovah, and pressed parchment in Hearth.
- The app background reproduces each theme's atmosphere, including its image treatment, glows, and
  fine haze, and the haze now fades out toward the bottom.
- Dialogs blur and re-color the page behind them for each theme.
- Appearance picker previews show each theme's own scene, sigil, and accent bars at the approved
  height.
- Dovah's connection icon is a diamond with its computer glyph upright, and Hearth's icon glyph
  takes its own brown.
- Secondary buttons, icon buttons, the pairing mark, and pairing code boxes are plain boxes rounded
  by the theme, as designed, and use the theme's control texture.
- The focused control's outline is 2px in the theme's accent, 3px outside the control. Bevelled
  cards and primary buttons keep it visible for keyboard users.
- The focused pairing code box keeps its border and gains a soft halo.
- Pairing's "Send Code Again" is a quiet text button, and only a disabled primary button dims.

### Fixed

- An open dialog now updates its backdrop as the application theme changes.
- Bevelled panels, cards, and buttons no longer draw a border line along the bevel.
- Canvas backgrounds now render each theme's complete atmosphere recipe, including its image
  treatment and haze layers.
- Pairing is clearly unavailable on platforms without secure client storage, and authentication is skipped there.

## [0.5.0] - 2026-09-24

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
