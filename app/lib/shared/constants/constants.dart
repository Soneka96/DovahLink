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
