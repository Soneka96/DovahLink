import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs [HelloResult] constructor behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test('Method constructor preserves bridgeVersion and trustState', () {
      const HelloResult result = HelloResult(
        bridgeVersion: '1.2.3',
        trustState: DovahLinkTrustState.trusted,
      );

      expect(result.bridgeVersion, '1.2.3');
      expect(result.trustState, DovahLinkTrustState.trusted);
      expect(result.recoveredFromRejectedCredential, isNull);
    });

    test('Method constructor preserves a credential rejection reason', () {
      const HelloResult result = HelloResult(
        bridgeVersion: '1.2.3',
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
