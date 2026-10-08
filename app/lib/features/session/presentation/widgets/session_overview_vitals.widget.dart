import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/session/presentation/viewdata/session_overview_vitals.viewdata.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel.widget.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show DovahLinkStateStatus;

/// The prototype's Health, Magicka, and Stamina progress rows.
class SessionOverviewVitalsPanel extends StatelessWidget {
  /// Creates the Current Status card from its Redux-backed presentation value.
  const SessionOverviewVitalsPanel({required this.viewData, super.key});

  /// Current/max ratios and the synchronization standing for the Vitals group.
  final SessionOverviewVitalsViewData viewData;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final List<(String, double?, Color)> values = [
      ('Health', viewData.healthRatio, tokens.health),
      ('Magicka', viewData.magickaRatio, tokens.magicka),
      ('Stamina', viewData.staminaRatio, tokens.stamina),
    ];
    final bool isStaleOrFailed =
        viewData.status == DovahLinkStateStatus.stale ||
        viewData.status == DovahLinkStateStatus.failed;
    final bool isRecovering =
        viewData.status == DovahLinkStateStatus.recovering;

    return DovahPanel(
      key: const Key('session-overview-vitals-panel'),
      raised: isRecovering,
      overlayGradient: tokens.overviewSidePanelTexture,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            tokens.uppercaseLabels ? 'CURRENT STATUS' : 'Current status',
            key: const Key('session-overview-vitals-title'),
            style: TextStyle(
              color: tokens.textPrimary,
              fontSize: DovahOverviewMetrics.panelTitleFontSize,
              fontWeight: FontWeight.w700,
              letterSpacing:
                  DovahOverviewMetrics.panelTitleFontSize *
                  context.dovahOverviewMetrics.panelTitleLetterSpacingEm,
            ),
          ),
          const SizedBox(height: DovahOverviewMetrics.panelTitleBottomGap),
          Column(
            children: [
              for (final (int index, (String label, double? ratio, Color color))
                  in values.indexed) ...[
                Semantics(
                  label: ratio == null
                      ? label
                      : '$label, ${(ratio * 100).round()} percent',
                  child: ExcludeSemantics(
                    child: Row(
                      children: [
                        SizedBox(
                          width: DovahOverviewMetrics.barLabelWidth,
                          child: Text(
                            label,
                            maxLines: 1,
                            softWrap: false,
                            style: TextStyle(
                              color: ratio == null
                                  ? tokens.textFaint
                                  : tokens.textMuted,
                              fontSize: DovahOverviewMetrics.barFontSize,
                            ),
                          ),
                        ),
                        const SizedBox(
                          width: DovahOverviewMetrics.barColumnGap,
                        ),
                        Expanded(
                          child: Container(
                            height: DovahOverviewMetrics.barHeight,
                            decoration: BoxDecoration(
                              color: tokens.barTrack,
                              borderRadius: BorderRadius.circular(
                                DovahOverviewMetrics.barRadius,
                              ),
                            ),
                            child: Align(
                              alignment: Alignment.centerLeft,
                              child: ratio == null
                                  ? const SizedBox.shrink()
                                  : FractionallySizedBox(
                                      widthFactor: ratio,
                                      child: DecoratedBox(
                                        key: Key(
                                          'session-overview-${label.toLowerCase()}-bar-fill',
                                        ),
                                        decoration: BoxDecoration(
                                          color: isStaleOrFailed
                                              ? color.withValues(alpha: 0.65)
                                              : color,
                                          borderRadius: BorderRadius.circular(
                                            DovahOverviewMetrics.barRadius,
                                          ),
                                        ),
                                      ),
                                    ),
                            ),
                          ),
                        ),
                        const SizedBox(
                          width: DovahOverviewMetrics.barColumnGap,
                        ),
                        SizedBox(
                          width: DovahOverviewMetrics.barValueWidth,
                          child: Align(
                            alignment: Alignment.centerRight,
                            child: Text(
                              ratio != null ? '${(ratio * 100).round()}%' : ' ',
                              maxLines: 1,
                              softWrap: false,
                              style: TextStyle(
                                color: ratio == null
                                    ? tokens.textFaint
                                    : isStaleOrFailed
                                    ? tokens.textMuted.withValues(alpha: 0.72)
                                    : isRecovering
                                    ? tokens.textPrimary
                                    : tokens.textMuted,
                                fontSize: DovahOverviewMetrics.barFontSize,
                                fontStyle: isStaleOrFailed
                                    ? FontStyle.italic
                                    : null,
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                if (index < values.length - 1)
                  const SizedBox(height: DovahOverviewMetrics.barRowGap),
              ],
            ],
          ),
        ],
      ),
    );
  }
}
