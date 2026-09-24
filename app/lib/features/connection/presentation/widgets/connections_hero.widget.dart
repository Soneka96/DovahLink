import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// The connections screen's title row: the "Your Skyrim" eyebrow, the page title, a supporting
/// description, and the Discover Skyrim action aligned to the row's bottom edge.
class ConnectionsHero extends StatelessWidget {
  /// Creates the connections title row.
  const ConnectionsHero({required this.onDiscover, super.key});

  /// Called when Discover Skyrim is tapped, or `null` to show the action disabled.
  final VoidCallback? onDiscover;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final bool uppercase = tokens.uppercaseLabels;
    const String title = 'Connections';

    return Row(
      crossAxisAlignment: CrossAxisAlignment.end,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'YOUR SKYRIM',
                style: TextStyle(
                  color: tokens.eyebrow,
                  fontSize: DovahThemeTokens.eyebrowFontSize,
                  fontWeight: FontWeight.w800,
                  letterSpacing:
                      DovahThemeTokens.eyebrowLetterSpacingEm *
                      DovahThemeTokens.eyebrowFontSize,
                ),
              ),
              const SizedBox(height: DovahThemeTokens.pageTitleTopGap),
              Semantics(
                header: true,
                label: title,
                excludeSemantics: true,
                child: Text(
                  uppercase ? title.toUpperCase() : title,
                  style: TextStyle(
                    color: tokens.textPrimary,
                    fontFamily: tokens.displayFontFamily,
                    fontSize: tokens.pageTitleFontSize,
                    fontWeight: uppercase ? FontWeight.w700 : FontWeight.w500,
                    letterSpacing:
                        (uppercase
                            ? DovahThemeTokens.pageTitleUppercaseLetterSpacingEm
                            : DovahThemeTokens.pageTitleLetterSpacingEm) *
                        tokens.pageTitleFontSize,
                  ),
                ),
              ),
              const SizedBox(height: DovahThemeTokens.pageTitleBottomGap),
              Text(
                'Select an available PC to enter its game.',
                style: TextStyle(
                  color: tokens.textMuted,
                  fontSize: DovahThemeTokens.pageDescriptionFontSize,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(width: DovahThemeTokens.rootHeroGap),
        DovahButton(
          label: 'Discover Skyrim',
          icon: Icons.zoom_in,
          onPressed: onDiscover,
        ),
      ],
    );
  }
}
