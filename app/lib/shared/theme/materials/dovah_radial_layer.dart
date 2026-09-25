import 'dart:math' as math;
import 'dart:ui';

import 'package:flutter/widgets.dart' show Matrix4;

import 'package:dovahlink_client/shared/theme/materials/dovah_gradient_colors.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';

/// A radial highlight, stain, speck, or ring pattern (the prototype's `radial-gradient(... at X% Y%,
/// ...)`). Its extent is either a [radius] in logical pixels or, without one, the surface's farthest
/// corner, so one recipe covers wide and tall surfaces alike; [stops] are fractions of that extent.
/// Its shape is a circle when [circular] and otherwise an ellipse with the proportions of the
/// farthest sides, as in CSS.
class DovahRadialLayer extends DovahMaterialLayer {
  /// The smallest radius the layer resolves to, so a zero-sized surface cannot collapse the
  /// gradient's transform.
  static const double minimumRadius = 0.001;

  /// The gradient center as fractions of the surface's width and height.
  final Offset center;

  /// The color at each stop.
  final List<Color> colors;

  /// The position of each stop as a fraction of the radius, from `0` to `1`.
  final List<double> stops;

  /// The horizontal radius in logical pixels, or `null` to reach the farthest corner.
  final double? radius;

  /// Whether the gradient is a circle instead of an ellipse.
  final bool circular;

  /// Whether the gradient repeats every [radius], as the prototype's `repeating-radial-gradient`
  /// rings do. Only meaningful together with a [radius].
  final bool repeating;

  /// Creates a radial layer. [colors] and [stops] must have the same length; [createShader]
  /// throws an [ArgumentError] otherwise.
  const DovahRadialLayer({
    required this.center,
    required this.colors,
    required this.stops,
    this.radius,
    this.circular = false,
    this.repeating = false,
  });

  /// See [DovahMaterialLayer.createShader].
  @override
  Shader createShader(Size size) {
    final Offset origin = Offset(
      size.width * center.dx,
      size.height * center.dy,
    );
    final double farthestX = math.max(origin.dx, size.width - origin.dx);
    final double farthestY = math.max(origin.dy, size.height - origin.dy);
    final double? fixedRadius = radius;
    final double radiusX =
        fixedRadius ??
        (circular
            ? math.sqrt(farthestX * farthestX + farthestY * farthestY)
            : farthestX * math.sqrt2);
    final double radiusY = circular
        ? radiusX
        : radiusX * (farthestX == 0 ? 1 : farthestY / farthestX);

    return Gradient.radial(
      Offset.zero,
      1,
      matchTransparentStops(colors),
      stops,
      repeating ? TileMode.repeated : TileMode.clamp,
      (Matrix4.translationValues(origin.dx, origin.dy, 0)..multiply(
            Matrix4.diagonal3Values(
              math.max(radiusX, minimumRadius),
              math.max(radiusY, minimumRadius),
              1,
            ),
          ))
          .storage,
    );
  }

  /// The fields that define this layer's value equality.
  @override
  List<Object?> get props => [
    center,
    colors,
    stops,
    radius,
    circular,
    repeating,
  ];
}
