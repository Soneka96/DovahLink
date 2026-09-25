import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_layers_painter.dart';

/// A theme scene: an optional image with gradient layers painted over it, all seen through one
/// color treatment, filling the space it is given. It is the shared picture behind both the canvas
/// atmosphere and an appearance-preset preview, so the two stay faithful to the same recipe. The
/// scene is decorative and adds no semantics.
class DovahScene extends StatelessWidget {
  /// The asset path of the image, or `null` for a scene of gradient layers alone.
  final String? imageAssetPath;

  /// The color treatment applied to the image and [layers] together.
  final DovahColorFilter imageFilter;

  /// The gradient layers painted over the image, base first.
  final List<DovahMaterialLayer> layers;

  /// Creates a scene from an optional image, its treatment, and its gradient layers.
  const DovahScene({
    required this.layers,
    this.imageAssetPath,
    this.imageFilter = DovahColorFilter.none,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final String? assetPath = imageAssetPath;
    final Widget scene = Stack(
      fit: StackFit.expand,
      children: [
        if (assetPath != null)
          Image.asset(assetPath, fit: BoxFit.cover, excludeFromSemantics: true),
        CustomPaint(painter: DovahLayersPainter(layers: layers)),
      ],
    );

    return imageFilter.isNeutral
        ? scene
        : ColorFiltered(colorFilter: imageFilter.toColorFilter(), child: scene);
  }
}
