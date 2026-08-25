import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';

import '../../../../fixtures/fixtures.dart';

/// Exercises pairing-handshake entity value preservation.
void main() {
  group('PairingHandshakeEntity', () {
    test('stores the reported bridge version and trust standing', () {
      final PairingHandshakeEntity handshake =
          Fixtures.buildPairingHandshakeEntity(
            bridgeVersion: '1.2.3',
            trusted: true,
          );

      expect(handshake.bridgeVersion, '1.2.3');
      expect(handshake.trusted, isTrue);
    });

    test('treats handshakes with different trust standing as unequal', () {
      final PairingHandshakeEntity first = Fixtures.buildPairingHandshakeEntity(
        bridgeVersion: '1.2.3',
        trusted: false,
      );
      final PairingHandshakeEntity second =
          Fixtures.buildPairingHandshakeEntity(
            bridgeVersion: '1.2.3',
            trusted: true,
          );

      expect(first == second, isFalse);
    });

    test('stores a credential-rejected message when set', () {
      final PairingHandshakeEntity handshake =
          Fixtures.buildPairingHandshakeEntity(
            bridgeVersion: '1.2.3',
            trusted: false,
            credentialRejectedMessage: "This device's trust was revoked.",
          );

      expect(handshake.credentialRejectedMessage, isA<String>());
      expect(
        handshake.credentialRejectedMessage,
        "This device's trust was revoked.",
      );
    });

    test('defaults the credential-rejected message to null', () {
      final PairingHandshakeEntity handshake =
          Fixtures.buildPairingHandshakeEntity(
            bridgeVersion: '1.2.3',
            trusted: false,
          );

      expect(handshake.credentialRejectedMessage, isNull);
    });

    test(
      'treats handshakes with different credential-rejected messages as unequal',
      () {
        final PairingHandshakeEntity first =
            Fixtures.buildPairingHandshakeEntity(
              bridgeVersion: '1.2.3',
              trusted: false,
            );
        final PairingHandshakeEntity second =
            Fixtures.buildPairingHandshakeEntity(
              bridgeVersion: '1.2.3',
              trusted: false,
              credentialRejectedMessage: "This device's trust was revoked.",
            );

        expect(first == second, isFalse);
      },
    );

    test(
      'treats handshakes with the same credential-rejected message as equal',
      () {
        final PairingHandshakeEntity first =
            Fixtures.buildPairingHandshakeEntity(
              bridgeVersion: '1.2.3',
              trusted: false,
              credentialRejectedMessage: "This device's trust was revoked.",
            );
        final PairingHandshakeEntity second =
            Fixtures.buildPairingHandshakeEntity(
              bridgeVersion: '1.2.3',
              trusted: false,
              credentialRejectedMessage: "This device's trust was revoked.",
            );

        expect(first == second, isTrue);
      },
    );
  });
}
