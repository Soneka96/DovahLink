import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_atmosphere.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_layers_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_scene.widget.dart';

/// The DovahLink application canvas: the theme's [DovahAtmosphere] behind [child]. It stacks the
/// theme's base color, the environment image (Frostbound, Hearth) with its gradient layers seen
/// through the theme's color treatment, and a faint haze of fine lines or grain that fades out
/// toward the bottom (the prototype's `body:after` keeps its `mask-image` under every theme). Dovah
/// has no environment image, so its atmosphere is gradient layers alone.
///
/// The canvas is atmosphere only: component texture belongs to the themed materials and feature
/// artwork to its feature, and nothing here darkens or covers [child]. The atmosphere is static, so
/// it sits in its own repaint boundary; the color treatment is applied once to the image and
/// gradients together, as the prototype's CSS `filter` does, and the haze's fade is one more
/// offscreen pass. Neither repaints while the canvas stands still.
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

    return Stack(
      fit: StackFit.expand,
      children: [
        Positioned.fill(
          child: RepaintBoundary(
            child: Stack(
              fit: StackFit.expand,
              children: [
                ColoredBox(color: tokens.background),
                DovahScene(
                  imageAssetPath: atmosphere.imageAssetPath,
                  imageFilter: atmosphere.imageFilter,
                  layers: atmosphere.layers,
                ),
                if (atmosphere.hazeLayers.isNotEmpty)
                  ShaderMask(
                    blendMode: BlendMode.dstIn,
                    shaderCallback: (Rect bounds) => const LinearGradient(
                      begin: Alignment.topCenter,
                      end: Alignment.bottomCenter,
                      colors: [Color(0xFF000000), Color(0x00000000)],
                      stops: [0, DovahAtmosphere.hazeFadeEnd],
                    ).createShader(bounds),
                    child: CustomPaint(
                      painter: DovahLayersPainter(
                        layers: atmosphere.hazeLayers,
                        opacity: atmosphere.hazeOpacity,
                      ),
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
