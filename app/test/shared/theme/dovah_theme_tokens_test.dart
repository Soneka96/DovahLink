import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import '../../fixtures/fixtures.dart';

/// Exercises [DovahThemeTokens]'s `copyWith`, `lerp`, and equality contracts.
void main() {
  group(
    'Behavior shared typography and surface constants behave correctly',
    () {
      test('Behavior shared typography constants keep the approved values', () {
        expect(DovahThemeTokens.compactFontSize, isA<double>());
        expect(DovahThemeTokens.compactFontSize, 13);
        expect(DovahThemeTokens.uppercaseLetterSpacingEm, isA<double>());
        expect(DovahThemeTokens.uppercaseLetterSpacingEm, 0.045);
        expect(DovahThemeTokens.bodyLineHeight, isA<double>());
        expect(DovahThemeTokens.bodyLineHeight, 4 / 3);
      });

      test('Behavior shared surface constants keep the approved values', () {
        expect(DovahThemeTokens.surfaceBorderWidth, isA<double>());
        expect(DovahThemeTokens.surfaceBorderWidth, 1);
        expect(DovahThemeTokens.environmentTopScrimOpacity, isA<double>());
        expect(DovahThemeTokens.environmentTopScrimOpacity, 0.82);
        expect(DovahThemeTokens.environmentBottomScrimOpacity, isA<double>());
        expect(DovahThemeTokens.environmentBottomScrimOpacity, 0.55);
      });
    },
  );

  group('Method copyWith behaves correctly', () {
    test('Method copyWith with no arguments returns an equal copy', () {
      final DovahThemeTokens original = Fixtures.buildDovahThemeTokens();

      final DovahThemeTokens copy = original.copyWith();

      expect(copy, original);
    });

    test('Method copyWith replaces only the given fields', () {
      final DovahThemeTokens original = Fixtures.buildDovahThemeTokens();

      final DovahThemeTokens copy = original.copyWith(
        background: const Color(0xFF000000),
        surface3: const Color(0xFFFFFFFF),
        soft: const Color(0xFF123456),
        cornerStyle: DovahPanelCornerStyle.rounded,
        primaryActionForeground: const Color(0xFFFFFFFF),
      );

      expect(copy.background, const Color(0xFF000000));
      expect(copy.surface3, const Color(0xFFFFFFFF));
      expect(copy.soft, const Color(0xFF123456));
      expect(copy.cornerStyle, DovahPanelCornerStyle.rounded);
      expect(copy.primaryActionForeground, const Color(0xFFFFFFFF));
      expect(copy.surface, original.surface);
      expect(copy.surfaceRaised, original.surfaceRaised);
      expect(copy.signal, original.signal);
      expect(copy.cornerRadius, original.cornerRadius);
    });

    test('Method copyWith replaces the root-screen tokens', () {
      final DovahThemeTokens original = Fixtures.buildDovahThemeTokens();

      final DovahThemeTokens copy = original.copyWith(
        eyebrow: const Color(0xFF123456),
        uppercaseLabels: true,
        rootHeaderRuleFraction: 0.2,
        pageTitleLineHeight: 1.0,
      );

      expect(copy.pageTitleLineHeight, isA<double>());
      expect(copy.pageTitleLineHeight, 1.0);
      expect(copy.rootHeaderRuleFraction, isA<double>());
      expect(copy.rootHeaderRuleFraction, 0.2);
      expect(copy.eyebrow, const Color(0xFF123456));
      expect(copy.uppercaseLabels, isTrue);
      expect(original.uppercaseLabels, isFalse);
    });

    test('Method copyWith replaces the identity and backdrop tokens', () {
      final DovahThemeTokens original = Fixtures.buildDovahThemeTokens();

      final DovahThemeTokens copy = original.copyWith(
        preset: DovahThemePreset.hearth,
        backdropColor: const Color(0x8A2F1F12),
        backdropBlurSigma: 9,
        panelCornerRadius: 14,
        primaryActionCornerRadius: 9,
      );

      expect(copy.preset, DovahThemePreset.hearth);
      expect(original.preset, DovahThemePreset.dovah);
      expect(copy.backdropColor, const Color(0x8A2F1F12));
      expect(copy.backdropBlurSigma, isA<double>());
      expect(copy.backdropBlurSigma, 9);
      expect(copy.panelCornerRadius, isA<double>());
      expect(copy.panelCornerRadius, 14);
      expect(copy.primaryActionCornerRadius, isA<double>());
      expect(copy.primaryActionCornerRadius, 9);
    });

    test('Method copyWith replaces the brand and status tones', () {
      final DovahThemeTokens original = Fixtures.buildDovahThemeTokens();

      final DovahThemeTokens copy = original.copyWith(
        statusOffline: const Color(0xFF010203),
        brandTagline: const Color(0xFF040506),
        brandAccent: const Color(0xFF070809),
        markIcon: const Color(0xFF0A0B0C),
        barTrack: const Color(0xFF0D0E0F),
        panelNote: const Color(0xFF101112),
        heroScrim: const LinearGradient(
          colors: [Color(0xFF111111), Color(0xFF222222)],
        ),
        heroFloorScrim: const LinearGradient(
          colors: [Color(0xFF333333), Color(0xFF444444)],
        ),
      );

      expect(copy.barTrack, const Color(0xFF0D0E0F));
      expect(copy.panelNote, const Color(0xFF101112));
      expect((copy.heroScrim as LinearGradient).colors, const [
        Color(0xFF111111),
        Color(0xFF222222),
      ]);
      expect((copy.heroFloorScrim as LinearGradient).colors, const [
        Color(0xFF333333),
        Color(0xFF444444),
      ]);
      expect(copy.statusOffline, const Color(0xFF010203));
      expect(copy.brandTagline, const Color(0xFF040506));
      expect(copy.brandAccent, const Color(0xFF070809));
      expect(copy.markIcon, const Color(0xFF0A0B0C));
      expect(original.statusOffline, const Color(0xFF7C8993));
    });

    test('Method copyWith omits environmentAssetPath when not passed', () {
      final DovahThemeTokens original = Fixtures.buildDovahThemeTokens(
        environmentAssetPath: 'assets/themes/hearth/hearth-environment.png',
      );

      final DovahThemeTokens copy = original.copyWith();

      expect(copy.environmentAssetPath, isA<String>());
      expect(
        copy.environmentAssetPath,
        'assets/themes/hearth/hearth-environment.png',
      );
    });

    test('Method copyWith sets environmentAssetPath from Option.of', () {
      final DovahThemeTokens original = Fixtures.buildDovahThemeTokens();

      final DovahThemeTokens copy = original.copyWith(
        environmentAssetPath: const Option.of(
          'assets/themes/frostbound/frostbound-environment.png',
        ),
      );

      expect(copy.environmentAssetPath, isA<String>());
      expect(
        copy.environmentAssetPath,
        'assets/themes/frostbound/frostbound-environment.png',
      );
    });

    test('Method copyWith clears environmentAssetPath from Option.none', () {
      final DovahThemeTokens original = Fixtures.buildDovahThemeTokens(
        environmentAssetPath: 'assets/themes/hearth/hearth-environment.png',
      );

      final DovahThemeTokens copy = original.copyWith(
        environmentAssetPath: const Option.none(),
      );

      expect(copy.environmentAssetPath, isNull);
    });
  });

  group('Method lerp behaves correctly', () {
    test('Method lerp at t=0 returns values equal to this', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens other = Fixtures.buildDovahThemeTokens(
        background: const Color(0xFFFFFFFF),
        surface3: const Color(0xFF000000),
        soft: const Color(0xFFFFFFFF),
        cornerStyle: DovahPanelCornerStyle.rounded,
        cornerRadius: 13,
      );

      final DovahThemeTokens result = tokens.lerp(other, 0);

      expect(result, tokens);
    });

    test('Method lerp at t=1 returns values equal to the other extension', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens other = Fixtures.buildDovahThemeTokens(
        background: const Color(0xFFFFFFFF),
        cornerStyle: DovahPanelCornerStyle.rounded,
        cornerRadius: 13,
      );

      final DovahThemeTokens result = tokens.lerp(other, 1);

      expect(result, other);
    });

    test('Method lerp interpolates continuous values partway', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens(
        cornerRadius: 0,
        cornerCutSize: 0,
      );
      final DovahThemeTokens other = Fixtures.buildDovahThemeTokens(
        cornerRadius: 10,
        cornerCutSize: 20,
      );

      final DovahThemeTokens result = tokens.lerp(other, 0.5);

      expect(result.cornerRadius, isA<double>());
      expect(result.cornerRadius, 5);
      expect(result.surface3, Color.lerp(tokens.surface3, other.surface3, 0.5));
      expect(result.soft, Color.lerp(tokens.soft, other.soft, 0.5));
      expect(result.cornerCutSize, isA<double>());
      expect(result.cornerCutSize, 10);
    });

    test('Method lerp interpolates the root-screen metrics and colors', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens(
        eyebrow: const Color(0xFF000000),
        rootHeaderRuleFraction: 0.2,
        pageTitleLineHeight: 1.0,
      );
      final DovahThemeTokens other = Fixtures.buildDovahThemeTokens(
        eyebrow: const Color(0xFFFFFFFF),
        rootHeaderRuleFraction: 0.6,
        pageTitleLineHeight: 1.4,
      );

      final DovahThemeTokens result = tokens.lerp(other, 0.5);

      expect(result.eyebrow, Color.lerp(tokens.eyebrow, other.eyebrow, 0.5));
      expect(result.rootHeaderRuleFraction, isA<double>());
      expect(result.rootHeaderRuleFraction, closeTo(0.4, 0.0001));
      expect(result.pageTitleLineHeight, isA<double>());
      expect(result.pageTitleLineHeight, closeTo(1.2, 0.0001));
    });

    test('Method lerp interpolates backdrop and radius tokens', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens(
        backdropColor: const Color(0x00000000),
        backdropBlurSigma: 6,
        panelCornerRadius: 0,
        primaryActionCornerRadius: 0,
      );
      final DovahThemeTokens other = Fixtures.buildDovahThemeTokens(
        backdropColor: const Color(0xFFFFFFFF),
        backdropBlurSigma: 10,
        panelCornerRadius: 14,
        primaryActionCornerRadius: 10,
      );

      final DovahThemeTokens result = tokens.lerp(other, 0.5);

      expect(
        result.backdropColor,
        Color.lerp(tokens.backdropColor, other.backdropColor, 0.5),
      );
      expect(result.backdropBlurSigma, isA<double>());
      expect(result.backdropBlurSigma, 8);
      expect(result.panelCornerRadius, isA<double>());
      expect(result.panelCornerRadius, 7);
      expect(result.primaryActionCornerRadius, isA<double>());
      expect(result.primaryActionCornerRadius, 5);
    });

    test('Method lerp interpolates the brand and status tones', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens(
        statusOffline: const Color(0xFF000000),
        brandTagline: const Color(0xFF000000),
        brandAccent: const Color(0xFF000000),
        markIcon: const Color(0xFF000000),
        barTrack: const Color(0xFF000000),
        panelNote: const Color(0xFF000000),
      );
      final DovahThemeTokens other = Fixtures.buildDovahThemeTokens(
        statusOffline: const Color(0xFFFFFFFF),
        brandTagline: const Color(0xFFFFFFFF),
        brandAccent: const Color(0xFFFFFFFF),
        markIcon: const Color(0xFFFFFFFF),
        barTrack: const Color(0xFFFFFFFF),
        panelNote: const Color(0xFFFFFFFF),
      );

      final DovahThemeTokens result = tokens.lerp(other, 0.5);

      final Color halfway = Color.lerp(
        const Color(0xFF000000),
        const Color(0xFFFFFFFF),
        0.5,
      )!;
      expect(result.statusOffline, halfway);
      expect(result.brandTagline, halfway);
      expect(result.brandAccent, halfway);
      expect(result.markIcon, halfway);
      expect(result.barTrack, halfway);
      expect(result.panelNote, halfway);
    });

    test('Method lerp switches preset at the midpoint', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens other = tokens.copyWith(
        preset: DovahThemePreset.hearth,
      );

      expect(tokens.lerp(other, 0.25).preset, DovahThemePreset.dovah);
      expect(tokens.lerp(other, 0.5).preset, DovahThemePreset.hearth);
    });

    test('Method lerp switches uppercaseLabels at the midpoint', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens other = tokens.copyWith(uppercaseLabels: true);

      expect(tokens.lerp(other, 0.25).uppercaseLabels, isFalse);
      expect(tokens.lerp(other, 0.5).uppercaseLabels, isTrue);
      expect(tokens.lerp(other, 0.75).uppercaseLabels, isTrue);
    });

    test('Method lerp blends the action foreground', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens other = tokens.copyWith(
        primaryActionForeground: const Color(0xFFFFFFFF),
      );

      final DovahThemeTokens beforeMidpoint = tokens.lerp(other, 0.25);
      final DovahThemeTokens atMidpoint = tokens.lerp(other, 0.5);

      expect(
        beforeMidpoint.primaryActionForeground,
        Color.lerp(
          tokens.primaryActionForeground,
          other.primaryActionForeground,
          0.25,
        ),
      );
      expect(
        atMidpoint.primaryActionForeground,
        Color.lerp(
          tokens.primaryActionForeground,
          other.primaryActionForeground,
          0.5,
        ),
      );
    });

    test(
      'Method lerp returns this when the other extension is not DovahThemeTokens',
      () {
        final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens();

        final DovahThemeTokens result = tokens.lerp(null, 0.5);

        expect(result, tokens);
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for tokens built from equal fields', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality fails when cornerStyle differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens(
        cornerStyle: DovahPanelCornerStyle.rounded,
      );

      expect(first, isNot(second));
      expect(first.hashCode, isNot(second.hashCode));
    });

    test('Behavior equality fails when surface3 differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens(
        surface3: const Color(0xFF000000),
      );

      expect(first, isNot(second));
      expect(first.hashCode, isNot(second.hashCode));
    });

    test('Behavior equality fails when soft differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens(
        soft: const Color(0xFF000000),
      );

      expect(first, isNot(second));
      expect(first.hashCode, isNot(second.hashCode));
    });

    test('Behavior equality fails when cornerRadius differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens(
        cornerRadius: 0,
      );
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens(
        cornerRadius: 10,
      );

      expect(first, isNot(second));
    });

    test('Behavior equality fails when cornerCutSize differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens(
        cornerCutSize: 0,
      );
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens(
        cornerCutSize: 20,
      );

      expect(first, isNot(second));
    });

    test('Behavior equality fails when displayFontFamily differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens(
        displayFontFamily: 'Georgia',
      );
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens(
        displayFontFamily: 'Arial Narrow',
      );

      expect(first, isNot(second));
    });

    test('Behavior equality fails when environmentAssetPath differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens(
        environmentAssetPath: 'assets/themes/hearth/hearth-environment.png',
      );

      expect(first, isNot(second));
    });

    test('Behavior equality fails when a root-screen token differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final List<DovahThemeTokens> others = <DovahThemeTokens>[
        first.copyWith(eyebrow: const Color(0xFF000000)),
        first.copyWith(uppercaseLabels: true),
        first.copyWith(rootHeaderRuleFraction: 0.01),
        first.copyWith(pageTitleLineHeight: 0.5),
      ];

      for (final DovahThemeTokens other in others) {
        expect(first, isNot(other));
      }
    });

    test(
      'Behavior equality fails when an identity or backdrop token differs',
      () {
        final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
        final List<DovahThemeTokens> others = <DovahThemeTokens>[
          first.copyWith(preset: DovahThemePreset.hearth),
          first.copyWith(backdropColor: const Color(0xFF000000)),
          first.copyWith(backdropBlurSigma: 1),
          first.copyWith(panelCornerRadius: 1),
          first.copyWith(primaryActionCornerRadius: 1),
          first.copyWith(statusOffline: const Color(0xFF000000)),
          first.copyWith(brandTagline: const Color(0xFF000000)),
          first.copyWith(brandAccent: const Color(0xFF000000)),
          first.copyWith(markIcon: const Color(0xFF000000)),
          first.copyWith(barTrack: const Color(0xFF000000)),
          first.copyWith(panelNote: const Color(0xFF000000)),
          first.copyWith(
            heroScrim: const LinearGradient(
              colors: [Color(0xFF000000), Color(0xFF111111)],
            ),
          ),
          first.copyWith(
            heroFloorScrim: const LinearGradient(
              colors: [Color(0xFF000000), Color(0xFF111111)],
            ),
          ),
        ];

        for (final DovahThemeTokens other in others) {
          expect(first, isNot(other));
        }
      },
    );

    test('Behavior equality fails when primaryActionForeground differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = first.copyWith(
        primaryActionForeground: const Color(0xFFFFFFFF),
      );

      expect(first, isNot(second));
    });
  });
}
