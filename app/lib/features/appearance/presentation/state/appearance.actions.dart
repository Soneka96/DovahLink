import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// Selects [preset] as the active DovahLink theme, both applying it immediately and requesting
/// that it be persisted.
class ThemePresetSelectedAction extends Equatable {
  /// Creates a theme-preset selection action.
  const ThemePresetSelectedAction(this.preset);

  /// The preset the user selected.
  final DovahThemePreset preset;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [preset];
}
