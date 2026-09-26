import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/host_identity_validator.dart';

/// Runs Host identity validation behavior tests.
void main() {
  group('Function isValidHostId behaves correctly', () {
    test('Function isValidHostId accepts the supported UUID shape', () {
      expect(isValidHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'), isTrue);
      expect(isValidHostId('81869993-955C-4BA3-A7D0-D35CA86078EA'), isTrue);
    });

    test('Function isValidHostId rejects empty, malformed, and zero UUIDs', () {
      for (final String value in <String>[
        '',
        'not-a-uuid',
        '00000000-0000-0000-0000-000000000000',
      ]) {
        expect(isValidHostId(value), isFalse, reason: value);
      }
    });
  });

  group('Function isValidHostName behaves correctly', () {
    test(
      'Function isValidHostName accepts names within the UTF-8 byte limit',
      () {
        expect(isValidHostName('GONCALO-DESKTOP'), isTrue);
        expect(isValidHostName(List<String>.filled(32, 'é').join()), isTrue);
        expect(isValidHostName('PC-😀'), isTrue);
      },
    );

    test(
      'Function isValidHostName rejects empty, control, and overlong names',
      () {
        expect(isValidHostName(''), isFalse);
        expect(isValidHostName('   '), isFalse);
        expect(isValidHostName('DESKTOP\nPC'), isFalse);
        expect(isValidHostName('DESKTOP\u007fPC'), isFalse);
        expect(isValidHostName(List<String>.filled(33, 'é').join()), isFalse);
      },
    );

    test('Function isValidHostName rejects unpaired UTF-16 surrogates', () {
      for (final String value in <String>[
        'DESKTOP\uD800',
        '\uDC00DESKTOP',
        'DESKTOP\uD800PC',
      ]) {
        expect(isValidHostName(value), isFalse);
      }
    });
  });
}
