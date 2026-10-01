import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/discover_dialog.viewmodel.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/discover_candidate_card.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/sections/pairing.section.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_dialog.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_progress.widget.dart';
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
class DiscoverDialog extends StatefulWidget {
  /// Creates the discovery dialog.
  const DiscoverDialog({super.key});

  /// Shows discovery and, when needed, pairing within one continuous dialog route. Discover owns the
  /// candidate pairing lifecycle it started, so it alone ends that lifecycle once the route pops;
  /// the embedded [PairingSection.parentOwned] never disposes it again during route teardown.
  static Future<void> show(BuildContext context) =>
      DovahDialog.showBuilder<void>(
        context,
        builder: (BuildContext _) => const DiscoverDialog(),
      );

  /// Creates the state that keeps the discovery and pairing flow in one route.
  @override
  State<DiscoverDialog> createState() => _DiscoverDialogState();
}

/// Keeps Discover open through authentication and any pairing required by the real outcome.
class _DiscoverDialogState extends State<DiscoverDialog> {
  /// Whether a real unpaired outcome has moved this dialog into the pairing flow.
  bool _isShowingPairing = false;

  /// Whether this route started the candidate pairing lifecycle it must dispose.
  bool _ownsCandidatePairingLifecycle = false;

  /// See [State.build].
  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, DiscoverDialogViewModel>(
      distinct: true,
      onInit: (Store<AppState> store) =>
          sl<DiscoverDialogViewModel>(param1: store).onDiscover(),
      onDispose: (Store<AppState> store) {
        if (_ownsCandidatePairingLifecycle) {
          sl<DiscoverDialogViewModel>(param1: store).onDispose();
        }
      },
      onDidChange: (DiscoverDialogViewModel? _, DiscoverDialogViewModel vm) {
        if (_isShowingPairing) {
          return;
        }
        if (vm.hasTrustedCandidate) {
          Navigator.of(context).pop();
        } else if (vm.shouldContinueToPairing) {
          setState(() => _isShowingPairing = true);
        }
      },
      converter: (Store<AppState> store) =>
          sl<DiscoverDialogViewModel>(param1: store),
      builder: (BuildContext context, DiscoverDialogViewModel viewModel) {
        if (_isShowingPairing) {
          return StoreConnector<AppState, PairingDialogViewModel>(
            distinct: true,
            converter: (Store<AppState> store) =>
                sl<PairingDialogViewModel>(param1: store),
            builder: (BuildContext context, PairingDialogViewModel pairing) =>
                DovahDialog(
                  title: pairing.title,
                  child: const PairingSection.parentOwned(),
                ),
          );
        }
        final tokens = context.dovahTokens;
        const String searchingMessage = 'Searching for DovahLink on this PC…';
        final HostCardViewData? selectedCandidate = viewModel.selectedCandidate;
        final bool isChecking =
            selectedCandidate != null &&
            viewModel.pairingPhase == PairingPhase.connecting;
        final bool isFailure =
            viewModel.status == ConnectionDiscoveryStatus.failed;
        final String retryMessage = isFailure
            ? (viewModel.failure ?? ConnectionFailureReason.unknown).message
            : 'No new local Hosts found.';
        final Widget content =
            selectedCandidate != null &&
                viewModel.pairingPhase == PairingPhase.disconnected
            ? PairingProgress(
                phase: PairingPhase.disconnected,
                hostName: selectedCandidate.title,
                onClose: () => Navigator.of(context).maybePop(),
              )
            : switch (viewModel.status) {
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
                      const SizedBox(
                        width: DovahDialogMetrics.progressStatusGap,
                      ),
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
                    when viewModel.candidates.isNotEmpty || isChecking =>
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Local Host found.',
                        style: TextStyle(
                          color: tokens.success,
                          fontSize: DovahThemeTokens.compactFontSize,
                          height: DovahThemeTokens.bodyLineHeight,
                        ),
                      ),
                      const SizedBox(
                        height: DovahDialogMetrics.progressStatusGap,
                      ),
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
                          const SizedBox(
                            width: DovahRootMetrics.sectionLabelGap,
                          ),
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
                      const SizedBox(
                        height: DovahRootMetrics.sectionLabelBottomGap,
                      ),
                      LayoutBuilder(
                        builder: (BuildContext context, BoxConstraints constraints) {
                          const double gap = DovahRootMetrics.listGap;
                          final List<HostCardViewData> candidates = isChecking
                              ? [selectedCandidate]
                              : viewModel.candidates;
                          final double cardWidth = candidates.length == 1
                              ? constraints.maxWidth
                              : (constraints.maxWidth - gap) / 2;

                          return Wrap(
                            spacing: gap,
                            runSpacing: gap,
                            children: [
                              for (final HostCardViewData candidate
                                  in candidates)
                                SizedBox(
                                  width: cardWidth,
                                  child: DiscoverCandidateCard(
                                    key: Key(
                                      'discover-candidate-${candidate.host.hostId}',
                                    ),
                                    title: candidate.title,
                                    subtitle: candidate.subtitle,
                                    isChecking: isChecking,
                                    onTap: viewModel.canSelectCandidate
                                        ? () {
                                            if (viewModel.onSelectCandidate(
                                              candidate,
                                            )) {
                                              _ownsCandidatePairingLifecycle =
                                                  true;
                                            }
                                          }
                                        : null,
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
                    const SizedBox(
                      height: DovahDialogMetrics.progressStatusGap,
                    ),
                    DovahButton(
                      key: const Key('discover-retry-button'),
                      label: 'Search again',
                      variant: DovahButtonVariant.secondary,
                      onPressed: viewModel.canDiscover
                          ? viewModel.onDiscover
                          : null,
                    ),
                  ],
                ),
              };

        return DovahDialog(title: 'Discover Skyrim', child: content);
      },
    );
  }
}
