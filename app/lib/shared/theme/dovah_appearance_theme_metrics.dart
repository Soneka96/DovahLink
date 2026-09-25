import 'dart:ui' show lerpDouble;

import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';

/// The appearance-preset card outlines each theme pins differently, as a [ThemeExtension] so
/// Flutter's [ThemeData] transition interpolates them alongside the colors instead of snapping
/// them. Every value is the theme's prototype-exact value; `DovahAppearanceMetrics.forWindow` adds
/// the window's measurements. The card's outline is its own, not the theme's panel outline: the
/// prototype's `.preset-frostbound`, `.preset-dovah`, and `.preset-hearth` set their own `clip-path`
/// and `border-radius`.
@immutable
class DovahAppearanceThemeMetrics
    extends ThemeExtension<DovahAppearanceThemeMetrics>
    with Equatable {
  /// Frostbound's card: a single 9px bevel (the prototype's `.preset-frostbound` `clip-path`).
  static const DovahAppearanceThemeMetrics frostbound =
      DovahAppearanceThemeMetrics(cornerCutSize: 9, cornerRadius: 0);

  /// The Dovah preset's card: a double 10px bevel (`.preset-dovah` `clip-path`), narrower than
  /// Dovah's own 12px panel bevel.
  static const DovahAppearanceThemeMetrics dovah = DovahAppearanceThemeMetrics(
    cornerCutSize: 10,
    cornerRadius: 0,
  );

  /// Hearth's card: plain 13px rounding (`.preset-hearth` `border-radius:13px`).
  static const DovahAppearanceThemeMetrics hearth = DovahAppearanceThemeMetrics(
    cornerCutSize: 0,
    cornerRadius: 13,
  );

  /// Bevel cut size of the card when its corner style is a bevel; unused by Hearth.
  final double cornerCutSize;

  /// Corner radius of the card when its corner style is rounded; unused by the bevelled themes.
  final double cornerRadius;

  /// Creates a complete set. Every value is required so a set cannot be assembled with an
  /// accidentally-inherited default.
  const DovahAppearanceThemeMetrics({
    required this.cornerCutSize,
    required this.cornerRadius,
  });

  /// Returns a copy with the given values replaced.
  @override
  DovahAppearanceThemeMetrics copyWith({
    double? cornerCutSize,
    double? cornerRadius,
  }) => DovahAppearanceThemeMetrics(
    cornerCutSize: cornerCutSize ?? this.cornerCutSize,
    cornerRadius: cornerRadius ?? this.cornerRadius,
  );

  /// Interpolates every value; each is a continuous measurement with no discrete counterpart.
  @override
  DovahAppearanceThemeMetrics lerp(
    ThemeExtension<DovahAppearanceThemeMetrics>? other,
    double t,
  ) {
    if (other is! DovahAppearanceThemeMetrics) {
      return this;
    }
    return DovahAppearanceThemeMetrics(
      cornerCutSize: lerpDouble(cornerCutSize, other.cornerCutSize, t)!,
      cornerRadius: lerpDouble(cornerRadius, other.cornerRadius, t)!,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [cornerCutSize, cornerRadius];
}
