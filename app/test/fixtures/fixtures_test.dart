import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/models/host_card.model.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

import 'fixtures.dart';

/// Exercises the Flutter app's representative typed fixture builders.
void main() {
  group('Method buildHost behaves correctly', () {
    test('Method buildHost builds representative defaults', () {
      final Host host = Fixtures.buildHost();

      expect(host.displayName, isA<String>());
      expect(host.displayName, 'Local Host');
      expect(host.uri, defaultHostUri);
    });

    test('Method buildHost preserves named overrides', () {
      final Uri uri = Uri.parse('ws://127.0.0.1:1/');
      final Host host = Fixtures.buildHost(displayName: 'Test Host', uri: uri);

      expect(host.displayName, isA<String>());
      expect(host.displayName, 'Test Host');
      expect(host.uri, uri);
    });

    test('Method buildHost returns a fresh value per call', () {
      final Host first = Fixtures.buildHost();
      final Host second = Fixtures.buildHost();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
      expect(identical(first, second), isFalse);
    });
  });

  group('Method buildHostCardModel behaves correctly', () {
    test('Method buildHostCardModel builds representative defaults', () {
      final HostCardModel card = Fixtures.buildHostCardModel();

      expect(card.host, Fixtures.buildHost());
      expect(card.title, isA<String>());
      expect(card.title, 'Local Host');
      expect(card.subtitle, isA<String>());
      expect(card.subtitle, 'DovahLink Host');
      expect(card.detail, isA<String>());
      expect(card.detail, '127.0.0.1:58231');
      expect(card.state, DovahConnectionCardState.unknown);
    });

    test('Method buildHostCardModel preserves named overrides', () {
      final Host host = Fixtures.buildHost(displayName: 'Other');
      final HostCardModel card = Fixtures.buildHostCardModel(
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

    test('Method buildHostCardModel returns a fresh value per call', () {
      final HostCardModel first = Fixtures.buildHostCardModel();
      final HostCardModel second = Fixtures.buildHostCardModel();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
      expect(identical(first, second), isFalse);
    });
  });

  group('Method buildPairingHandshake behaves correctly', () {
    test('Method buildPairingHandshake builds representative defaults', () {
      final PairingHandshake handshake = Fixtures.buildPairingHandshake();

      expect(handshake.hostVersion, isA<String>());
      expect(handshake.hostVersion, '1.2.3');
      expect(handshake.trusted, isA<bool>());
      expect(handshake.trusted, isTrue);
      expect(handshake.credentialRejectedMessage, isNull);
    });

    test('Method buildPairingHandshake preserves named overrides', () {
      final PairingHandshake handshake = Fixtures.buildPairingHandshake(
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
      'Method buildPairingHandshake keeps trust and rejection independent',
      () {
        final PairingHandshake untrustedWithoutMessage =
            Fixtures.buildPairingHandshake(trusted: false);
        final PairingHandshake trustedWithMessage =
            Fixtures.buildPairingHandshake(
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

    test('Method buildPairingHandshake returns a fresh value per call', () {
      final PairingHandshake first = Fixtures.buildPairingHandshake();
      final PairingHandshake second = Fixtures.buildPairingHandshake();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
      expect(identical(first, second), isFalse);
    });
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
      expect(tokens.connectionCardMinHeight, 80);
      expect(tokens.uppercaseLabels, isFalse);
      expect(tokens.rootContentTopPadding, 30);
      expect(tokens.rootHeroBottomGap, 28);
      expect(tokens.rootHeaderRuleFraction, 0.36);
      expect(tokens.pageTitleLineHeight, 1.14);
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
