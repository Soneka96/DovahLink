import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';

/// Holds client identity, credential, and recovery state only for the lifetime of this storage
/// instance. Used by bounded SDK probes that must not read or mutate consumer persistence.
class TransientClientStorage implements IClientStorage {
  /// The transient state, initially empty.
  PersistedClientState _state = const PersistedClientState();

  /// Creates an empty transient storage instance.
  TransientClientStorage();

  /// Returns the currently held transient state.
  @override
  Future<PersistedClientState> load() async => _state;

  /// Replaces the currently held transient state.
  @override
  Future<void> save(PersistedClientState state) async {
    _state = state;
  }

  /// Resets the transient state to empty.
  @override
  Future<void> clear() async {
    _state = const PersistedClientState();
  }
}
