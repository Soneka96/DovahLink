import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_card_body.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_appearance_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_appearance_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_hover_lift.widget.dart';

/// One selectable theme preset card in the appearance picker (the prototype's `.preset-card`): a
/// picture of that preset drawn from its own theme (independent of the currently active theme),
/// its name, a summary of its character, and its materials, with a check badge floating over the
/// picture while it is the active preset. The whole card is one accessible button.
///
/// The card is dressed in the shown preset's own card recipe, outline, and text tones, as the
/// prototype's `.preset-frostbound`, `.preset-dovah`, and `.preset-hearth` are. Beside it, the
/// prototype's focus outline and selected ring use the active theme's accent, so those two sit
/// outside the shown preset's theme. The 2px selected ring is a `box-shadow`, which `clip-path`
/// clips, so only a rounded (Hearth) card shows it; a bevelled card shows its selection through the
/// badge. While hovered the card rises by 2px. The badge's check is Material's check icon; the
/// prototype types a check character, whose look depends on the font.
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
    final bool rounded =
        previewTokens.cornerStyle == DovahPanelCornerStyle.rounded;
    final double outlineRadius = rounded
        ? previewTheme.extension<DovahAppearanceThemeMetrics>()!.cornerRadius
        : 0;
    final Color ringColor = context.dovahTokens.accentPrimary;

    return Semantics(
      excludeSemantics: true,
      button: true,
      enabled: true,
      selected: selected,
      label: preset.label,
      hint: preset.summary,
      onTap: onTap,
      child: DovahHoverLift(
        enabled: true,
        offset: DovahAppearanceMetrics.hoverOffset,
        builder: (BuildContext context, bool hovered) => InkWell(
          onTap: onTap,
          mouseCursor: SystemMouseCursors.click,
          child: Builder(
            builder: (BuildContext context) {
              final bool focused = Focus.of(context).hasPrimaryFocus;

              return DovahFocusRing(
                focused: focused,
                cornerRadius: outlineRadius,
                child: DecoratedBox(
                  key: const Key('appearance-preset-card-selection-ring'),
                  decoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(outlineRadius),
                    boxShadow: selected && rounded
                        ? [
                            BoxShadow(
                              color: ringColor,
                              spreadRadius:
                                  DovahAppearanceMetrics.selectedRingWidth,
                            ),
                          ]
                        : null,
                  ),
                  child: Theme(
                    data: previewTheme,
                    child: AppearancePresetCardBody(
                      preset: preset,
                      selected: selected,
                    ),
                  ),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
