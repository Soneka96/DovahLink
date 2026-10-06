import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_overview.viewmodel.dart';
import 'package:dovahlink_client/features/session/presentation/widgets/session_overview_character.widget.dart';
import 'package:dovahlink_client/features/session/presentation/widgets/session_overview_quest.widget.dart';
import 'package:dovahlink_client/features/session/presentation/widgets/session_overview_vitals.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// The prototype-shaped Overview content inside the Session Shell.
class SessionOverviewScreen extends StatelessWidget {
  /// Creates the Redux-backed Session Overview content.
  const SessionOverviewScreen({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) =>
      StoreConnector<AppState, SessionOverviewViewModel>(
        distinct: true,
        converter: (Store<AppState> store) =>
            sl<SessionOverviewViewModel>(param1: store),
        builder: (BuildContext context, SessionOverviewViewModel viewModel) {
          final DovahPageMetrics pageMetrics = context.dovahPageMetrics;
          final DovahOverviewMetrics overviewMetrics =
              context.dovahOverviewMetrics;
          final DovahThemeTokens tokens = context.dovahTokens;
          final String? contextLine = viewModel.contextLine;
          final bool isContextStale = viewModel.isContextStale;
          final bool isContextRecovering = viewModel.isContextRecovering;
          final Widget introContent = Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Semantics(
                key: const Key('session-overview-title'),
                header: true,
                child: Text(
                  'Current play session',
                  style: TextStyle(
                    color: tokens.textPrimary,
                    fontFamily: tokens.displayFontFamily,
                    fontFamilyFallback: tokens.displayFontFamilyFallback,
                    fontSize: pageMetrics.introTitleFontSize,
                    fontWeight: FontWeight.w500,
                    letterSpacing:
                        pageMetrics.introTitleFontSize *
                        DovahPageMetrics.introTitleLetterSpacingEm,
                    height: tokens.pageTitleLineHeight,
                  ),
                ),
              ),
              const SizedBox(height: DovahPageMetrics.introTitleBottomGap),
              if (contextLine case final String line)
                Text(
                  line,
                  key: const Key('session-overview-context'),
                  style: TextStyle(
                    color: isContextStale
                        ? tokens.textPrimary
                        : isContextRecovering
                        ? tokens.textPrimary
                        : tokens.textMuted,
                    fontSize: DovahPageMetrics.introDescriptionFontSize,
                    fontStyle: isContextStale ? FontStyle.italic : null,
                  ),
                )
              else
                const SizedBox(
                  height:
                      DovahPageMetrics.introDescriptionFontSize *
                      DovahThemeTokens.bodyLineHeight,
                ),
            ],
          );
          final Widget intro = pageMetrics.introCornerRadius == 0
              ? introContent
              : DovahSurface(
                  role: DovahMaterialRole.surface,
                  cornerRadius: pageMetrics.introCornerRadius,
                  padding: pageMetrics.introPadding,
                  child: introContent,
                );

          return SingleChildScrollView(
            key: const Key('session-overview-scroll'),
            padding: const EdgeInsets.only(
              bottom: DovahPageMetrics.contentBottomPadding,
            ).copyWith(top: pageMetrics.contentTopPadding),
            child: Padding(
              padding: EdgeInsets.symmetric(horizontal: pageMetrics.sideMargin),
              child: Center(
                child: ConstrainedBox(
                  constraints: const BoxConstraints(
                    maxWidth: DovahPageMetrics.contentMaxWidth,
                  ),
                  child: Column(
                    key: const Key('session-overview-page'),
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      intro,
                      SizedBox(height: pageMetrics.introBottomGap),
                      IntrinsicHeight(
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            Expanded(
                              flex: overviewMetrics.mainColumnFlex,
                              child: SessionOverviewCharacterPanel(
                                name: viewModel.characterName,
                                nameStatus: viewModel.characterIdentity.status,
                                race: viewModel.characterRace,
                                level: viewModel.characterLevelText,
                                levelStatus: viewModel.characterLevel.status,
                                supernaturalLabel: viewModel.supernaturalLabel,
                                supernaturalStatus:
                                    viewModel.supernaturalTraits.status,
                                vitals: viewModel.vitalsViewData,
                              ),
                            ),
                            SizedBox(width: overviewMetrics.gridGap),
                            Expanded(
                              flex: overviewMetrics.sideColumnFlex,
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.stretch,
                                children: [
                                  SessionOverviewQuestPanel(
                                    viewData: viewModel.questsViewData,
                                  ),
                                  SizedBox(height: overviewMetrics.gridGap),
                                  SessionOverviewVitalsPanel(
                                    viewData: viewModel.vitalsViewData,
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
              ),
            ),
          );
        },
      );
}
