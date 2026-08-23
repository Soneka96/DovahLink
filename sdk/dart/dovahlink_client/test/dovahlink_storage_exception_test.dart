import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';

/// Runs [DovahLinkStorageException] behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test('Method constructor preserves the diagnostic message', () {
      const DovahLinkStorageException exception = DovahLinkStorageException(
        'undecryptable persisted state',
      );

      expect(exception.message, 'undecryptable persisted state');
    });

    test('Method constructor creates a throwable storage exception', () {
      expect(
        () => throw const DovahLinkStorageException('corrupt store'),
        throwsA(isA<DovahLinkStorageException>()),
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
}
