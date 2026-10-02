import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// Runs Known Host invalidation value behavior tests.
void main() {
  group('Behavior DovahLinkKnownHostInvalidation behaves correctly', () {
    test('DovahLinkKnownHostInvalidation retains its Host and reason', () {
      final DovahLinkHostId hostId = DovahLinkHostId(
        '81869993-955c-4ba3-a7d0-d35ca86078ea',
      );
      const AdministrativeInvalidationReason reason =
          AdministrativeInvalidationReason.trustReset;

      final DovahLinkKnownHostInvalidation invalidation =
          DovahLinkKnownHostInvalidation(hostId: hostId, reason: reason);

      expect(invalidation.hostId, hostId);
      expect(invalidation.reason, reason);
    });
  });
}
