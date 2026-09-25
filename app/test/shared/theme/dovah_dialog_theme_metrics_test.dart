import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Exercises [DovahDialogThemeMetrics]'s prototype values per preset, its interpolation, and its
/// value semantics.
void main() {
  group('Property preset values behave correctly', () {
    test('Property frostbound keeps square marks and code boxes', () {
      const DovahDialogThemeMetrics metrics =
          DovahDialogThemeMetrics.frostbound;

      expect(metrics.regularMarkCornerRadius, isA<double>());
      expect(metrics.regularMarkCornerRadius, 0);
      expect(metrics.compactMarkCornerRadius, 0);
      expect(metrics.codeBoxCornerRadius, isA<double>());
      expect(metrics.codeBoxCornerRadius, 0);
    });

    test('Property dovah keeps square marks and code boxes', () {
      const DovahDialogThemeMetrics metrics = DovahDialogThemeMetrics.dovah;

      expect(metrics.regularMarkCornerRadius, 0);
      expect(metrics.compactMarkCornerRadius, 0);
      expect(metrics.codeBoxCornerRadius, 0);
    });

    test('Property hearth keeps a circular mark and 9px code boxes', () {
      const DovahDialogThemeMetrics metrics = DovahDialogThemeMetrics.hearth;

      expect(metrics.regularMarkCornerRadius, 27);
      expect(metrics.compactMarkCornerRadius, 21);
      expect(metrics.codeBoxCornerRadius, 9);
    });

    test('Property hearth mark radius is half its tile size', () {
      const DovahDialogThemeMetrics metrics = DovahDialogThemeMetrics.hearth;

      expect(metrics.regularMarkCornerRadius, 54 / 2);
      expect(metrics.compactMarkCornerRadius, 42 / 2);
    });

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      test(
        'Property ${preset.name} is the metrics its theme builder installs',
        () {
          final DovahDialogThemeMetrics expected = switch (preset) {
            DovahThemePreset.frostbound => DovahDialogThemeMetrics.frostbound,
            DovahThemePreset.dovah => DovahDialogThemeMetrics.dovah,
            DovahThemePreset.hearth => DovahDialogThemeMetrics.hearth,
          };

          expect(
            dovahThemeDataFor(preset).extension<DovahDialogThemeMetrics>(),
            expected,
          );
        },
      );
    }
  });

  group('Method lerp behaves correctly', () {
    test('Method lerp returns the receiver at t 0 and the target at t 1', () {
      const DovahDialogThemeMetrics from = DovahDialogThemeMetrics.dovah;
      const DovahDialogThemeMetrics to = DovahDialogThemeMetrics.hearth;

      expect(from.lerp(to, 0), from);
      expect(from.lerp(to, 1), to);
    });

    test('Method lerp lands every radius between the endpoints at t 0.5', () {
      final DovahDialogThemeMetrics mid = DovahDialogThemeMetrics.dovah.lerp(
        DovahDialogThemeMetrics.hearth,
        0.5,
      );

      expect(mid.regularMarkCornerRadius, 13.5);
      expect(mid.compactMarkCornerRadius, 10.5);
      expect(mid.codeBoxCornerRadius, 4.5);
    });

    test('Method lerp returns the receiver when the target is null', () {
      expect(
        DovahDialogThemeMetrics.hearth.lerp(null, 0.5),
        DovahDialogThemeMetrics.hearth,
      );
    });
  });

  group('Method copyWith behaves correctly', () {
    test('Method copyWith replaces every given value', () {
      final DovahDialogThemeMetrics copy = DovahDialogThemeMetrics.dovah
          .copyWith(
            regularMarkCornerRadius: 1,
            compactMarkCornerRadius: 2,
            codeBoxCornerRadius: 3,
          );

      expect(copy.regularMarkCornerRadius, 1);
      expect(copy.compactMarkCornerRadius, 2);
      expect(copy.codeBoxCornerRadius, 3);
    });

    test('Method copyWith without arguments keeps every value', () {
      expect(
        DovahDialogThemeMetrics.hearth.copyWith(),
        DovahDialogThemeMetrics.hearth,
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same values', () {
      final DovahDialogThemeMetrics copy = DovahDialogThemeMetrics.hearth
          .copyWith();

      expect(copy, DovahDialogThemeMetrics.hearth);
      expect(copy.hashCode, DovahDialogThemeMetrics.hearth.hashCode);
    });

    test('Behavior equality fails when any single value differs', () {
      const DovahDialogThemeMetrics base = DovahDialogThemeMetrics.hearth;

      for (final DovahDialogThemeMetrics changed in [
        base.copyWith(regularMarkCornerRadius: 1),
        base.copyWith(compactMarkCornerRadius: 1),
        base.copyWith(codeBoxCornerRadius: 1),
      ]) {
        expect(changed, isNot(base));
      }
    });
  });
}
