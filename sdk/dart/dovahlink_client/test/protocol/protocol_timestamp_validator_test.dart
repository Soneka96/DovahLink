import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_timestamp_validator.dart';

/// Runs [ProtocolTimestampValidator.validateUtcRfc3339] behavior tests.
void main() {
  group('Method validateUtcRfc3339 behaves correctly', () {
    test(
      'Method validateUtcRfc3339 accepts a second-precision UTC timestamp',
      () {
        expect(
          () => ProtocolTimestampValidator.validateUtcRfc3339(
            '2026-08-11T12:00:00Z',
          ),
          returnsNormally,
        );
      },
    );

    test('Method validateUtcRfc3339 accepts microsecond precision', () {
      expect(
        () => ProtocolTimestampValidator.validateUtcRfc3339(
          '2026-08-11T12:00:00.123456Z',
        ),
        returnsNormally,
      );
    });

    test('Method validateUtcRfc3339 accepts one fractional digit', () {
      expect(
        () => ProtocolTimestampValidator.validateUtcRfc3339(
          '2026-08-11T12:00:00.1Z',
        ),
        returnsNormally,
      );
    });

    test('Method validateUtcRfc3339 rejects an empty value', () {
      expect(
        () => ProtocolTimestampValidator.validateUtcRfc3339(''),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method validateUtcRfc3339 rejects a malformed value', () {
      expect(
        () => ProtocolTimestampValidator.validateUtcRfc3339('not-a-timestamp'),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method validateUtcRfc3339 rejects a non-UTC offset', () {
      expect(
        () => ProtocolTimestampValidator.validateUtcRfc3339(
          '2026-08-11T12:00:00+01:00',
        ),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test(
      'Method validateUtcRfc3339 rejects zero offsets that are not canonical Z',
      () {
        for (final String value in <String>[
          '2026-08-11T12:00:00+00:00',
          '2026-08-11T12:00:00-00:00',
        ]) {
          expect(
            () => ProtocolTimestampValidator.validateUtcRfc3339(value),
            throwsA(isA<ProtocolFormatException>()),
          );
        }
      },
    );

    test(
      'Method validateUtcRfc3339 rejects more than six fractional digits',
      () {
        expect(
          () => ProtocolTimestampValidator.validateUtcRfc3339(
            '2026-08-11T12:00:00.1234567Z',
          ),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test('Method validateUtcRfc3339 rejects invalid clock components', () {
      for (final String value in <String>[
        '2026-08-11T24:00:00Z',
        '2026-08-11T12:60:00Z',
        '2026-08-11T12:00:60Z',
      ]) {
        expect(
          () => ProtocolTimestampValidator.validateUtcRfc3339(value),
          throwsA(isA<ProtocolFormatException>()),
        );
      }
    });

    test('Method validateUtcRfc3339 rejects an invalid calendar date', () {
      expect(
        () => ProtocolTimestampValidator.validateUtcRfc3339(
          '2026-02-30T12:00:00Z',
        ),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
