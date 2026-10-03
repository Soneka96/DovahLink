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
        DovahLinkKnownHostInvalidation,
        DovahLinkKnownHostState;

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
  final Map<
    Store<AppState>,
    ({
      StreamSubscription<List<DovahLinkKnownHostState>> knownHosts,
      StreamSubscription<List<DovahLinkHost>> candidates,
      StreamSubscription<DovahLinkKnownHostInvalidation> invalidations,
    })
  >
  _subscriptions = {};

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
        _connectionDiscoveryRequested(store);
    }
  }

  /// Implements [IConnectionMiddleware.initialize].
  @override
  void initialize(Store<AppState> store) {
    if (_isShuttingDown || _subscriptions.containsKey(store)) {
      return;
    }
    final DovahLinkClient client = sl<DovahLinkClient>();
    final StreamSubscription<List<DovahLinkKnownHostState>> knownHosts = client
        .hosts
        .knownHostStatesChanges
        .listen(
          (List<DovahLinkKnownHostState> sdkKnownHosts) {
            if (!_isShuttingDown) {
              store.dispatch(
                ConnectionKnownHostsChangedAction(
                  sdkKnownHosts
                      .map(HostMapper.fromSdkKnownHostState)
                      .toList(growable: false),
                ),
              );
            }
          },
          onError: (Object error, StackTrace stackTrace) {
            if (!_isShuttingDown) {
              store.dispatch(
                const ConnectionKnownHostsObservationFailedAction(),
              );
            }
            FlutterError.reportError(
              FlutterErrorDetails(
                exception: error,
                stack: stackTrace,
                library: 'DovahLink Known Hosts observation',
              ),
            );
          },
        );
    final StreamSubscription<List<DovahLinkHost>> candidates = client
        .pairing
        .candidates
        .listen(
          (List<DovahLinkHost> sdkCandidates) {
            if (!_isShuttingDown) {
              store.dispatch(
                ConnectionCandidatesChangedAction(
                  sdkCandidates.map(HostMapper.fromSdk).toList(growable: false),
                ),
              );
            }
          },
          onError: (Object error, StackTrace stackTrace) {
            if (!_isShuttingDown) {
              store.dispatch(
                ConnectionDiscoveryFailedAction(
                  ConnectionFailureReason.fromDiscoveryError(error),
                ),
              );
            }
            FlutterError.reportError(
              FlutterErrorDetails(
                exception: error,
                stack: stackTrace,
                library: 'DovahLink candidates observation',
              ),
            );
          },
        );
    final StreamSubscription<DovahLinkKnownHostInvalidation> invalidations =
        client.connections.knownHostInvalidations.listen((
          DovahLinkKnownHostInvalidation invalidation,
        ) {
          if (!_isShuttingDown) {
            store.dispatch(
              ConnectionKnownHostInvalidatedAction(
                hostId: invalidation.hostId.value,
                reason: invalidation.reason,
              ),
            );
          }
        });
    _subscriptions[store] = (
      knownHosts: knownHosts,
      candidates: candidates,
      invalidations: invalidations,
    );
  }

  /// Implements [IConnectionMiddleware.shutdown].
  @override
  Future<void> shutdown() {
    _isShuttingDown = true;
    return _shutdownFuture ??= Future.wait<void>([
      for (final ({
            StreamSubscription<List<DovahLinkKnownHostState>> knownHosts,
            StreamSubscription<List<DovahLinkHost>> candidates,
            StreamSubscription<DovahLinkKnownHostInvalidation> invalidations,
          })
          subscriptions
          in _subscriptions.values) ...[
        subscriptions.knownHosts.cancel(),
        subscriptions.candidates.cancel(),
        subscriptions.invalidations.cancel(),
      ],
    ]).then((_) => _subscriptions.clear());
  }

  /// Requests local Host discovery through the SDK and maps its result to Redux actions.
  /// @param store The application store receiving discovery state transitions.
  Future<void> _connectionDiscoveryRequested(Store<AppState> store) async {
    if (_isShuttingDown ||
        !ConnectionSelectors.canDiscoverSelector(store.state)) {
      return;
    }

    store.dispatch(const ConnectionDiscoveryStartedAction());
    try {
      final List<DovahLinkHost> discoveredHosts = await sl<DovahLinkClient>()
          .pairing
          .discoverHosts();
      if (_isShuttingDown) {
        return;
      }
      store.dispatch(
        ConnectionDiscoverySucceededAction(
          hasCandidates: discoveredHosts.isNotEmpty,
        ),
      );
    } on Object catch (error) {
      if (_isShuttingDown) {
        return;
      }
      store.dispatch(
        ConnectionDiscoveryFailedAction(
          ConnectionFailureReason.fromDiscoveryError(error),
        ),
      );
    }
  }
}
