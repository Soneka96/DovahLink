import 'dart:ui';

import 'package:dovahlink_client/shared/theme/materials/dovah_gradient_colors.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_gradient_line.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';

/// A linear gradient at a CSS angle (the prototype's `linear-gradient(<angle>, ...)`), stretched
/// across the whole surface: [stops] are fractions of the gradient line, so a fracture line at
/// `17%` stays at 17% of any surface size.
class DovahLinearLayer extends DovahMaterialLayer {
  /// The gradient direction in degrees; `0` points up and angles grow clockwise.
  final double angleDegrees;

  /// The color at each stop.
  final List<Color> colors;

  /// The position of each stop as a fraction of the gradient line, from `0` to `1`.
  final List<double> stops;

  /// Creates a linear layer. [colors] and [stops] must have the same length; [createShader]
  /// throws an [ArgumentError] otherwise.
  const DovahLinearLayer({
    required this.angleDegrees,
    required this.colors,
    required this.stops,
  });

  /// See [DovahMaterialLayer.createShader].
  @override
  Shader createShader(Size size) {
    final ({Offset start, Offset end}) line = buildDovahGradientLine(
      size,
      angleDegrees,
    );

    return Gradient.linear(
      line.start,
      line.end,
      matchTransparentStops(colors),
      stops,
    );
  }

  /// The fields that define this layer's value equality.
  @override
  List<Object?> get props => [angleDegrees, colors, stops];
}
