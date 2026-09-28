import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
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

  /// Emits the persisted Known Host on listen and after each committed Host change. A failure to
  /// load the initial persisted state is sent to the stream as an error.
  /// @return A broadcast stream that replays the current Host to each listener.
  Stream<DovahLinkHost?> get knownHostChanges;
}

/// Implements [IClientStateService] as the single owner of persisted client-state changes.
class ClientStateService implements IClientStateService {
  /// The platform storage used for complete client-state transactions.
  final IClientStorage _storage;

  /// Replays the latest committed Known Host to each subscriber.
  final CurrentValueStream<DovahLinkHost?> _knownHost =
      CurrentValueStream<DovahLinkHost?>(null);

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
    _knownHost.update(next.knownHost);
  });

  /// Implements [IClientStateService.knownHostChanges].
  @override
  Stream<DovahLinkHost?> get knownHostChanges => Stream<DovahLinkHost?>.multi((
    MultiStreamController<DovahLinkHost?> sink,
  ) {
    unawaited(_subscribeToKnownHost(sink));
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
      _knownHost.update(loaded.knownHost);
      return loaded;
    } on Object {
      if (identical(_loadFuture, loading)) {
        _loadFuture = null;
      }
      rethrow;
    }
  }

  /// Waits for initialization, then attaches to the replaying Host stream.
  Future<void> _subscribeToKnownHost(
    MultiStreamController<DovahLinkHost?> sink,
  ) async {
    try {
      await _run(_loadState);
      final StreamSubscription<DovahLinkHost?> subscription = _knownHost.stream
          .listen(sink.add, onError: sink.addError);
      sink.onCancel = subscription.cancel;
    } on Object catch (error, stackTrace) {
      sink.addError(error, stackTrace);
    }
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
