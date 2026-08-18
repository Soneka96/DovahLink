import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';

void main() {
  group('PersistedClientState', () {
    group('construction', () {
      test('defaults to the empty state a fresh install starts from', () {
        const PersistedClientState state = PersistedClientState();

        expect(state.clientId, isNull);
        expect(state.credential, isNull);
        expect(state.recoveryState, PairingRecoveryState.none);
      });

      test('currentFormatVersion is 1', () {
        expect(PersistedClientState.currentFormatVersion, 1);
      });
    });

    group('copyWith', () {
      test('replaces only the given recoveryState', () {
        const PersistedClientState original = PersistedClientState(
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

      test('replaces only the given clientId', () {
        const PersistedClientState original = PersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3',
        );

        final PersistedClientState updated = original.copyWith(
          clientId: 'client-2',
        );

        expect(updated.clientId, 'client-2');
        expect(updated.credential, 'a1b2c3');
      });

      test('replaces only the given credential', () {
        const PersistedClientState original = PersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3',
        );

        final PersistedClientState updated = original.copyWith(
          credential: 'd4e5f6',
        );

        expect(updated.clientId, 'client-1');
        expect(updated.credential, 'd4e5f6');
      });

      test('with no arguments returns an equal copy', () {
        const PersistedClientState original = PersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3',
          recoveryState: PairingRecoveryState.confirming,
        );

        expect(original.copyWith(), original);
      });
    });

    group('equality', () {
      test('two instances with the same fields are equal', () {
        const PersistedClientState a = PersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3',
          recoveryState: PairingRecoveryState.confirming,
        );
        const PersistedClientState b = PersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3',
          recoveryState: PairingRecoveryState.confirming,
        );

        expect(a, b);
        expect(a.hashCode, b.hashCode);
      });

      test('instances differing by clientId are not equal', () {
        const PersistedClientState a = PersistedClientState(
          clientId: 'client-1',
        );
        const PersistedClientState b = PersistedClientState(
          clientId: 'client-2',
        );

        expect(a, isNot(b));
        expect(a.hashCode, isNot(b.hashCode));
      });

      test('instances differing by credential are not equal', () {
        const PersistedClientState a = PersistedClientState(
          credential: 'a1b2c3',
        );
        const PersistedClientState b = PersistedClientState(
          credential: 'd4e5f6',
        );

        expect(a, isNot(b));
      });

      test('instances differing by recoveryState are not equal', () {
        const PersistedClientState a = PersistedClientState();
        const PersistedClientState b = PersistedClientState(
          recoveryState: PairingRecoveryState.confirming,
        );

        expect(a, isNot(b));
      });

      test('is not equal to a non-PersistedClientState object', () {
        const PersistedClientState state = PersistedClientState(
          clientId: 'client-1',
        );
        const Object other = 'client-1';

        expect(state == other, isFalse);
      });
    });
  });
}
