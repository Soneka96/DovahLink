import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_atmosphere.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_layers_painter.dart';

/// The DovahLink application canvas: the theme's [DovahAtmosphere] behind [child]. It stacks the
/// theme's base color, the environment image (Frostbound, Hearth) with its gradient layers seen
/// through the theme's color treatment, and a faint haze of fine lines or grain. Dovah has no
/// environment image, so its atmosphere is gradient layers alone.
///
/// The canvas is atmosphere only: component texture belongs to the themed materials and feature
/// artwork to its feature, and nothing here darkens or covers [child]. The atmosphere is static, so
/// it sits in its own repaint boundary; the color treatment is the one offscreen pass, applied once
/// to the image and gradients together as the prototype's CSS `filter` does.
class DovahEnvironmentBackground extends StatelessWidget {
  /// The foreground content rendered above the atmosphere.
  final Widget child;

  /// Creates the atmospheric background behind [child].
  const DovahEnvironmentBackground({required this.child, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    final DovahAtmosphere atmosphere = context.dovahMaterials.atmosphere;
    final String? imageAssetPath = atmosphere.imageAssetPath;

    final Widget scene = Stack(
      fit: StackFit.expand,
      children: [
        if (imageAssetPath != null)
          Image.asset(
            imageAssetPath,
            fit: BoxFit.cover,
            excludeFromSemantics: true,
          ),
        CustomPaint(painter: DovahLayersPainter(layers: atmosphere.layers)),
      ],
    );

    return Stack(
      fit: StackFit.expand,
      children: [
        Positioned.fill(
          child: RepaintBoundary(
            child: Stack(
              fit: StackFit.expand,
              children: [
                ColoredBox(color: tokens.background),
                atmosphere.imageFilter.isNeutral
                    ? scene
                    : ColorFiltered(
                        colorFilter: atmosphere.imageFilter.toColorFilter(),
                        child: scene,
                      ),
                CustomPaint(
                  painter: DovahLayersPainter(
                    layers: atmosphere.hazeLayers,
                    opacity: atmosphere.hazeOpacity,
                  ),
                ),
              ],
            ),
          ),
        ),
        child,
      ],
    );
  }
}
