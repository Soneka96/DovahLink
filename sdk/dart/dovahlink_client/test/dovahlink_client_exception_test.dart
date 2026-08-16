import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_client_exception.dart';

/// The pre-existing [DovahLinkConnectionException], [DovahLinkProtocolException], and
/// [DovahLinkPairingException] are exercised indirectly through `dovahlink_client_test.dart`,
/// matching this file's existing precedent; only the new [DovahLinkStorageException] is tested
/// directly here.
void main() {
  group('DovahLinkStorageException', () {
    group('methods', () {
      test('exposes the diagnostic message it was created with', () {
        const DovahLinkStorageException exception = DovahLinkStorageException(
          'undecryptable persisted state',
        );

        expect(exception.message, 'undecryptable persisted state');
      });

      test('toString includes the diagnostic message', () {
        const DovahLinkStorageException exception = DovahLinkStorageException(
          'undecryptable persisted state',
        );

        expect(
          exception.toString(),
          'DovahLinkStorageException: undecryptable persisted state',
        );
      });

      test('is throwable and catchable as an Exception', () {
        expect(
          () => throw const DovahLinkStorageException('corrupt store'),
          throwsA(isA<DovahLinkStorageException>()),
        );
      });
    });
  });
}
