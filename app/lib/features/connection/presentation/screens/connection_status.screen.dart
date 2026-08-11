import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connection_status_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Displays the current connection lifecycle and safe error state.
class ConnectionStatusScreen extends StatelessWidget {
  /// Creates the connection status screen.
  const ConnectionStatusScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, ConnectionStatusScreenViewModel>(
      distinct: true,
      converter: (Store<AppState> store) =>
          sl<ConnectionStatusScreenViewModel>(param1: store),
      builder:
          (BuildContext context, ConnectionStatusScreenViewModel viewModel) {
            final String? sessionId = viewModel.sessionId;
            final String? error = viewModel.error;
            return Scaffold(
              body: Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      'DovahLink',
                      style: Theme.of(context).textTheme.headlineSmall,
                    ),
                    Text(
                      viewModel.statusLabel,
                      key: const Key('connection-status'),
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    if (sessionId != null) Text('Session: $sessionId'),
                    if (error != null)
                      Text(
                        error,
                        key: const Key('connection-error'),
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          color: Theme.of(context).colorScheme.error,
                        ),
                      ),
                  ],
                ),
              ),
            );
          },
    );
  }
}
