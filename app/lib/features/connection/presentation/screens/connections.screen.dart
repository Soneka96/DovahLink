import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connections_screen.viewmodel.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/connections_footer.widget.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/connections_hero.widget.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/connections_host_section.widget.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/discover_dialog.widget.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/root_header.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_dialog.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_state_layout.widget.dart';
import 'package:dovahlink_client/features/settings/presentation/widgets/settings_dialog.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_environment_background.widget.dart';

/// The root screen: DovahLink's branded header, the "Connections" title with its Discover Skyrim
/// action, and durable Known Hosts over the theme's atmosphere. Online and Pair again cards open
/// authentication; Pair again asks for confirmation first, and Connected cards re-enter their
/// admitted Session Shell directly. Discovery candidates are presented separately by the Discover
/// flow. The header's Settings action opens the shared settings dialog. Content is capped at a comfortable
/// reading width and scrolls both ways below its minimum width.
class ConnectionsScreen extends StatelessWidget {
  /// Creates the connections screen.
  const ConnectionsScreen({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, ConnectionsScreenViewModel>(
      distinct: true,
      converter: (Store<AppState> store) =>
          sl<ConnectionsScreenViewModel>(param1: store),
      builder: (BuildContext context, ConnectionsScreenViewModel viewModel) {
        final DovahRootMetrics metrics = context.dovahRootMetrics;

        return Scaffold(
          body: DovahEnvironmentBackground(
            child: LayoutBuilder(
              builder: (BuildContext context, BoxConstraints constraints) {
                return SingleChildScrollView(
                  scrollDirection: Axis.horizontal,
                  child: SizedBox(
                    width: math.max(
                      constraints.maxWidth,
                      DovahRootMetrics.minimumWidth,
                    ),
                    child: SingleChildScrollView(
                      child: Center(
                        child: Padding(
                          padding: EdgeInsets.symmetric(
                            horizontal: metrics.sideMargin,
                          ),
                          child: ConstrainedBox(
                            constraints: const BoxConstraints(
                              maxWidth: DovahRootMetrics.contentMaxWidth,
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                RootHeader(
                                  onOpenSettings: () =>
                                      SettingsDialog.show(context),
                                ),
                                SizedBox(height: metrics.contentTopPadding),
                                ConnectionsHero(
                                  onDiscover: viewModel.canDiscover
                                      ? () async {
                                          await DiscoverDialog.show(context);
                                        }
                                      : null,
                                ),
                                SizedBox(height: metrics.heroBottomGap),
                                // TODO: Add dedicated Known Host management/removal UI once
                                // removal semantics are defined.
                                ConnectionsHostSection(
                                  cards: viewModel.hostCards,
                                  onReenterConnectedHost:
                                      viewModel.onReenterConnectedHost,
                                  onShowOfflineHost: (HostCardViewData card) {
                                    DovahDialog.show<void>(
                                      context,
                                      title: 'Skyrim isn’t running',
                                      child: PairingStateLayout(
                                        mark: const PairingMark(
                                          icon: Icons.radio_button_unchecked,
                                        ),
                                        heading: '${card.title} is offline',
                                        body: card.pairingRequired
                                            ? 'Start Skyrim, then choose Pair again when ${card.title} is Online.'
                                            : 'Start Skyrim and DovahLink will reconnect automatically when the game becomes available.',
                                        children: [
                                          DovahButton(
                                            label: 'Close',
                                            variant:
                                                DovahButtonVariant.secondary,
                                            onPressed: () => Navigator.of(
                                              context,
                                            ).maybePop(),
                                          ),
                                        ],
                                      ),
                                    );
                                  },
                                  onSelectHost: (HostCardViewData card) {
                                    if (card.state ==
                                        DovahConnectionCardState.repair) {
                                      DovahDialog.show<void>(
                                        context,
                                        title: 'Pairing required',
                                        child: PairingStateLayout(
                                          mark: const PairingMark(
                                            icon: Icons.autorenew,
                                          ),
                                          heading: 'Pair ${card.title} again',
                                          body:
                                              'Your connection changed in Skyrim. Pair again to restore automatic connections.',
                                          children: [
                                            DovahButton(
                                              label: 'Cancel',
                                              variant:
                                                  DovahButtonVariant.secondary,
                                              onPressed: () => Navigator.of(
                                                context,
                                              ).maybePop(),
                                            ),
                                            DovahButton(
                                              label: 'Pair again',
                                              onPressed: () {
                                                Navigator.of(
                                                  context,
                                                ).maybePop();
                                                viewModel.onSelectHost(card);
                                                PairingDialog.show(
                                                  context,
                                                  requestCodeAfterConfirmedRepair:
                                                      true,
                                                );
                                              },
                                            ),
                                          ],
                                        ),
                                      );
                                    } else {
                                      viewModel.onSelectHost(card);
                                      PairingDialog.show(context);
                                    }
                                  },
                                ),
                                const ConnectionsFooter(),
                                const SizedBox(
                                  height: DovahRootMetrics.contentBottomPadding,
                                ),
                              ],
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
        );
      },
    );
  }
}
