import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_layers_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_scene.widget.dart';

/// Paints the active theme's complete canvas atmosphere behind [child]. The optional environment
/// image and main atmosphere layers share the recipe's color filter; haze layers and application
/// content are painted afterward without that filter.
class DovahEnvironmentBackground extends StatelessWidget {
  /// Creates the atmospheric background behind [child].
  const DovahEnvironmentBackground({required this.child, super.key});

  /// The foreground content rendered above the atmosphere.
  final Widget child;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    final atmosphere = context.dovahMaterials.atmosphere;

    return Stack(
      fit: StackFit.expand,
      children: [
        ColoredBox(color: tokens.background),
        Positioned.fill(
          child: DovahScene(
            imageAssetPath: atmosphere.imageAssetPath,
            imageFilter: atmosphere.imageFilter,
            layers: atmosphere.layers,
          ),
        ),
        if (atmosphere.hazeLayers.isNotEmpty)
          Positioned.fill(
            child: CustomPaint(
              painter: DovahLayersPainter(
                layers: atmosphere.hazeLayers,
                opacity: atmosphere.hazeOpacity,
              ),
            ),
          ),
        child,
      ],
    );
  }
}
