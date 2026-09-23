import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// Runs public compatibility-exception behavior tests.
void main() {
  group('Behavior compatibility details behave correctly', () {
    test(
      'Behavior compatibility details preserve the Host version, supported range, and failure direction',
      () {
        const DovahLinkCompatibilityException error =
            DovahLinkCompatibilityException(
              hostVersion: '0.5.0',
              supportedHostVersionRange: '0.4.x',
              failure: HostVersionCompatibilityFailure.hostTooNew,
            );

        expect(error.hostVersion, '0.5.0');
        expect(error.supportedHostVersionRange, '0.4.x');
        expect(error.failure, HostVersionCompatibilityFailure.hostTooNew);
        expect(
          error.toString(),
          contains('DovahLinkCompatibilityException(hostTooNew'),
        );
      },
    );
  });
}
