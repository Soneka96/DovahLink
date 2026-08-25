import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';

import 'fixtures.dart';

/// Exercises the Flutter app's representative typed fixture builders.
void main() {
  group('Method buildBridgeEntity behaves correctly', () {
    test('Method buildBridgeEntity builds representative defaults', () {
      final BridgeEntity bridge = Fixtures.buildBridgeEntity();

      expect(bridge.displayName, isA<String>());
      expect(bridge.displayName, 'Local Bridge');
      expect(bridge.uri, defaultBridgeUri);
    });

    test('Method buildBridgeEntity preserves named overrides', () {
      final Uri uri = Uri.parse('ws://127.0.0.1:1/');
      final BridgeEntity bridge = Fixtures.buildBridgeEntity(
        displayName: 'Test Bridge',
        uri: uri,
      );

      expect(bridge.displayName, isA<String>());
      expect(bridge.displayName, 'Test Bridge');
      expect(bridge.uri, uri);
    });

    test('Method buildBridgeEntity returns a fresh value per call', () {
      final BridgeEntity first = Fixtures.buildBridgeEntity();
      final BridgeEntity second = Fixtures.buildBridgeEntity();

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

        expect(handshake.bridgeVersion, isA<String>());
        expect(handshake.bridgeVersion, '1.2.3');
        expect(handshake.trusted, isA<bool>());
        expect(handshake.trusted, isTrue);
        expect(handshake.credentialRejectedMessage, isNull);
      },
    );

    test('Method buildPairingHandshakeEntity preserves named overrides', () {
      final PairingHandshakeEntity handshake =
          Fixtures.buildPairingHandshakeEntity(
            bridgeVersion: '2.0.0',
            trusted: false,
            credentialRejectedMessage: 'Pairing is required again.',
          );

      expect(handshake.bridgeVersion, isA<String>());
      expect(handshake.bridgeVersion, '2.0.0');
      expect(handshake.trusted, isA<bool>());
      expect(handshake.trusted, isFalse);
      expect(handshake.credentialRejectedMessage, isA<String>());
      expect(handshake.credentialRejectedMessage, 'Pairing is required again.');
    });

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
