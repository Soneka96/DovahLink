import 'client_storage.dart';
import 'persisted_client_state.dart';

/// An in-memory [ClientStorage] fake for deterministic tests: holds the last saved state in a
/// field, with no real persistence. Passing the same instance to a second [DovahLinkClient]
/// simulates state surviving an app relaunch without touching the filesystem or a real platform
/// secure-storage mechanism.
class InMemoryClientStorage implements ClientStorage {
  /// The currently held state, defaulting to the empty state a fresh install starts from.
  PersistedClientState _state = const PersistedClientState();

  @override
  Future<PersistedClientState> load() async => _state;

  @override
  Future<void> save(PersistedClientState state) async {
    _state = state;
  }

  @override
  Future<void> clear() async {
    _state = const PersistedClientState();
  }
}
