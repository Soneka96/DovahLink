import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/client_id_resolver.dart';
import 'package:dovahlink_client_sdk/src/internal/uuid_v4_generator.dart';
import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';

void main() {
  group('ClientIdResolver', () {
    test(
      'reuses an existing persisted client ID without replacing the state',
      () async {
        final InMemoryClientStorage storage = InMemoryClientStorage();
        const PersistedClientState state = PersistedClientState(
          clientId: 'client-1',
          credential: 'credential-1',
          recoveryState: PairingRecoveryState.confirming,
        );
        await storage.save(state);
        final ClientIdResolver resolver = ClientIdResolver(
          storage: storage,
          uuidV4Generator: UuidV4Generator(),
        );

        final String clientId = await resolver.resolve(await storage.load());

        expect(clientId, 'client-1');
        expect(await storage.load(), state);
      },
    );

    test('generates and persists a client ID on first use', () async {
      final InMemoryClientStorage storage = InMemoryClientStorage();
      const PersistedClientState state = PersistedClientState(
        credential: 'credential-1',
        recoveryState: PairingRecoveryState.confirming,
      );
      await storage.save(state);
      final ClientIdResolver resolver = ClientIdResolver(
        storage: storage,
        uuidV4Generator: UuidV4Generator(),
      );

      final String clientId = await resolver.resolve(await storage.load());
      final PersistedClientState persisted = await storage.load();

      expect(
        clientId,
        matches(
          RegExp(
            r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
          ),
        ),
      );
      expect(persisted.clientId, clientId);
      expect(persisted.credential, 'credential-1');
      expect(persisted.recoveryState, PairingRecoveryState.confirming);
    });

    test('replaces an empty persisted client ID', () async {
      final InMemoryClientStorage storage = InMemoryClientStorage();
      await storage.save(
        const PersistedClientState(
          clientId: '',
          credential: 'credential-1',
          recoveryState: PairingRecoveryState.confirming,
        ),
      );
      final ClientIdResolver resolver = ClientIdResolver(
        storage: storage,
        uuidV4Generator: UuidV4Generator(),
      );

      final String clientId = await resolver.resolve(await storage.load());

      expect(clientId, isNotEmpty);
      final PersistedClientState persisted = await storage.load();
      expect(persisted.clientId, clientId);
      expect(persisted.credential, 'credential-1');
      expect(persisted.recoveryState, PairingRecoveryState.confirming);
    });
  });
}
