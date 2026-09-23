import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
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
      button: true,
      enabled: true,
      selected: selected,
      label: preset.label,
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
                          width: DovahThemeTokens.focusOutlineWidth,
                        ),
                        borderRadius: BorderRadius.circular(
                          previewTokens.cornerRadius,
                        ),
                        boxShadow: <BoxShadow>[
                          BoxShadow(
                            color: previewTokens.soft,
                            blurRadius: DovahThemeTokens.focusGlowBlurRadius,
                          ),
                        ],
                      )
                    : null,
                child: DovahSurface(
                  key: const Key('appearance-preset-card-surface'),
                  raised: selected,
                  padding: const EdgeInsets.all(DovahThemeTokens.spacing12),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Container(
                        key: const Key('appearance-preset-card-preview'),
                        height: DovahThemeTokens.appearancePreviewHeight,
                        decoration: BoxDecoration(
                          gradient: previewTokens.materialGradient,
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
                            height:
                                DovahThemeTokens.appearancePreviewAccentHeight,
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
                      const SizedBox(height: DovahThemeTokens.spacing8),
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
                              size:
                                  DovahThemeTokens.appearanceSelectionIconSize,
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
