import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';

void main() {
  late InMemoryClientStorage storage;

  setUp(() {
    storage = InMemoryClientStorage();
  });

  group('load', () {
    test('returns the empty state before anything has been saved', () async {
      final PersistedClientState state = await storage.load();

      expect(state, const PersistedClientState());
    });

    test('returns the most recently saved state', () async {
      const PersistedClientState saved = PersistedClientState(
        clientId: 'client-1',
        credential: 'a1b2c3',
        recoveryState: PairingRecoveryState.confirming,
      );
      await storage.save(saved);

      final PersistedClientState loaded = await storage.load();

      expect(loaded, saved);
    });
  });

  group('save', () {
    test('overwrites a previously saved state', () async {
      await storage.save(const PersistedClientState(clientId: 'client-1'));
      await storage.save(const PersistedClientState(clientId: 'client-2'));

      final PersistedClientState loaded = await storage.load();

      expect(loaded.clientId, 'client-2');
    });
  });

  group('clear', () {
    test('resets to the empty state', () async {
      await storage.save(
        const PersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3',
          recoveryState: PairingRecoveryState.confirming,
        ),
      );

      await storage.clear();

      final PersistedClientState loaded = await storage.load();
      expect(loaded, const PersistedClientState());
    });

    test('is idempotent: calling it twice does not throw', () async {
      await storage.clear();

      await expectLater(storage.clear(), completes);
    });
  });
}
