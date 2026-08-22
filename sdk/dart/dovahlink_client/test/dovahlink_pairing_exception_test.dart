import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs [DovahLinkPairingException] behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test('Method constructor preserves outcome and retryAfterSeconds', () {
      const DovahLinkPairingException exception = DovahLinkPairingException(
        PairingOutcome.pacingLimited,
        retryAfterSeconds: 2,
      );

      expect(exception.outcome, PairingOutcome.pacingLimited);
      expect(exception.retryAfterSeconds, 2);
    });

    test('Method constructor creates a throwable pairing exception', () {
      expect(
        () => throw const DovahLinkPairingException(PairingOutcome.expired),
        throwsA(isA<DovahLinkPairingException>()),
      );
    });

    test('Method constructor defaults retryAfterSeconds to null', () {
      const DovahLinkPairingException exception = DovahLinkPairingException(
        PairingOutcome.expired,
      );

      expect(exception.retryAfterSeconds, isNull);
    });
  });

  group('Method toString behaves correctly', () {
    test('Method toString returns the pairing outcome diagnostic', () {
      const DovahLinkPairingException exception = DovahLinkPairingException(
        PairingOutcome.expired,
      );

      expect(
        exception.toString(),
        'DovahLinkPairingException: PairingOutcome.expired',
      );
    });
  });
}
