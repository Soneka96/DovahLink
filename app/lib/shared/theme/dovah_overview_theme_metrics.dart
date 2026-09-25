import 'dart:ui' show lerpDouble;

import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';

/// The Overview-page measurements each theme pins differently, as a [ThemeExtension] so Flutter's
/// [ThemeData] transition interpolates them alongside the colors instead of snapping them. Every
/// value is the theme's prototype-exact value for each window mode, with no dependence on the
/// window itself; `DovahOverviewMetrics.forWindow` selects the mode for the current window. The
/// grid's column ratio varies by window alone, so it is not here.
@immutable
class DovahOverviewThemeMetrics
    extends ThemeExtension<DovahOverviewThemeMetrics>
    with Equatable {
  /// Frostbound's values, from the prototype's `index.html` media queries and `themes.css`
  /// per-theme overrides.
  static const DovahOverviewThemeMetrics frostbound = DovahOverviewThemeMetrics(
    gridGap: 10,
    regularHeroMinHeight: 226,
    compactHeroMinHeight: 205,
    regularStatsTopGap: 14,
    compactStatsTopGap: 14,
  );

  /// The Dovah preset's values, from the prototype's `index.html` media queries and `themes.css`
  /// per-theme overrides.
  static const DovahOverviewThemeMetrics dovah = DovahOverviewThemeMetrics(
    gridGap: 14,
    regularHeroMinHeight: 270,
    compactHeroMinHeight: 210,
    regularStatsTopGap: 20,
    compactStatsTopGap: 14,
  );

  /// Hearth's values, from the prototype's `index.html` media queries and `themes.css` per-theme
  /// overrides.
  static const DovahOverviewThemeMetrics hearth = DovahOverviewThemeMetrics(
    gridGap: 14,
    regularHeroMinHeight: 278,
    compactHeroMinHeight: 215,
    regularStatsTopGap: 20,
    compactStatsTopGap: 14,
  );

  /// Gap between the grid's columns and between the side column's panels in every window mode.
  final double gridGap;

  /// Minimum height of the hero panel in a regular or narrow window. It is the prototype's
  /// declared `min-height`, which its content can exceed.
  final double regularHeroMinHeight;

  /// Minimum height of the hero panel in a compact-height window.
  final double compactHeroMinHeight;

  /// Gap above the hero panel's stats in a regular or narrow window.
  final double regularStatsTopGap;

  /// Gap above the hero panel's stats in a compact-height window.
  final double compactStatsTopGap;

  /// Creates a complete set. Every value is required so a set cannot be assembled with an
  /// accidentally-inherited default.
  const DovahOverviewThemeMetrics({
    required this.gridGap,
    required this.regularHeroMinHeight,
    required this.compactHeroMinHeight,
    required this.regularStatsTopGap,
    required this.compactStatsTopGap,
  });

  /// Returns a copy with the given values replaced.
  @override
  DovahOverviewThemeMetrics copyWith({
    double? gridGap,
    double? regularHeroMinHeight,
    double? compactHeroMinHeight,
    double? regularStatsTopGap,
    double? compactStatsTopGap,
  }) => DovahOverviewThemeMetrics(
    gridGap: gridGap ?? this.gridGap,
    regularHeroMinHeight: regularHeroMinHeight ?? this.regularHeroMinHeight,
    compactHeroMinHeight: compactHeroMinHeight ?? this.compactHeroMinHeight,
    regularStatsTopGap: regularStatsTopGap ?? this.regularStatsTopGap,
    compactStatsTopGap: compactStatsTopGap ?? this.compactStatsTopGap,
  );

  /// Interpolates every value; each is a continuous measurement with no discrete counterpart.
  @override
  DovahOverviewThemeMetrics lerp(
    ThemeExtension<DovahOverviewThemeMetrics>? other,
    double t,
  ) {
    if (other is! DovahOverviewThemeMetrics) {
      return this;
    }
    return DovahOverviewThemeMetrics(
      gridGap: lerpDouble(gridGap, other.gridGap, t)!,
      regularHeroMinHeight: lerpDouble(
        regularHeroMinHeight,
        other.regularHeroMinHeight,
        t,
      )!,
      compactHeroMinHeight: lerpDouble(
        compactHeroMinHeight,
        other.compactHeroMinHeight,
        t,
      )!,
      regularStatsTopGap: lerpDouble(
        regularStatsTopGap,
        other.regularStatsTopGap,
        t,
      )!,
      compactStatsTopGap: lerpDouble(
        compactStatsTopGap,
        other.compactStatsTopGap,
        t,
      )!,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    gridGap,
    regularHeroMinHeight,
    compactHeroMinHeight,
    regularStatsTopGap,
    compactStatsTopGap,
  ];
}
