import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs [HelloResult] constructor behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test('Method constructor preserves hostVersion and trustState', () {
      const HelloResult result = HelloResult(
        hostVersion: '1.2.3',
        trustState: DovahLinkTrustState.trusted,
      );

      expect(result.hostVersion, '1.2.3');
      expect(result.trustState, DovahLinkTrustState.trusted);
      expect(result.recoveredFromRejectedCredential, isNull);
    });

    test('Method constructor preserves a credential rejection reason', () {
      const HelloResult result = HelloResult(
        hostVersion: '1.2.3',
        trustState: DovahLinkTrustState.unpaired,
        recoveredFromRejectedCredential: CredentialRejectionReason.revoked,
      );

      expect(
        result.recoveredFromRejectedCredential,
        CredentialRejectionReason.revoked,
      );
    });
  });
}
