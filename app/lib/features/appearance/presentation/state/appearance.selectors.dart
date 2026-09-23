import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors over [AppState] for appearance presentation state.
abstract final class AppearanceSelectors {
  /// Returns the currently active theme preset.
  static DovahThemePreset activePresetSelector(AppState state) =>
      state.appearance.activePreset;
}
