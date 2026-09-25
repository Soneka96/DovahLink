import 'dart:math' as math;
import 'dart:ui';

import 'package:flutter/widgets.dart' show Matrix4;

import 'package:dovahlink_client/shared/theme/materials/dovah_gradient_colors.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';

/// A radial highlight, stain, or speck (the prototype's `radial-gradient(... at X% Y%, ...)`).
/// Without a [radius] it is a CSS `ellipse farthest-corner`: an ellipse that reaches the surface's
/// farthest corner, so the same recipe covers wide and tall surfaces alike and [stops] are
/// fractions of that ellipse. With a [radius] it is a circle of that many logical pixels.
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

  /// The circle's radius in logical pixels, or `null` for an ellipse to the farthest corner.
  final double? radius;

  /// Creates a radial layer. [colors] and [stops] must have the same length; [createShader]
  /// throws an [ArgumentError] otherwise.
  const DovahRadialLayer({
    required this.center,
    required this.colors,
    required this.stops,
    this.radius,
  });

  /// See [DovahMaterialLayer.createShader].
  @override
  Shader createShader(Size size) {
    final Offset origin = Offset(
      size.width * center.dx,
      size.height * center.dy,
    );
    final double? circleRadius = radius;
    final double radiusX =
        circleRadius ??
        math.max(origin.dx, size.width - origin.dx) * math.sqrt2;
    final double radiusY =
        circleRadius ??
        math.max(origin.dy, size.height - origin.dy) * math.sqrt2;

    return Gradient.radial(
      Offset.zero,
      1,
      matchTransparentStops(colors),
      stops,
      TileMode.clamp,
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
  List<Object?> get props => [center, colors, stops, radius];
}
