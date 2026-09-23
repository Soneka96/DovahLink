import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Static selectors over [AppState] for appearance presentation state.
abstract final class AppearanceSelectors {
  /// Returns the currently active theme preset.
  static DovahThemePreset activePresetSelector(AppState state) =>
      state.appearance.activePreset;

  /// Returns the complete [ThemeData] for the currently active theme preset.
  static ThemeData activeThemeDataSelector(AppState state) =>
      dovahThemeDataFor(activePresetSelector(state));
}
