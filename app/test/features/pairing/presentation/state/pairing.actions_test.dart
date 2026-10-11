import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// The fixed instant tests supply as the SDK result's observation time.
final DateTime _observedAt = DateTime.utc(2026, 1, 1, 12);

/// Exercises pairing action construction and equality.
void main() {
  group('Behavior equality in PairingStartedAction behaves correctly', () {
    test(
      'PairingStartedAction equality reflects its input-free start request',
      () {
        const PairingStartedAction action = PairingStartedAction();

        expect(action.props, isEmpty);
      },
    );
  });

  group(
    'Behavior equality in PairingCodeSubmittedAction behaves correctly',
    () {
      test('Behavior equality contains only the entered pairing code', () {
        const PairingCodeSubmittedAction action = PairingCodeSubmittedAction(
          code: '123456',
        );

        expect(action.props, ['123456']);
      });
    },
  );

  group('Behavior equality in PairingAuthenticatedAction behaves correctly', () {
    test(
      'Behavior equality in PairingAuthenticatedAction includes the typed rejection reason',
      () {
        const PairingAuthenticatedAction revoked = PairingAuthenticatedAction(
          hostVersion: '1.2.3',
          trusted: false,
          credentialRejectionReason: PairingCredentialRejectionReason.revoked,
        );
        const PairingAuthenticatedAction blocked = PairingAuthenticatedAction(
          hostVersion: '1.2.3',
          trusted: false,
          credentialRejectionReason: PairingCredentialRejectionReason.blocked,
        );

        expect(revoked == blocked, isFalse);
      },
    );

    test(
      'Behavior equality in PairingAuthenticatedAction gives matching hashes for equal rejection data',
      () {
        const PairingAuthenticatedAction first = PairingAuthenticatedAction(
          hostVersion: '1.2.3',
          trusted: false,
          credentialRejectionReason: PairingCredentialRejectionReason.blocked,
          credentialRejectedMessage: 'Blocked.',
        );
        const PairingAuthenticatedAction second = PairingAuthenticatedAction(
          hostVersion: '1.2.3',
          trusted: false,
          credentialRejectionReason: PairingCredentialRejectionReason.blocked,
          credentialRejectedMessage: 'Blocked.',
        );

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      },
    );
  });

  group(
    'Behavior equality in PairingRenotifyRequestedAction behaves correctly',
    () {
      test(
        'Behavior equality in PairingRenotifyRequestedAction treats two instances as equal',
        () {
          const action1 = PairingRenotifyRequestedAction();
          const action2 = PairingRenotifyRequestedAction();

          expect(action1, action2);
          expect(action1.hashCode, action2.hashCode);
        },
      );

      test(
        'Behavior equality in PairingRenotifyRequestedAction rejects a different action type',
        () {
          const action = PairingRenotifyRequestedAction();

          expect(action, isNot(const PairingCancelRequestedAction()));
        },
      );
    },
  );

  group(
    'Behavior equality in PairingRenotifySucceededAction behaves correctly',
    () {
      test(
        'Behavior equality in PairingRenotifySucceededAction treats two instances as equal',
        () {
          final action1 = PairingRenotifySucceededAction(
            observedAt: _observedAt,
          );
          final action2 = PairingRenotifySucceededAction(
            observedAt: _observedAt,
          );

          expect(action1, action2);
          expect(action1.hashCode, action2.hashCode);
        },
      );

      test('Behavior equality includes the observation instant', () {
        final PairingRenotifySucceededAction first =
            PairingRenotifySucceededAction(observedAt: _observedAt);
        final PairingRenotifySucceededAction later =
            PairingRenotifySucceededAction(
              observedAt: _observedAt.add(const Duration(seconds: 1)),
            );

        expect(first, isNot(later));
      });

      test('Behavior equality includes the Host retry interval', () {
        final PairingRenotifySucceededAction immediate =
            PairingRenotifySucceededAction(observedAt: _observedAt);
        final PairingRenotifySucceededAction cooldown =
            PairingRenotifySucceededAction(
              observedAt: _observedAt,
              retryAfterSeconds: 5,
            );

        expect(immediate, isNot(cooldown));
      });
    },
  );

  group(
    'Behavior equality in PairingRenotifyAlreadyIdleAction behaves correctly',
    () {
      test('Behavior equality treats two already-idle actions as equal', () {
        const PairingRenotifyAlreadyIdleAction first =
            PairingRenotifyAlreadyIdleAction();
        const PairingRenotifyAlreadyIdleAction second =
            PairingRenotifyAlreadyIdleAction();

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      });
    },
  );

  group('Behavior equality in PairingRenotifyCooldownAction behaves correctly', () {
    test(
      'Behavior equality in PairingRenotifyCooldownAction treats instances with the same '
      'retryAfterSeconds as equal',
      () {
        final action1 = PairingRenotifyCooldownAction(
          observedAt: _observedAt,
          retryAfterSeconds: 3,
        );
        final action2 = PairingRenotifyCooldownAction(
          observedAt: _observedAt,
          retryAfterSeconds: 3,
        );

        expect(action1, action2);
        expect(action1.hashCode, action2.hashCode);
      },
    );

    test(
      'Behavior equality in PairingRenotifyCooldownAction rejects instances with a different '
      'retryAfterSeconds',
      () {
        final action1 = PairingRenotifyCooldownAction(
          observedAt: _observedAt,
          retryAfterSeconds: 3,
        );
        final action2 = PairingRenotifyCooldownAction(
          observedAt: _observedAt,
          retryAfterSeconds: 5,
        );

        expect(action1, isNot(action2));
      },
    );

    test(
      'Behavior equality in PairingRenotifyCooldownAction treats zero retryAfterSeconds '
      '(immediate retry) as a normal value',
      () {
        final action1 = PairingRenotifyCooldownAction(
          observedAt: _observedAt,
          retryAfterSeconds: 0,
        );
        final action2 = PairingRenotifyCooldownAction(
          observedAt: _observedAt,
          retryAfterSeconds: 0,
        );

        expect(action1, action2);
        expect(action1.retryAfterSeconds, isA<int>());
      },
    );
  });

  group('Behavior equality in PairingCodeAvailableAction behaves correctly', () {
    test(
      'Behavior equality in PairingCodeAvailableAction treats instances with the same '
      'expiresInSeconds as equal',
      () {
        final action1 = PairingCodeAvailableAction(
          observedAt: _observedAt,
          expiresInSeconds: 30,
        );
        final action2 = PairingCodeAvailableAction(
          observedAt: _observedAt,
          expiresInSeconds: 30,
        );

        expect(action1, action2);
        expect(action1.hashCode, action2.hashCode);
      },
    );

    test(
      'Behavior equality in PairingCodeAvailableAction rejects instances with a different '
      'expiresInSeconds',
      () {
        final action1 = PairingCodeAvailableAction(
          observedAt: _observedAt,
          expiresInSeconds: 30,
        );
        final action2 = PairingCodeAvailableAction(
          observedAt: _observedAt,
          expiresInSeconds: 15,
        );

        expect(action1, isNot(action2));
      },
    );

    test(
      'Behavior equality in PairingCodeAvailableAction treats a null expiresInSeconds as equal '
      'to another null expiresInSeconds',
      () {
        final action1 = PairingCodeAvailableAction(observedAt: _observedAt);
        final action2 = PairingCodeAvailableAction(
          observedAt: _observedAt,
          expiresInSeconds: null,
        );

        expect(action1, action2);
        expect(
          action1,
          isNot(
            PairingCodeAvailableAction(
              observedAt: _observedAt,
              expiresInSeconds: 30,
            ),
          ),
        );
      },
    );
  });

  group(
    'Behavior equality in PairingCancelRequestedAction behaves correctly',
    () {
      test(
        'Behavior equality in PairingCancelRequestedAction treats two instances as equal',
        () {
          const action1 = PairingCancelRequestedAction();
          const action2 = PairingCancelRequestedAction();

          expect(action1, action2);
          expect(action1.hashCode, action2.hashCode);
        },
      );
    },
  );

  group(
    'Behavior equality in PairingCancelSucceededAction behaves correctly',
    () {
      test(
        'Behavior equality in PairingCancelSucceededAction treats two instances as equal',
        () {
          const action1 = PairingCancelSucceededAction();
          const action2 = PairingCancelSucceededAction();

          expect(action1, action2);
          expect(action1.hashCode, action2.hashCode);
        },
      );
    },
  );

  group(
    'Behavior equality in PairingConfirmFailedWithAttemptsRemainingAction behaves correctly',
    () {
      test(
        'Behavior equality in PairingConfirmFailedWithAttemptsRemainingAction treats '
        'instances with the same message as equal',
        () {
          const action1 = PairingConfirmFailedWithAttemptsRemainingAction(
            message: 'invalid',
            pairingOutcome: PairingFailureOutcome.invalid,
            attemptsRemaining: 2,
          );
          const action2 = PairingConfirmFailedWithAttemptsRemainingAction(
            message: 'invalid',
            pairingOutcome: PairingFailureOutcome.invalid,
            attemptsRemaining: 2,
          );

          expect(action1, action2);
          expect(action1.hashCode, action2.hashCode);
        },
      );

      test(
        'Behavior equality in PairingConfirmFailedWithAttemptsRemainingAction rejects '
        'instances with a different message',
        () {
          const action1 = PairingConfirmFailedWithAttemptsRemainingAction(
            message: 'invalid',
            pairingOutcome: PairingFailureOutcome.invalid,
          );
          const action2 = PairingConfirmFailedWithAttemptsRemainingAction(
            message: 'expired',
            pairingOutcome: PairingFailureOutcome.expired,
          );

          expect(action1, isNot(action2));
        },
      );

      test(
        'Behavior equality in PairingConfirmFailedWithAttemptsRemainingAction treats an '
        'empty string message as a normal value',
        () {
          const action1 = PairingConfirmFailedWithAttemptsRemainingAction(
            message: '',
            pairingOutcome: PairingFailureOutcome.invalid,
          );
          const action2 = PairingConfirmFailedWithAttemptsRemainingAction(
            message: '',
            pairingOutcome: PairingFailureOutcome.invalid,
          );

          expect(action1, action2);
          expect(action1.message, isEmpty);
        },
      );
    },
  );

  group('Behavior equality in PairingFailedAction behaves correctly', () {
    test('Behavior equality includes terminal pairing metadata', () {
      const PairingFailedAction first = PairingFailedAction(
        'Pairing ended.',
        pairingOutcome: PairingFailureOutcome.expired,
      );
      const PairingFailedAction equal = PairingFailedAction(
        'Pairing ended.',
        pairingOutcome: PairingFailureOutcome.expired,
      );
      const PairingFailedAction different = PairingFailedAction(
        'Pairing ended.',
        pairingOutcome: PairingFailureOutcome.hardLimitReached,
      );

      expect(first, equal);
      expect(first.hashCode, equal.hashCode);
      expect(first, isNot(different));
    });
  });

  group('Behavior equality in PairingSessionTrustedAction behaves correctly', () {
    test(
      'Behavior equality in PairingSessionTrustedAction treats two instances as equal',
      () {
        const action1 = PairingSessionTrustedAction();
        const action2 = PairingSessionTrustedAction();

        expect(action1, action2);
        expect(action1.hashCode, action2.hashCode);
      },
    );

    test(
      'Behavior equality in PairingSessionTrustedAction rejects a different action type',
      () {
        const action = PairingSessionTrustedAction();

        expect(action, isNot(const PairingConfirmedAction()));
      },
    );
  });
}
