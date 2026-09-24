import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/host_card.viewmodel.dart';
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

  group('Method buildHostCardViewModel behaves correctly', () {
    test('Method buildHostCardViewModel builds representative defaults', () {
      final HostCardViewModel card = Fixtures.buildHostCardViewModel();

      expect(card.host, Fixtures.buildHostEntity());
      expect(card.title, isA<String>());
      expect(card.title, 'Local Host');
      expect(card.subtitle, isA<String>());
      expect(card.subtitle, 'DovahLink Host');
      expect(card.detail, isA<String>());
      expect(card.detail, '127.0.0.1:58231');
      expect(card.state, DovahConnectionCardState.unknown);
    });

    test('Method buildHostCardViewModel preserves named overrides', () {
      final HostEntity host = Fixtures.buildHostEntity(displayName: 'Other');
      final HostCardViewModel card = Fixtures.buildHostCardViewModel(
        host: host,
        title: 'Other',
        subtitle: 'Sub',
        detail: 'Detail',
        state: DovahConnectionCardState.repair,
      );

      expect(card.host, host);
      expect(card.title, isA<String>());
      expect(card.title, 'Other');
      expect(card.subtitle, isA<String>());
      expect(card.subtitle, 'Sub');
      expect(card.detail, isA<String>());
      expect(card.detail, 'Detail');
      expect(card.state, DovahConnectionCardState.repair);
    });

    test('Method buildHostCardViewModel returns a fresh value per call', () {
      final HostCardViewModel first = Fixtures.buildHostCardViewModel();
      final HostCardViewModel second = Fixtures.buildHostCardViewModel();

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
      expect(tokens.surface3, isA<Color>());
      expect(tokens.surface3, const Color(0xFF162735));
      expect(tokens.soft, isA<Color>());
      expect(tokens.soft, const Color(0x2174BDE8));
      expect(tokens.signal, isA<Color>());
      expect(tokens.signal, const Color(0xFF74BDE8));
      expect(tokens.primaryActionForeground, isA<Color>());
      expect(tokens.primaryActionForeground, const Color(0xFF1A0E04));
      expect((tokens.primaryActionGradient as LinearGradient).colors, const [
        Color(0xFFF0BD73),
        Color(0xFFC77D38),
      ]);
      expect(tokens.cornerStyle, isA<DovahPanelCornerStyle>());
      expect(tokens.cornerStyle, DovahPanelCornerStyle.doubleBevel);
      expect(tokens.cornerRadius, isA<double>());
      expect(tokens.cornerRadius, 3);
      expect(tokens.densityScale, isA<double>());
      expect(tokens.densityScale, 1);
      expect(tokens.displayFontFamily, isA<String>());
      expect(tokens.displayFontFamily, 'Georgia');
      expect(tokens.environmentAssetPath, isNull);
      expect(tokens.eyebrow, isA<Color>());
      expect(tokens.eyebrow, const Color(0xFFE2A55E));
      expect(tokens.rootHeaderHeight, isA<double>());
      expect(tokens.rootHeaderHeight, 88);
      expect(tokens.pageTitleFontSize, isA<double>());
      expect(tokens.pageTitleFontSize, 34);
      expect(tokens.connectionCardMinHeight, isA<double>());
      expect(tokens.connectionCardMinHeight, 78);
      expect(tokens.uppercaseLabels, isFalse);
    });

    test('Method buildDovahThemeTokens preserves named overrides', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens(
        background: const Color(0xFF000000),
        surface3: const Color(0xFF010203),
        soft: const Color(0x04050607),
        cornerStyle: DovahPanelCornerStyle.rounded,
        cornerRadius: 13,
        densityScale: 1.15,
        environmentAssetPath: 'assets/themes/hearth/hearth-environment.png',
        eyebrow: const Color(0xFF010203),
        rootHeaderHeight: 70,
        pageTitleFontSize: 31,
        connectionCardMinHeight: 61,
        uppercaseLabels: true,
      );

      expect(tokens.eyebrow, isA<Color>());
      expect(tokens.eyebrow, const Color(0xFF010203));
      expect(tokens.rootHeaderHeight, isA<double>());
      expect(tokens.rootHeaderHeight, 70);
      expect(tokens.pageTitleFontSize, isA<double>());
      expect(tokens.pageTitleFontSize, 31);
      expect(tokens.connectionCardMinHeight, isA<double>());
      expect(tokens.connectionCardMinHeight, 61);
      expect(tokens.uppercaseLabels, isTrue);
      expect(tokens.background, isA<Color>());
      expect(tokens.background, const Color(0xFF000000));
      expect(tokens.surface3, isA<Color>());
      expect(tokens.surface3, const Color(0xFF010203));
      expect(tokens.soft, isA<Color>());
      expect(tokens.soft, const Color(0x04050607));
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
