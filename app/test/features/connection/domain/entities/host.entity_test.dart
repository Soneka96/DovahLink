import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises Host entity value preservation.
void main() {
  group('Property displayName behaves correctly', () {
    test('Host.displayName stores the supplied display name', () {
      final Host host = Fixtures.buildHost(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(host.displayName, 'Local Host');
    });
  });

  group('Property uri behaves correctly', () {
    test('Host.uri stores the supplied endpoint', () {
      final Host host = Fixtures.buildHost(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(host.uri, Uri.parse('ws://127.0.0.1:58231/'));
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Host equality changes when endpoints differ', () {
      final Host first = Fixtures.buildHost(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final Host second = Fixtures.buildHost(
        uri: Uri.parse('ws://127.0.0.1:9999/'),
      );

      expect(first == second, isFalse);
    });

    test('Host equality changes when display names differ', () {
      final Host first = Fixtures.buildHost(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final Host second = Fixtures.buildHost(
        displayName: 'Other Host',
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(first == second, isFalse);
    });

    test('Host equality gives matching hashes for equal values', () {
      final Host first = Fixtures.buildHost(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final Host second = Fixtures.buildHost(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(first == second, isTrue);
      expect(first.hashCode, second.hashCode);
    });
  });
}
