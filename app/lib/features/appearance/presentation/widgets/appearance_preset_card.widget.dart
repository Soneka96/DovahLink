import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// One selectable theme preset card in the appearance picker: a small preview of that preset's
/// own material and accent colors (independent of the currently active theme), its label, and a
/// selection indicator.
class AppearancePresetCard extends StatelessWidget {
  /// Creates a preset card.
  const AppearancePresetCard({
    required this.preset,
    required this.selected,
    required this.onTap,
    super.key,
  });

  /// The preset this card previews and selects.
  final DovahThemePreset preset;

  /// Whether [preset] is the currently active theme.
  final bool selected;

  /// Called when the card is tapped.
  final VoidCallback onTap;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final ThemeData previewTheme = dovahThemeDataFor(preset);
    final DovahThemeTokens previewTokens = previewTheme
        .extension<DovahThemeTokens>()!;

    return Semantics(
      excludeSemantics: true,
      button: true,
      enabled: true,
      selected: selected,
      label: preset.label,
      onTap: onTap,
      child: InkWell(
        onTap: onTap,
        mouseCursor: SystemMouseCursors.click,
        child: Builder(
          builder: (BuildContext context) {
            final bool focused = Focus.of(context).hasPrimaryFocus;

            return Theme(
              data: previewTheme,
              child: Container(
                key: focused
                    ? const Key('appearance-preset-card-focus-outline')
                    : null,
                foregroundDecoration: focused
                    ? BoxDecoration(
                        border: Border.all(
                          color: previewTokens.signal,
                          width: DovahControlMetrics.focusOutlineWidth,
                        ),
                        borderRadius: BorderRadius.circular(
                          previewTokens.cornerRadius,
                        ),
                        boxShadow: <BoxShadow>[
                          BoxShadow(
                            color: previewTokens.soft,
                            blurRadius: DovahControlMetrics.focusGlowBlurRadius,
                          ),
                        ],
                      )
                    : null,
                child: DovahSurface(
                  key: const Key('appearance-preset-card-surface'),
                  role: selected
                      ? DovahMaterialRole.raised
                      : DovahMaterialRole.surface,
                  padding: const EdgeInsets.all(12),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Container(
                        key: const Key('appearance-preset-card-preview'),
                        height:
                            context.dovahDialogMetrics.appearancePreviewHeight,
                        decoration: BoxDecoration(
                          color: previewTokens.surface,
                          image: preset == DovahThemePreset.dovah
                              ? const DecorationImage(
                                  image: AssetImage(
                                    'assets/themes/dovah/dovahlink-connection-hero.png',
                                  ),
                                  fit: BoxFit.cover,
                                )
                              : null,
                          border: Border.all(color: previewTokens.lineStrong),
                        ),
                        child: Align(
                          alignment: Alignment.bottomCenter,
                          child: SizedBox(
                            height: appearancePreviewAccentHeight,
                            child: Row(
                              children: [
                                Expanded(
                                  child: ColoredBox(
                                    color: previewTokens.signal,
                                  ),
                                ),
                                Expanded(
                                  child: ColoredBox(color: previewTokens.ember),
                                ),
                              ],
                            ),
                          ),
                        ),
                      ),
                      const SizedBox(height: 8),
                      Row(
                        children: [
                          Expanded(
                            child: Text(
                              preset.label,
                              style: TextStyle(
                                color: previewTokens.textPrimary,
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                          ),
                          if (selected)
                            Icon(
                              Icons.check_circle,
                              color: previewTokens.signal,
                              size: appearanceSelectionIconSize,
                            ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
            );
          },
        ),
      ),
    );
  }
}
