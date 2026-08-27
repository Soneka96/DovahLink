import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';

/// The SDK-owned persistence boundary for [PersistedClientState]: the stable local client ID,
/// pairing credential, and `CONFIRMING` recovery state. Implementations own where and how this is
/// durably stored (see `ai/context/sdk/persistence.md`'s "Storage abstraction" and
/// `ai/context/sdk/architecture.md`'s "Platform ports"); the client engine depends only on this
/// interface, never on a concrete storage mechanism.
abstract interface class IClientStorage {
  /// Loads the current persisted state, or the empty [PersistedClientState] when nothing has been
  /// saved yet -- a missing store is a valid empty store, not corruption.
  /// @throws [DovahLinkStorageException] if a store exists but cannot be read as valid state:
  ///     corrupt, undecryptable, malformed, or written by an unsupported format version. Never
  ///     silently substitutes the empty state for a store that exists but cannot be trusted.
  Future<PersistedClientState> load();

  /// Durably persists [state], replacing any previously saved state.
  Future<void> save(PersistedClientState state);

  /// Erases any persisted state. Idempotent.
  Future<void> clear();
}
