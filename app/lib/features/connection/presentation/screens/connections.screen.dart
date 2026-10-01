import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/sections/appearance.section.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connections_screen.viewmodel.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/connections_footer.widget.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/connections_hero.widget.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/connections_host_section.widget.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/discover_dialog.widget.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/root_header.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_dialog.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_environment_background.widget.dart';

/// The root screen: DovahLink's branded header, the "Connections" title with its Discover Skyrim
/// action, and durable Known Hosts available to select, over the theme's atmosphere. Selecting a
/// Known Host records the relationship and opens the pairing dialog; discovery candidates are
/// presented separately by the Discover flow. The header's appearance action opens the theme
/// picker. Content is capped at a comfortable reading width and scrolls both ways below its minimum
/// width.
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
                                  onOpenAppearance: () =>
                                      DovahDialog.show<void>(
                                        context,
                                        title: 'Appearance',
                                        child: const AppearanceSection(),
                                      ),
                                ),
                                SizedBox(height: metrics.contentTopPadding),
                                ConnectionsHero(
                                  onDiscover: viewModel.canDiscover
                                      ? () async {
                                          final HostCardViewData? candidate =
                                              await DiscoverDialog.show(
                                                context,
                                              );
                                          if (candidate == null ||
                                              !context.mounted) {
                                            return;
                                          }
                                          await PairingDialog.show(
                                            context,
                                            startOnInit: false,
                                          );
                                        }
                                      : null,
                                ),
                                SizedBox(height: metrics.heroBottomGap),
                                // TODO: Add dedicated Known Host management/removal UI once
                                // removal semantics are defined.
                                ConnectionsHostSection(
                                  cards: viewModel.hostCards,
                                  onSelectHost: (HostCardViewData card) {
                                    viewModel.onSelectHost(card);
                                    PairingDialog.show(context);
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
