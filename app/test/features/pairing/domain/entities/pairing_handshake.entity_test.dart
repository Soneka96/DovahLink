import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';

/// Exercises pairing-handshake entity value preservation.
void main() {
  group('PairingHandshakeEntity', () {
    test('stores the reported bridge version and trust standing', () {
      const PairingHandshakeEntity handshake = PairingHandshakeEntity(
        bridgeVersion: '1.2.3',
        trusted: true,
      );

      expect(handshake.bridgeVersion, '1.2.3');
      expect(handshake.trusted, isTrue);
    });

    test('treats handshakes with different trust standing as unequal', () {
      const PairingHandshakeEntity first = PairingHandshakeEntity(
        bridgeVersion: '1.2.3',
        trusted: false,
      );
      const PairingHandshakeEntity second = PairingHandshakeEntity(
        bridgeVersion: '1.2.3',
        trusted: true,
      );

      expect(first == second, isFalse);
    });
  });
}
