import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// Runs typed Host-identifier behavior tests.
void main() {
  const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';

  group('Method constructor behaves correctly', () {
    test('Method constructor stores a validated Host UUID', () {
      expect(DovahLinkHostId(hostId).value, hostId);
    });

    test('Method constructor canonicalizes uppercase UUID letters', () {
      expect(DovahLinkHostId(hostId.toUpperCase()).value, hostId);
    });

    test('Method constructor rejects values that are not Host UUIDs', () {
      expect(() => DovahLinkHostId('client-1'), throwsArgumentError);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality compares Host UUIDs without case differences', () {
      final DovahLinkHostId lower = DovahLinkHostId(hostId);
      final DovahLinkHostId upper = DovahLinkHostId(hostId.toUpperCase());

      expect(lower, upper);
      expect(lower.hashCode, upper.hashCode);
    });
  });
}
