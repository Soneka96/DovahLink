import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Exercises [DovahOverviewThemeMetrics]'s prototype values per preset, its interpolation, and its
/// value semantics.
void main() {
  group('Property preset values behave correctly', () {
    test('Property frostbound keeps the prototype values', () {
      const DovahOverviewThemeMetrics metrics =
          DovahOverviewThemeMetrics.frostbound;

      expect(metrics.gridGap, isA<double>());
      expect(metrics.gridGap, 10);
      expect(metrics.regularHeroMinHeight, isA<double>());
      expect(metrics.regularHeroMinHeight, 226);
      expect(metrics.compactHeroMinHeight, isA<double>());
      expect(metrics.compactHeroMinHeight, 205);
      expect(metrics.regularStatsTopGap, isA<double>());
      expect(metrics.regularStatsTopGap, 14);
      expect(metrics.compactStatsTopGap, isA<double>());
      expect(metrics.compactStatsTopGap, 14);
    });

    test('Property dovah keeps the prototype values', () {
      const DovahOverviewThemeMetrics metrics = DovahOverviewThemeMetrics.dovah;

      expect(metrics.gridGap, 14);
      expect(metrics.regularHeroMinHeight, 270);
      expect(metrics.compactHeroMinHeight, 210);
      expect(metrics.regularStatsTopGap, 20);
      expect(metrics.compactStatsTopGap, 14);
    });

    test('Property hearth keeps the prototype values', () {
      const DovahOverviewThemeMetrics metrics =
          DovahOverviewThemeMetrics.hearth;

      expect(metrics.gridGap, 14);
      expect(metrics.regularHeroMinHeight, 278);
      expect(metrics.compactHeroMinHeight, 215);
      expect(metrics.regularStatsTopGap, 20);
      expect(metrics.compactStatsTopGap, 14);
    });

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      test(
        'Property ${preset.name} is the metrics its theme builder installs',
        () {
          final DovahOverviewThemeMetrics expected = switch (preset) {
            DovahThemePreset.frostbound => DovahOverviewThemeMetrics.frostbound,
            DovahThemePreset.dovah => DovahOverviewThemeMetrics.dovah,
            DovahThemePreset.hearth => DovahOverviewThemeMetrics.hearth,
          };

          expect(
            dovahThemeDataFor(preset).extension<DovahOverviewThemeMetrics>(),
            expected,
          );
        },
      );
    }
  });

  group('Method lerp behaves correctly', () {
    test('Method lerp returns the receiver at t 0 and the target at t 1', () {
      const DovahOverviewThemeMetrics from = DovahOverviewThemeMetrics.dovah;
      const DovahOverviewThemeMetrics to = DovahOverviewThemeMetrics.hearth;

      expect(from.lerp(to, 0), from);
      expect(from.lerp(to, 1), to);
    });

    test('Method lerp lands regular values between the endpoints at t 0.5', () {
      final DovahOverviewThemeMetrics mid = DovahOverviewThemeMetrics.dovah
          .lerp(DovahOverviewThemeMetrics.hearth, 0.5);

      expect(mid.regularHeroMinHeight, isA<double>());
      expect(mid.regularHeroMinHeight, 274);
      expect(mid.regularStatsTopGap, 20);
      expect(mid.gridGap, 14);
    });

    test('Method lerp lands compact values between the endpoints at t 0.5', () {
      final DovahOverviewThemeMetrics mid = DovahOverviewThemeMetrics.dovah
          .lerp(DovahOverviewThemeMetrics.hearth, 0.5);

      expect(mid.compactHeroMinHeight, 212.5);
      expect(mid.compactStatsTopGap, 14);
    });

    test('Method lerp interpolates the grid gap and stats gap at t 0.5', () {
      final DovahOverviewThemeMetrics mid = DovahOverviewThemeMetrics.frostbound
          .lerp(DovahOverviewThemeMetrics.dovah, 0.5);

      expect(mid.gridGap, 12);
      expect(mid.regularStatsTopGap, 17);
      expect(mid.regularHeroMinHeight, 248);
      expect(mid.compactHeroMinHeight, 207.5);
    });

    test('Method lerp between identical metrics keeps every value', () {
      expect(
        DovahOverviewThemeMetrics.hearth.lerp(
          DovahOverviewThemeMetrics.hearth,
          0.37,
        ),
        DovahOverviewThemeMetrics.hearth,
      );
    });

    test('Method lerp returns the receiver when the target is null', () {
      expect(
        DovahOverviewThemeMetrics.dovah.lerp(null, 0.5),
        DovahOverviewThemeMetrics.dovah,
      );
    });

    test(
      'Method lerp flows through forWindow so mid-transition metrics resolve per window mode',
      () {
        final DovahOverviewThemeMetrics mid = DovahOverviewThemeMetrics
            .frostbound
            .lerp(DovahOverviewThemeMetrics.dovah, 0.5);

        final DovahOverviewMetrics regular = DovahOverviewMetrics.forWindow(
          themeMetrics: mid,
          window: const Size(1280, 720),
        );
        final DovahOverviewMetrics compact = DovahOverviewMetrics.forWindow(
          themeMetrics: mid,
          window: const Size(1280, 560),
        );

        expect(regular.gridGap, 12);
        expect(regular.heroMinHeight, 248);
        expect(regular.statsTopGap, 17);
        expect(compact.heroMinHeight, 207.5);
        expect(compact.statsTopGap, 14);
      },
    );

    test('Method lerp leaves the column ratio to the window alone', () {
      final DovahOverviewThemeMetrics mid = DovahOverviewThemeMetrics.dovah
          .lerp(DovahOverviewThemeMetrics.hearth, 0.5);

      for (final DovahOverviewThemeMetrics themeMetrics in [
        DovahOverviewThemeMetrics.frostbound,
        DovahOverviewThemeMetrics.hearth,
        mid,
      ]) {
        final DovahOverviewMetrics wide = DovahOverviewMetrics.forWindow(
          themeMetrics: themeMetrics,
          window: const Size(1280, 720),
        );
        final DovahOverviewMetrics narrow = DovahOverviewMetrics.forWindow(
          themeMetrics: themeMetrics,
          window: const Size(800, 700),
        );

        expect(wide.mainColumnFlex, 125);
        expect(wide.sideColumnFlex, 75);
        expect(narrow.mainColumnFlex, 115);
        expect(narrow.sideColumnFlex, 85);
      }
    });

    test(
      'Method lerp interpolates the metrics installed by ThemeData.lerp',
      () {
        final DovahOverviewThemeMetrics mid = ThemeData.lerp(
          dovahThemeDataFor(DovahThemePreset.dovah),
          dovahThemeDataFor(DovahThemePreset.hearth),
          0.5,
        ).extension<DovahOverviewThemeMetrics>()!;

        expect(mid.regularHeroMinHeight, 274);
      },
    );
  });

  group('Method copyWith behaves correctly', () {
    test('Method copyWith replaces every given value', () {
      final DovahOverviewThemeMetrics copy = DovahOverviewThemeMetrics.dovah
          .copyWith(
            gridGap: 1,
            regularHeroMinHeight: 2,
            compactHeroMinHeight: 3,
            regularStatsTopGap: 4,
            compactStatsTopGap: 5,
          );

      expect(copy.gridGap, 1);
      expect(copy.regularHeroMinHeight, 2);
      expect(copy.compactHeroMinHeight, 3);
      expect(copy.regularStatsTopGap, 4);
      expect(copy.compactStatsTopGap, 5);
    });

    test('Method copyWith keeps the values it is not given', () {
      final DovahOverviewThemeMetrics copy = DovahOverviewThemeMetrics.hearth
          .copyWith(gridGap: 1);

      expect(copy.gridGap, 1);
      expect(copy.regularHeroMinHeight, 278);
      expect(copy.compactStatsTopGap, 14);
    });

    test('Method copyWith without arguments keeps every value', () {
      expect(
        DovahOverviewThemeMetrics.hearth.copyWith(),
        DovahOverviewThemeMetrics.hearth,
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same values', () {
      final DovahOverviewThemeMetrics copy = DovahOverviewThemeMetrics.dovah
          .copyWith();

      expect(copy, DovahOverviewThemeMetrics.dovah);
      expect(copy.hashCode, DovahOverviewThemeMetrics.dovah.hashCode);
    });

    test('Behavior equality fails between different presets', () {
      expect(
        DovahOverviewThemeMetrics.dovah,
        isNot(DovahOverviewThemeMetrics.hearth),
      );
    });

    test('Behavior equality fails when any single value differs', () {
      const DovahOverviewThemeMetrics base = DovahOverviewThemeMetrics.dovah;

      for (final DovahOverviewThemeMetrics changed
          in <DovahOverviewThemeMetrics>[
            base.copyWith(gridGap: 1),
            base.copyWith(regularHeroMinHeight: 1),
            base.copyWith(compactHeroMinHeight: 1),
            base.copyWith(regularStatsTopGap: 1),
            base.copyWith(compactStatsTopGap: 1),
          ]) {
        expect(changed, isNot(base));
      }
    });
  });
}
