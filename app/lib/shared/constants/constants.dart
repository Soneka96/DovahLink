/// Every small cross-cutting constant value in the app lives here, regardless of which feature
/// uses it, grouped by the area it belongs to.
library;

import 'package:dovahlink_client/shared/constants/enums.dart';

// ---- Hosts ----

/// The sole entry in the static Host list until Host discovery exists (matches the retired
/// native plugin's original default loopback port).
final Uri defaultHostUri = Uri.parse('ws://127.0.0.1:58231/');

// ---- Appearance ----

/// The theme preset used when nothing is persisted yet, or a persisted value is no longer
/// recognized -- the approved prototype's own default (`localStorage.getItem('dovahlink-preset')
/// || 'dovah'`).
const DovahThemePreset defaultThemePreset = DovahThemePreset.dovah;

/// Minimum width of an appearance-preset card before it wraps to another row.
const double appearancePresetCardMinimumWidth = 160;

/// Height of each accent bar in an appearance-preset preview (the prototype's `.preset-ui i`).
const double appearancePreviewAccentHeight = 6;

/// Width and height of the sigil tile in an appearance-preset preview, border and padding included
/// (the prototype's `.preset-sigil`).
const double appearancePreviewSigilSize = 45;

/// Padding between the sigil tile's border and its mark (the prototype's `.preset-sigil`).
const double appearancePreviewSigilPadding = 7;

/// Distance of the accent bars from the preview's left and right edges (the prototype's
/// `.preset-ui`).
const double appearancePreviewBarsInset = 12;

/// Distance of the accent bars from the preview's bottom edge (the prototype's `.preset-ui`).
const double appearancePreviewBarsBottom = 10;

/// Gap between adjacent accent bars (the prototype's `.preset-ui`).
const double appearancePreviewBarsGap = 4;

/// The relative widths of the three accent bars, in hundredths (the prototype's `.preset-ui`
/// `grid-template-columns:1.4fr .8fr .45fr`).
const List<int> appearancePreviewBarFlexes = [140, 80, 45];

/// Size of the selected-preset indicator.
const double appearanceSelectionIconSize = 20;

// ---- Theme assets ----

/// The Frostbound preset's environment image, used by its canvas atmosphere and its appearance
/// preview.
const String frostboundEnvironmentAsset =
    'assets/themes/frostbound/frostbound-environment.png';

/// The Dovah preset's scene artwork. Dovah's canvas has no environment image; this art is used by
/// its appearance preview.
const String dovahConnectionHeroAsset =
    'assets/themes/dovah/dovahlink-connection-hero.png';

/// The Hearth preset's environment image, used by its canvas atmosphere and its appearance
/// preview.
const String hearthEnvironmentAsset =
    'assets/themes/hearth/hearth-environment.png';

// ---- Pairing ----

/// The number of digits in a pairing code. Matches the Host's own
/// `Constants.PairingChallengeCodeDigits`, which rejects a `pairing_confirm` code of any other
/// length; the approved prototype's six `.otp` boxes match that contract.
const int pairingCodeLength = 6;
