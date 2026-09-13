import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';

import '../../../../fixtures/fixtures.dart';

/// Exercises Host entity value preservation.
void main() {
  group('HostEntity', () {
    test('stores the display name and endpoint', () {
      final HostEntity host = Fixtures.buildHostEntity(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(host.displayName, 'Local Bridge');
      expect(host.uri, Uri.parse('ws://127.0.0.1:58231/'));
    });

    test('treats hosts with different endpoints as unequal', () {
      final HostEntity first = Fixtures.buildHostEntity(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final HostEntity second = Fixtures.buildHostEntity(
        uri: Uri.parse('ws://127.0.0.1:9999/'),
      );

      expect(first == second, isFalse);
    });

    test('treats hosts with different display names as unequal', () {
      final HostEntity first = Fixtures.buildHostEntity(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final HostEntity second = Fixtures.buildHostEntity(
        displayName: 'Other Bridge',
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(first == second, isFalse);
    });

    test('treats hosts with the same display name and endpoint as equal', () {
      final HostEntity first = Fixtures.buildHostEntity(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final HostEntity second = Fixtures.buildHostEntity(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(first == second, isTrue);
    });
  });
}
