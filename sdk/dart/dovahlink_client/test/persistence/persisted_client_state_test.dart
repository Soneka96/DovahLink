import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/persistence/pending_pairing_recovery.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../fixtures/fixtures.dart';

/// Runs persisted client-state behavior tests.
void main() {
  const String hostA = '81869993-955c-4ba3-a7d0-d35ca86078ea';
  const String hostB = '81f6cc90-3a88-40c7-8351-104d4a36c971';

  group('Method constructor behaves correctly', () {
    test('Method constructor defaults to empty Host relationships', () {
      final PersistedClientState state = PersistedClientState();

      expect(state.clientId, isNull);
      expect(state.knownHosts, isEmpty);
      expect(state.pendingPairingRecovery, isNull);
    });

    test('Method constructor rejects mismatched Host map keys', () {
      expect(
        () => PersistedClientState(
          knownHosts: <String, PersistedKnownHost>{
            hostB: PersistedKnownHost(
              host: Fixtures.buildDovahLinkHost(hostId: hostA),
            ),
          },
        ),
        throwsArgumentError,
      );
    });

    test('Method constructor rejects recovery without its owning Host', () {
      expect(
        () => PersistedClientState(
          pendingPairingRecovery: const PendingPairingRecovery(
            hostId: hostA,
            state: PairingRecoveryState.confirming,
          ),
        ),
        throwsArgumentError,
      );
    });

    test('Method constructor normalizes Host UUID key casing', () {
      final PersistedClientState state = PersistedClientState(
        knownHosts: <String, PersistedKnownHost>{
          hostA.toUpperCase(): PersistedKnownHost(
            host: Fixtures.buildDovahLinkHost(hostId: hostA.toUpperCase()),
          ),
        },
      );

      expect(state.knownHosts.keys, <String>[hostA]);
    });

    test('Method constructor canonicalizes every stored Host UUID', () {
      final PersistedClientState state = PersistedClientState(
        knownHosts: <String, PersistedKnownHost>{
          hostA.toUpperCase(): PersistedKnownHost(
            host: Fixtures.buildDovahLinkHost(hostId: hostA.toUpperCase()),
            credential: 'credential-a',
          ),
        },
        pendingPairingRecovery: PendingPairingRecovery(
          hostId: hostA.toUpperCase(),
          state: PairingRecoveryState.confirming,
        ),
      );

      expect(state.knownHosts.keys, <String>[hostA]);
      expect(state.knownHosts[hostA]?.host.hostId, hostA);
      expect(state.pendingPairingRecovery?.hostId, hostA);
    });

    test(
      'Method constructor preserves the pairing hint while normalizing a Host ID',
      () {
        final PersistedClientState state = PersistedClientState(
          knownHosts: <String, PersistedKnownHost>{
            hostA.toUpperCase(): PersistedKnownHost(
              host: Fixtures.buildDovahLinkHost(hostId: hostA.toUpperCase()),
              pairingRequired: true,
            ),
          },
        );

        expect(state.knownHosts[hostA]?.pairingRequired, isTrue);
      },
    );
  });

  group('Property currentFormatVersion behaves correctly', () {
    test('Property currentFormatVersion reports version 4', () {
      expect(PersistedClientState.currentFormatVersion, 4);
    });
  });

  group('Method copyWith behaves correctly', () {
    test(
      'Method copyWith replaces Host relationships without mutating input',
      () {
        final PersistedKnownHost relationshipA = PersistedKnownHost(
          host: Fixtures.buildDovahLinkHost(hostId: hostA),
          credential: 'credential-a',
        );
        final PersistedClientState original = PersistedClientState(
          clientId: 'client-1',
          knownHosts: <String, PersistedKnownHost>{hostA: relationshipA},
        );
        final Map<String, PersistedKnownHost> replacement =
            <String, PersistedKnownHost>{
              hostB: PersistedKnownHost(
                host: Fixtures.buildDovahLinkHost(hostId: hostB),
                credential: 'credential-b',
              ),
            };

        final PersistedClientState updated = original.copyWith(
          knownHosts: replacement,
        );
        replacement.clear();

        expect(original.knownHosts, <String, PersistedKnownHost>{
          hostA: relationshipA,
        });
        expect(updated.knownHosts.keys, <String>[hostB]);
      },
    );

    test('Method copyWith clears pending recovery explicitly', () {
      final PersistedClientState original = PersistedClientState(
        knownHosts: <String, PersistedKnownHost>{
          hostA: PersistedKnownHost(
            host: Fixtures.buildDovahLinkHost(hostId: hostA),
            credential: 'credential-a',
          ),
        },
        pendingPairingRecovery: const PendingPairingRecovery(
          hostId: hostA,
          state: PairingRecoveryState.confirming,
        ),
      );

      expect(
        original
            .copyWith(clearPendingPairingRecovery: true)
            .pendingPairingRecovery,
        isNull,
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality treats equivalent Host maps as equal', () {
      /// Builds the representative confirming state used by this assertion.
      PersistedClientState buildState() => PersistedClientState(
        clientId: 'client-1',
        knownHosts: <String, PersistedKnownHost>{
          hostA: PersistedKnownHost(
            host: Fixtures.buildDovahLinkHost(hostId: hostA),
            credential: 'credential-a',
          ),
        },
        pendingPairingRecovery: const PendingPairingRecovery(
          hostId: hostA,
          state: PairingRecoveryState.confirming,
        ),
      );

      expect(buildState(), buildState());
      expect(buildState().hashCode, buildState().hashCode);
    });

    test('Behavior equality keeps credentials scoped to each Host', () {
      final PersistedKnownHost relationshipA = PersistedKnownHost(
        host: Fixtures.buildDovahLinkHost(hostId: hostA),
        credential: 'credential-a',
      );
      final PersistedKnownHost relationshipB = PersistedKnownHost(
        host: Fixtures.buildDovahLinkHost(hostId: hostB),
        credential: 'credential-b',
      );
      final PersistedClientState state = PersistedClientState(
        knownHosts: <String, PersistedKnownHost>{
          hostA: relationshipA,
          hostB: relationshipB,
        },
      );

      expect(state.knownHosts[hostA]?.credential, 'credential-a');
      expect(state.knownHosts[hostB]?.credential, 'credential-b');
      expect(
        state,
        isNot(
          PersistedClientState(
            knownHosts: <String, PersistedKnownHost>{
              hostA: relationshipA,
              hostB: PersistedKnownHost(
                host: relationshipB.host,
                credential: 'wrong-credential',
              ),
            },
          ),
        ),
      );
    });

    test('Behavior equality detects changed Host metadata', () {
      final PersistedClientState first = PersistedClientState(
        knownHosts: <String, PersistedKnownHost>{
          hostA: PersistedKnownHost(
            host: Fixtures.buildDovahLinkHost(hostId: hostA),
          ),
        },
      );
      final PersistedClientState second = PersistedClientState(
        knownHosts: <String, PersistedKnownHost>{
          hostA: PersistedKnownHost(
            host: Fixtures.buildDovahLinkHost(
              hostId: hostA,
              hostName: 'RENAMED-DESKTOP',
            ),
          ),
        },
      );

      expect(first, isNot(second));
    });
  });
}
