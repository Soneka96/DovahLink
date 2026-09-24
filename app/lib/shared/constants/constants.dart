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

/// Height of an appearance-preset preview.
const double appearancePreviewHeight = 48;

/// Minimum width of an appearance-preset card before it wraps to another row.
const double appearancePresetCardMinimumWidth = 160;

/// Height of the accent strip in an appearance-preset preview.
const double appearancePreviewAccentHeight = 6;

/// Size of the selected-preset indicator.
const double appearanceSelectionIconSize = 20;

// ---- Pairing ----

/// The number of digits in a pairing code. Matches the Host's own
/// `Constants.PairingChallengeCodeDigits`, which rejects a `pairing_confirm` code of any other
/// length; the approved prototype's five-digit boxes predate that contract.
const int pairingCodeLength = 6;
