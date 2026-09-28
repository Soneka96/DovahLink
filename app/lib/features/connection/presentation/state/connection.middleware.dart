import 'dart:async';

import 'package:flutter/foundation.dart' show FlutterError, FlutterErrorDetails;

import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/host.mapper.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkClient,
        DovahLinkHost,
        IClientStorage,
        IDovahLinkDiscoveryService,
        UnsupportedClientStorage;

/// Defines the connection feature's Redux middleware contract.
abstract interface class IConnectionMiddleware {
  /// Handles one Redux [action] with the supplied [store] and [next] dispatcher.
  void call(Store<AppState> store, dynamic action, NextDispatcher next);

  /// Starts SDK Known Host observation for [store]. Repeated calls are safe.
  /// @param store The Redux store that receives mapped SDK state.
  void initialize(Store<AppState> store);

  /// Cancels every SDK subscription owned by this middleware.
  /// @return A future completing after all subscriptions are cancelled.
  Future<void> shutdown();
}

/// Orchestrates discovery and mirrors SDK Known Host state into Redux.
class ConnectionMiddleware extends MiddlewareClass<AppState>
    implements IConnectionMiddleware {
  /// The active SDK subscriptions, keyed by their Redux stores.
  final Map<Store<AppState>, StreamSubscription<List<DovahLinkHost>>>
  _knownHostSubscriptions =
      <Store<AppState>, StreamSubscription<List<DovahLinkHost>>>{};

  /// The shared cancellation future returned to repeated shutdown callers.
  Future<void>? _shutdownFuture;

  /// Whether shutdown has begun and no new subscription can be created.
  bool _isShuttingDown = false;

  /// Creates connection middleware without an active SDK subscription.
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

  /// Implements [IConnectionMiddleware.initialize].
  @override
  void initialize(Store<AppState> store) {
    if (_isShuttingDown ||
        _knownHostSubscriptions.containsKey(store) ||
        sl<IClientStorage>() is UnsupportedClientStorage) {
      return;
    }
    _knownHostSubscriptions[store] = sl<DovahLinkClient>().knownHostsChanges
        .listen(
          (List<DovahLinkHost> sdkHosts) {
            if (!_isShuttingDown) {
              store.dispatch(
                ConnectionKnownHostsChangedAction(
                  sdkHosts.map(HostMapper.fromSdk).toList(growable: false),
                ),
              );
            }
          },
          onError: (Object error, StackTrace stackTrace) {
            FlutterError.reportError(
              FlutterErrorDetails(
                exception: error,
                stack: stackTrace,
                library: 'DovahLink Known Hosts observation',
              ),
            );
          },
        );
  }

  /// Implements [IConnectionMiddleware.shutdown].
  @override
  Future<void> shutdown() {
    _isShuttingDown = true;
    return _shutdownFuture ??= Future.wait<void>(
      _knownHostSubscriptions.values.map(
        (StreamSubscription<List<DovahLinkHost>> subscription) =>
            subscription.cancel(),
      ),
    ).then((_) => _knownHostSubscriptions.clear());
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
      final List<DovahLinkHost> discoveredHosts =
          await sl<IDovahLinkDiscoveryService>().discover();
      store.dispatch(
        ConnectionDiscoverySucceededAction(
          discoveredHosts.map(HostMapper.fromSdk).toList(growable: false),
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
