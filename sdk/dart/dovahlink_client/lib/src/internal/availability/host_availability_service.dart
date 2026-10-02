import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_state.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Owns runtime availability values and complete Known Host session projections.
abstract interface class IHostAvailabilityService {
  /// Emits complete Known Host runtime snapshots and storage observation errors.
  Stream<List<DovahLinkKnownHostState>> get knownHostStatesChanges;

  /// Sets one Host's runtime availability, or clears it when [availability] is `unknown`.
  /// @param hostId The durable Known Host whose runtime state changes.
  /// @param availability The new runtime reachability state.
  void setAvailability(
    DovahLinkHostId hostId,
    DovahLinkHostAvailability availability,
  );

  /// Sets the exact Known Host session projection, clearing it when [hostId] is `null`.
  void setSessionState(
    DovahLinkHostId? hostId,
    DovahLinkKnownHostSessionState sessionState,
  );

  /// Cancels durable-state observation and closes the runtime projection stream.
  Future<void> close();
}

/// Implements [IHostAvailabilityService] over durable Known Host metadata and session evidence.
class HostAvailabilityService implements IHostAvailabilityService {
  /// The owner of durable Known Host metadata and its replayable snapshots.
  final IClientStateService _clientStateService;

  /// The runtime availability values, keyed by stable Host ID.
  final Map<String, DovahLinkHostAvailability> _availability =
      <String, DovahLinkHostAvailability>{};

  /// The current exact Known Host session projection.
  DovahLinkHostId? _sessionHostId;

  /// The session phase associated with [_sessionHostId].
  DovahLinkKnownHostSessionState _sessionState =
      DovahLinkKnownHostSessionState.disconnected;

  /// Publishes complete projection changes and storage errors.
  final StreamController<List<DovahLinkKnownHostState>> _changes =
      StreamController<List<DovahLinkKnownHostState>>.broadcast();

  /// The latest derived complete projection, or `null` until storage provides a snapshot.
  List<DovahLinkKnownHostState>? _currentStates;

  /// The latest storage observation error, replayed to new subscribers until recovery.
  Object? _lastError;

  /// The stack trace associated with [_lastError].
  StackTrace? _lastErrorStackTrace;

  /// The durable-state subscription, started when state is first observed or updated.
  StreamSubscription<List<PersistedKnownHost>>? _knownHostsSubscription;

  /// The terminal stream cleanup operation shared by repeated calls.
  Future<void>? _closeFuture;

  /// Whether the service has completed or begun terminal cleanup.
  bool _isClosed = false;

  /// Creates an availability owner over the client's durable Known Hosts.
  /// @param clientStateService Supplies complete committed Host metadata snapshots.
  HostAvailabilityService({required IClientStateService clientStateService})
    : _clientStateService = clientStateService;

  /// Implements [IHostAvailabilityService.knownHostStatesChanges].
  @override
  Stream<List<DovahLinkKnownHostState>> get knownHostStatesChanges =>
      Stream<List<DovahLinkKnownHostState>>.multi((
        MultiStreamController<List<DovahLinkKnownHostState>> sink,
      ) {
        _ensureKnownHostsObservation();
        final List<DovahLinkKnownHostState>? current = _currentStates;
        if (current != null) {
          sink.add(current);
        }
        final Object? error = _lastError;
        if (error != null) {
          sink.addError(error, _lastErrorStackTrace);
        }
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            _changes.stream.listen(sink.add, onError: sink.addError);
        sink.onCancel = subscription.cancel;
      }, isBroadcast: true);

  /// Implements [IHostAvailabilityService.setAvailability].
  @override
  void setAvailability(
    DovahLinkHostId hostId,
    DovahLinkHostAvailability availability,
  ) {
    if (_isClosed) {
      return;
    }
    _ensureKnownHostsObservation();
    if (availability == DovahLinkHostAvailability.unknown) {
      _availability.remove(hostId.value);
    } else {
      _availability[hostId.value] = availability;
    }
    _publishCurrentStates();
  }

  /// Implements [IHostAvailabilityService.setSessionState].
  @override
  void setSessionState(
    DovahLinkHostId? hostId,
    DovahLinkKnownHostSessionState sessionState,
  ) {
    if (_isClosed) {
      return;
    }
    final DovahLinkKnownHostSessionState nextState = hostId == null
        ? DovahLinkKnownHostSessionState.disconnected
        : sessionState;
    if (hostId == _sessionHostId && nextState == _sessionState) {
      return;
    }
    _sessionHostId = hostId;
    _sessionState = nextState;
    _publishCurrentStates();
  }

