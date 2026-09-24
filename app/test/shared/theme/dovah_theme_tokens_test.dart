import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

import '../../fixtures/fixtures.dart';

/// Exercises [DovahThemeTokens]'s `copyWith`, `lerp`, and equality contracts.
void main() {
  group('Behavior shared visual tokens behave correctly', () {
    test(
      'Behavior shared visual tokens keep the approved component metrics',
      () {
        expect(
          <Object>[
            DovahThemeTokens.spacing4,
            DovahThemeTokens.spacing6,
            DovahThemeTokens.spacing8,
            DovahThemeTokens.spacing12,
            DovahThemeTokens.spacing16,
            DovahThemeTokens.spacing17,
            DovahThemeTokens.spacing18,
            DovahThemeTokens.spacing19,
            DovahThemeTokens.spacing22,
            DovahThemeTokens.spacing24,
            DovahThemeTokens.compactFontSize,
            DovahThemeTokens.dialogTitleFontSize,
            DovahThemeTokens.connectionStateMarkerSize,
            DovahThemeTokens.connectionIconTileSize,
            DovahThemeTokens.connectionIconTileRadius,
            DovahThemeTokens.connectionIconSize,
            DovahThemeTokens.appearancePreviewHeight,
            DovahThemeTokens.appearancePresetCardMinimumWidth,
            DovahThemeTokens.appearancePreviewAccentHeight,
            DovahThemeTokens.appearanceSelectionIconSize,
            DovahThemeTokens.focusOutlineWidth,
            DovahThemeTokens.minimumTapTargetSize,
            DovahThemeTokens.focusGlowBlurRadius,
            DovahThemeTokens.disabledControlOpacity,
            DovahThemeTokens.primaryButtonHoverBrightness,
            DovahThemeTokens.buttonHoverDuration,
            DovahThemeTokens.surfaceBorderWidth,
            DovahThemeTokens.dialogBackdropBlurSigma,
            DovahThemeTokens.dialogBackdropOpacity,
            DovahThemeTokens.dialogBackdropColor,
            DovahThemeTokens.environmentTopScrimOpacity,
            DovahThemeTokens.environmentBottomScrimOpacity,
          ],
          <Object>[
            4.0,
            6.0,
            8.0,
            12.0,
            16.0,
            17.0,
            18.0,
            19.0,
            22.0,
            24.0,
            13.0,
            23.0,
            8.0,
            43.0,
            9.0,
            21.0,
            48.0,
            160.0,
            6.0,
            20.0,
            2.0,
            48.0,
            8.0,
            0.46,
            0.07,
            const Duration(milliseconds: 160),
            1.0,
            8.0,
            0.35,
            Colors.black,
            0.82,
            0.55,
          ],
        );
      },
    );
  });

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
        primaryActionGradient: const LinearGradient(
          colors: [Color(0xFF123456), Color(0xFF654321)],
        ),
        primaryActionForeground: const Color(0xFFFFFFFF),
      );

      expect(copy.background, const Color(0xFF000000));
      expect(copy.surface3, const Color(0xFFFFFFFF));
      expect(copy.soft, const Color(0xFF123456));
      expect(copy.cornerStyle, DovahPanelCornerStyle.rounded);
      expect((copy.primaryActionGradient as LinearGradient).colors, const [
        Color(0xFF123456),
        Color(0xFF654321),
      ]);
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
        rootHeaderHeight: 70,
        pageTitleFontSize: 31,
        connectionCardMinHeight: 61,
        uppercaseLabels: true,
      );

      expect(copy.eyebrow, const Color(0xFF123456));
      expect(copy.rootHeaderHeight, isA<double>());
      expect(copy.rootHeaderHeight, 70);
      expect(copy.pageTitleFontSize, isA<double>());
      expect(copy.pageTitleFontSize, 31);
      expect(copy.connectionCardMinHeight, isA<double>());
      expect(copy.connectionCardMinHeight, 61);
      expect(copy.uppercaseLabels, isTrue);
      expect(original.uppercaseLabels, isFalse);
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
        densityScale: 1.15,
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
        densityScale: 1.15,
      );

      final DovahThemeTokens result = tokens.lerp(other, 1);

      expect(result, other);
    });

    test('Method lerp interpolates continuous values partway', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens(
        cornerRadius: 0,
        cornerCutSize: 0,
        densityScale: 0,
      );
      final DovahThemeTokens other = Fixtures.buildDovahThemeTokens(
        cornerRadius: 10,
        cornerCutSize: 20,
        densityScale: 2,
      );

      final DovahThemeTokens result = tokens.lerp(other, 0.5);

      expect(result.cornerRadius, isA<double>());
      expect(result.cornerRadius, 5);
      expect(result.surface3, Color.lerp(tokens.surface3, other.surface3, 0.5));
      expect(result.soft, Color.lerp(tokens.soft, other.soft, 0.5));
      expect(result.cornerCutSize, isA<double>());
      expect(result.cornerCutSize, 10);
      expect(result.densityScale, isA<double>());
      expect(result.densityScale, 1);
    });

    test('Method lerp interpolates the root-screen metrics and colors', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens(
        eyebrow: const Color(0xFF000000),
        rootHeaderHeight: 60,
        pageTitleFontSize: 30,
        connectionCardMinHeight: 50,
      );
      final DovahThemeTokens other = Fixtures.buildDovahThemeTokens(
        eyebrow: const Color(0xFFFFFFFF),
        rootHeaderHeight: 80,
        pageTitleFontSize: 40,
        connectionCardMinHeight: 90,
      );

      final DovahThemeTokens result = tokens.lerp(other, 0.5);

      expect(result.eyebrow, Color.lerp(tokens.eyebrow, other.eyebrow, 0.5));
      expect(result.rootHeaderHeight, isA<double>());
      expect(result.rootHeaderHeight, 70);
      expect(result.pageTitleFontSize, isA<double>());
      expect(result.pageTitleFontSize, 35);
      expect(result.connectionCardMinHeight, isA<double>());
      expect(result.connectionCardMinHeight, 70);
    });

    test('Method lerp switches uppercaseLabels at the midpoint', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens other = tokens.copyWith(uppercaseLabels: true);

      expect(tokens.lerp(other, 0.25).uppercaseLabels, isFalse);
      expect(tokens.lerp(other, 0.5).uppercaseLabels, isTrue);
      expect(tokens.lerp(other, 0.75).uppercaseLabels, isTrue);
    });

    test('Method lerp blends action foreground and switches its gradient', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens other = tokens.copyWith(
        primaryActionGradient: const LinearGradient(
          colors: [Color(0xFF123456), Color(0xFF654321)],
        ),
        primaryActionForeground: const Color(0xFFFFFFFF),
      );

      final DovahThemeTokens beforeMidpoint = tokens.lerp(other, 0.25);
      final DovahThemeTokens atMidpoint = tokens.lerp(other, 0.5);
      final DovahThemeTokens afterMidpoint = tokens.lerp(other, 0.75);

      expect(
        beforeMidpoint.primaryActionForeground,
        Color.lerp(
          tokens.primaryActionForeground,
          other.primaryActionForeground,
          0.25,
        ),
      );
      expect(
        (beforeMidpoint.primaryActionGradient as LinearGradient).colors,
        (tokens.primaryActionGradient as LinearGradient).colors,
      );
      expect(
        atMidpoint.primaryActionForeground,
        Color.lerp(
          tokens.primaryActionForeground,
          other.primaryActionForeground,
          0.5,
        ),
      );
      expect(
        (atMidpoint.primaryActionGradient as LinearGradient).colors,
        (other.primaryActionGradient as LinearGradient).colors,
      );
      expect(
        (afterMidpoint.primaryActionGradient as LinearGradient).colors,
        (other.primaryActionGradient as LinearGradient).colors,
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

    test('Behavior equality fails when densityScale differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens(
        densityScale: 0.85,
      );
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens(
        densityScale: 1.15,
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

    test('Behavior equality fails when panelShadow differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens(
        panelShadow: const [
          BoxShadow(
            color: Color(0x11111111),
            blurRadius: 1,
            offset: Offset.zero,
          ),
        ],
      );

      expect(first, isNot(second));
    });

    test('Behavior equality fails when materialGradient differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens(
        materialGradient: const LinearGradient(
          colors: [Color(0xFF000000), Color(0xFFFFFFFF)],
        ),
      );

      expect(first, isNot(second));
    });

    test('Behavior equality fails when materialRaisedGradient differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens(
        materialRaisedGradient: const LinearGradient(
          colors: [Color(0xFF000000), Color(0xFFFFFFFF)],
        ),
      );

      expect(first, isNot(second));
    });

    test('Behavior equality fails when primaryActionGradient differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = first.copyWith(
        primaryActionGradient: const LinearGradient(
          colors: [Color(0xFF000000), Color(0xFFFFFFFF)],
        ),
      );

      expect(first, isNot(second));
    });

    test('Behavior equality fails when a root-screen token differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final List<DovahThemeTokens> others = <DovahThemeTokens>[
        first.copyWith(eyebrow: const Color(0xFF000000)),
        first.copyWith(rootHeaderHeight: 1),
        first.copyWith(pageTitleFontSize: 1),
        first.copyWith(connectionCardMinHeight: 1),
        first.copyWith(uppercaseLabels: true),
      ];

      for (final DovahThemeTokens other in others) {
        expect(first, isNot(other));
      }
    });

    test('Behavior equality fails when primaryActionForeground differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = first.copyWith(
        primaryActionForeground: const Color(0xFFFFFFFF),
      );

      expect(first, isNot(second));
    });
  });
}
