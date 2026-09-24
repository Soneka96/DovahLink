import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs [CredentialRejectionReason.fromProtocolErrorCode] behavior tests.
void main() {
  group('DovahLinkStateArea behaves correctly', () {
    test('protocolValue maps every typed state area to its canonical name', () {
      expect(
        DovahLinkStateArea.values.map(
          (DovahLinkStateArea area) => area.protocolValue,
        ),
        <String>[
          'character_xp',
          'character_health',
          'character_magicka',
          'character_stamina',
          'character_level',
        ],
      );
    });

    test(
      'fromProtocolValue round-trips each known area and rejects unknown names',
      () {
        for (final DovahLinkStateArea area in DovahLinkStateArea.values) {
          expect(
            DovahLinkStateArea.fromProtocolValue(area.protocolValue),
            area,
          );
        }
        expect(DovahLinkStateArea.fromProtocolValue('unknown_area'), isNull);
      },
    );
  });

  group('Method fromProtocolErrorCode behaves correctly', () {
    test(
      'Method fromProtocolErrorCode maps recoverable typed protocol errors',
      () {
        expect(
          CredentialRejectionReason.fromProtocolErrorCode(
            ProtocolErrorCode.revoked,
          ),
          CredentialRejectionReason.revoked,
        );
        expect(
          CredentialRejectionReason.fromProtocolErrorCode(
            ProtocolErrorCode.unauthenticated,
          ),
          CredentialRejectionReason.unrecognized,
        );
        expect(
          CredentialRejectionReason.fromProtocolErrorCode(
            ProtocolErrorCode.blocked,
          ),
          CredentialRejectionReason.blocked,
        );
      },
    );

    test(
      'Method fromProtocolErrorCode returns null for every non-recoverable typed protocol error',
      () {
        const Set<ProtocolErrorCode> recoverable = <ProtocolErrorCode>{
          ProtocolErrorCode.revoked,
          ProtocolErrorCode.unauthenticated,
          ProtocolErrorCode.blocked,
        };
        for (final ProtocolErrorCode code in ProtocolErrorCode.values) {
          if (!recoverable.contains(code)) {
            expect(
              CredentialRejectionReason.fromProtocolErrorCode(code),
              isNull,
              reason: '$code is not a recoverable credential rejection',
            );
          }
        }
      },
    );
  });

  group('Method fromOutcome in PairingRenotifyStatus behaves correctly', () {
    test(
      'Method fromOutcome in PairingRenotifyStatus maps every valid renotify outcome',
      () {
        expect(
          PairingRenotifyStatus.fromOutcome(PairingOutcome.renotified),
          PairingRenotifyStatus.renotified,
        );
        expect(
          PairingRenotifyStatus.fromOutcome(PairingOutcome.renotifyCooldown),
          PairingRenotifyStatus.cooldown,
        );
        expect(
          PairingRenotifyStatus.fromOutcome(PairingOutcome.alreadyIdle),
          PairingRenotifyStatus.alreadyIdle,
        );
      },
    );

    test(
      'Method fromOutcome in PairingRenotifyStatus returns null for every other outcome',
      () {
        const Set<PairingOutcome> validOutcomes = <PairingOutcome>{
          PairingOutcome.renotified,
          PairingOutcome.renotifyCooldown,
          PairingOutcome.alreadyIdle,
        };
        for (final PairingOutcome outcome in PairingOutcome.values) {
          if (!validOutcomes.contains(outcome)) {
            expect(
              PairingRenotifyStatus.fromOutcome(outcome),
              isNull,
              reason: '$outcome is not a renotify outcome',
            );
          }
        }
      },
    );
  });

  group('Method fromOutcome in PairingCancelStatus behaves correctly', () {
    test(
      'Method fromOutcome in PairingCancelStatus maps every valid cancel outcome',
      () {
        expect(
          PairingCancelStatus.fromOutcome(PairingOutcome.cancelled),
          PairingCancelStatus.cancelled,
        );
        expect(
          PairingCancelStatus.fromOutcome(PairingOutcome.alreadyIdle),
          PairingCancelStatus.alreadyIdle,
        );
      },
    );

    test(
      'Method fromOutcome in PairingCancelStatus returns null for every other outcome',
      () {
        const Set<PairingOutcome> validOutcomes = <PairingOutcome>{
          PairingOutcome.cancelled,
          PairingOutcome.alreadyIdle,
        };
        for (final PairingOutcome outcome in PairingOutcome.values) {
          if (!validOutcomes.contains(outcome)) {
            expect(
              PairingCancelStatus.fromOutcome(outcome),
              isNull,
              reason: '$outcome is not a cancel outcome',
            );
          }
        }
      },
    );
  });

  test(
    'PairingOutcome includes pairing_invalidated as a registered outcome value',
    () {
      expect(
        PairingOutcome.values,
        contains(PairingOutcome.pairingInvalidated),
      );
    },
  );

  group('Property values in DovahLinkStateStatus behaves correctly', () {
    test('Property values in DovahLinkStateStatus includes every standing', () {
      expect(DovahLinkStateStatus.values, <DovahLinkStateStatus>[
        DovahLinkStateStatus.notSubscribed,
        DovahLinkStateStatus.unavailable,
        DovahLinkStateStatus.synchronized,
        DovahLinkStateStatus.stale,
        DovahLinkStateStatus.recovering,
        DovahLinkStateStatus.failed,
      ]);
    });
  });

  group('Property values in StateEventApplyResult behaves correctly', () {
    test('Property values in StateEventApplyResult includes every result', () {
      expect(StateEventApplyResult.values, <StateEventApplyResult>[
        StateEventApplyResult.applied,
        StateEventApplyResult.buffered,
        StateEventApplyResult.ignored,
        StateEventApplyResult.recoveryRequired,
      ]);
    });
  });
}
