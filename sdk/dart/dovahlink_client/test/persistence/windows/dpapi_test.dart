@TestOn('windows')
library;

import 'dart:convert';
import 'dart:typed_data';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/persistence/windows/dpapi.dart';

void main() {
  group('Dpapi', () {
    group('protect/unprotect', () {
      test('round-trips a normal payload', () {
        final Uint8List plaintext = Uint8List.fromList(utf8.encode('hello'));

        final Uint8List encrypted = Dpapi.protect(plaintext);
        final Uint8List decrypted = Dpapi.unprotect(encrypted);

        expect(utf8.decode(decrypted), 'hello');
      });

      test('round-trips an empty payload', () {
        final Uint8List plaintext = Uint8List(0);

        final Uint8List encrypted = Dpapi.protect(plaintext);
        final Uint8List decrypted = Dpapi.unprotect(encrypted);

        expect(decrypted, isEmpty);
      });
    });

    group('unprotect', () {
      test(
        'throws DovahLinkStorageException for bytes that are not a DPAPI blob',
        () {
          expect(
            () => Dpapi.unprotect(Uint8List.fromList(<int>[1, 2, 3, 4])),
            throwsA(isA<DovahLinkStorageException>()),
          );
        },
      );

      test(
        'throws DovahLinkStorageException for an empty (zero-length) input',
        () {
          expect(
            () => Dpapi.unprotect(Uint8List(0)),
            throwsA(isA<DovahLinkStorageException>()),
          );
        },
      );
    });
  });
}
