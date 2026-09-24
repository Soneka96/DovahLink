import 'dart:ui' show lerpDouble;

import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';

/// The root (Connections) screen measurements each theme pins differently, as a [ThemeExtension]
/// so Flutter's [ThemeData] transition interpolates them alongside the colors instead of snapping
/// them. Every value is the theme's prototype-exact value for each window mode, with no dependence
/// on the window itself; `DovahRootMetrics.forWindow` selects the mode for the current window.
/// Values that vary only by window, never by theme, are not here.
@immutable
class DovahRootThemeMetrics extends ThemeExtension<DovahRootThemeMetrics>
    with Equatable {
  /// Frostbound's values, from the prototype's `index.html` media queries and `themes.css`
  /// per-theme overrides.
  static const DovahRootThemeMetrics frostbound = DovahRootThemeMetrics(
    regularHeaderHeight: 70,
    compactHeaderHeight: 56,
    regularContentTopPadding: 20,
    narrowContentTopPadding: 20,
    compactContentTopPadding: 20,
    regularHeroBottomGap: 18,
    narrowHeroBottomGap: 18,
    compactHeroBottomGap: 18,
    regularPageTitleFontSize: 31,
    narrowPageTitleFontSize: 31,
    compactPageTitleFontSize: 31,
    brandTaglineLetterSpacingEm: 0.24,
  );

  /// The Dovah preset's values, from the prototype's `index.html` media queries and `themes.css`
  /// per-theme overrides.
  static const DovahRootThemeMetrics dovah = DovahRootThemeMetrics(
    regularHeaderHeight: 88,
    compactHeaderHeight: 62,
    regularContentTopPadding: 30,
    narrowContentTopPadding: 20,
    compactContentTopPadding: 14,
    regularHeroBottomGap: 28,
    narrowHeroBottomGap: 20,
    compactHeroBottomGap: 14,
    regularPageTitleFontSize: 34,
    narrowPageTitleFontSize: 28,
    compactPageTitleFontSize: 25,
    brandTaglineLetterSpacingEm: 0.2,
  );

  /// Hearth's values, from the prototype's `index.html` media queries and `themes.css` per-theme
  /// overrides.
  static const DovahRootThemeMetrics hearth = DovahRootThemeMetrics(
    regularHeaderHeight: 86,
    compactHeaderHeight: 62,
    regularContentTopPadding: 30,
    narrowContentTopPadding: 20,
    compactContentTopPadding: 14,
    regularHeroBottomGap: 28,
    narrowHeroBottomGap: 20,
    compactHeroBottomGap: 14,
    regularPageTitleFontSize: 38,
    narrowPageTitleFontSize: 38,
    compactPageTitleFontSize: 38,
    brandTaglineLetterSpacingEm: 0.14,
  );

  /// Height of the header bar in a regular or narrow window; the prototype does not change it at
  /// the narrow width.
  final double regularHeaderHeight;

  /// Height of the header bar in a compact-height window.
  final double compactHeaderHeight;

  /// Padding above the title block in a regular window.
  final double regularContentTopPadding;

  /// Padding above the title block in a narrow window.
  final double narrowContentTopPadding;

  /// Padding above the title block in a compact-height window.
  final double compactContentTopPadding;

  /// Gap between the title block and the first section in a regular window.
  final double regularHeroBottomGap;

  /// Gap between the title block and the first section in a narrow window.
  final double narrowHeroBottomGap;

  /// Gap between the title block and the first section in a compact-height window.
  final double compactHeroBottomGap;

  /// Font size of the page title in a regular window.
  final double regularPageTitleFontSize;

  /// Font size of the page title in a narrow window.
  final double narrowPageTitleFontSize;

  /// Font size of the page title in a compact-height window.
  final double compactPageTitleFontSize;

  /// Letter spacing, in ems, of the wordmark's tagline, which differs per theme but not per
  /// window.
  final double brandTaglineLetterSpacingEm;

  /// Creates a complete set. Every value is required so a set cannot be assembled with an
  /// accidentally-inherited default.
  const DovahRootThemeMetrics({
    required this.regularHeaderHeight,
    required this.compactHeaderHeight,
    required this.regularContentTopPadding,
    required this.narrowContentTopPadding,
    required this.compactContentTopPadding,
    required this.regularHeroBottomGap,
    required this.narrowHeroBottomGap,
    required this.compactHeroBottomGap,
    required this.regularPageTitleFontSize,
    required this.narrowPageTitleFontSize,
    required this.compactPageTitleFontSize,
    required this.brandTaglineLetterSpacingEm,
  });

  /// Returns a copy with the given values replaced.
  @override
  DovahRootThemeMetrics copyWith({
    double? regularHeaderHeight,
    double? compactHeaderHeight,
    double? regularContentTopPadding,
    double? narrowContentTopPadding,
    double? compactContentTopPadding,
    double? regularHeroBottomGap,
    double? narrowHeroBottomGap,
    double? compactHeroBottomGap,
    double? regularPageTitleFontSize,
    double? narrowPageTitleFontSize,
    double? compactPageTitleFontSize,
    double? brandTaglineLetterSpacingEm,
  }) => DovahRootThemeMetrics(
    regularHeaderHeight: regularHeaderHeight ?? this.regularHeaderHeight,
    compactHeaderHeight: compactHeaderHeight ?? this.compactHeaderHeight,
    regularContentTopPadding:
        regularContentTopPadding ?? this.regularContentTopPadding,
    narrowContentTopPadding:
        narrowContentTopPadding ?? this.narrowContentTopPadding,
    compactContentTopPadding:
        compactContentTopPadding ?? this.compactContentTopPadding,
    regularHeroBottomGap: regularHeroBottomGap ?? this.regularHeroBottomGap,
    narrowHeroBottomGap: narrowHeroBottomGap ?? this.narrowHeroBottomGap,
    compactHeroBottomGap: compactHeroBottomGap ?? this.compactHeroBottomGap,
    regularPageTitleFontSize:
        regularPageTitleFontSize ?? this.regularPageTitleFontSize,
    narrowPageTitleFontSize:
        narrowPageTitleFontSize ?? this.narrowPageTitleFontSize,
    compactPageTitleFontSize:
        compactPageTitleFontSize ?? this.compactPageTitleFontSize,
    brandTaglineLetterSpacingEm:
        brandTaglineLetterSpacingEm ?? this.brandTaglineLetterSpacingEm,
  );

  /// Interpolates every value; each is a continuous measurement with no discrete counterpart.
  @override
  DovahRootThemeMetrics lerp(
    ThemeExtension<DovahRootThemeMetrics>? other,
    double t,
  ) {
    if (other is! DovahRootThemeMetrics) {
      return this;
    }
    return DovahRootThemeMetrics(
      regularHeaderHeight: lerpDouble(
        regularHeaderHeight,
        other.regularHeaderHeight,
        t,
      )!,
      compactHeaderHeight: lerpDouble(
        compactHeaderHeight,
        other.compactHeaderHeight,
        t,
      )!,
      regularContentTopPadding: lerpDouble(
        regularContentTopPadding,
        other.regularContentTopPadding,
        t,
      )!,
      narrowContentTopPadding: lerpDouble(
        narrowContentTopPadding,
        other.narrowContentTopPadding,
        t,
      )!,
      compactContentTopPadding: lerpDouble(
        compactContentTopPadding,
        other.compactContentTopPadding,
        t,
      )!,
      regularHeroBottomGap: lerpDouble(
        regularHeroBottomGap,
        other.regularHeroBottomGap,
        t,
      )!,
      narrowHeroBottomGap: lerpDouble(
        narrowHeroBottomGap,
        other.narrowHeroBottomGap,
        t,
      )!,
      compactHeroBottomGap: lerpDouble(
        compactHeroBottomGap,
        other.compactHeroBottomGap,
        t,
      )!,
      regularPageTitleFontSize: lerpDouble(
        regularPageTitleFontSize,
        other.regularPageTitleFontSize,
        t,
      )!,
      narrowPageTitleFontSize: lerpDouble(
        narrowPageTitleFontSize,
        other.narrowPageTitleFontSize,
        t,
      )!,
      compactPageTitleFontSize: lerpDouble(
        compactPageTitleFontSize,
        other.compactPageTitleFontSize,
        t,
      )!,
      brandTaglineLetterSpacingEm: lerpDouble(
        brandTaglineLetterSpacingEm,
        other.brandTaglineLetterSpacingEm,
        t,
      )!,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    regularHeaderHeight,
    compactHeaderHeight,
    regularContentTopPadding,
    narrowContentTopPadding,
    compactContentTopPadding,
    regularHeroBottomGap,
    narrowHeroBottomGap,
    compactHeroBottomGap,
    regularPageTitleFontSize,
    narrowPageTitleFontSize,
    compactPageTitleFontSize,
    brandTaglineLetterSpacingEm,
  ];
}
