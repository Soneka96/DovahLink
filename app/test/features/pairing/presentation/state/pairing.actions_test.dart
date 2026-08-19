import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';

/// Exercises pairing action construction and equality.
void main() {
  group('PairingRenotifyRequestedAction', () {
    test('is constructible and equatable', () {
      const action1 = PairingRenotifyRequestedAction();
      const action2 = PairingRenotifyRequestedAction();

      expect(action1, action2);
    });

    test('is not equal to other action types', () {
      const action = PairingRenotifyRequestedAction();

      expect(action, isNot(const PairingCancelRequestedAction()));
    });
  });

  group('PairingRenotifySucceededAction', () {
    test('is constructible and equatable', () {
      const action1 = PairingRenotifySucceededAction();
      const action2 = PairingRenotifySucceededAction();

      expect(action1, action2);
    });
  });

  group('PairingRenotifyCooldownAction', () {
    test('stores and compares retryAfterSeconds', () {
      const action1 = PairingRenotifyCooldownAction(retryAfterSeconds: 3);
      const action2 = PairingRenotifyCooldownAction(retryAfterSeconds: 3);

      expect(action1, action2);
    });

    test('is not equal when retryAfterSeconds differs', () {
      const action1 = PairingRenotifyCooldownAction(retryAfterSeconds: 3);
      const action2 = PairingRenotifyCooldownAction(retryAfterSeconds: 5);

      expect(action1, isNot(action2));
    });

    test('handles zero retryAfterSeconds (immediate retry)', () {
      const action1 = PairingRenotifyCooldownAction(retryAfterSeconds: 0);
      const action2 = PairingRenotifyCooldownAction(retryAfterSeconds: 0);

      expect(action1, action2);
      expect(action1.retryAfterSeconds, isA<int>());
    });
  });

  group('PairingCodeAvailableAction', () {
    test('stores and compares expiresInSeconds', () {
      const action1 = PairingCodeAvailableAction(expiresInSeconds: 30);
      const action2 = PairingCodeAvailableAction(expiresInSeconds: 30);

      expect(action1, action2);
    });

    test('is not equal when expiresInSeconds differs', () {
      const action1 = PairingCodeAvailableAction(expiresInSeconds: 30);
      const action2 = PairingCodeAvailableAction(expiresInSeconds: 15);

      expect(action1, isNot(action2));
    });

    test('handles a null expiresInSeconds', () {
      const action1 = PairingCodeAvailableAction();
      const action2 = PairingCodeAvailableAction(expiresInSeconds: null);

      expect(action1, action2);
      expect(
        action1,
        isNot(const PairingCodeAvailableAction(expiresInSeconds: 30)),
      );
    });
  });

  group('PairingCancelRequestedAction', () {
    test('is constructible and equatable', () {
      const action1 = PairingCancelRequestedAction();
      const action2 = PairingCancelRequestedAction();

      expect(action1, action2);
    });
  });

  group('PairingCancelSucceededAction', () {
    test('is constructible and equatable', () {
      const action1 = PairingCancelSucceededAction();
      const action2 = PairingCancelSucceededAction();

      expect(action1, action2);
    });
  });

  group('PairingConfirmFailedWithAttemptsRemainingAction', () {
    test('stores and compares message', () {
      const action1 = PairingConfirmFailedWithAttemptsRemainingAction(
        message: 'invalid',
      );
      const action2 = PairingConfirmFailedWithAttemptsRemainingAction(
        message: 'invalid',
      );

      expect(action1, action2);
    });

    test('is not equal when message differs', () {
      const action1 = PairingConfirmFailedWithAttemptsRemainingAction(
        message: 'invalid',
      );
      const action2 = PairingConfirmFailedWithAttemptsRemainingAction(
        message: 'expired',
      );

      expect(action1, isNot(action2));
    });

    test('handles empty string message', () {
      const action1 = PairingConfirmFailedWithAttemptsRemainingAction(
        message: '',
      );
      const action2 = PairingConfirmFailedWithAttemptsRemainingAction(
        message: '',
      );

      expect(action1, action2);
      expect(action1.message, isEmpty);
    });
  });
}
