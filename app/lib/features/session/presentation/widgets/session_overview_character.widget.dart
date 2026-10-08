import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/session/presentation/viewdata/session_overview_vitals.viewdata.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show DovahLinkStateStatus;

/// The prototype's character hero panel and its Health, Magicka, and Stamina values.
class SessionOverviewCharacterPanel extends StatelessWidget {
  /// Creates the Current Character panel from Redux-backed presentation values.
  const SessionOverviewCharacterPanel({
    required this.name,
    required this.nameStatus,
    required this.race,
    required this.level,
    required this.levelStatus,
    required this.supernaturalLabel,
    required this.supernaturalStatus,
    required this.vitals,
    super.key,
  });

  /// The current character name, if available.
  final String? name;

  /// The Identity synchronization state used to style [name].
  final DovahLinkStateStatus nameStatus;

  /// The current character race, if available.
  final String? race;

  /// The current level without XP, if available.
  final String? level;

  /// The Level synchronization state used to style [level].
  final DovahLinkStateStatus levelStatus;

  /// The applicable supernatural display label, if available.
  final String? supernaturalLabel;

  /// The Supernatural Traits synchronization state used to style its label.
  final DovahLinkStateStatus supernaturalStatus;

  /// The current vital values, safe ratios, and synchronization status.
  final SessionOverviewVitalsViewData vitals;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final DovahOverviewMetrics overviewMetrics = context.dovahOverviewMetrics;
    final DovahPageMetrics pageMetrics = context.dovahPageMetrics;
    final bool isRecovering =
        nameStatus == DovahLinkStateStatus.recovering ||
        levelStatus == DovahLinkStateStatus.recovering ||
        supernaturalStatus == DovahLinkStateStatus.recovering ||
        vitals.status == DovahLinkStateStatus.recovering;
    final String? health = _formatVital(vitals.healthCurrent);
    final String? magicka = _formatVital(vitals.magickaCurrent);
    final String? stamina = _formatVital(vitals.staminaCurrent);
    final List<(String, String?, Color, bool)> statValues = [
      ('Health', health, tokens.health, health != null),
      ('Magicka', magicka, tokens.magicka, magicka != null),
      ('Stamina', stamina, tokens.stamina, stamina != null),
    ];

