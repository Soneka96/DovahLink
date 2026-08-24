import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_revision_validator.dart';

/// Runs [ProtocolRevisionValidator.validateNonNegativeInt] behavior tests.
void main() {
  group('Method validateNonNegativeInt behaves correctly', () {
    test('Method validateNonNegativeInt accepts zero', () {
      expect(
        () => ProtocolRevisionValidator.validateNonNegativeInt(0, 'revision'),
        returnsNormally,
      );
    });

    test('Method validateNonNegativeInt accepts a positive integer', () {
      expect(
        () => ProtocolRevisionValidator.validateNonNegativeInt(42, 'revision'),
        returnsNormally,
      );
    });

    test('Method validateNonNegativeInt rejects negative integers', () {
      expect(
        () => ProtocolRevisionValidator.validateNonNegativeInt(-1, 'revision'),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method validateNonNegativeInt rejects fractional numbers', () {
      expect(
        () => ProtocolRevisionValidator.validateNonNegativeInt(1.5, 'revision'),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method validateNonNegativeInt rejects strings and null', () {
      for (final Object? value in <Object?>['1', null]) {
        expect(
          () => ProtocolRevisionValidator.validateNonNegativeInt(
            value,
            'revision',
          ),
          throwsA(isA<ProtocolFormatException>()),
        );
      }
    });
  });
}
