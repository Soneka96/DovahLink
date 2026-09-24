import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';

import '../../../../fixtures/fixtures.dart';

/// Exercises pairing-handshake entity value preservation.
void main() {
  group('Property hostVersion behaves correctly', () {
    test('PairingHandshake.hostVersion stores the reported host version', () {
      final PairingHandshake handshake = Fixtures.buildPairingHandshake(
        hostVersion: '1.2.3',
        trusted: true,
      );

      expect(handshake.hostVersion, '1.2.3');
    });
  });

  group('Property trusted behaves correctly', () {
    test('PairingHandshake.trusted stores the trust standing', () {
      final PairingHandshake handshake = Fixtures.buildPairingHandshake(
        hostVersion: '1.2.3',
        trusted: true,
      );

      expect(handshake.trusted, isTrue);
    });
  });

  group('Property credentialRejectedMessage behaves correctly', () {
    test(
      'PairingHandshake.credentialRejectedMessage stores a supplied message',
      () {
        final PairingHandshake handshake = Fixtures.buildPairingHandshake(
          hostVersion: '1.2.3',
          trusted: false,
          credentialRejectedMessage: "This device's trust was revoked.",
        );

        expect(handshake.credentialRejectedMessage, isA<String>());
        expect(
          handshake.credentialRejectedMessage,
          "This device's trust was revoked.",
        );
      },
    );

    test('PairingHandshake.credentialRejectedMessage defaults to null', () {
      final PairingHandshake handshake = Fixtures.buildPairingHandshake(
        hostVersion: '1.2.3',
        trusted: false,
      );

      expect(handshake.credentialRejectedMessage, isNull);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('PairingHandshake equality changes when host versions differ', () {
      final PairingHandshake first = Fixtures.buildPairingHandshake(
        hostVersion: '1.2.3',
        trusted: true,
      );
      final PairingHandshake second = Fixtures.buildPairingHandshake(
        hostVersion: '2.0.0',
        trusted: true,
      );

      expect(first == second, isFalse);
    });

    test('PairingHandshake equality changes when trust standings differ', () {
      final PairingHandshake first = Fixtures.buildPairingHandshake(
        hostVersion: '1.2.3',
        trusted: false,
      );
      final PairingHandshake second = Fixtures.buildPairingHandshake(
        hostVersion: '1.2.3',
        trusted: true,
      );

      expect(first == second, isFalse);
    });

    test(
      'PairingHandshake equality changes when rejection messages differ',
      () {
        final PairingHandshake first = Fixtures.buildPairingHandshake(
          hostVersion: '1.2.3',
          trusted: false,
        );
        final PairingHandshake second = Fixtures.buildPairingHandshake(
          hostVersion: '1.2.3',
          trusted: false,
          credentialRejectedMessage: "This device's trust was revoked.",
        );

        expect(first == second, isFalse);
      },
    );

    test(
      'PairingHandshake equality gives matching hashes for equal values',
      () {
        final PairingHandshake first = Fixtures.buildPairingHandshake(
          hostVersion: '1.2.3',
          trusted: false,
          credentialRejectedMessage: "This device's trust was revoked.",
        );
        final PairingHandshake second = Fixtures.buildPairingHandshake(
          hostVersion: '1.2.3',
          trusted: false,
          credentialRejectedMessage: "This device's trust was revoked.",
        );

        expect(first == second, isTrue);
        expect(first.hashCode, second.hashCode);
      },
    );
  });
}
