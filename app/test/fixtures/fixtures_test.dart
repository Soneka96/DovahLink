import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';

import 'fixtures.dart';

/// Exercises the Flutter app's representative typed fixture builders.
void main() {
  group('Method buildHostEntity behaves correctly', () {
    test('Method buildHostEntity builds representative defaults', () {
      final HostEntity host = Fixtures.buildHostEntity();

      expect(host.displayName, isA<String>());
      expect(host.displayName, 'Local Host');
      expect(host.uri, defaultHostUri);
    });

    test('Method buildHostEntity preserves named overrides', () {
      final Uri uri = Uri.parse('ws://127.0.0.1:1/');
      final HostEntity host = Fixtures.buildHostEntity(
        displayName: 'Test Host',
        uri: uri,
      );

      expect(host.displayName, isA<String>());
      expect(host.displayName, 'Test Host');
      expect(host.uri, uri);
    });

    test('Method buildHostEntity returns a fresh value per call', () {
      final HostEntity first = Fixtures.buildHostEntity();
      final HostEntity second = Fixtures.buildHostEntity();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
      expect(identical(first, second), isFalse);
    });
  });

  group('Method buildPairingHandshakeEntity behaves correctly', () {
    test(
      'Method buildPairingHandshakeEntity builds representative defaults',
      () {
        final PairingHandshakeEntity handshake =
            Fixtures.buildPairingHandshakeEntity();

        expect(handshake.hostVersion, isA<String>());
        expect(handshake.hostVersion, '1.2.3');
        expect(handshake.trusted, isA<bool>());
        expect(handshake.trusted, isTrue);
        expect(handshake.credentialRejectedMessage, isNull);
      },
    );

    test('Method buildPairingHandshakeEntity preserves named overrides', () {
      final PairingHandshakeEntity handshake =
          Fixtures.buildPairingHandshakeEntity(
            hostVersion: '2.0.0',
            trusted: false,
            credentialRejectedMessage: 'Pairing is required again.',
          );

      expect(handshake.hostVersion, isA<String>());
      expect(handshake.hostVersion, '2.0.0');
      expect(handshake.trusted, isA<bool>());
      expect(handshake.trusted, isFalse);
      expect(handshake.credentialRejectedMessage, isA<String>());
      expect(handshake.credentialRejectedMessage, 'Pairing is required again.');
    });

    test(
      'Method buildPairingHandshakeEntity keeps trust and rejection independent',
      () {
        final PairingHandshakeEntity untrustedWithoutMessage =
            Fixtures.buildPairingHandshakeEntity(trusted: false);
        final PairingHandshakeEntity trustedWithMessage =
            Fixtures.buildPairingHandshakeEntity(
              credentialRejectedMessage: 'Pairing is required again.',
            );

        expect(untrustedWithoutMessage.trusted, isFalse);
        expect(untrustedWithoutMessage.credentialRejectedMessage, isNull);
        expect(trustedWithMessage.trusted, isTrue);
        expect(
          trustedWithMessage.credentialRejectedMessage,
          'Pairing is required again.',
        );
      },
    );

    test(
      'Method buildPairingHandshakeEntity returns a fresh value per call',
      () {
        final PairingHandshakeEntity first =
            Fixtures.buildPairingHandshakeEntity();
        final PairingHandshakeEntity second =
            Fixtures.buildPairingHandshakeEntity();

        expect(first, second);
        expect(first.hashCode, second.hashCode);
        expect(identical(first, second), isFalse);
      },
    );
  });
}
