import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/bridge_list_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Displays the Bridges available to select; selecting one navigates to pairing.
class BridgeListScreen extends StatelessWidget {
  /// Creates the Bridge-list screen.
  const BridgeListScreen({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, BridgeListScreenViewModel>(
      distinct: true,
      converter: (Store<AppState> store) =>
          sl<BridgeListScreenViewModel>(param1: store),
      builder: (BuildContext context, BridgeListScreenViewModel viewModel) {
        return Scaffold(
          body: ListView(
            children: [
              for (final BridgeEntity bridge in viewModel.bridges)
                ListTile(
                  key: Key('bridge-tile-${bridge.displayName}'),
                  title: Text(bridge.displayName),
                  onTap: () => viewModel.onSelectBridge(bridge),
                ),
            ],
          ),
        );
      },
    );
  }
}
