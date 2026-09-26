import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// Runs Host identity mismatch exception behavior tests.
void main() {
  group('Properties Host identity mismatch behave correctly', () {
    test('Exception exposes both IDs through the public SDK API', () {
      const DovahLinkHostIdentityMismatchException error =
          DovahLinkHostIdentityMismatchException(
            knownHostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
            reportedHostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          );

      expect(error.knownHostId, '81869993-955c-4ba3-a7d0-d35ca86078ea');
      expect(error.reportedHostId, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');
      expect(error.toString(), contains(error.knownHostId));
      expect(error.toString(), contains(error.reportedHostId));
    });
  });
}
