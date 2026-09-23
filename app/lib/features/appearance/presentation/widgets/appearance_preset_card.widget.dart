import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
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
    final DovahThemeTokens activeTokens = context.dovahTokens;
    final DovahThemeTokens previewTokens = dovahThemeDataFor(
      preset,
    ).extension<DovahThemeTokens>()!;

    return MouseRegion(
      cursor: SystemMouseCursors.click,
      child: GestureDetector(
        onTap: onTap,
        child: Semantics(
          button: true,
          selected: selected,
          label: preset.label,
          child: DovahSurface(
            raised: selected,
            padding: const EdgeInsets.all(12),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Container(
                  height: 48,
                  decoration: BoxDecoration(
                    gradient: previewTokens.materialGradient,
                    border: Border.all(color: previewTokens.lineStrong),
                  ),
                  child: Align(
                    alignment: Alignment.bottomCenter,
                    child: SizedBox(
                      height: 6,
                      child: Row(
                        children: [
                          Expanded(
                            child: ColoredBox(color: previewTokens.signal),
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
                          color: activeTokens.textPrimary,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ),
                    if (selected)
                      Icon(
                        Icons.check_circle,
                        color: activeTokens.signal,
                        size: 20,
                      ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
