import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_page_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// Renders the canonical placeholder for a Session destination without gameplay support.
class SessionSectionPlaceholder extends StatelessWidget {
  const SessionSectionPlaceholder({required this.page, super.key});

  final String page;

  static const Map<String, (String, String)> _copy = {
    'Map': (
      'World Map',
      'Explore discovered locations and the player’s current position.',
    ),
    'Quests': (
      'Quest Journal',
      'Follow active objectives without opening the in-game journal.',
    ),
    'Inventory': (
      'Inventory',
      'Browse carried equipment, ingredients and other items.',
    ),
    'Character': ('Character', 'Review skills, effects and the current build.'),
  };

  @override
  Widget build(BuildContext context) {
    final DovahPageMetrics metrics = context.dovahPageMetrics;
    final DovahThemeTokens tokens = context.dovahTokens;
    final (String title, String description) = _copy[page]!;
    final Widget introContent = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Semantics(
          header: true,
          child: Text(
            title,
            key: const Key('session-placeholder-title'),
            style: TextStyle(
              color: tokens.textPrimary,
              fontFamily: tokens.displayFontFamily,
              fontFamilyFallback: tokens.displayFontFamilyFallback,
              fontSize: metrics.introTitleFontSize,
              fontWeight: FontWeight.w500,
              letterSpacing:
                  metrics.introTitleFontSize *
                  DovahPageMetrics.introTitleLetterSpacingEm,
              height: tokens.pageTitleLineHeight,
            ),
          ),
        ),
        const SizedBox(height: DovahPageMetrics.introTitleBottomGap),
        Text(
          description,
          style: TextStyle(
            color: tokens.textMuted,
            fontSize: DovahPageMetrics.introDescriptionFontSize,
          ),
        ),
      ],
    );
    final Widget intro = metrics.introCornerRadius == 0
        ? introContent
        : DovahSurface(
            cornerRadius: metrics.introCornerRadius,
            padding: metrics.introPadding,
            child: introContent,
          );

    return SingleChildScrollView(
      key: const Key('session-shell-placeholder-page'),
      padding: EdgeInsets.only(
        top: metrics.contentTopPadding,
        bottom: DovahPageMetrics.contentBottomPadding,
      ),
      child: Padding(
        padding: EdgeInsets.symmetric(horizontal: metrics.sideMargin),
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(
              maxWidth: DovahPageMetrics.contentMaxWidth,
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                intro,
                SizedBox(height: metrics.introBottomGap),
                LayoutBuilder(
                  builder: (BuildContext context, BoxConstraints constraints) {
                    final double cardWidth =
                        (constraints.maxWidth -
                            DovahPageMetrics.placeholderGap *
                                (metrics.placeholderColumns - 1)) /
                        metrics.placeholderColumns;
                    return Wrap(
                      spacing: DovahPageMetrics.placeholderGap,
                      runSpacing: DovahPageMetrics.placeholderGap,
                      children:
                          [
                            (
                              '$page preview',
                              'This prototype shows where the live ${page.toLowerCase()} experience will live after selecting a Skyrim connection.',
                            ),
                            (
                              'Session aware',
                              'All information belongs to the selected PC and current play context.',
                            ),
                            (
                              'Automatic refresh',
                              'Loading another save updates this view without returning to Connections.',
                            ),
                          ].map(((String heading, String body) card) {
                            return SizedBox(
                              width: cardWidth,
                              child: ConstrainedBox(
                                constraints: const BoxConstraints(
                                  minHeight:
                                      DovahPageMetrics.placeholderCardMinHeight,
                                ),
                                child: DovahPanel(
                                  padding: const EdgeInsets.all(
                                    DovahPageMetrics.placeholderCardPadding,
                                  ),
                                  child: Column(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      Text(
                                        card.$1,
                                        style: const TextStyle(
                                          fontSize: 13,
                                          fontWeight: FontWeight.w700,
                                        ),
                                      ),
                                      const SizedBox(
                                        height: DovahPageMetrics
                                            .placeholderCardTitleBottomGap,
                                      ),
                                      Text(
                                        card.$2,
                                        style: TextStyle(
                                          color: tokens.textMuted,
                                          fontSize: DovahPageMetrics
                                              .placeholderBodyFontSize,
                                          height: DovahPageMetrics
                                              .placeholderBodyLineHeight,
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                              ),
                            );
                          }).toList(),
                    );
                  },
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
