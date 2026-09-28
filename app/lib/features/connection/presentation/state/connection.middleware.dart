import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show DovahLinkHost, IDovahLinkDiscoveryService;

/// Defines the connection feature's Redux middleware contract.
abstract interface class IConnectionMiddleware {
  /// Handles one Redux [action] with the supplied [store] and [next] dispatcher.
  void call(Store<AppState> store, dynamic action, NextDispatcher next);
}

/// Orchestrates Host discovery through the SDK-owned discovery operation.
class ConnectionMiddleware extends MiddlewareClass<AppState>
    implements IConnectionMiddleware {
  /// Creates connection middleware without retained mutable state.
  ConnectionMiddleware();

  /// See [MiddlewareClass.call].
  @override
  void call(Store<AppState> store, dynamic action, NextDispatcher next) {
    next(action);
    switch (action) {
      case ConnectionDiscoveryRequestedAction _:
        _connectionDiscoveryRequested(store, action);
    }
  }

  /// Handles [ConnectionDiscoveryRequestedAction] through the SDK boundary.
  Future<void> _connectionDiscoveryRequested(
    Store<AppState> store,
    ConnectionDiscoveryRequestedAction action,
  ) async {
    if (!ConnectionSelectors.canDiscoverSelector(store.state)) {
      return;
    }

    store.dispatch(const ConnectionDiscoveryStartedAction());
    try {
      final DovahLinkHost? discoveredHost =
          await sl<IDovahLinkDiscoveryService>().discoverLocalHost();
      store.dispatch(
        ConnectionDiscoverySucceededAction(
          discoveredHost == null
              ? const <Host>[]
              : <Host>[
                  Host(displayName: 'Local Host', uri: discoveredHost.endpoint),
                ],
        ),
      );
    } on Object catch (error) {
      store.dispatch(
        ConnectionDiscoveryFailedAction(
          ConnectionFailureReason.fromDiscoveryError(error),
        ),
      );
    }
  }
}
