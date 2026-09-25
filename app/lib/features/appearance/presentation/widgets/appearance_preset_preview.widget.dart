import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_appearance_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_scene.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_layers_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_scene.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil.widget.dart';

/// The picture an appearance-preset card shows of its theme: the theme's own scene image under its
/// gradient layers and color treatment, a sigil tile, and three accent bars, all read from the
/// [DovahPreviewScene] of the theme in scope. The card places it under the previewed preset's own
/// theme, so this widget takes nothing but the theme and draws the real assets, colors, and
/// treatment rather than a separate miniature. It fills the width it is given at the height
/// [DovahAppearanceMetrics.previewHeight] resolves for the window, and it is decorative, so
/// it adds no semantics.
class AppearancePresetPreview extends StatelessWidget {
  /// Creates the preview of the theme in scope.
  const AppearancePresetPreview({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahPreviewScene scene = context.dovahMaterials.previewScene;
    const double markSize =
        DovahAppearanceMetrics.previewSigilSize -
        2 * DovahAppearanceMetrics.previewSigilPadding -
        2 * DovahThemeTokens.surfaceBorderWidth;
    final bool diamond = scene.sigil.shape == DovahPreviewSigilShape.diamond;

    const Widget mark = DovahSigil(size: markSize);
    Widget sigil = Container(
      width: DovahAppearanceMetrics.previewSigilSize,
      height: DovahAppearanceMetrics.previewSigilSize,
      padding: const EdgeInsets.all(DovahAppearanceMetrics.previewSigilPadding),
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: scene.sigil.fill,
        border: Border.all(color: scene.sigil.border),
        shape: scene.sigil.shape == DovahPreviewSigilShape.circle
            ? BoxShape.circle
            : BoxShape.rectangle,
      ),
      child: Transform.rotate(
        angle: diamond ? -math.pi / 4 : 0,
        child: scene.sigil.markFilter.isNeutral
            ? mark
            : ColorFiltered(
                colorFilter: scene.sigil.markFilter.toColorFilter(),
                child: mark,
              ),
      ),
    );
    if (diamond) {
      sigil = Transform.rotate(angle: math.pi / 4, child: sigil);
    }

    final Color? barEdgeColor = scene.barEdgeColor;
    final Widget barBody = Stack(
      fit: StackFit.expand,
      children: [
        CustomPaint(painter: DovahLayersPainter(layers: [scene.barFill])),
        if (barEdgeColor != null)
          Positioned(
            left: 0,
            top: 0,
            bottom: 0,
            width: 2,
            child: ColoredBox(color: barEdgeColor),
          ),
      ],
    );
    final Widget bar = scene.barCornerRadius > 0
        ? ClipRRect(
            borderRadius: BorderRadius.circular(scene.barCornerRadius),
            child: barBody,
          )
        : barBody;

    return ExcludeSemantics(
      child: SizedBox(
        height: context.dovahAppearanceMetrics.previewHeight,
        child: Stack(
          fit: StackFit.expand,
          children: [
            DovahScene(
              imageAssetPath: scene.imageAssetPath,
              imageFilter: scene.imageFilter,
              layers: scene.layers,
            ),
            Center(child: sigil),
            Positioned(
              left: DovahAppearanceMetrics.previewBarsInset,
              right: DovahAppearanceMetrics.previewBarsInset,
              bottom: DovahAppearanceMetrics.previewBarsBottom,
              height: DovahAppearanceMetrics.previewAccentHeight,
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  for (
                    int index = 0;
                    index < DovahAppearanceMetrics.previewBarFlexes.length;
                    index++
                  ) ...[
                    if (index > 0)
                      const SizedBox(
                        width: DovahAppearanceMetrics.previewBarsGap,
                      ),
                    Expanded(
                      flex: DovahAppearanceMetrics.previewBarFlexes[index],
                      child: bar,
                    ),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
