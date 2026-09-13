import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/host_list_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Displays the Hosts available to select; selecting one navigates to pairing.
class HostListScreen extends StatelessWidget {
  /// Creates the Host-list screen.
  const HostListScreen({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, HostListScreenViewModel>(
      distinct: true,
      converter: (Store<AppState> store) =>
          sl<HostListScreenViewModel>(param1: store),
      builder: (BuildContext context, HostListScreenViewModel viewModel) {
        return Scaffold(
          body: ListView(
            children: [
              for (final HostEntity host in viewModel.hosts)
                ListTile(
                  key: Key('host-tile-${host.displayName}'),
                  title: Text(host.displayName),
                  onTap: () => viewModel.onSelectHost(host),
                ),
            ],
          ),
        );
      },
    );
  }
}
