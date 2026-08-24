import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/failures/failures.dart';

/// Runs [SessionInvalidatedFailure] behavior tests -- the only [Failure]
/// subclass in this file without existing coverage before this addition.
void main() {
  group('Property message behaves correctly', () {
    test('Property message carries the given value', () {
      const SessionInvalidatedFailure failure = SessionInvalidatedFailure(
        'session ended',
      );

      expect(failure.message, isA<String>());
      expect(failure.message, 'session ended');
    });
  });

  group('Behavior equality behaves correctly', () {
    test(
      'Behavior equality treats instances with the same message as equal',
      () {
        const SessionInvalidatedFailure first = SessionInvalidatedFailure(
          'session ended',
        );
        const SessionInvalidatedFailure second = SessionInvalidatedFailure(
          'session ended',
        );

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      },
    );

    test('Behavior equality rejects instances with a different message', () {
      const SessionInvalidatedFailure first = SessionInvalidatedFailure(
        'session ended',
      );
      const SessionInvalidatedFailure second = SessionInvalidatedFailure(
        'different reason',
      );

      expect(first == second, isFalse);
    });

    test(
      'Behavior equality rejects a NetworkFailure with the same message',
      () {
        const SessionInvalidatedFailure invalidated = SessionInvalidatedFailure(
          'session ended',
        );
        const NetworkFailure network = NetworkFailure('session ended');

        expect(invalidated == network, isFalse);
      },
    );
  });

  group('Property administrative behaves correctly', () {
    test('Property administrative carries the canonical reason-agnostic message', () {
      expect(
        SessionInvalidatedFailure.administrative.message,
        isA<String>(),
      );
      expect(
        SessionInvalidatedFailure.administrative.message,
        'This device was disconnected by the bridge. Try again.',
      );
    });
  });
}
