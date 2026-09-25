import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Exercises [DovahSessionThemeMetrics]'s prototype values per preset, its interpolation, and its
/// value semantics.
void main() {
  group('Property preset values behave correctly', () {
    test('Property frostbound pins the navigation height', () {
      const DovahSessionThemeMetrics metrics =
          DovahSessionThemeMetrics.frostbound;

      expect(metrics.regularNavHeight, isA<double>());
      expect(metrics.regularNavHeight, 45);
      expect(metrics.compactNavHeight, isA<double>());
      expect(metrics.compactNavHeight, 45);
    });

    test('Property dovah keeps the prototype values', () {
      const DovahSessionThemeMetrics metrics = DovahSessionThemeMetrics.dovah;

      expect(metrics.regularNavHeight, 53);
      expect(metrics.compactNavHeight, 45);
    });

    test('Property hearth keeps the prototype values', () {
      const DovahSessionThemeMetrics metrics = DovahSessionThemeMetrics.hearth;

      expect(metrics.regularNavHeight, 53);
      expect(metrics.compactNavHeight, 45);
    });

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      test(
        'Property ${preset.name} is the metrics its theme builder installs',
        () {
          final DovahSessionThemeMetrics expected = switch (preset) {
            DovahThemePreset.frostbound => DovahSessionThemeMetrics.frostbound,
            DovahThemePreset.dovah => DovahSessionThemeMetrics.dovah,
            DovahThemePreset.hearth => DovahSessionThemeMetrics.hearth,
          };

          expect(
            dovahThemeDataFor(preset).extension<DovahSessionThemeMetrics>(),
            expected,
          );
        },
      );
    }
  });

  group('Method lerp behaves correctly', () {
    test('Method lerp returns the receiver at t 0 and the target at t 1', () {
      const DovahSessionThemeMetrics from = DovahSessionThemeMetrics.frostbound;
      const DovahSessionThemeMetrics to = DovahSessionThemeMetrics.dovah;

      expect(from.lerp(to, 0), from);
      expect(from.lerp(to, 1), to);
    });

    test(
      'Method lerp lands the regular navigation height between the endpoints',
      () {
        final DovahSessionThemeMetrics mid = DovahSessionThemeMetrics.frostbound
            .lerp(DovahSessionThemeMetrics.dovah, 0.5);

        expect(mid.regularNavHeight, isA<double>());
        expect(mid.regularNavHeight, 49);
      },
    );

    test(
      'Method lerp keeps the compact navigation height where it is shared',
      () {
        final DovahSessionThemeMetrics mid = DovahSessionThemeMetrics.frostbound
            .lerp(DovahSessionThemeMetrics.hearth, 0.5);

        expect(mid.compactNavHeight, 45);
      },
    );

    test('Method lerp interpolates a quarter of the way', () {
      final DovahSessionThemeMetrics quarter = DovahSessionThemeMetrics
          .frostbound
          .lerp(DovahSessionThemeMetrics.dovah, 0.25);

      expect(quarter.regularNavHeight, 47);
    });

    test('Method lerp between identical metrics keeps every value', () {
      expect(
        DovahSessionThemeMetrics.hearth.lerp(
          DovahSessionThemeMetrics.hearth,
          0.37,
        ),
        DovahSessionThemeMetrics.hearth,
      );
    });

    test('Method lerp returns the receiver when the target is null', () {
      expect(
        DovahSessionThemeMetrics.dovah.lerp(null, 0.5),
        DovahSessionThemeMetrics.dovah,
      );
    });

    test(
      'Method lerp flows through forWindow so mid-transition metrics resolve per window mode',
      () {
        final DovahSessionThemeMetrics mid = DovahSessionThemeMetrics.frostbound
            .lerp(DovahSessionThemeMetrics.dovah, 0.5);

        expect(
          DovahSessionMetrics.forWindow(
            themeMetrics: mid,
            window: const Size(1280, 720),
          ).navHeight,
          49,
        );
        expect(
          DovahSessionMetrics.forWindow(
            themeMetrics: mid,
            window: const Size(800, 700),
          ).navHeight,
          49,
        );
        expect(
          DovahSessionMetrics.forWindow(
            themeMetrics: mid,
            window: const Size(1280, 560),
          ).navHeight,
          45,
        );
      },
    );

    test(
      'Method lerp interpolates the metrics installed by ThemeData.lerp',
      () {
        final DovahSessionThemeMetrics mid = ThemeData.lerp(
          dovahThemeDataFor(DovahThemePreset.frostbound),
          dovahThemeDataFor(DovahThemePreset.dovah),
          0.5,
        ).extension<DovahSessionThemeMetrics>()!;

        expect(mid.regularNavHeight, 49);
      },
    );
  });

  group('Method copyWith behaves correctly', () {
    test('Method copyWith replaces every given value', () {
      final DovahSessionThemeMetrics copy = DovahSessionThemeMetrics.dovah
          .copyWith(regularNavHeight: 1, compactNavHeight: 2);

      expect(copy.regularNavHeight, 1);
      expect(copy.compactNavHeight, 2);
    });

    test('Method copyWith keeps the values it is not given', () {
      final DovahSessionThemeMetrics copy = DovahSessionThemeMetrics.dovah
          .copyWith(regularNavHeight: 1);

      expect(copy.regularNavHeight, 1);
      expect(copy.compactNavHeight, 45);
    });

    test('Method copyWith without arguments keeps every value', () {
      expect(
        DovahSessionThemeMetrics.hearth.copyWith(),
        DovahSessionThemeMetrics.hearth,
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same values', () {
      final DovahSessionThemeMetrics copy = DovahSessionThemeMetrics.dovah
          .copyWith();

      expect(copy, DovahSessionThemeMetrics.dovah);
      expect(copy.hashCode, DovahSessionThemeMetrics.dovah.hashCode);
    });

    test('Behavior equality holds between presets that share a table row', () {
      expect(DovahSessionThemeMetrics.dovah, DovahSessionThemeMetrics.hearth);
    });

    test(
      'Behavior equality fails when Frostbound pins its navigation height',
      () {
        expect(
          DovahSessionThemeMetrics.frostbound,
          isNot(DovahSessionThemeMetrics.dovah),
        );
      },
    );

    test('Behavior equality fails when any single value differs', () {
      const DovahSessionThemeMetrics base = DovahSessionThemeMetrics.dovah;

      expect(base.copyWith(regularNavHeight: 1), isNot(base));
      expect(base.copyWith(compactNavHeight: 1), isNot(base));
    });
  });
}
