import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Exercises [DovahRootThemeMetrics]'s prototype values per preset, its interpolation, and its
/// value semantics.
void main() {
  group('Property preset values behave correctly', () {
    test('Property frostbound keeps the prototype values', () {
      const DovahRootThemeMetrics metrics = DovahRootThemeMetrics.frostbound;

      expect(metrics.regularHeaderHeight, isA<double>());
      expect(metrics.regularHeaderHeight, 70);
      expect(metrics.compactHeaderHeight, isA<double>());
      expect(metrics.compactHeaderHeight, 56);
      expect(metrics.regularContentTopPadding, 20);
      expect(metrics.narrowContentTopPadding, 20);
      expect(metrics.compactContentTopPadding, 20);
      expect(metrics.regularHeroBottomGap, 18);
      expect(metrics.narrowHeroBottomGap, 18);
      expect(metrics.compactHeroBottomGap, 18);
      expect(metrics.regularPageTitleFontSize, 31);
      expect(metrics.narrowPageTitleFontSize, 31);
      expect(metrics.compactPageTitleFontSize, 31);
      expect(metrics.brandTaglineLetterSpacingEm, isA<double>());
      expect(metrics.brandTaglineLetterSpacingEm, 0.24);
    });

    test('Property dovah keeps the prototype values', () {
      const DovahRootThemeMetrics metrics = DovahRootThemeMetrics.dovah;

      expect(metrics.regularHeaderHeight, 88);
      expect(metrics.compactHeaderHeight, 62);
      expect(metrics.regularContentTopPadding, 30);
      expect(metrics.narrowContentTopPadding, 20);
      expect(metrics.compactContentTopPadding, 14);
      expect(metrics.regularHeroBottomGap, 28);
      expect(metrics.narrowHeroBottomGap, 20);
      expect(metrics.compactHeroBottomGap, 14);
      expect(metrics.regularPageTitleFontSize, 34);
      expect(metrics.narrowPageTitleFontSize, 28);
      expect(metrics.compactPageTitleFontSize, 25);
      expect(metrics.brandTaglineLetterSpacingEm, 0.2);
    });

    test('Property hearth keeps the prototype values', () {
      const DovahRootThemeMetrics metrics = DovahRootThemeMetrics.hearth;

      expect(metrics.regularHeaderHeight, 86);
      expect(metrics.compactHeaderHeight, 62);
      expect(metrics.regularContentTopPadding, 30);
      expect(metrics.narrowContentTopPadding, 20);
      expect(metrics.compactContentTopPadding, 14);
      expect(metrics.regularHeroBottomGap, 28);
      expect(metrics.narrowHeroBottomGap, 20);
      expect(metrics.compactHeroBottomGap, 14);
      expect(metrics.regularPageTitleFontSize, 38);
      expect(metrics.narrowPageTitleFontSize, 38);
      expect(metrics.compactPageTitleFontSize, 38);
      expect(metrics.brandTaglineLetterSpacingEm, 0.14);
    });

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      test(
        'Property ${preset.name} is the metrics its theme builder installs',
        () {
          final DovahRootThemeMetrics expected = switch (preset) {
            DovahThemePreset.frostbound => DovahRootThemeMetrics.frostbound,
            DovahThemePreset.dovah => DovahRootThemeMetrics.dovah,
            DovahThemePreset.hearth => DovahRootThemeMetrics.hearth,
          };

          expect(
            dovahThemeDataFor(preset).extension<DovahRootThemeMetrics>(),
            expected,
          );
        },
      );
    }
  });

  group('Method lerp behaves correctly', () {
    test('Method lerp returns the receiver at t 0 and the target at t 1', () {
      const DovahRootThemeMetrics from = DovahRootThemeMetrics.dovah;
      const DovahRootThemeMetrics to = DovahRootThemeMetrics.hearth;

      expect(from.lerp(to, 0), from);
      expect(from.lerp(to, 1), to);
    });

    test('Method lerp lands regular values between the endpoints at t 0.5', () {
      final DovahRootThemeMetrics mid = DovahRootThemeMetrics.dovah.lerp(
        DovahRootThemeMetrics.hearth,
        0.5,
      );

      expect(mid.regularPageTitleFontSize, isA<double>());
      expect(mid.regularPageTitleFontSize, 36);
      expect(mid.regularHeaderHeight, 87);
      expect(mid.regularContentTopPadding, 30);
      expect(mid.regularHeroBottomGap, 28);
      expect(mid.brandTaglineLetterSpacingEm, closeTo(0.17, 1e-9));
    });

    test('Method lerp lands narrow values between the endpoints at t 0.5', () {
      final DovahRootThemeMetrics mid = DovahRootThemeMetrics.dovah.lerp(
        DovahRootThemeMetrics.hearth,
        0.5,
      );

      expect(mid.narrowPageTitleFontSize, 33);
      expect(mid.narrowContentTopPadding, 20);
      expect(mid.narrowHeroBottomGap, 20);
    });

    test('Method lerp lands compact values between the endpoints at t 0.5', () {
      final DovahRootThemeMetrics mid = DovahRootThemeMetrics.frostbound.lerp(
        DovahRootThemeMetrics.dovah,
        0.5,
      );

      expect(mid.compactHeaderHeight, 59);
      expect(mid.compactContentTopPadding, 17);
      expect(mid.compactHeroBottomGap, 16);
      expect(mid.compactPageTitleFontSize, 28);
    });

    test('Method lerp interpolates a quarter of the way', () {
      final DovahRootThemeMetrics quarter = DovahRootThemeMetrics.dovah.lerp(
        DovahRootThemeMetrics.hearth,
        0.25,
      );

      expect(quarter.regularPageTitleFontSize, 35);
    });

    test('Method lerp between identical metrics keeps every value', () {
      expect(
        DovahRootThemeMetrics.hearth.lerp(DovahRootThemeMetrics.hearth, 0.37),
        DovahRootThemeMetrics.hearth,
      );
    });

    test('Method lerp returns the receiver when the target is null', () {
      expect(
        DovahRootThemeMetrics.dovah.lerp(null, 0.5),
        DovahRootThemeMetrics.dovah,
      );
    });

    test(
      'Method lerp interpolates the metrics installed by ThemeData.lerp',
      () {
        final DovahRootThemeMetrics mid = ThemeData.lerp(
          dovahThemeDataFor(DovahThemePreset.dovah),
          dovahThemeDataFor(DovahThemePreset.hearth),
          0.5,
        ).extension<DovahRootThemeMetrics>()!;

        expect(mid.regularPageTitleFontSize, 36);
        expect(mid.regularHeaderHeight, 87);
      },
    );
  });

  group('Method copyWith behaves correctly', () {
    test('Method copyWith replaces every given value', () {
      final DovahRootThemeMetrics copy = DovahRootThemeMetrics.dovah.copyWith(
        regularHeaderHeight: 1,
        compactHeaderHeight: 2,
        regularContentTopPadding: 3,
        narrowContentTopPadding: 4,
        compactContentTopPadding: 5,
        regularHeroBottomGap: 6,
        narrowHeroBottomGap: 7,
        compactHeroBottomGap: 8,
        regularPageTitleFontSize: 9,
        narrowPageTitleFontSize: 10,
        compactPageTitleFontSize: 11,
        brandTaglineLetterSpacingEm: 12,
      );

      expect(copy.regularHeaderHeight, 1);
      expect(copy.compactHeaderHeight, 2);
      expect(copy.regularContentTopPadding, 3);
      expect(copy.narrowContentTopPadding, 4);
      expect(copy.compactContentTopPadding, 5);
      expect(copy.regularHeroBottomGap, 6);
      expect(copy.narrowHeroBottomGap, 7);
      expect(copy.compactHeroBottomGap, 8);
      expect(copy.regularPageTitleFontSize, 9);
      expect(copy.narrowPageTitleFontSize, 10);
      expect(copy.compactPageTitleFontSize, 11);
      expect(copy.brandTaglineLetterSpacingEm, 12);
    });

    test('Method copyWith without arguments keeps every value', () {
      expect(
        DovahRootThemeMetrics.hearth.copyWith(),
        DovahRootThemeMetrics.hearth,
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same values', () {
      final DovahRootThemeMetrics copy = DovahRootThemeMetrics.dovah.copyWith();

      expect(copy, DovahRootThemeMetrics.dovah);
      expect(copy.hashCode, DovahRootThemeMetrics.dovah.hashCode);
    });

    test('Behavior equality fails between different presets', () {
      expect(DovahRootThemeMetrics.dovah, isNot(DovahRootThemeMetrics.hearth));
    });

    test('Behavior equality fails when any single value differs', () {
      const DovahRootThemeMetrics base = DovahRootThemeMetrics.dovah;

      for (final DovahRootThemeMetrics changed in <DovahRootThemeMetrics>[
        base.copyWith(regularHeaderHeight: 1),
        base.copyWith(compactHeaderHeight: 1),
        base.copyWith(regularContentTopPadding: 1),
        base.copyWith(narrowContentTopPadding: 1),
        base.copyWith(compactContentTopPadding: 1),
        base.copyWith(regularHeroBottomGap: 1),
        base.copyWith(narrowHeroBottomGap: 1),
        base.copyWith(compactHeroBottomGap: 1),
        base.copyWith(regularPageTitleFontSize: 1),
        base.copyWith(narrowPageTitleFontSize: 1),
        base.copyWith(compactPageTitleFontSize: 1),
        base.copyWith(brandTaglineLetterSpacingEm: 1),
      ]) {
        expect(changed, isNot(base));
      }
    });
  });
}
