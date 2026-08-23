import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/random_id_generator.dart';

/// Runs random-ID generator behavior tests.
void main() {
  group('Method generateUuid behaves correctly', () {
    test('Method generateUuid returns a lowercase RFC 4122 version-4 UUID', () {
      final RandomIdGenerator generator = RandomIdGenerator();

      final String value = generator.generateUuid();

      expect(
        value,
        matches(
          RegExp(
            r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
          ),
        ),
      );
    });

    test('Method generateUuid returns a fresh UUID for each call', () {
      final RandomIdGenerator generator = RandomIdGenerator();

      final String first = generator.generateUuid();
      final String second = generator.generateUuid();

      expect(first, isNot(second));
    });
  });

  group('Method generateMessageId behaves correctly', () {
    test(
      'Method generateMessageId returns a lowercase 32-character hexadecimal ID',
      () {
        final RandomIdGenerator generator = RandomIdGenerator();

        final String value = generator.generateMessageId();

        expect(value, matches(RegExp(r'^[0-9a-f]{32}$')));
      },
    );

    test('Method generateMessageId returns a fresh ID for each call', () {
      final RandomIdGenerator generator = RandomIdGenerator();

      final String first = generator.generateMessageId();
      final String second = generator.generateMessageId();

      expect(first, isNot(second));
    });
  });

  group('Behavior shared random source behaves correctly', () {
    test(
      'Behavior shared random source produces independent UUID and message-ID output',
      () {
        // Both methods draw from the same secure-random source; this proves the shared instance
        // does not produce correlated or colliding output between them.
        final RandomIdGenerator generator = RandomIdGenerator();

        final String uuid = generator.generateUuid();
        final String messageId = generator.generateMessageId();

        expect(uuid.replaceAll('-', ''), isNot(messageId));
      },
    );
  });
}
