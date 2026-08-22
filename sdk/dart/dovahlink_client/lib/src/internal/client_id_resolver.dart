import 'package:dovahlink_client_sdk/src/internal/uuid_v4_generator.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';

/// Resolves one installation's stable client ID, creating and persisting it on first use.
class ClientIdResolver {
  /// The persistence boundary that owns the installation state.
  final ClientStorage _storage;

  /// The generator used when the persisted state has no usable client ID.
  final UuidV4Generator _uuidV4Generator;

  /// Creates a resolver using [storage] and [uuidV4Generator].
  ClientIdResolver({
    required ClientStorage storage,
    required UuidV4Generator uuidV4Generator,
  }) : _storage = storage,
       _uuidV4Generator = uuidV4Generator;

  /// Returns [state]'s client ID, generating and persisting one when it is absent or empty.
  Future<String> resolve(PersistedClientState state) async {
    final String? existing = state.clientId;
    if (existing != null && existing.isNotEmpty) {
      return existing;
    }

    final String generated = _uuidV4Generator.generate();
    await _storage.save(state.copyWith(clientId: generated));
    return generated;
  }
}
