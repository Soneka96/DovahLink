import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

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

  group('Method buildDovahThemeTokens behaves correctly', () {
    test('Method buildDovahThemeTokens builds representative defaults', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens();

      expect(tokens.background, isA<Color>());
      expect(tokens.background, const Color(0xFF05090E));
      expect(tokens.signal, isA<Color>());
      expect(tokens.signal, const Color(0xFF74BDE8));
      expect(tokens.cornerStyle, isA<DovahPanelCornerStyle>());
      expect(tokens.cornerStyle, DovahPanelCornerStyle.doubleBevel);
      expect(tokens.cornerRadius, isA<double>());
      expect(tokens.cornerRadius, 3);
      expect(tokens.densityScale, isA<double>());
      expect(tokens.densityScale, 1);
      expect(tokens.displayFontFamily, isA<String>());
      expect(tokens.displayFontFamily, 'Georgia');
      expect(tokens.environmentAssetPath, isNull);
    });

    test('Method buildDovahThemeTokens preserves named overrides', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens(
        background: const Color(0xFF000000),
        cornerStyle: DovahPanelCornerStyle.rounded,
        cornerRadius: 13,
        densityScale: 1.15,
        environmentAssetPath: 'assets/themes/hearth/hearth-environment.png',
      );

      expect(tokens.background, isA<Color>());
      expect(tokens.background, const Color(0xFF000000));
      expect(tokens.cornerStyle, isA<DovahPanelCornerStyle>());
      expect(tokens.cornerStyle, DovahPanelCornerStyle.rounded);
      expect(tokens.cornerRadius, isA<double>());
      expect(tokens.cornerRadius, 13);
      expect(tokens.densityScale, isA<double>());
      expect(tokens.densityScale, 1.15);
      expect(tokens.environmentAssetPath, isA<String>());
      expect(
        tokens.environmentAssetPath,
        'assets/themes/hearth/hearth-environment.png',
      );
    });

    test('Method buildDovahThemeTokens returns a fresh value per call', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
      expect(identical(first, second), isFalse);
    });
  });
}
