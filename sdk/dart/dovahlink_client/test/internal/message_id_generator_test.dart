import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/message_id_generator.dart';

/// Runs message-ID generator behavior tests.
void main() {
  group('Method generate behaves correctly', () {
    test('Method generate returns a lowercase 32-character hexadecimal ID', () {
      final MessageIdGenerator generator = MessageIdGenerator();

      final String value = generator.generate();

      expect(value, matches(RegExp(r'^[0-9a-f]{32}$')));
    });

    test('Method generate returns a fresh ID for each call', () {
      final MessageIdGenerator generator = MessageIdGenerator();

      final String first = generator.generate();
      final String second = generator.generate();

      expect(first, isNot(second));
    });
  });
}
