import 'dart:async';

import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';

/// Owns loading, persistence, and observable projections of this client's durable state.
abstract interface class IClientStateService {
  /// Loads the latest committed client state.
  /// @return The complete persisted state after any earlier state operation completes.
  Future<PersistedClientState> load();

  /// Applies [update] to the latest state, persists it, then publishes its semantic changes.
  /// @param update A synchronous transformation of the latest committed client state.
  Future<void> updateState(
    PersistedClientState Function(PersistedClientState state) update,
  );

  /// Emits Host metadata and recovery hints on listen and after each committed semantic change;
  /// credentials are omitted. An initial read failure is reported and the listener can recover
  /// after a later successful SDK load or mutation.
  /// @return A broadcast stream of immutable, Host-ID-sorted projections.
  Stream<List<PersistedKnownHost>> get knownHostsChanges;
}

/// Implements [IClientStateService] as the single owner of persisted client-state changes.
class ClientStateService implements IClientStateService {
  /// The platform storage used for complete client-state transactions.
  final IClientStorage _storage;

  /// Replays the latest committed Known Host relationships to each subscriber.
  final CurrentValueStream<List<PersistedKnownHost>?> _knownHosts =
      CurrentValueStream<List<PersistedKnownHost>?>(null);

  /// The last committed full client state, once loaded.
  PersistedClientState? _state;

  /// A shared in-flight initial load, if one is running.
  Future<PersistedClientState>? _loadFuture;

  /// Serializes reads and writes so full-state transactions cannot overwrite one another.
  Future<void> _operationTail = Future<void>.value();

  /// Creates a client-state owner over [storage].
  /// @param storage The SDK storage boundary for persisted client state.
  ClientStateService({required IClientStorage storage}) : _storage = storage;

  /// Implements [IClientStateService.load].
  @override
  Future<PersistedClientState> load() => _run(_loadState);

  /// Implements [IClientStateService.updateState].
  @override
  Future<void> updateState(
    PersistedClientState Function(PersistedClientState state) update,
  ) => _run(() async {
    final PersistedClientState current = await _loadState();
    final PersistedClientState next = update(current);
    if (next == current) {
      return;
    }
    await _storage.save(next);
    _state = next;
    _updateKnownHosts(next);
  });

  /// Implements [IClientStateService.knownHostsChanges].
  @override
  Stream<List<PersistedKnownHost>> get knownHostsChanges =>
      Stream<List<PersistedKnownHost>>.multi((
        MultiStreamController<List<PersistedKnownHost>> sink,
      ) {
        unawaited(_subscribeToKnownHosts(sink));
      }, isBroadcast: true);

  /// Loads once, retaining the failed load only until its error is returned.
  /// @return The loaded persisted client state.
  Future<PersistedClientState> _loadState() async {
    final PersistedClientState? state = _state;
    if (state != null) {
      return state;
    }
    final Future<PersistedClientState> loading = _loadFuture ??= _storage
        .load();
    try {
      final PersistedClientState loaded = await loading;
      _state = loaded;
      _updateKnownHosts(loaded);
      return loaded;
    } on Object {
      if (identical(_loadFuture, loading)) {
        _loadFuture = null;
      }
      rethrow;
    }
  }

  /// Emits load errors while keeping the subscriber for a later successful SDK load.
  /// @param sink The subscriber receiving complete Known Hosts views and storage errors.
  Future<void> _subscribeToKnownHosts(
    MultiStreamController<List<PersistedKnownHost>> sink,
  ) async {
    bool cancelled = false;
    StreamSubscription<List<PersistedKnownHost>?>? subscription;
    sink.onCancel = () async {
      cancelled = true;
      await subscription?.cancel();
    };
    try {
      await _run(_loadState);
    } on Object catch (error, stackTrace) {
      sink.addError(error, stackTrace);
    }
    if (cancelled) {
      return;
    }
    subscription = _knownHosts.stream.listen((List<PersistedKnownHost>? hosts) {
      if (_state != null && hosts != null) {
        sink.add(hosts);
      }
    }, onError: sink.addError);
  }

  /// Returns a sorted immutable projection without credentials.
  /// @param state The persisted relationship snapshot to project.
  /// @return The immutable Known Hosts list in stable Host-ID order.
  static List<PersistedKnownHost> _hosts(PersistedClientState state) {
    final List<PersistedKnownHost> hosts =
        state.knownHosts.values
            .map(
              (PersistedKnownHost relationship) => PersistedKnownHost(
                host: relationship.host,
                pairingRequired: relationship.pairingRequired,
              ),
            )
            .toList()
          ..sort(
            (left, right) => left.host.hostId.compareTo(right.host.hostId),
          );
    return List<PersistedKnownHost>.unmodifiable(hosts);
  }

  /// Publishes only when Host metadata or recovery hints changed.
  /// @param state The persisted state whose Known Host projection may have changed.
  void _updateKnownHosts(PersistedClientState state) {
    final List<PersistedKnownHost> next = _hosts(state);
    final List<PersistedKnownHost>? current = _knownHosts.value;
    if (current != null &&
        next.length == current.length &&
        next.indexed.every((entry) => entry.$2 == current[entry.$1])) {
      return;
    }
    _knownHosts.update(next);
  }

  /// Runs [operation] after prior state work and keeps the queue usable after failure.
  /// @param operation The serialized client-state operation to run.
  /// @return The operation's result or error.
  Future<T> _run<T>(FutureOr<T> Function() operation) {
    final Future<T> result = _operationTail.then((_) => operation());
    _operationTail = result.then<void>(
      (_) {},
      onError: (Object _, StackTrace __) {},
    );
    return result;
  }
}
