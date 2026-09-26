import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../fixtures/fixtures.dart';

/// Builds a representative Host association for persisted-state tests.
DovahLinkHost _knownHost({
  String hostName = 'GONCALO-DESKTOP',
  String endpoint = 'ws://127.0.0.1:58231/',
}) => DovahLinkHost(
  hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
  hostName: hostName,
  endpoint: Uri.parse(endpoint),
);

/// Runs persisted-client-state behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test('Method constructor defaults to an empty persisted state', () {
      const PersistedClientState state = PersistedClientState();

      expect(state.clientId, isNull);
      expect(state.credential, isNull);
      expect(state.recoveryState, PairingRecoveryState.none);
      expect(state.knownHost, isNull);
    });
  });

  group('Property currentFormatVersion behaves correctly', () {
    test('Property currentFormatVersion reports version 2', () {
      expect(PersistedClientState.currentFormatVersion, 2);
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

    test('Method copyWith preserves Known Host when other fields change', () {
      final DovahLinkHost knownHost = _knownHost();
      final PersistedClientState original = PersistedClientState(
        clientId: 'client-1',
        credential: 'a1b2c3',
        knownHost: knownHost,
      );

      final PersistedClientState updated = original.copyWith(
        recoveryState: PairingRecoveryState.confirming,
      );

      expect(updated.knownHost, knownHost);
    });

    test('Method copyWith sets Known Host when supplied', () {
      final DovahLinkHost knownHost = _knownHost();

      final PersistedClientState updated = const PersistedClientState()
          .copyWith(knownHost: knownHost);

      expect(updated.knownHost, knownHost);
    });

    test('Method constructor explicitly clears nullable fields', () {
      final PersistedClientState original = PersistedClientState(
        clientId: 'client-1',
        credential: 'a1b2c3',
        knownHost: _knownHost(),
      );

      final PersistedClientState cleared = PersistedClientState(
        clientId: original.clientId,
        recoveryState: PairingRecoveryState.none,
      );

      expect(cleared.credential, isNull);
      expect(cleared.knownHost, isNull);
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
        ).copyWith(knownHost: _knownHost());
        final PersistedClientState b = Fixtures.buildPersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3',
          recoveryState: PairingRecoveryState.confirming,
        ).copyWith(knownHost: _knownHost());

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

    test('Behavior equality detects differing Known Host metadata', () {
      final PersistedClientState first = PersistedClientState(
        knownHost: _knownHost(),
      );
      final PersistedClientState second = PersistedClientState(
        knownHost: _knownHost(hostName: 'RENAMED-DESKTOP'),
      );

      expect(first, isNot(second));
    });

    test('Behavior equality detects a differing Known Host endpoint', () {
      final PersistedClientState first = PersistedClientState(
        knownHost: _knownHost(),
      );
      final PersistedClientState second = PersistedClientState(
        knownHost: _knownHost(endpoint: 'ws://127.0.0.1:58232/'),
      );

      expect(first, isNot(second));
    });
  });
}
