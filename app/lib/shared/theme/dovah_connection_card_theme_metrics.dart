import 'dart:math' as math;
import 'dart:ui' show lerpDouble;

import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';

/// The connection-card measurements each theme pins differently, as a [ThemeExtension] so
/// Flutter's [ThemeData] transition interpolates them alongside the colors instead of snapping
/// them. Every value is the theme's prototype-exact value for each window mode, with no dependence
/// on the window itself; `DovahConnectionCardMetrics.forWindow` selects the mode for the current
/// window. Values that vary only by window, never by theme, are not here.
///
/// The card's height is the height the prototype's card actually renders at (measured in the
/// prototype), not its `min-height` declaration: with the prototype's fonts the card's content is
/// taller than the declared minimum in most themes.
@immutable
class DovahConnectionCardThemeMetrics
    extends ThemeExtension<DovahConnectionCardThemeMetrics>
    with Equatable {
  /// Frostbound's values, from the prototype's `index.html` media queries and `themes.css`
  /// per-theme overrides.
  static const DovahConnectionCardThemeMetrics frostbound =
      DovahConnectionCardThemeMetrics(
        regularPadding: EdgeInsets.symmetric(vertical: 10, horizontal: 14),
        compactPadding: EdgeInsets.symmetric(vertical: 7, horizontal: 12),
        regularMinHeight: 68,
        compactMinHeight: 62,
        regularIconTileSize: 37,
        compactIconTileSize: 37,
        regularIconTileRadius: 0,
        compactIconTileRadius: 0,
        cornerCutSize: 11,
        cornerRadius: 0,
        iconTileRotation: 0,
        hoverOffset: Offset(2, 0),
      );

  /// The Dovah preset's values, from the prototype's `index.html` media queries and `themes.css`
  /// per-theme overrides.
  static const DovahConnectionCardThemeMetrics dovah =
      DovahConnectionCardThemeMetrics(
        regularPadding: EdgeInsets.symmetric(vertical: 16, horizontal: 18),
        compactPadding: EdgeInsets.symmetric(vertical: 10, horizontal: 14),
        regularMinHeight: 80,
        compactMinHeight: 68,
        regularIconTileSize: 43,
        compactIconTileSize: 37,
        regularIconTileRadius: 0,
        compactIconTileRadius: 0,
        cornerCutSize: 16,
        cornerRadius: 0,
        iconTileRotation: math.pi / 4,
        hoverOffset: Offset(5, 0),
      );

  /// Hearth's values, from the prototype's `index.html` media queries and `themes.css` per-theme
  /// overrides. Its icon tile is a full circle, so its radius is half the tile size.
  static const DovahConnectionCardThemeMetrics hearth =
      DovahConnectionCardThemeMetrics(
        regularPadding: EdgeInsets.symmetric(vertical: 16, horizontal: 18),
        compactPadding: EdgeInsets.symmetric(vertical: 10, horizontal: 14),
        regularMinHeight: 82,
        compactMinHeight: 68,
        regularIconTileSize: 43,
        compactIconTileSize: 37,
        regularIconTileRadius: 21.5,
        compactIconTileRadius: 18.5,
        cornerCutSize: 0,
        cornerRadius: 12,
        iconTileRotation: 0,
        hoverOffset: Offset(0, -2),
      );

  /// The card's padding in a regular or narrow window; the card has no narrow-only geometry.
  final EdgeInsets regularPadding;

  /// The card's padding in a compact-height window.
  final EdgeInsets compactPadding;

  /// The height the card renders at in a regular or narrow window.
  final double regularMinHeight;

  /// The height the card renders at in a compact-height window.
  final double compactMinHeight;

  /// Width and height of the icon tile in a regular or narrow window.
  final double regularIconTileSize;

  /// Width and height of the icon tile in a compact-height window.
  final double compactIconTileSize;

  /// Corner radius of the icon tile in a regular or narrow window: none in Frostbound and Dovah,
  /// a full circle in Hearth.
  final double regularIconTileRadius;

  /// Corner radius of the icon tile in a compact-height window.
  final double compactIconTileRadius;

  /// Bevel cut size of the card when the theme's corner style is a bevel: 11 in Frostbound and
  /// 16 in Dovah, differing from the theme's general bevel; unused by Hearth.
  final double cornerCutSize;

  /// Corner radius of the card when the theme's corner style is rounded: 12 in Hearth, differing
  /// from the theme's general radius; unused by the bevelled themes.
  final double cornerRadius;

  /// How far the icon tile turns, in radians, with its glyph turned back upright: a quarter of a
  /// half turn in Dovah, whose tile is a diamond (the prototype's `.pc-icon` `rotate(45deg)`), and
  /// none in Frostbound and Hearth.
  final double iconTileRotation;

  /// How far the card slides while hovered: 2px right in Frostbound, 5px right in Dovah, and 2px up
  /// in Hearth (the prototype's `.connection:hover` `transform`).
  final Offset hoverOffset;

  /// Creates a complete set. Every value is required so a set cannot be assembled with an
  /// accidentally-inherited default.
  const DovahConnectionCardThemeMetrics({
    required this.regularPadding,
    required this.compactPadding,
    required this.regularMinHeight,
    required this.compactMinHeight,
    required this.regularIconTileSize,
    required this.compactIconTileSize,
    required this.regularIconTileRadius,
    required this.compactIconTileRadius,
    required this.cornerCutSize,
    required this.cornerRadius,
    required this.iconTileRotation,
    required this.hoverOffset,
  });

  /// Returns a copy with the given values replaced.
  @override
  DovahConnectionCardThemeMetrics copyWith({
    EdgeInsets? regularPadding,
    EdgeInsets? compactPadding,
    double? regularMinHeight,
    double? compactMinHeight,
    double? regularIconTileSize,
    double? compactIconTileSize,
    double? regularIconTileRadius,
    double? compactIconTileRadius,
    double? cornerCutSize,
    double? cornerRadius,
    double? iconTileRotation,
    Offset? hoverOffset,
  }) => DovahConnectionCardThemeMetrics(
    regularPadding: regularPadding ?? this.regularPadding,
    compactPadding: compactPadding ?? this.compactPadding,
    regularMinHeight: regularMinHeight ?? this.regularMinHeight,
    compactMinHeight: compactMinHeight ?? this.compactMinHeight,
    regularIconTileSize: regularIconTileSize ?? this.regularIconTileSize,
    compactIconTileSize: compactIconTileSize ?? this.compactIconTileSize,
    regularIconTileRadius: regularIconTileRadius ?? this.regularIconTileRadius,
    compactIconTileRadius: compactIconTileRadius ?? this.compactIconTileRadius,
    cornerCutSize: cornerCutSize ?? this.cornerCutSize,
    cornerRadius: cornerRadius ?? this.cornerRadius,
    iconTileRotation: iconTileRotation ?? this.iconTileRotation,
    hoverOffset: hoverOffset ?? this.hoverOffset,
  );

  /// Interpolates every value; each is a continuous measurement with no discrete counterpart.
  @override
  DovahConnectionCardThemeMetrics lerp(
    ThemeExtension<DovahConnectionCardThemeMetrics>? other,
    double t,
  ) {
    if (other is! DovahConnectionCardThemeMetrics) {
      return this;
    }
    return DovahConnectionCardThemeMetrics(
      regularPadding: EdgeInsets.lerp(regularPadding, other.regularPadding, t)!,
      compactPadding: EdgeInsets.lerp(compactPadding, other.compactPadding, t)!,
      regularMinHeight: lerpDouble(
        regularMinHeight,
        other.regularMinHeight,
        t,
      )!,
      compactMinHeight: lerpDouble(
        compactMinHeight,
        other.compactMinHeight,
        t,
      )!,
      regularIconTileSize: lerpDouble(
        regularIconTileSize,
        other.regularIconTileSize,
        t,
      )!,
      compactIconTileSize: lerpDouble(
        compactIconTileSize,
        other.compactIconTileSize,
        t,
      )!,
      regularIconTileRadius: lerpDouble(
        regularIconTileRadius,
        other.regularIconTileRadius,
        t,
      )!,
      compactIconTileRadius: lerpDouble(
        compactIconTileRadius,
        other.compactIconTileRadius,
        t,
      )!,
      cornerCutSize: lerpDouble(cornerCutSize, other.cornerCutSize, t)!,
      cornerRadius: lerpDouble(cornerRadius, other.cornerRadius, t)!,
      iconTileRotation: lerpDouble(
        iconTileRotation,
        other.iconTileRotation,
        t,
      )!,
      hoverOffset: Offset.lerp(hoverOffset, other.hoverOffset, t)!,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    regularPadding,
    compactPadding,
    regularMinHeight,
    compactMinHeight,
    regularIconTileSize,
    compactIconTileSize,
    regularIconTileRadius,
    compactIconTileRadius,
    cornerCutSize,
    cornerRadius,
    iconTileRotation,
    hoverOffset,
  ];
}
