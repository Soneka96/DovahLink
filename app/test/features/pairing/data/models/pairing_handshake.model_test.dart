import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/data/models/pairing_handshake.model.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises SDK authentication result mapping for [PairingHandshakeModel].
void main() {
  group('Method fromHelloResult behaves correctly', () {
    test(
      'Method fromHelloResult maps the host version and resolved trust status',
      () {
        final HelloResult hello = Fixtures.buildSdkHelloResult(
          hostVersion: '1.2.3',
          trustState: DovahLinkTrustState.unpaired,
        );

        final PairingHandshakeModel model =
            PairingHandshakeModel.fromHelloResult(hello: hello, trusted: true);

        expect(model, isA<PairingHandshake>());
        expect(model.hostVersion, '1.2.3');
        expect(model.trusted, isTrue);
        expect(model.credentialRejectionReason, isNull);
        expect(model.credentialRejectedMessage, isNull);
      },
    );

    test('Method fromHelloResult maps each rejected credential reason', () {
      final List<
        (CredentialRejectionReason, PairingCredentialRejectionReason, String)
      >
      mappings = [
        (
          CredentialRejectionReason.revoked,
          PairingCredentialRejectionReason.revoked,
          "This device's trust was revoked.",
        ),
        (
          CredentialRejectionReason.unrecognized,
          PairingCredentialRejectionReason.unrecognized,
          "This device isn't recognized by this host.",
        ),
        (
          CredentialRejectionReason.blocked,
          PairingCredentialRejectionReason.blocked,
          'This device is blocked by the host and cannot be paired again until an '
              'administrator unblocks it.',
        ),
      ];

      for (final (
            CredentialRejectionReason reason,
            PairingCredentialRejectionReason appReason,
            String message,
          )
          in mappings) {
        final PairingHandshakeModel model =
            PairingHandshakeModel.fromHelloResult(
              hello: Fixtures.buildSdkHelloResult(
                hostVersion: '2.0.0',
                trustState: DovahLinkTrustState.unpaired,
                recoveredFromRejectedCredential: reason,
              ),
              trusted: false,
            );

        expect(
          model.credentialRejectionReason,
          isA<PairingCredentialRejectionReason>(),
        );
        expect(model.credentialRejectionReason, appReason);
        expect(model.credentialRejectedMessage, message);
        expect(model.hostVersion, '2.0.0');
        expect(model.trusted, isFalse);
      }
    });
  });
}
