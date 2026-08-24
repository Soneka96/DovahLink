import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';

/// Exercises pairing action construction and equality.
void main() {
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
          const action1 = PairingRenotifySucceededAction();
          const action2 = PairingRenotifySucceededAction();

          expect(action1, action2);
          expect(action1.hashCode, action2.hashCode);
        },
      );
    },
  );

  group('Behavior equality in PairingRenotifyCooldownAction behaves correctly', () {
    test(
      'Behavior equality in PairingRenotifyCooldownAction treats instances with the same '
      'retryAfterSeconds as equal',
      () {
        const action1 = PairingRenotifyCooldownAction(retryAfterSeconds: 3);
        const action2 = PairingRenotifyCooldownAction(retryAfterSeconds: 3);

        expect(action1, action2);
        expect(action1.hashCode, action2.hashCode);
      },
    );

    test(
      'Behavior equality in PairingRenotifyCooldownAction rejects instances with a different '
      'retryAfterSeconds',
      () {
        const action1 = PairingRenotifyCooldownAction(retryAfterSeconds: 3);
        const action2 = PairingRenotifyCooldownAction(retryAfterSeconds: 5);

        expect(action1, isNot(action2));
      },
    );

    test(
      'Behavior equality in PairingRenotifyCooldownAction treats zero retryAfterSeconds '
      '(immediate retry) as a normal value',
      () {
        const action1 = PairingRenotifyCooldownAction(retryAfterSeconds: 0);
        const action2 = PairingRenotifyCooldownAction(retryAfterSeconds: 0);

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
        const action1 = PairingCodeAvailableAction(expiresInSeconds: 30);
        const action2 = PairingCodeAvailableAction(expiresInSeconds: 30);

        expect(action1, action2);
        expect(action1.hashCode, action2.hashCode);
      },
    );

    test(
      'Behavior equality in PairingCodeAvailableAction rejects instances with a different '
      'expiresInSeconds',
      () {
        const action1 = PairingCodeAvailableAction(expiresInSeconds: 30);
        const action2 = PairingCodeAvailableAction(expiresInSeconds: 15);

        expect(action1, isNot(action2));
      },
    );

    test(
      'Behavior equality in PairingCodeAvailableAction treats a null expiresInSeconds as equal '
      'to another null expiresInSeconds',
      () {
        const action1 = PairingCodeAvailableAction();
        const action2 = PairingCodeAvailableAction(expiresInSeconds: null);

        expect(action1, action2);
        expect(
          action1,
          isNot(const PairingCodeAvailableAction(expiresInSeconds: 30)),
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
          );
          const action2 = PairingConfirmFailedWithAttemptsRemainingAction(
            message: 'invalid',
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
          );
          const action2 = PairingConfirmFailedWithAttemptsRemainingAction(
            message: 'expired',
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
          );
          const action2 = PairingConfirmFailedWithAttemptsRemainingAction(
            message: '',
          );

          expect(action1, action2);
          expect(action1.message, isEmpty);
        },
      );
    },
  );

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
