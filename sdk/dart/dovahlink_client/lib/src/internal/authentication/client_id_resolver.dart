import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/internal/random_id_generator.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';

/// Resolves one installation's stable client ID, creating and persisting it on first use.
class ClientIdResolver {
  /// The owner of persisted client state.
  final IClientStateService _clientStateService;

  /// The generator used when the persisted state has no usable client ID.
  final RandomIdGenerator _randomIdGenerator;

  /// Creates a resolver using [clientStateService] and [randomIdGenerator].
  /// @param clientStateService The owner of persisted client state.
  /// @param randomIdGenerator The generator used for a missing client ID.
  ClientIdResolver({
    required IClientStateService clientStateService,
    required RandomIdGenerator randomIdGenerator,
  }) : _clientStateService = clientStateService,
       _randomIdGenerator = randomIdGenerator;

  /// Returns [state]'s client ID, generating and persisting one when it is absent or empty.
  /// @param state The client state observed by the authentication operation.
  Future<String> resolve(PersistedClientState state) async {
    final String? existing = state.clientId;
    if (existing != null && existing.isNotEmpty) {
      return existing;
    }

    final String generated = _randomIdGenerator.generateUuid();
    String resolved = generated;
    await _clientStateService.updateState((PersistedClientState current) {
      final String? clientId = current.clientId;
      if (clientId != null && clientId.isNotEmpty) {
        resolved = clientId;
        return current;
      }
      return current.copyWith(clientId: generated);
    });
    return resolved;
  }
}
