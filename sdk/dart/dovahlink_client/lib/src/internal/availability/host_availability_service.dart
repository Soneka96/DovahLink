import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_state.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Owns runtime availability values and complete Known Host projections.
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
}

/// Implements [IHostAvailabilityService] over the durable Known Host projection.
class HostAvailabilityService implements IHostAvailabilityService {
  /// The owner of durable Known Host metadata and its replayable snapshots.
  final IClientStateService _clientStateService;

  /// The runtime availability values, keyed by stable Host ID.
  final Map<String, DovahLinkHostAvailability> _availability =
      <String, DovahLinkHostAvailability>{};

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
  StreamSubscription<List<DovahLinkHost>>? _knownHostsSubscription;

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
    _ensureKnownHostsObservation();
    if (availability == DovahLinkHostAvailability.unknown) {
      _availability.remove(hostId.value);
    } else {
      _availability[hostId.value] = availability;
    }
    _publishCurrentStates();
  }

  /// Starts the single durable-state listener on first use.
  void _ensureKnownHostsObservation() {
    if (_knownHostsSubscription != null) {
      return;
    }
    _knownHostsSubscription = _clientStateService.knownHostsChanges.listen(
      _updateKnownHosts,
      onError: _reportStorageError,
    );
  }

  /// Reconciles runtime values with the latest durable Known Host collection.
  /// @param hosts The latest metadata snapshot from client state.
  void _updateKnownHosts(List<DovahLinkHost> hosts) {
    final bool recoveredFromError = _lastError != null;
    final Set<String> hostIds = hosts
        .map((DovahLinkHost host) => host.hostId)
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
        ),
      ),
    );
  }

  /// Projects durable Host metadata with current runtime availability values.
  /// @param hosts The latest complete metadata snapshot.
  /// @param force Whether to emit this complete snapshot to signal stream recovery.
  void _publishHosts(List<DovahLinkHost> hosts, {bool force = false}) =>
      _publishStates(
        hosts.map(
          (DovahLinkHost host) => DovahLinkKnownHostState(
            host: host,
            availability:
                _availability[host.hostId] ?? DovahLinkHostAvailability.unknown,
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
