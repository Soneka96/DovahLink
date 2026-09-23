import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// Parameters for persisting the active DovahLink theme preset.
@immutable
class SetThemePresetParams extends Equatable {
  /// Creates theme-preset persistence parameters.
  const SetThemePresetParams({required this.preset});

  /// The preset to persist as the active theme.
  final DovahThemePreset preset;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [preset];
}
