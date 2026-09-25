import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_appearance_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Exercises [DovahAppearanceThemeMetrics]'s prototype values per preset, its interpolation, and
/// its value semantics.
void main() {
  group('Property preset values behave correctly', () {
    test('Property frostbound keeps a 9px single bevel', () {
      const DovahAppearanceThemeMetrics metrics =
          DovahAppearanceThemeMetrics.frostbound;

      expect(metrics.cornerCutSize, isA<double>());
      expect(metrics.cornerCutSize, 9);
      expect(metrics.cornerRadius, isA<double>());
      expect(metrics.cornerRadius, 0);
    });

    test('Property dovah keeps a 10px double bevel', () {
      const DovahAppearanceThemeMetrics metrics =
          DovahAppearanceThemeMetrics.dovah;

      expect(metrics.cornerCutSize, 10);
      expect(metrics.cornerRadius, 0);
    });

    test('Property hearth keeps 13px rounding and no bevel', () {
      const DovahAppearanceThemeMetrics metrics =
          DovahAppearanceThemeMetrics.hearth;

      expect(metrics.cornerCutSize, 0);
      expect(metrics.cornerRadius, 13);
    });

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      test(
        'Property ${preset.name} is the metrics its theme builder installs',
        () {
          final DovahAppearanceThemeMetrics expected = switch (preset) {
            DovahThemePreset.frostbound =>
              DovahAppearanceThemeMetrics.frostbound,
            DovahThemePreset.dovah => DovahAppearanceThemeMetrics.dovah,
            DovahThemePreset.hearth => DovahAppearanceThemeMetrics.hearth,
          };

          expect(
            dovahThemeDataFor(preset).extension<DovahAppearanceThemeMetrics>(),
            expected,
          );
        },
      );
    }
  });

  group('Method lerp behaves correctly', () {
    test('Method lerp returns the receiver at t 0 and the target at t 1', () {
      const DovahAppearanceThemeMetrics from =
          DovahAppearanceThemeMetrics.dovah;
      const DovahAppearanceThemeMetrics to = DovahAppearanceThemeMetrics.hearth;

      expect(from.lerp(to, 0), from);
      expect(from.lerp(to, 1), to);
    });

    test('Method lerp lands both values between the endpoints at t 0.5', () {
      final DovahAppearanceThemeMetrics mid = DovahAppearanceThemeMetrics.dovah
          .lerp(DovahAppearanceThemeMetrics.hearth, 0.5);

      expect(mid.cornerCutSize, 5);
      expect(mid.cornerRadius, 6.5);
    });

    test('Method lerp returns the receiver when the target is null', () {
      expect(
        DovahAppearanceThemeMetrics.hearth.lerp(null, 0.5),
        DovahAppearanceThemeMetrics.hearth,
      );
    });
  });

  group('Method copyWith behaves correctly', () {
    test('Method copyWith replaces every given value', () {
      final DovahAppearanceThemeMetrics copy = DovahAppearanceThemeMetrics.dovah
          .copyWith(cornerCutSize: 1, cornerRadius: 2);

      expect(copy.cornerCutSize, 1);
      expect(copy.cornerRadius, 2);
    });

    test('Method copyWith without arguments keeps every value', () {
      expect(
        DovahAppearanceThemeMetrics.hearth.copyWith(),
        DovahAppearanceThemeMetrics.hearth,
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same values', () {
      final DovahAppearanceThemeMetrics copy = DovahAppearanceThemeMetrics
          .hearth
          .copyWith();

      expect(copy, DovahAppearanceThemeMetrics.hearth);
      expect(copy.hashCode, DovahAppearanceThemeMetrics.hearth.hashCode);
    });

    test('Behavior equality fails when any single value differs', () {
      const DovahAppearanceThemeMetrics base =
          DovahAppearanceThemeMetrics.hearth;

      expect(base.copyWith(cornerCutSize: 1), isNot(base));
      expect(base.copyWith(cornerRadius: 1), isNot(base));
    });
  });
}
