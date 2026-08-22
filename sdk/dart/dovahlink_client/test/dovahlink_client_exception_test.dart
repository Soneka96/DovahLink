import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs typed exception behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test(
      'Method constructor preserves outcome and retryAfterSeconds for pairing exceptions',
      () {
        const DovahLinkPairingException exception = DovahLinkPairingException(
          PairingOutcome.pacingLimited,
          retryAfterSeconds: 2,
        );

        expect(exception.outcome, PairingOutcome.pacingLimited);
        expect(exception.retryAfterSeconds, 2);
      },
    );

    test('Method constructor creates a throwable pairing exception', () {
      expect(
        () => throw const DovahLinkPairingException(PairingOutcome.expired),
        throwsA(isA<DovahLinkPairingException>()),
      );
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

  group('Method toString behaves correctly', () {
    test('Method toString returns the storage diagnostic message', () {
      const DovahLinkStorageException exception = DovahLinkStorageException(
        'undecryptable persisted state',
      );

      expect(
        exception.toString(),
        'DovahLinkStorageException: undecryptable persisted state',
      );
    });
  });

  group('Method constructor behaves correctly', () {
    test(
      'Method constructor preserves the diagnostic message for storage exceptions',
      () {
        const DovahLinkStorageException exception = DovahLinkStorageException(
          'undecryptable persisted state',
        );

        expect(exception.message, 'undecryptable persisted state');
      },
    );

    test('Method constructor creates a throwable storage exception', () {
      expect(
        () => throw const DovahLinkStorageException('corrupt store'),
        throwsA(isA<DovahLinkStorageException>()),
      );
    });
  });
}
