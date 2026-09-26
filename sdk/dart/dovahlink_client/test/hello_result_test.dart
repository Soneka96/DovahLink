import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs [HelloResult] constructor behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test('Method constructor preserves hostVersion and trustState', () {
      const HelloResult result = HelloResult(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'GONCALO-DESKTOP',
        hostVersion: '1.2.3',
        trustState: DovahLinkTrustState.trusted,
      );

      expect(result.hostId, '81869993-955c-4ba3-a7d0-d35ca86078ea');
      expect(result.hostName, 'GONCALO-DESKTOP');
      expect(result.hostVersion, '1.2.3');
      expect(result.trustState, DovahLinkTrustState.trusted);
      expect(result.recoveredFromRejectedCredential, isNull);
    });

    test('Method constructor preserves a credential rejection reason', () {
      const HelloResult result = HelloResult(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'GONCALO-DESKTOP',
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
