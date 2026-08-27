import 'package:dovahlink_client_sdk/src/internal/random_id_generator.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';

/// Resolves one installation's stable client ID, creating and persisting it on first use.
class ClientIdResolver {
  /// The persistence boundary that owns the installation state.
  final IClientStorage _storage;

  /// The generator used when the persisted state has no usable client ID.
  final RandomIdGenerator _randomIdGenerator;

  /// Creates a resolver using [storage] and [randomIdGenerator].
  ClientIdResolver({
    required IClientStorage storage,
    required RandomIdGenerator randomIdGenerator,
  }) : _storage = storage,
       _randomIdGenerator = randomIdGenerator;

  /// Returns [state]'s client ID, generating and persisting one when it is absent or empty.
  Future<String> resolve(PersistedClientState state) async {
    final String? existing = state.clientId;
    if (existing != null && existing.isNotEmpty) {
      return existing;
    }

    final String generated = _randomIdGenerator.generateUuid();
    await _storage.save(state.copyWith(clientId: generated));
    return generated;
  }
}
