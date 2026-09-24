import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/pairing/data/models/pairing_handshake.model.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/authenticate.params.dart';
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

  group('Method buildAuthenticateParams behaves correctly', () {
    test('Method buildAuthenticateParams targets the representative Host', () {
      final AuthenticateParams params = Fixtures.buildAuthenticateParams();

      expect(params.hostUri, defaultHostUri);
    });

    test('Method buildAuthenticateParams preserves the named override', () {
      final Uri uri = Uri.parse('ws://127.0.0.1:2/');

      final AuthenticateParams params = Fixtures.buildAuthenticateParams(
        hostUri: uri,
      );

      expect(params.hostUri, uri);
    });
  });

  group('Method buildHostCardViewData behaves correctly', () {
    test('Method buildHostCardViewData builds representative defaults', () {
      final HostCardViewData card = Fixtures.buildHostCardViewData();

      expect(card.host, Fixtures.buildHost());
      expect(card.title, isA<String>());
      expect(card.title, 'Local Host');
      expect(card.subtitle, isA<String>());
      expect(card.subtitle, 'DovahLink Host');
      expect(card.detail, isA<String>());
      expect(card.detail, '127.0.0.1:58231');
      expect(card.state, DovahConnectionCardState.unknown);
    });

    test('Method buildHostCardViewData preserves named overrides', () {
      final Host host = Fixtures.buildHost(displayName: 'Other');
      final HostCardViewData card = Fixtures.buildHostCardViewData(
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

    test('Method buildHostCardViewData returns a fresh value per call', () {
      final HostCardViewData first = Fixtures.buildHostCardViewData();
      final HostCardViewData second = Fixtures.buildHostCardViewData();

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
      expect(handshake.credentialRejectionReason, isNull);
      expect(handshake.credentialRejectedMessage, isNull);
    });

    test('Method buildPairingHandshake preserves named overrides', () {
      final PairingHandshake handshake = Fixtures.buildPairingHandshake(
        hostVersion: '2.0.0',
        trusted: false,
        credentialRejectionReason: PairingCredentialRejectionReason.revoked,
        credentialRejectedMessage: 'Pairing is required again.',
      );

      expect(handshake.hostVersion, isA<String>());
      expect(handshake.hostVersion, '2.0.0');
      expect(handshake.trusted, isA<bool>());
      expect(handshake.trusted, isFalse);
      expect(
        handshake.credentialRejectionReason,
        PairingCredentialRejectionReason.revoked,
      );
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
        expect(untrustedWithoutMessage.credentialRejectionReason, isNull);
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

  group('Method buildPairingHandshakeModel behaves correctly', () {
    test(
      'Method buildPairingHandshakeModel builds representative defaults',
      () {
        final PairingHandshakeModel model =
            Fixtures.buildPairingHandshakeModel();

        expect(model.hostVersion, '1.2.3');
        expect(model.trusted, isTrue);
        expect(model.credentialRejectionReason, isNull);
        expect(model.credentialRejectedMessage, isNull);
      },
    );

    test('Method buildPairingHandshakeModel preserves named overrides', () {
      final PairingHandshakeModel model = Fixtures.buildPairingHandshakeModel(
        hostVersion: '2.0.0',
        trusted: false,
        credentialRejectionReason: PairingCredentialRejectionReason.blocked,
        credentialRejectedMessage: 'Pairing is required again.',
      );

      expect(model.hostVersion, '2.0.0');
      expect(model.trusted, isFalse);
      expect(
        model.credentialRejectionReason,
        PairingCredentialRejectionReason.blocked,
      );
      expect(model.credentialRejectedMessage, 'Pairing is required again.');
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
      expect(tokens.uppercaseLabels, isFalse);
      expect(tokens.rootHeaderRuleFraction, 0.36);
      expect(tokens.pageTitleLineHeight, 1.14);
      expect(tokens.preset, DovahThemePreset.dovah);
      expect(tokens.backdropColor, isA<Color>());
      expect(tokens.backdropColor, const Color(0xC2020407));
      expect(tokens.backdropBlurSigma, isA<double>());
      expect(tokens.backdropBlurSigma, 8);
      expect(tokens.panelCornerRadius, isA<double>());
      expect(tokens.panelCornerRadius, 0);
      expect(tokens.primaryActionCornerRadius, isA<double>());
      expect(tokens.primaryActionCornerRadius, 0);
      expect(tokens.statusOffline, const Color(0xFF7C8993));
      expect(tokens.brandTagline, const Color(0xFF72899A));
      expect(tokens.brandAccent, const Color(0xFF74BDE8));
      expect(tokens.markIcon, const Color(0xFFE2A55E));
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
        uppercaseLabels: true,
        preset: DovahThemePreset.hearth,
        backdropColor: const Color(0x8A2F1F12),
        backdropBlurSigma: 9,
        panelCornerRadius: 14,
        primaryActionCornerRadius: 9,
      );

      expect(tokens.preset, DovahThemePreset.hearth);
      expect(tokens.backdropColor, const Color(0x8A2F1F12));
      expect(tokens.backdropBlurSigma, 9);
      expect(tokens.panelCornerRadius, 14);
      expect(tokens.primaryActionCornerRadius, 9);
      expect(tokens.eyebrow, isA<Color>());
      expect(tokens.eyebrow, const Color(0xFF010203));
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
