import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';

import '../../../../fixtures/fixtures.dart';

/// Exercises Bridge entity value preservation.
void main() {
  group('BridgeEntity', () {
    test('stores the display name and endpoint', () {
      final BridgeEntity bridge = Fixtures.buildBridgeEntity(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(bridge.displayName, 'Local Bridge');
      expect(bridge.uri, Uri.parse('ws://127.0.0.1:58231/'));
    });

    test('treats bridges with different endpoints as unequal', () {
      final BridgeEntity first = Fixtures.buildBridgeEntity(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final BridgeEntity second = Fixtures.buildBridgeEntity(
        uri: Uri.parse('ws://127.0.0.1:9999/'),
      );

      expect(first == second, isFalse);
    });

    test('treats bridges with different display names as unequal', () {
      final BridgeEntity first = Fixtures.buildBridgeEntity(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final BridgeEntity second = Fixtures.buildBridgeEntity(
        displayName: 'Other Bridge',
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(first == second, isFalse);
    });

    test('treats bridges with the same display name and endpoint as equal', () {
      final BridgeEntity first = Fixtures.buildBridgeEntity(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final BridgeEntity second = Fixtures.buildBridgeEntity(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(first == second, isTrue);
    });
  });
}
