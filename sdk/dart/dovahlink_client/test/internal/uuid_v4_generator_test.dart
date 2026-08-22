import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/uuid_v4_generator.dart';

/// Runs UUID generator behavior tests.
void main() {
  group('Method generate behaves correctly', () {
    test('Method generate returns a lowercase RFC 4122 version-4 UUID', () {
      final UuidV4Generator generator = UuidV4Generator();

      final String value = generator.generate();

      expect(
        value,
        matches(
          RegExp(
            r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
          ),
        ),
      );
    });
  });
}
