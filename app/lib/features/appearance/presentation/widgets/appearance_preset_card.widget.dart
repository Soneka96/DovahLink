import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_preview.widget.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// One selectable theme preset card in the appearance picker: a picture of that preset drawn from
/// its own theme (independent of the currently active theme), its label, and a selection
/// indicator.
///
/// Known limitation: the prototype gives each card its own border and bevel (Dovah's 10px bevel,
/// Hearth's 13px radius) and adds summary lines and a floating check badge. The card instead uses
/// the theme's panel geometry, its raised material when selected, and a check icon beside the
/// label, so the picker stays a single accessible control.
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
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const AppearancePresetPreview(
                        key: Key('appearance-preset-card-preview'),
                      ),
                      Padding(
                        padding: EdgeInsets.all(
                          context.dovahDialogMetrics.appearanceCopyPadding,
                        ),
                        child: Row(
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
