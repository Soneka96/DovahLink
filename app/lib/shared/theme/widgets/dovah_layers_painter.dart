import 'package:flutter/foundation.dart' show listEquals;
import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';

/// Paints gradient layers across its whole area, base first, at a uniform opacity. It is the
/// cheap, `saveLayer`-free painter for backdrops that have no outline of their own, such as a canvas
/// atmosphere or a preview scrim; a surface with an outline uses the material painter instead.
class DovahLayersPainter extends CustomPainter {
  /// The layers to paint, base first.
  final List<DovahMaterialLayer> layers;

  /// The opacity every layer is painted at, from `0` to `1`.
  final double opacity;

  /// Creates a painter for [layers] at [opacity].
  const DovahLayersPainter({required this.layers, this.opacity = 1});

  /// See [CustomPainter.paint].
  @override
  void paint(Canvas canvas, Size size) {
    if (size.isEmpty) {
      return;
    }

    final Rect bounds = Offset.zero & size;
    // Modulating by a translucent white scales the layer's alpha without an offscreen buffer.
    final ColorFilter? fade = opacity == 1
        ? null
        : ColorFilter.mode(
            Color.fromRGBO(255, 255, 255, opacity),
            BlendMode.modulate,
          );
    for (final DovahMaterialLayer layer in layers) {
      canvas.drawRect(
        bounds,
        Paint()
          ..shader = layer.createShader(size)
          ..colorFilter = fade,
      );
    }
  }

  /// See [CustomPainter.shouldRepaint].
  @override
  bool shouldRepaint(covariant DovahLayersPainter oldPainter) =>
      !listEquals(oldPainter.layers, layers) || oldPainter.opacity != opacity;
}
