import 'dart:ui' show lerpDouble;

import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';

/// The session-shell measurements each theme pins differently, as a [ThemeExtension] so Flutter's
/// [ThemeData] transition interpolates them alongside the colors instead of snapping them. Only
/// the navigation height varies by theme; every other session-shell measurement varies by window
/// alone. Every value is the theme's prototype-exact value for each window mode, with no
/// dependence on the window itself; `DovahSessionMetrics.forWindow` selects the mode for the
/// current window.
@immutable
class DovahSessionThemeMetrics extends ThemeExtension<DovahSessionThemeMetrics>
    with Equatable {
  /// Frostbound's values, from the prototype's `index.html` media queries and `themes.css`
  /// per-theme overrides: it pins the navigation height at its compact value.
  static const DovahSessionThemeMetrics frostbound = DovahSessionThemeMetrics(
    regularNavHeight: 45,
    compactNavHeight: 45,
  );

  /// The Dovah preset's values, from the prototype's `index.html` media queries and `themes.css`
  /// per-theme overrides.
  static const DovahSessionThemeMetrics dovah = DovahSessionThemeMetrics(
    regularNavHeight: 53,
    compactNavHeight: 45,
  );

  /// Hearth's values, from the prototype's `index.html` media queries and `themes.css` per-theme
  /// overrides.
  static const DovahSessionThemeMetrics hearth = DovahSessionThemeMetrics(
    regularNavHeight: 53,
    compactNavHeight: 45,
  );

  /// Height of the navigation in a regular or narrow window; the prototype does not change it at
  /// the narrow width.
  final double regularNavHeight;

  /// Height of the navigation in a compact-height window.
  final double compactNavHeight;

  /// Creates a complete set. Every value is required so a set cannot be assembled with an
  /// accidentally-inherited default.
  const DovahSessionThemeMetrics({
    required this.regularNavHeight,
    required this.compactNavHeight,
  });

  /// Returns a copy with the given values replaced.
  @override
  DovahSessionThemeMetrics copyWith({
    double? regularNavHeight,
    double? compactNavHeight,
  }) => DovahSessionThemeMetrics(
    regularNavHeight: regularNavHeight ?? this.regularNavHeight,
    compactNavHeight: compactNavHeight ?? this.compactNavHeight,
  );

  /// Interpolates every value; each is a continuous measurement with no discrete counterpart.
  @override
  DovahSessionThemeMetrics lerp(
    ThemeExtension<DovahSessionThemeMetrics>? other,
    double t,
  ) {
    if (other is! DovahSessionThemeMetrics) {
      return this;
    }
    return DovahSessionThemeMetrics(
      regularNavHeight: lerpDouble(
        regularNavHeight,
        other.regularNavHeight,
        t,
      )!,
      compactNavHeight: lerpDouble(
        compactNavHeight,
        other.compactNavHeight,
        t,
      )!,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [regularNavHeight, compactNavHeight];
}
