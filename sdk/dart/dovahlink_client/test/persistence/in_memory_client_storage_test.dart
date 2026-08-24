import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../fixtures/fixtures.dart';

/// Runs in-memory client-storage behavior tests.
void main() {
  late InMemoryClientStorage storage;

  setUp(() {
    storage = InMemoryClientStorage();
  });

  group('Method load behaves correctly', () {
    test(
      'Method load returns the empty state before anything has been saved',
      () async {
        final PersistedClientState state = await storage.load();

        expect(state, Fixtures.buildPersistedClientState(clientId: null));
      },
    );

    test('Method load returns the most recently saved state', () async {
      final PersistedClientState saved = Fixtures.buildPersistedClientState(
        clientId: 'client-1',
        credential: 'a1b2c3',
        recoveryState: PairingRecoveryState.confirming,
      );
      await storage.save(saved);

      final PersistedClientState loaded = await storage.load();

      expect(loaded, saved);
    });
  });

  group('Method save behaves correctly', () {
    test('Method save overwrites a previously saved state', () async {
      await storage.save(
        Fixtures.buildPersistedClientState(clientId: 'client-1'),
      );
      await storage.save(
        Fixtures.buildPersistedClientState(clientId: 'client-2'),
      );

      final PersistedClientState loaded = await storage.load();

      expect(loaded.clientId, 'client-2');
    });
  });

  group('Method clear behaves correctly', () {
    test('Method clear resets to the empty state', () async {
      await storage.save(
        Fixtures.buildPersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3',
          recoveryState: PairingRecoveryState.confirming,
        ),
      );

      await storage.clear();

      final PersistedClientState loaded = await storage.load();
      expect(loaded, Fixtures.buildPersistedClientState(clientId: null));
    });

    test('Method clear is idempotent when called twice', () async {
      await storage.clear();

      await expectLater(storage.clear(), completes);
    });
  });
}
