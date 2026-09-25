import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_preview.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_appearance_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preset_card_style.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// The card's surface and content: the shown preset's card material, outline, and text tones,
/// its preview, its copy, and the selected badge. It reads its recipe and measurements from the theme
/// in scope, so the card places it under the shown preset's own theme.
class AppearancePresetCardBody extends StatelessWidget {
  /// Creates the body of a preset card.
  const AppearancePresetCardBody({
    required this.preset,
    required this.selected,
    super.key,
  });

  /// The preset the card shows.
  final DovahThemePreset preset;

  /// Whether the preset is the active theme, which shows the badge.
  final bool selected;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahAppearanceMetrics metrics = context.dovahAppearanceMetrics;
    final DovahPresetCardStyle style = context.dovahMaterials.presetCard;

    return DovahSurface(
      key: const Key('appearance-preset-card-surface'),
      material: style.material,
      cornerRadius: metrics.cornerRadius,
      cornerCutSize: metrics.cornerCutSize,
      // The preview sits inside the card's one-pixel border, as the prototype's does.
      padding: const EdgeInsets.all(DovahThemeTokens.surfaceBorderWidth),
      child: Stack(
        children: [
          Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const AppearancePresetPreview(
                key: Key('appearance-preset-card-preview'),
              ),
              Padding(
                padding: EdgeInsets.all(metrics.copyPadding),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  spacing: DovahAppearanceMetrics.copyGap,
                  children: [
                    Text(
                      preset.label,
                      style: TextStyle(
                        color: style.titleColor,
                        fontSize: DovahAppearanceMetrics.titleFontSize,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    Text(
                      preset.summary,
                      style: TextStyle(
                        color: style.summaryColor,
                        fontSize: DovahAppearanceMetrics.summaryFontSize,
                        height: DovahAppearanceMetrics.copyLineHeight,
                      ),
                    ),
                    if (metrics.showDetail)
                      Text(
                        preset.materials,
                        style: TextStyle(
                          color: style.detailColor,
                          fontSize: DovahAppearanceMetrics.detailFontSize,
                          height: DovahAppearanceMetrics.copyLineHeight,
                        ),
                      ),
                  ],
                ),
              ),
            ],
          ),
          if (selected)
            Positioned(
              key: const Key('appearance-preset-card-badge'),
              top: DovahAppearanceMetrics.badgeInset,
              right: DovahAppearanceMetrics.badgeInset,
              width: DovahAppearanceMetrics.badgeSize,
              height: DovahAppearanceMetrics.badgeSize,
              child: DecoratedBox(
                decoration: BoxDecoration(
                  color: style.badgeFill,
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  Icons.check,
                  size: DovahAppearanceMetrics.badgeGlyphSize,
                  color: style.badgeForeground,
                ),
              ),
            ),
        ],
      ),
    );
  }
}
