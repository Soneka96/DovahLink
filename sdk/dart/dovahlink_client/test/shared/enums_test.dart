import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs [CredentialRejectionReason.fromProtocolErrorCode] behavior tests.
void main() {
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
      },
    );

    test(
      'Method fromProtocolErrorCode returns null for non-recoverable typed protocol errors',
      () {
        expect(
          CredentialRejectionReason.fromProtocolErrorCode(
            ProtocolErrorCode.rateLimited,
          ),
          isNull,
        );
      },
    );
  });

  group('Method fromOutcome behaves correctly', () {
    test(
      'PairingRenotifyStatus.fromOutcome maps every valid renotify outcome',
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
      'PairingRenotifyStatus.fromOutcome returns null for every other outcome',
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

  group('Method fromOutcome behaves correctly', () {
    test('PairingCancelStatus.fromOutcome maps every valid cancel outcome', () {
      expect(
        PairingCancelStatus.fromOutcome(PairingOutcome.cancelled),
        PairingCancelStatus.cancelled,
      );
      expect(
        PairingCancelStatus.fromOutcome(PairingOutcome.alreadyIdle),
        PairingCancelStatus.alreadyIdle,
      );
    });

    test(
      'PairingCancelStatus.fromOutcome returns null for every other outcome',
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
}
