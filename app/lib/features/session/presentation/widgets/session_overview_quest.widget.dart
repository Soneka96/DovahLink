import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/session/presentation/viewdata/session_overview_quest.viewdata.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel.widget.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show DovahLinkStateStatus;

/// The prototype's tracked-quest card, with truthful single or plural content.
class SessionOverviewQuestPanel extends StatelessWidget {
  /// Creates the tracked-quest card from its Redux-backed presentation value.
  const SessionOverviewQuestPanel({required this.viewData, super.key});

  /// The truthful quest title, detail, and synchronization standing.
  final SessionOverviewQuestViewData viewData;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final DovahOverviewMetrics metrics = context.dovahOverviewMetrics;
    final bool isStale = viewData.status == DovahLinkStateStatus.stale;
    final bool isStaleOrFailed =
        isStale || viewData.status == DovahLinkStateStatus.failed;
    final bool isRecovering =
        viewData.status == DovahLinkStateStatus.recovering;
    final bool isDormant = viewData.title == null && viewData.detail == null;

    return DovahPanel(
      key: const Key('session-overview-quest-panel'),
      raised: isRecovering,
      overlayGradient: tokens.overviewSidePanelTexture,
      leadingAccent: tokens.ember,
      child: Stack(
        children: [
          if (isRecovering)
            Positioned.fill(
              child: IgnorePointer(
                child: DecoratedBox(
                  key: const Key('session-overview-quest-sheen'),
                  decoration: BoxDecoration(
                    gradient: LinearGradient(
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                      colors: [
                        Colors.transparent,
                        tokens.signal.withValues(alpha: 0.08),
                        Colors.transparent,
                      ],
                    ),
                  ),
                ),
              ),
            ),
          Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Expanded(
                    child: Text(
                      tokens.uppercaseLabels
                          ? 'TRACKED QUEST'
                          : 'Tracked quest',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      softWrap: false,
                      style: TextStyle(
                        color: tokens.textPrimary,
                        fontSize: DovahOverviewMetrics.panelTitleFontSize,
                        fontWeight: FontWeight.w700,
                        letterSpacing:
                            DovahOverviewMetrics.panelTitleFontSize *
                            metrics.panelTitleLetterSpacingEm,
                      ),
                    ),
                  ),
                  Text(
                    'Journal',
                    maxLines: 1,
                    softWrap: false,
                    style: TextStyle(
                      color: tokens.panelNote,
                      fontSize: DovahOverviewMetrics.panelTitleNoteFontSize,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: DovahOverviewMetrics.panelTitleBottomGap),
              IntrinsicHeight(
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Container(
                      key: const Key('session-overview-quest-inner-rule'),
                      width: DovahOverviewMetrics.questRuleWidth,
                      decoration: BoxDecoration(
                        gradient: LinearGradient(
                          begin: Alignment.topCenter,
                          end: Alignment.bottomCenter,
                          colors: [tokens.ember, tokens.signal],
                        ),
                      ),
                    ),
                    const SizedBox(width: DovahOverviewMetrics.questIndent),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            viewData.title ?? ' ',
                            key: const Key('session-overview-quest-title'),
                            style: TextStyle(
                              color: isDormant
                                  ? tokens.textFaint
                                  : isStaleOrFailed
                                  ? tokens.textMuted
                                  : tokens.textPrimary,
                              fontSize: DovahOverviewMetrics.questTitleFontSize,
                              fontWeight: FontWeight.w700,
                              fontStyle: isStale ? FontStyle.italic : null,
                            ),
                          ),
                          const SizedBox(
                            height: DovahOverviewMetrics.questTitleBottomGap,
                          ),
                          Text(
                            viewData.detail ?? ' ',
                            key: const Key('session-overview-quest-detail'),
                            style: TextStyle(
                              color: isDormant
                                  ? tokens.textFaint
                                  : tokens.textMuted,
                              fontSize:
                                  DovahOverviewMetrics.questDescriptionFontSize,
                              height: DovahOverviewMetrics
                                  .questDescriptionLineHeight,
                              fontStyle: isStale ? FontStyle.italic : null,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