  /// Implements [IHostAvailabilityService.close].
  @override
  Future<void> close() {
    final Future<void>? closing = _closeFuture;
    if (closing != null) {
      return closing;
    }
    _isClosed = true;
    final StreamSubscription<List<PersistedKnownHost>>? subscription =
        _knownHostsSubscription;
    _knownHostsSubscription = null;
    final Future<void> closeChanges = subscription == null
        ? _changes.close()
        : subscription.cancel().then((_) => _changes.close());
    _closeFuture = closeChanges;
    return closeChanges;
  }

  /// Starts the single durable-state listener on first use.
  void _ensureKnownHostsObservation() {
    if (_isClosed || _knownHostsSubscription != null) {
      return;
    }
    _knownHostsSubscription = _clientStateService.knownHostsChanges.listen(
      _updateKnownHosts,
      onError: _reportStorageError,
    );
  }

  /// Reconciles runtime values with the latest durable Known Host collection.
  /// @param hosts The latest metadata snapshot from client state.
  void _updateKnownHosts(List<PersistedKnownHost> hosts) {
    if (_isClosed) {
      return;
    }
    final bool recoveredFromError = _lastError != null;
    final Set<String> hostIds = hosts
        .map((PersistedKnownHost relationship) => relationship.host.hostId)
        .toSet();
    _availability.removeWhere(
      (String hostId, DovahLinkHostAvailability _) => !hostIds.contains(hostId),
    );
    _lastError = null;
    _lastErrorStackTrace = null;
    _publishHosts(hosts, force: recoveredFromError);
  }

  /// Records a durable-state observation error without dropping the last good projection.
  /// @param error The storage error delivered by the state owner.
  /// @param stackTrace The stack trace delivered with [error].
  void _reportStorageError(Object error, StackTrace stackTrace) {
    if (_isClosed) {
      return;
    }
    _lastError = error;
    _lastErrorStackTrace = stackTrace;
    _changes.addError(error, stackTrace);
  }

  /// Publishes a new immutable complete projection only when it changed.
  void _publishCurrentStates() {
    final List<DovahLinkKnownHostState>? current = _currentStates;
    if (current == null) {
      return;
    }
    _publishStates(
      current.map(
        (DovahLinkKnownHostState state) => DovahLinkKnownHostState(
          host: state.host,
          availability:
              _availability[state.host.hostId] ??
              DovahLinkHostAvailability.unknown,
          sessionState: state.host.hostId == _sessionHostId?.value
              ? _sessionState
              : DovahLinkKnownHostSessionState.disconnected,
          pairingRequired: state.pairingRequired,
        ),
      ),
    );
  }

  /// Projects durable Host metadata with current runtime availability values.
  /// @param hosts The latest complete metadata snapshot.
  /// @param force Whether to emit this complete snapshot to signal stream recovery.
  void _publishHosts(List<PersistedKnownHost> hosts, {bool force = false}) =>
      _publishStates(
        hosts.map(
          (PersistedKnownHost relationship) => DovahLinkKnownHostState(
            host: relationship.host,
            availability:
                _availability[relationship.host.hostId] ??
                DovahLinkHostAvailability.unknown,
            sessionState: relationship.host.hostId == _sessionHostId?.value
                ? _sessionState
                : DovahLinkKnownHostSessionState.disconnected,
            pairingRequired: relationship.pairingRequired,
          ),
        ),
        force: force,
      );

  /// Publishes a complete projection only when its semantic values changed.
  /// @param states The candidate runtime projection.
  /// @param force Whether an equivalent recovery snapshot must be re-announced.
  void _publishStates(
    Iterable<DovahLinkKnownHostState> states, {
    bool force = false,
  }) {
    if (_isClosed) {
      return;
    }
    final List<DovahLinkKnownHostState> next =
        List<DovahLinkKnownHostState>.unmodifiable(states);
    final List<DovahLinkKnownHostState>? current = _currentStates;
    if (!force &&
        current != null &&
        next.length == current.length &&
        next.indexed.every((entry) => entry.$2 == current[entry.$1])) {
      return;
    }
    _currentStates = next;
    _changes.add(next);
  }
}