    return ConstrainedBox(
      key: const Key('session-overview-character-panel'),
      constraints: BoxConstraints(minHeight: overviewMetrics.heroMinHeight),
      child: DovahSurface(
        child: Stack(
          fit: StackFit.expand,
          children: [
            Positioned.fill(
              left: 1,
              top: 1,
              right: 1,
              bottom: 1,
              child: Stack(
                key: const Key('session-overview-character-artwork'),
                fit: StackFit.expand,
                children: [
                  const ExcludeSemantics(
                    child: Image(
                      image: AssetImage(_characterEnvironmentAsset),
                      fit: BoxFit.cover,
                    ),
                  ),
                  DecoratedBox(
                    key: const Key('session-overview-character-hero-scrim'),
                    decoration: BoxDecoration(gradient: tokens.heroScrim),
                  ),
                  if (tokens.heroTexture case final Gradient texture)
                    DecoratedBox(
                      key: const Key('session-overview-character-texture'),
                      decoration: BoxDecoration(gradient: texture),
                    ),
                  DecoratedBox(
                    key: const Key('session-overview-character-floor-scrim'),
                    decoration: BoxDecoration(gradient: tokens.heroFloorScrim),
                  ),
                  if (isRecovering)
                    DecoratedBox(
                      key: const Key('session-overview-character-sheen'),
                      decoration: BoxDecoration(
                        gradient: LinearGradient(
                          begin: Alignment.topLeft,
                          end: Alignment.bottomRight,
                          colors: [
                            Colors.transparent,
                            tokens.signal.withValues(alpha: 0.1),
                            Colors.transparent,
                          ],
                        ),
                      ),
                    ),
                  Positioned(
                    left: 0,
                    top: 0,
                    bottom: 0,
                    width: DovahOverviewMetrics.panelAccentWidth,
                    child: ColoredBox(color: tokens.signal),
                  ),
                  Padding(
                    padding: pageMetrics.panelPadding,
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.end,
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'CURRENT CHARACTER',
                          key: const Key('session-overview-character-kicker'),
                          style: TextStyle(
                            color: tokens.accentSecondary,
                            fontSize: DovahOverviewMetrics.kickerFontSize,
                            fontWeight: FontWeight.w800,
                            letterSpacing:
                                DovahOverviewMetrics.kickerFontSize *
                                DovahOverviewMetrics.kickerLetterSpacingEm,
                          ),
                        ),
                        const SizedBox(
                          height: DovahOverviewMetrics.heroTitleTopGap,
                        ),
                        Text(
                          name ?? ' ',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          softWrap: false,
                          key: const Key('session-overview-character-name'),
                          style: TextStyle(
                            color: _characterNameColor(tokens, nameStatus),
                            fontFamily: tokens.displayFontFamily,
                            fontFamilyFallback:
                                tokens.displayFontFamilyFallback,
                            fontSize: DovahOverviewMetrics.heroTitleFontSize,
                            fontWeight: FontWeight.w500,
                            fontStyle: nameStatus == DovahLinkStateStatus.stale
                                ? FontStyle.italic
                                : null,
                            letterSpacing:
                                DovahOverviewMetrics.heroTitleFontSize *
                                overviewMetrics.heroTitleLetterSpacingEm,
                          ),
                        ),
                        const SizedBox(
                          height: DovahOverviewMetrics.heroTitleBottomGap,
                        ),
                        Text.rich(
                          TextSpan(
                            children: [
                              if (race case final String value) ...[
                                TextSpan(
                                  text: value,
                                  style: _profileStyle(tokens, nameStatus),
                                ),
                              ],
                              if (level case final String value) ...[
                                if (race != null)
                                  TextSpan(
                                    text: ' · ',
                                    style: TextStyle(color: tokens.textMuted),
                                  ),
                                TextSpan(
                                  text: value,
                                  style: _profileStyle(tokens, levelStatus),
                                ),
                              ],
                              if (supernaturalLabel
                                  case final String value) ...[
                                if (race != null || level != null)
                                  TextSpan(
                                    text: ' · ',
                                    style: TextStyle(color: tokens.textMuted),
                                  ),
                                TextSpan(
                                  text: value,
                                  style: _profileStyle(
                                    tokens,
                                    supernaturalStatus,
                                  ),
                                ),
                              ],
                            ],
                          ),
                          key: const Key('session-overview-character-details'),
                          style: TextStyle(
                            color: tokens.textMuted,
                            fontSize:
                                DovahOverviewMetrics.heroDescriptionFontSize,
                          ),
                        ),
                        SizedBox(height: overviewMetrics.statsTopGap),
                        Row(
                          children: [
                            for (
                              int index = 0;
                              index < statValues.length;
                              index++
                            ) ...[
                              Expanded(
                                child: Column(
                                  crossAxisAlignment:
                                      CrossAxisAlignment.stretch,
                                  children: [
                                    Container(
                                      height: 1,
                                      decoration: BoxDecoration(
                                        gradient: LinearGradient(
                                          colors: [
                                            tokens.ember,
                                            tokens.signal,
                                            Colors.transparent,
                                          ],
                                        ),
                                      ),
                                    ),
                                    const SizedBox(
                                      height:
                                          DovahOverviewMetrics.statTopPadding,
                                    ),
                                    SizedBox(
                                      height:
                                          DovahOverviewMetrics
                                              .statValueFontSize *
                                          1.2,
                                      child: Align(
                                        alignment: Alignment.centerLeft,
                                        child: FittedBox(
                                          fit: BoxFit.scaleDown,
                                          alignment: Alignment.centerLeft,
                                          child: Text(
                                            statValues[index].$2 ?? ' ',
                                            key: Key(
                                              'session-overview-${statValues[index].$1.toLowerCase()}-current',
                                            ),
                                            style: TextStyle(
                                              color: statValues[index].$4
                                                  ? _vitalValueColor(
                                                      tokens,
                                                      vitals.status,
                                                    )
                                                  : tokens.textFaint,
                                              fontFamily:
                                                  tokens.displayFontFamily,
                                              fontFamilyFallback: tokens
                                                  .displayFontFamilyFallback,
                                              fontSize: DovahOverviewMetrics
                                                  .statValueFontSize,
                                              fontWeight: FontWeight.w500,
                                              fontStyle:
                                                  vitals.status ==
                                                          DovahLinkStateStatus
                                                              .stale ||
                                                      vitals.status ==
                                                          DovahLinkStateStatus
                                                              .failed
                                                  ? FontStyle.italic
                                                  : null,
                                            ),
                                          ),
                                        ),
                                      ),
                                    ),
                                    Text(
                                      statValues[index].$1,
                                      style: TextStyle(
                                        color: statValues[index].$4
                                            ? tokens.textMuted
                                            : tokens.textFaint,
                                        fontSize: DovahOverviewMetrics
                                            .statLabelFontSize,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                              if (index < statValues.length - 1)
                                const SizedBox(
                                  width: DovahOverviewMetrics.statGap,
                                ),
                            ],
                          ],
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The shared environment artwork behind the character panel.
const String _characterEnvironmentAsset =
    'assets/branding/character-environment.png';

/// Returns profile text treatment without reducing its contrast or adding status copy.
TextStyle _profileStyle(DovahThemeTokens tokens, DovahLinkStateStatus status) =>
    TextStyle(
      color: switch (status) {
        DovahLinkStateStatus.synchronized => tokens.textMuted,
        DovahLinkStateStatus.stale ||
        DovahLinkStateStatus.failed => tokens.textMuted,
        DovahLinkStateStatus.recovering => tokens.textPrimary,
        DovahLinkStateStatus.notSubscribed ||
        DovahLinkStateStatus.unavailable => tokens.textFaint,
      },
      fontStyle: status == DovahLinkStateStatus.stale ? FontStyle.italic : null,
    );

/// Returns the display color for the character name without status copy.
Color _characterNameColor(
  DovahThemeTokens tokens,
  DovahLinkStateStatus status,
) => switch (status) {
  DovahLinkStateStatus.synchronized => tokens.textPrimary,
  DovahLinkStateStatus.stale ||
  DovahLinkStateStatus.recovering => tokens.textPrimary,
  DovahLinkStateStatus.failed => tokens.textMuted,
  DovahLinkStateStatus.notSubscribed ||
  DovahLinkStateStatus.unavailable => tokens.textFaint,
};

/// Returns the hero statistic color treatment for its synchronization status.
Color _vitalValueColor(DovahThemeTokens tokens, DovahLinkStateStatus status) =>
    switch (status) {
      DovahLinkStateStatus.synchronized => tokens.textPrimary,
      DovahLinkStateStatus.stale ||
      DovahLinkStateStatus.recovering => tokens.textPrimary,
      DovahLinkStateStatus.failed => tokens.textMuted,
      DovahLinkStateStatus.notSubscribed ||
      DovahLinkStateStatus.unavailable => tokens.textFaint,
    };

/// Formats a current vital for display while keeping the SDK value untouched.
String? _formatVital(double? value) {
  if (value == null || !value.isFinite || value < 0) {
    return null;
  }
  return value.ceil().toString();
}
