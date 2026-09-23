import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

import '../../fixtures/fixtures.dart';

/// Exercises [DovahThemeTokens]'s `copyWith`, `lerp`, and equality contracts.
void main() {
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
        cornerStyle: DovahPanelCornerStyle.rounded,
        primaryActionGradient: const LinearGradient(
          colors: [Color(0xFF123456), Color(0xFF654321)],
        ),
        primaryActionForeground: const Color(0xFFFFFFFF),
      );

      expect(copy.background, const Color(0xFF000000));
      expect(copy.cornerStyle, DovahPanelCornerStyle.rounded);
      expect((copy.primaryActionGradient as LinearGradient).colors, const [
        Color(0xFF123456),
        Color(0xFF654321),
      ]);
      expect(copy.primaryActionForeground, const Color(0xFFFFFFFF));
      expect(copy.surface, original.surface);
      expect(copy.signal, original.signal);
      expect(copy.cornerRadius, original.cornerRadius);
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
      expect(result.cornerCutSize, isA<double>());
      expect(result.cornerCutSize, 10);
      expect(result.densityScale, isA<double>());
      expect(result.densityScale, 1);
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

    test('Behavior equality fails when primaryActionForeground differs', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = first.copyWith(
        primaryActionForeground: const Color(0xFFFFFFFF),
      );

      expect(first, isNot(second));
    });
  });
}
