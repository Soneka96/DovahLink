import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/discover_dialog.viewmodel.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/discover_candidate_card.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';

/// The prototype-shaped discovery modal backed by current Redux candidates and status.
class DiscoverDialog extends StatelessWidget {
  /// Creates the discovery dialog.
  const DiscoverDialog({super.key});

  /// Shows the dialog and returns the selected candidate, if any.
  static Future<HostCardViewData?> show(BuildContext context) =>
      DovahDialog.showBuilder<HostCardViewData>(
        context,
        builder: (BuildContext _) => const DiscoverDialog(),
      );

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, DiscoverDialogViewModel>(
      distinct: true,
      onInit: (Store<AppState> store) =>
          sl<DiscoverDialogViewModel>(param1: store).onDiscover(),
      converter: (Store<AppState> store) =>
          sl<DiscoverDialogViewModel>(param1: store),
      builder: (BuildContext context, DiscoverDialogViewModel viewModel) {
        final tokens = context.dovahTokens;
        const String searchingMessage = 'Searching for DovahLink on this PC…';
        final bool isFailure =
            viewModel.status == ConnectionDiscoveryStatus.failed;
        final String retryMessage = isFailure
            ? (viewModel.failure ?? ConnectionFailureReason.unknown).message
            : 'No new local Hosts found.';
        final Widget content = switch (viewModel.status) {
          ConnectionDiscoveryStatus.idle ||
          ConnectionDiscoveryStatus.discovering => Semantics(
            label: searchingMessage,
            liveRegion: true,
            excludeSemantics: true,
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                SizedBox.square(
                  dimension: DovahDialogMetrics.progressIndicatorSize,
                  child: CircularProgressIndicator(
                    key: const Key('discover-searching-spinner'),
                    strokeWidth:
                        DovahDialogMetrics.progressIndicatorStrokeWidth,
                    color: tokens.accentPrimary,
                    backgroundColor: tokens.lineSubtle,
                  ),
                ),
                const SizedBox(width: DovahDialogMetrics.progressStatusGap),
                Flexible(
                  child: Text(
                    searchingMessage,
                    key: const Key('discover-searching-message'),
                    style: TextStyle(
                      color: tokens.textMuted,
                      fontSize: DovahThemeTokens.compactFontSize,
                      height: DovahThemeTokens.bodyLineHeight,
                    ),
                  ),
                ),
              ],
            ),
          ),
          ConnectionDiscoveryStatus.available
              when viewModel.candidates.isNotEmpty =>
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Text(
                      'AVAILABLE',
                      style: TextStyle(
                        color: tokens.textMuted,
                        fontSize: DovahRootMetrics.sectionLabelFontSize,
                        height: DovahThemeTokens.bodyLineHeight,
                        fontWeight: FontWeight.w800,
                        letterSpacing:
                            DovahRootMetrics.sectionLabelLetterSpacingEm *
                            DovahRootMetrics.sectionLabelFontSize,
                      ),
                    ),
                    const SizedBox(width: DovahRootMetrics.sectionLabelGap),
                    Expanded(
                      child: DecoratedBox(
                        decoration: BoxDecoration(
                          gradient: LinearGradient(
                            colors: [
                              tokens.lineSubtle,
                              tokens.lineSubtle.withValues(alpha: 0),
                            ],
                          ),
                        ),
                        child: const SizedBox(
                          height: DovahThemeTokens.surfaceBorderWidth,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: DovahRootMetrics.sectionLabelBottomGap),
                LayoutBuilder(
                  builder: (BuildContext context, BoxConstraints constraints) {
                    const double gap = DovahRootMetrics.listGap;
                    final double cardWidth = viewModel.candidates.length == 1
                        ? constraints.maxWidth
                        : (constraints.maxWidth - gap) / 2;

                    return Wrap(
                      spacing: gap,
                      runSpacing: gap,
                      children: [
                        for (final HostCardViewData candidate
                            in viewModel.candidates)
                          SizedBox(
                            width: cardWidth,
                            child: DiscoverCandidateCard(
                              key: Key(
                                'discover-candidate-${candidate.host.hostId}',
                              ),
                              title: candidate.title,
                              subtitle: candidate.subtitle,
                              onTap: () => Navigator.of(context).pop(candidate),
                            ),
                          ),
                      ],
                    );
                  },
                ),
              ],
            ),
          ConnectionDiscoveryStatus.available ||
          ConnectionDiscoveryStatus.empty ||
          ConnectionDiscoveryStatus.failed => Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Semantics(
                label: retryMessage,
                liveRegion: true,
                excludeSemantics: true,
                child: Text(
                  retryMessage,
                  key: const Key('discover-retry-message'),
                  style: TextStyle(
                    color: isFailure ? tokens.danger : tokens.textMuted,
                    fontSize: DovahThemeTokens.compactFontSize,
                    height: DovahThemeTokens.bodyLineHeight,
                  ),
                ),
              ),
              const SizedBox(height: DovahDialogMetrics.progressStatusGap),
              DovahButton(
                key: const Key('discover-retry-button'),
                label: 'Search again',
                variant: DovahButtonVariant.secondary,
                onPressed: viewModel.canDiscover ? viewModel.onDiscover : null,
              ),
            ],
          ),
        };

        return DovahDialog(title: 'Discover Skyrim', child: content);
      },
    );
  }
}
