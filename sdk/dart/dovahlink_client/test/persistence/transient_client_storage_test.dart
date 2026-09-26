import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/transient_client_storage.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../fixtures/fixtures.dart';

void main() {
  group('Method load behaves correctly', () {
    test('Method load starts with empty transient state', () async {
      final TransientClientStorage storage = TransientClientStorage();

      expect(
        await storage.load(),
        Fixtures.buildPersistedClientState(clientId: null),
      );
    });
  });

  group('Method save behaves correctly', () {
    test('Method save holds state only within this storage instance', () async {
      final TransientClientStorage storage = TransientClientStorage();
      final PersistedClientState state = Fixtures.buildPersistedClientState(
        clientId: 'probe-client',
        credential: 'probe-credential',
        recoveryState: PairingRecoveryState.confirming,
      );

      await storage.save(state);

      expect(await storage.load(), state);
      expect(
        await TransientClientStorage().load(),
        Fixtures.buildPersistedClientState(clientId: null),
      );
    });
  });

  group('Method clear behaves correctly', () {
    test('Method clear resets transient state to empty', () async {
      final TransientClientStorage storage = TransientClientStorage();
      await storage.save(
        Fixtures.buildPersistedClientState(clientId: 'probe-client'),
      );

      await storage.clear();

      expect(
        await storage.load(),
        Fixtures.buildPersistedClientState(clientId: null),
      );
    });
  });
}
