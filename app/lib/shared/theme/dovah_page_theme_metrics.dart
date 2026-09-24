import 'dart:ui' show lerpDouble;

import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';

/// The game-page measurements each theme pins differently, as a [ThemeExtension] so Flutter's
/// [ThemeData] transition interpolates them alongside the colors instead of snapping them. Every
/// value is the theme's prototype-exact value for each window mode, with no dependence on the
/// window itself; `DovahPageMetrics.forWindow` selects the mode for the current window. Values that
/// vary only by window, never by theme, are not here.
@immutable
class DovahPageThemeMetrics extends ThemeExtension<DovahPageThemeMetrics>
    with Equatable {
  /// Frostbound's values, from the prototype's `index.html` media queries and `themes.css`
  /// per-theme overrides.
  static const DovahPageThemeMetrics frostbound = DovahPageThemeMetrics(
    regularContentTopPadding: 22,
    compactContentTopPadding: 22,
    regularIntroBottomGap: 14,
    compactIntroBottomGap: 14,
    introPadding: EdgeInsets.zero,
    regularPanelPadding: EdgeInsets.all(14),
    compactPanelPadding: EdgeInsets.all(14),
  );

  /// The Dovah preset's values, from the prototype's `index.html` media queries and `themes.css`
  /// per-theme overrides.
  static const DovahPageThemeMetrics dovah = DovahPageThemeMetrics(
    regularContentTopPadding: 28,
    compactContentTopPadding: 18,
    regularIntroBottomGap: 20,
    compactIntroBottomGap: 14,
    introPadding: EdgeInsets.zero,
    regularPanelPadding: EdgeInsets.all(18),
    compactPanelPadding: EdgeInsets.all(15),
  );

  /// Hearth's values, from the prototype's `index.html` media queries and `themes.css` per-theme
  /// overrides. Only Hearth boxes the page intro.
  static const DovahPageThemeMetrics hearth = DovahPageThemeMetrics(
    regularContentTopPadding: 28,
    compactContentTopPadding: 18,
    regularIntroBottomGap: 20,
    compactIntroBottomGap: 14,
    introPadding: EdgeInsets.symmetric(vertical: 10, horizontal: 13),
    regularPanelPadding: EdgeInsets.all(18),
    compactPanelPadding: EdgeInsets.all(15),
  );

  /// Padding above the page's content in a regular or narrow window; the prototype does not
  /// change it at the narrow width.
  final double regularContentTopPadding;

  /// Padding above the page's content in a compact-height window.
  final double compactContentTopPadding;

  /// Gap between the page intro and the content below it in a regular or narrow window.
  final double regularIntroBottomGap;

  /// Gap between the page intro and the content below it in a compact-height window.
  final double compactIntroBottomGap;

  /// Padding inside the page intro in every window mode: only Hearth boxes it, so it is zero
  /// elsewhere.
  final EdgeInsets introPadding;

  /// Padding inside a content panel in a regular or narrow window.
  final EdgeInsets regularPanelPadding;

  /// Padding inside a content panel in a compact-height window.
  final EdgeInsets compactPanelPadding;

  /// Creates a complete set. Every value is required so a set cannot be assembled with an
  /// accidentally-inherited default.
  const DovahPageThemeMetrics({
    required this.regularContentTopPadding,
    required this.compactContentTopPadding,
    required this.regularIntroBottomGap,
    required this.compactIntroBottomGap,
    required this.introPadding,
    required this.regularPanelPadding,
    required this.compactPanelPadding,
  });

  /// Returns a copy with the given values replaced.
  @override
  DovahPageThemeMetrics copyWith({
    double? regularContentTopPadding,
    double? compactContentTopPadding,
    double? regularIntroBottomGap,
    double? compactIntroBottomGap,
    EdgeInsets? introPadding,
    EdgeInsets? regularPanelPadding,
    EdgeInsets? compactPanelPadding,
  }) => DovahPageThemeMetrics(
    regularContentTopPadding:
        regularContentTopPadding ?? this.regularContentTopPadding,
    compactContentTopPadding:
        compactContentTopPadding ?? this.compactContentTopPadding,
    regularIntroBottomGap: regularIntroBottomGap ?? this.regularIntroBottomGap,
    compactIntroBottomGap: compactIntroBottomGap ?? this.compactIntroBottomGap,
    introPadding: introPadding ?? this.introPadding,
    regularPanelPadding: regularPanelPadding ?? this.regularPanelPadding,
    compactPanelPadding: compactPanelPadding ?? this.compactPanelPadding,
  );

  /// Interpolates every value; each is a continuous measurement with no discrete counterpart.
  @override
  DovahPageThemeMetrics lerp(
    ThemeExtension<DovahPageThemeMetrics>? other,
    double t,
  ) {
    if (other is! DovahPageThemeMetrics) {
      return this;
    }
    return DovahPageThemeMetrics(
      regularContentTopPadding: lerpDouble(
        regularContentTopPadding,
        other.regularContentTopPadding,
        t,
      )!,
      compactContentTopPadding: lerpDouble(
        compactContentTopPadding,
        other.compactContentTopPadding,
        t,
      )!,
      regularIntroBottomGap: lerpDouble(
        regularIntroBottomGap,
        other.regularIntroBottomGap,
        t,
      )!,
      compactIntroBottomGap: lerpDouble(
        compactIntroBottomGap,
        other.compactIntroBottomGap,
        t,
      )!,
      introPadding: EdgeInsets.lerp(introPadding, other.introPadding, t)!,
      regularPanelPadding: EdgeInsets.lerp(
        regularPanelPadding,
        other.regularPanelPadding,
        t,
      )!,
      compactPanelPadding: EdgeInsets.lerp(
        compactPanelPadding,
        other.compactPanelPadding,
        t,
      )!,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    regularContentTopPadding,
    compactContentTopPadding,
    regularIntroBottomGap,
    compactIntroBottomGap,
    introPadding,
    regularPanelPadding,
    compactPanelPadding,
  ];
}
