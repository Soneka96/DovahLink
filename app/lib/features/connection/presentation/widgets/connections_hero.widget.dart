import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// The connections screen's title row: the "Your Skyrim" eyebrow, the page title, a supporting
/// description, and the Discover Skyrim action aligned to the row's bottom edge.
class ConnectionsHero extends StatelessWidget {
  /// Called when Discover Skyrim is tapped, or `null` to show the action disabled.
  final VoidCallback? onDiscover;

  /// Creates the connections title row.
  const ConnectionsHero({required this.onDiscover, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final DovahRootMetrics metrics = context.dovahRootMetrics;
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
                  fontSize: DovahRootMetrics.eyebrowFontSize,
                  height: DovahThemeTokens.bodyLineHeight,
                  fontWeight: FontWeight.w800,
                  letterSpacing:
                      DovahRootMetrics.eyebrowLetterSpacingEm *
                      DovahRootMetrics.eyebrowFontSize,
                ),
              ),
              SizedBox(height: metrics.pageTitleTopGap),
              Semantics(
                header: true,
                label: title,
                excludeSemantics: true,
                child: Text(
                  uppercase ? title.toUpperCase() : title,
                  style: TextStyle(
                    color: tokens.textPrimary,
                    fontFamily: tokens.displayFontFamily,
                    fontSize: metrics.pageTitleFontSize,
                    height: tokens.pageTitleLineHeight,
                    fontWeight: uppercase ? FontWeight.w700 : FontWeight.w500,
                    letterSpacing:
                        (uppercase
                            ? DovahRootMetrics.pageTitleUppercaseLetterSpacingEm
                            : DovahRootMetrics.pageTitleLetterSpacingEm) *
                        metrics.pageTitleFontSize,
                  ),
                ),
              ),
              const SizedBox(height: DovahRootMetrics.pageTitleBottomGap),
              Text(
                'Select an available PC to enter its game.',
                style: TextStyle(
                  color: tokens.textMuted,
                  fontSize: DovahRootMetrics.pageDescriptionFontSize,
                  height: DovahThemeTokens.bodyLineHeight,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(width: DovahRootMetrics.heroGap),
        DovahButton(
          label: 'Discover Skyrim',
          icon: Icons.zoom_in,
          onPressed: onDiscover,
        ),
      ],
    );
  }
}
