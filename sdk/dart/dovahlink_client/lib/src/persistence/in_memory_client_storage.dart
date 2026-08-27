import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// An in-memory [IClientStorage] fake for deterministic tests: holds the last saved state in a
/// field, with no real persistence. Passing the same instance to a second [DovahLinkClient]
/// simulates state surviving an app relaunch without touching the filesystem or a real platform
/// secure-storage mechanism.
class InMemoryClientStorage implements IClientStorage {
  /// The currently held state, defaulting to the empty state a fresh install starts from.
  PersistedClientState _state = const PersistedClientState();

  /// See [IClientStorage.load].
  @override
  Future<PersistedClientState> load() async => _state;

  /// See [IClientStorage.save].
  @override
  Future<void> save(PersistedClientState state) async {
    _state = state;
  }

  /// See [IClientStorage.clear].
  @override
  Future<void> clear() async {
    _state = const PersistedClientState();
  }
}
