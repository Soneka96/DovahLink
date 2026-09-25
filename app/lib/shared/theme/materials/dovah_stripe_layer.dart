import 'dart:math' as math;
import 'dart:ui';

import 'package:dovahlink_client/shared/theme/materials/dovah_gradient_colors.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_gradient_line.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';

/// Fine repeating lines at a CSS angle (the prototype's `repeating-linear-gradient(<angle>, ...)`
/// scratches, scanlines, and fibres). Unlike a linear layer, the stops are in logical pixels
/// and the last one is the period, so a 19px scratch rhythm stays 19px on every surface size.
class DovahStripeLayer extends DovahMaterialLayer {
  /// The direction the stripes repeat along in degrees; `0` points up and angles grow clockwise.
  final double angleDegrees;

  /// The color at each stop.
  final List<Color> colors;

  /// The position of each stop in logical pixels along one period, starting at `0`; the last
  /// entry is the period length.
  final List<double> stopsPx;

  /// Creates a stripe layer. [colors] and [stopsPx] must have the same length; [createShader]
  /// throws an [ArgumentError] otherwise.
  const DovahStripeLayer({
    required this.angleDegrees,
    required this.colors,
    required this.stopsPx,
  });

  /// The length of one repeat, in logical pixels.
  double get period => stopsPx.last;

  /// See [DovahMaterialLayer.createShader].
  @override
  Shader createShader(Size size) {
    final Offset start = buildDovahGradientLine(size, angleDegrees).start;
    final Offset direction = Offset.fromDirection(
      angleDegrees * math.pi / 180 - math.pi / 2,
    );

    return Gradient.linear(
      start,
      start + direction * period,
      matchTransparentStops(colors),
      [for (final double stop in stopsPx) stop / period],
      TileMode.repeated,
    );
  }

  /// The fields that define this layer's value equality.
  @override
  List<Object?> get props => [angleDegrees, colors, stopsPx];
}
