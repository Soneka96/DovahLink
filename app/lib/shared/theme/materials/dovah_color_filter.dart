import 'dart:ui';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_color_matrix.dart';

/// A CSS-style color treatment (the prototype's `filter: grayscale() saturate() contrast() ...`),
/// as a typed value a theme recipe can hold as a constant. Every amount defaults to no change, so a
/// recipe names only the functions its prototype rule uses. The functions always apply in the order
/// sepia, hue-rotate, grayscale and saturate, brightness, contrast, which is equivalent for every
/// prototype chain.
class DovahColorFilter extends Equatable {
  /// A filter that changes nothing.
  static const DovahColorFilter none = DovahColorFilter();

  /// The CSS `grayscale()` amount, from `0` (none) to `1` (fully gray).
  final double grayscale;

  /// The CSS `sepia()` amount, from `0` (none) to `1` (fully sepia).
  final double sepia;

  /// The CSS `hue-rotate()` angle in degrees.
  final double hueRotateDegrees;

  /// The CSS `saturate()` amount; `1` is no change.
  final double saturate;

  /// The CSS `brightness()` amount; `1` is no change.
  final double brightness;

  /// The CSS `contrast()` amount; `1` is no change.
  final double contrast;

  /// Creates a filter from the CSS function amounts it applies.
  const DovahColorFilter({
    this.grayscale = 0,
    this.sepia = 0,
    this.hueRotateDegrees = 0,
    this.saturate = 1,
    this.brightness = 1,
    this.contrast = 1,
  });

  /// Whether the filter changes nothing, so a caller can skip applying it.
  bool get isNeutral => this == none;

  /// Returns the Flutter color filter that applies this treatment.
  ColorFilter toColorFilter() => ColorFilter.matrix(
    buildDovahColorMatrix(
      sepia: sepia,
      hueRotateDegrees: hueRotateDegrees,
      saturation: (1 - grayscale) * saturate,
      brightness: brightness,
      contrast: contrast,
    ),
  );

  /// The fields that define this filter's value equality.
  @override
  List<Object?> get props => [
    grayscale,
    sepia,
    hueRotateDegrees,
    saturate,
    brightness,
    contrast,
  ];
}
