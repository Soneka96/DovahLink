import 'dart:async';

import 'package:flutter/foundation.dart' show FlutterError, FlutterErrorDetails;

import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkClient,
        DovahLinkConnectionState,
        DovahLinkStateArea,
        DovahLinkTrustState,
        IDovahLinkCurrentHost,
        StateSynchronization;

/// Defines app-lifetime SDK state observation for the live-state projection.
abstract interface class ILiveStateMiddleware {
  /// Forwards one Redux [action] without coupling subscriptions to a route.
  /// @param store The application store receiving projected live state.
  /// @param action The Redux action being processed.
  /// @param next The next middleware or reducer in the chain.
  void call(Store<AppState> store, dynamic action, NextDispatcher next);

  /// Observes SDK lifecycle and gameplay streams for [store]. Repeated calls are safe.
  /// @param store The application store receiving projected live state.
  void initialize(Store<AppState> store);

  /// Cancels every lifecycle and gameplay subscription owned by this middleware.
  /// @return A future completing after owned listeners are cancelled.
  Future<void> shutdown();
}

/// Forwards public SDK gameplay synchronization values into the Redux slice.
class LiveStateMiddleware extends MiddlewareClass<AppState>
    implements ILiveStateMiddleware {
  /// The complete set of gameplay domains required by the future Overview.
  static const List<DovahLinkStateArea> _requiredAreas = <DovahLinkStateArea>[
    DovahLinkStateArea.characterVitals,
    DovahLinkStateArea.characterXp,
    DovahLinkStateArea.characterLevel,
    DovahLinkStateArea.characterIdentity,
    DovahLinkStateArea.characterSupernaturalTraits,
    DovahLinkStateArea.playerLocation,
    DovahLinkStateArea.gameTime,
    DovahLinkStateArea.trackedQuests,
  ];

  /// SDK lifecycle listeners keyed by the Redux stores they notify.
  final Map<Store<AppState>, StreamSubscription<DovahLinkConnectionState>>
  _lifecycleSubscriptions =
      <Store<AppState>, StreamSubscription<DovahLinkConnectionState>>{};

  /// Cancellation callbacks for domain listeners attached to each admitted session.
  final Map<Store<AppState>, List<Future<void> Function()>>
  _stateSubscriptionCancellations =
      <Store<AppState>, List<Future<void> Function()>>{};

  /// Active admitted-session observation identities keyed by the stores they serve.
  final Map<Store<AppState>, Object> _activeObservationTokens =
      <Store<AppState>, Object>{};

  /// Stores whose SDK desired areas were established for this middleware lifetime.
  final Set<Store<AppState>> _requestedDesiredAreaStores = <Store<AppState>>{};

  /// The shared shutdown operation returned to repeated callers.
  Future<void>? _shutdownFuture;

  /// Whether shutdown has begun and no new listeners or projections are allowed.
  bool _isShuttingDown = false;

  /// Creates middleware without attaching to the SDK until store initialization.
  LiveStateMiddleware();

  /// See [MiddlewareClass.call].
  @override
  void call(Store<AppState> store, dynamic action, NextDispatcher next) {
    next(action);
    switch (action) {
      case PairingSessionTrustedAction _:
        _sessionTrusted(store);
      case final PairingDisposedAction disposedAction:
        _pairingDisposed(store, disposedAction);
    }
  }

  /// Implements [ILiveStateMiddleware.initialize].
  @override
  void initialize(Store<AppState> store) {
    if (_isShuttingDown || _lifecycleSubscriptions.containsKey(store)) {
      return;
    }
    final DovahLinkClient client = sl<DovahLinkClient>();
    final StreamSubscription<DovahLinkConnectionState> subscription = client
        .connections
        .stateChanges
        .listen(
          (DovahLinkConnectionState state) =>
              _connectionStateChanged(store, client, state),
          onError: (Object error, StackTrace stackTrace) =>
              _reportObservationFailure(error, stackTrace),
        );
    _lifecycleSubscriptions[store] = subscription;
  }

  /// Implements [ILiveStateMiddleware.shutdown].
  @override
  Future<void> shutdown() {
    _isShuttingDown = true;
    _activeObservationTokens.clear();
    return _shutdownFuture ??=
        Future.wait<void>([
          for (final StreamSubscription<DovahLinkConnectionState> subscription
              in _lifecycleSubscriptions.values)
            subscription.cancel(),
          for (final List<Future<void> Function()> cancellations
              in _stateSubscriptionCancellations.values)
            for (final Future<void> Function() cancel in cancellations)
              cancel(),
        ]).then((_) {
          _lifecycleSubscriptions.clear();
          _stateSubscriptionCancellations.clear();
          _activeObservationTokens.clear();
          _requestedDesiredAreaStores.clear();
        });
  }

  /// Starts or stops state observation only at SDK session lifecycle boundaries.
  /// @param store The store to receive lifecycle changes.
  /// @param client The SDK client whose public views own lifecycle and gameplay state.
  /// @param state The current SDK connection lifecycle state.
  void _connectionStateChanged(
    Store<AppState> store,
    DovahLinkClient client,
    DovahLinkConnectionState state,
  ) {
    if (_isShuttingDown) {
      return;
    }
    if (state == DovahLinkConnectionState.connected) {
      if (client.currentHost.trustState == DovahLinkTrustState.trusted) {
        _attachStateStreams(store, client);
      }
      return;
    }
    if (state == DovahLinkConnectionState.disconnected) {
      _requestedDesiredAreaStores.remove(store);
      _endAdmittedSession(store);
      return;
    }
    if (state == DovahLinkConnectionState.administrativelyInvalidated) {
      _requestedDesiredAreaStores.remove(store);
      _endAdmittedSession(store);
    }
  }

  /// Starts observation after pairing establishes trust on an already-connected session.
  /// @param store The store receiving projected gameplay state.
  void _sessionTrusted(Store<AppState> store) {
    if (_isShuttingDown) {
      return;
    }
    final DovahLinkClient client = sl<DovahLinkClient>();
    if (client.connections.state == DovahLinkConnectionState.connected &&
        client.currentHost.trustState == DovahLinkTrustState.trusted) {
      _attachStateStreams(store, client);
      final Object? token = _activeObservationTokens[store];
      if (token != null && _requestedDesiredAreaStores.add(store)) {
        unawaited(_requestRequiredAreas(store, client.currentHost, token));
      }
    }
  }

  /// Attaches each replaying SDK state stream once for an admitted session.
  /// @param store The store to receive state changes.
  /// @param client The SDK client exposing the public state groups.
  void _attachStateStreams(Store<AppState> store, DovahLinkClient client) {
    if (_isShuttingDown || _stateSubscriptionCancellations.containsKey(store)) {
      return;
    }
    final Object token = Object();
    _activeObservationTokens[store] = token;
    _stateSubscriptionCancellations[store] = <Future<void> Function()>[];
    final currentHost = client.currentHost;
    final character = currentHost.character;
    _observe(
      store,
      token,
      character.vitalsChanges,
      CharacterVitalsSynchronizationChangedAction.new,
    );
    _observe(
      store,
      token,
      character.xpChanges,
      CharacterXpSynchronizationChangedAction.new,
    );
    _observe(
      store,
      token,
      character.levelChanges,
      CharacterLevelSynchronizationChangedAction.new,
    );
    _observe(
      store,
      token,
      character.identityChanges,
      CharacterIdentitySynchronizationChangedAction.new,
    );
    _observe(
      store,
      token,
      character.supernaturalTraitsChanges,
      CharacterSupernaturalTraitsSynchronizationChangedAction.new,
    );
    _observe(
      store,
      token,
      currentHost.playerLocationChanges,
      PlayerLocationSynchronizationChangedAction.new,
    );
    _observe(
      store,
      token,
      currentHost.gameTimeChanges,
      GameTimeSynchronizationChangedAction.new,
    );
    _observe(
      store,
      token,
      currentHost.trackedQuestsChanges,
      TrackedQuestsSynchronizationChangedAction.new,
    );
  }

  /// Clears request deduplication only when pairing intentionally disconnects.
  /// @param store The store whose pairing flow ended.
  /// @param action The pairing-disposal intent captured before teardown.
  void _pairingDisposed(Store<AppState> store, PairingDisposedAction action) {
    if (!action.wasTrusted) {
      _requestedDesiredAreaStores.remove(store);
    }
  }

  /// Subscribes to one typed SDK stream and forwards its value unchanged.
  /// @param store The store receiving the typed action.
  /// @param token The observation identity that owns this stream listener.
  /// @param changes The SDK synchronization stream for one domain.
  /// @param action Creates the domain-specific Redux action.
  void _observe<T>(
    Store<AppState> store,
    Object token,
    Stream<StateSynchronization<T>> changes,
    Object Function(StateSynchronization<T>) action,
  ) {
    final StreamSubscription<StateSynchronization<T>> subscription = changes
        .listen(
          (StateSynchronization<T> synchronization) {
            if (_isCurrentObservation(store, token)) {
              store.dispatch(action(synchronization));
            }
          },
          onError: (Object error, StackTrace stackTrace) {
            if (_isCurrentObservation(store, token)) {
              _reportObservationFailure(error, stackTrace);
            }
          },
        );
    _stateSubscriptionCancellations[store]?.add(subscription.cancel);
  }

  /// Adds every required domain through the SDK's additive desired-set API.
  /// @param store The store whose session remains observed.
  /// @param currentHost The SDK current-Host view owning subscription intent.
  /// @param token The observation identity that owns the request sequence.
  Future<void> _requestRequiredAreas(
    Store<AppState> store,
    IDovahLinkCurrentHost currentHost,
    Object token,
  ) async {
    for (final DovahLinkStateArea area in _requiredAreas) {
      if (!_isCurrentObservation(store, token)) {
        return;
      }
      try {
        final Set<DovahLinkStateArea> rejected = await currentHost
            .subscribeStateArea(area);
        if (!_isCurrentObservation(store, token)) {
          return;
        }
        if (rejected.contains(area)) {
          _reportSubscriptionRejection(area);
        }
      } on Object catch (error, stackTrace) {
        if (!_isCurrentObservation(store, token)) {
          return;
        }
        _reportObservationFailure(error, stackTrace);
      }
    }
  }

  /// Cancels gameplay listeners and clears the ended session's Redux projection.
  /// @param store The store whose admitted session has ended.
  void _endAdmittedSession(Store<AppState> store) {
    _activeObservationTokens.remove(store);
    final List<Future<void> Function()>? cancellations =
        _stateSubscriptionCancellations.remove(store);
    if (cancellations == null) {
      return;
    }
    for (final Future<void> Function() cancel in cancellations) {
      unawaited(cancel());
    }
    if (!_isShuttingDown) {
      store.dispatch(const SessionLiveStateResetAction());
    }
  }

  /// Checks whether callbacks or asynchronous work still belong to the active session.
  /// @param store The Redux store owning the observation.
  /// @param token The observation identity captured by that work.
  /// @return Whether the middleware still accepts work for this observation.
  bool _isCurrentObservation(Store<AppState> store, Object token) =>
      !_isShuttingDown && identical(_activeObservationTokens[store], token);

  /// Reports an SDK stream or subscription failure through Flutter's diagnostics channel.
  /// @param error The observed exception.
  /// @param stackTrace The exception's stack trace.
  void _reportObservationFailure(Object error, StackTrace stackTrace) {
    if (_isShuttingDown) {
      return;
    }
    FlutterError.reportError(
      FlutterErrorDetails(
        exception: error,
        stack: stackTrace,
        library: 'DovahLink live-state observation',
      ),
    );
  }

  /// Reports a state area that the Host declined to accept.
  /// @param area The SDK state area rejected by the Host.
  void _reportSubscriptionRejection(DovahLinkStateArea area) {
    if (_isShuttingDown) {
      return;
    }
    FlutterError.reportError(
      FlutterErrorDetails(
        exception: StateError('The Host rejected the $area state area.'),
        library: 'DovahLink live-state subscription',
      ),
    );
  }
}
