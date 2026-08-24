import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../fixtures/fixtures.dart';

/// Runs persisted-client-state behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test('Method constructor defaults to an empty persisted state', () {
      const PersistedClientState state = PersistedClientState();

      expect(state.clientId, isNull);
      expect(state.credential, isNull);
      expect(state.recoveryState, PairingRecoveryState.none);
    });
  });

  group('Property currentFormatVersion behaves correctly', () {
    test('Property currentFormatVersion reports version 1', () {
      expect(PersistedClientState.currentFormatVersion, 1);
    });
  });

  group('Method copyWith behaves correctly', () {
    test('Method copyWith replaces only the given recoveryState', () {
      final PersistedClientState original = Fixtures.buildPersistedClientState(
        clientId: 'client-1',
        credential: 'a1b2c3',
        recoveryState: PairingRecoveryState.confirming,
      );

      final PersistedClientState updated = original.copyWith(
        recoveryState: PairingRecoveryState.none,
      );

      expect(updated.clientId, 'client-1');
      expect(updated.credential, 'a1b2c3');
      expect(updated.recoveryState, PairingRecoveryState.none);
    });

    test('Method copyWith replaces only the given clientId', () {
      final PersistedClientState original = Fixtures.buildPersistedClientState(
        clientId: 'client-1',
        credential: 'a1b2c3',
      );

      final PersistedClientState updated = original.copyWith(
        clientId: 'client-2',
      );

      expect(updated.clientId, 'client-2');
      expect(updated.credential, 'a1b2c3');
    });

    test('Method copyWith replaces only the given credential', () {
      final PersistedClientState original = Fixtures.buildPersistedClientState(
        clientId: 'client-1',
        credential: 'a1b2c3',
      );

      final PersistedClientState updated = original.copyWith(
        credential: 'd4e5f6',
      );

      expect(updated.clientId, 'client-1');
      expect(updated.credential, 'd4e5f6');
    });

    test('Method copyWith with no arguments returns an equal copy', () {
      final PersistedClientState original = Fixtures.buildPersistedClientState(
        clientId: 'client-1',
        credential: 'a1b2c3',
        recoveryState: PairingRecoveryState.confirming,
      );

      expect(original.copyWith(), original);
    });
  });

  group('Behavior equality behaves correctly', () {
    test(
      'Behavior equality treats instances with the same fields as equal',
      () {
        final PersistedClientState a = Fixtures.buildPersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3',
          recoveryState: PairingRecoveryState.confirming,
        );
        final PersistedClientState b = Fixtures.buildPersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3',
          recoveryState: PairingRecoveryState.confirming,
        );

        expect(a, b);
        expect(a.hashCode, b.hashCode);
      },
    );

    test('Behavior equality rejects instances differing by clientId', () {
      final PersistedClientState a = Fixtures.buildPersistedClientState(
        clientId: 'client-1',
      );
      final PersistedClientState b = Fixtures.buildPersistedClientState(
        clientId: 'client-2',
      );

      expect(a, isNot(b));
      expect(a.hashCode, isNot(b.hashCode));
    });

    test('Behavior equality rejects instances differing by credential', () {
      final PersistedClientState a = Fixtures.buildPersistedClientState(
        clientId: null,
        credential: 'a1b2c3',
      );
      final PersistedClientState b = Fixtures.buildPersistedClientState(
        clientId: null,
        credential: 'd4e5f6',
      );

      expect(a, isNot(b));
    });

    test('Behavior equality rejects instances differing by recoveryState', () {
      final PersistedClientState a = Fixtures.buildPersistedClientState(
        clientId: null,
      );
      final PersistedClientState b = Fixtures.buildPersistedClientState(
        clientId: null,
        recoveryState: PairingRecoveryState.confirming,
      );

      expect(a, isNot(b));
    });

    test('Behavior equality rejects a non-PersistedClientState object', () {
      final PersistedClientState state = Fixtures.buildPersistedClientState(
        clientId: 'client-1',
      );
      const Object other = 'client-1';

      expect(state == other, isFalse);
    });
  });
}
