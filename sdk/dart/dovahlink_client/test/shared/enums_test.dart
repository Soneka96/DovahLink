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

  group('Method toRenotifyStatus behaves correctly', () {
    test('Method toRenotifyStatus maps every valid renotify outcome', () {
      expect(
        PairingOutcome.renotified.toRenotifyStatus(),
        PairingRenotifyStatus.renotified,
      );
      expect(
        PairingOutcome.renotifyCooldown.toRenotifyStatus(),
        PairingRenotifyStatus.cooldown,
      );
      expect(
        PairingOutcome.alreadyIdle.toRenotifyStatus(),
        PairingRenotifyStatus.alreadyIdle,
      );
    });

    test(
      'Method toRenotifyStatus returns null for an outcome from another exchange',
      () {
        expect(PairingOutcome.cancelled.toRenotifyStatus(), isNull);
      },
    );
  });

  group('Method toCancelStatus behaves correctly', () {
    test('Method toCancelStatus maps every valid cancel outcome', () {
      expect(
        PairingOutcome.cancelled.toCancelStatus(),
        PairingCancelStatus.cancelled,
      );
      expect(
        PairingOutcome.alreadyIdle.toCancelStatus(),
        PairingCancelStatus.alreadyIdle,
      );
    });

    test(
      'Method toCancelStatus returns null for an outcome from another exchange',
      () {
        expect(PairingOutcome.renotified.toCancelStatus(), isNull);
      },
    );
  });
}
