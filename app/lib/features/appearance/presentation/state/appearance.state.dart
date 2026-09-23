import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Immutable Redux state for the active DovahLink theme preset.
@immutable
class AppearanceState extends Equatable {
  /// Creates appearance state with an explicit active preset.
  const AppearanceState({required this.activePreset});

  /// Returns the initial appearance state with [defaultThemePreset] active.
  factory AppearanceState.initial() =>
      const AppearanceState(activePreset: defaultThemePreset);

  /// The currently active theme preset.
  final DovahThemePreset activePreset;

  /// Returns a copy with selected values replaced.
  AppearanceState copyWith({DovahThemePreset? activePreset}) =>
      AppearanceState(activePreset: activePreset ?? this.activePreset);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [activePreset];
}
