import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';

/// An explicit storage boundary for platforms without a secure client-storage implementation.
///
/// Construction is safe on any platform. Each storage operation throws [UnsupportedError] so an
/// application can start without silently persisting client identity or credentials insecurely.
class UnsupportedClientStorage implements IClientStorage {
  /// Creates a storage boundary that fails when persistence is requested.
  const UnsupportedClientStorage();

  /// Implements [IClientStorage.load].
  /// @throws [UnsupportedError] because no secure storage implementation is available.
  @override
  Future<PersistedClientState> load() async {
    throw UnsupportedError(
      'Secure client storage is not implemented for this platform.',
    );
  }

  /// Implements [IClientStorage.save].
  /// @param state The client state that cannot be persisted on this platform.
  /// @throws [UnsupportedError] because no secure storage implementation is available.
  @override
  Future<void> save(PersistedClientState state) async {
    throw UnsupportedError(
      'Secure client storage is not implemented for this platform.',
    );
  }

  /// Implements [IClientStorage.clear].
  /// @throws [UnsupportedError] because no secure storage implementation is available.
  @override
  Future<void> clear() async {
    throw UnsupportedError(
      'Secure client storage is not implemented for this platform.',
    );
  }
}
