import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Exercises [DovahPageThemeMetrics]'s prototype values per preset, its interpolation, and its
/// value semantics.
void main() {
  group('Property preset values behave correctly', () {
    test('Property frostbound keeps the prototype values', () {
      const DovahPageThemeMetrics metrics = DovahPageThemeMetrics.frostbound;

      expect(metrics.regularContentTopPadding, isA<double>());
      expect(metrics.regularContentTopPadding, 22);
      expect(metrics.compactContentTopPadding, isA<double>());
      expect(metrics.compactContentTopPadding, 22);
      expect(metrics.regularIntroBottomGap, 14);
      expect(metrics.compactIntroBottomGap, 14);
      expect(metrics.introPadding, EdgeInsets.zero);
      expect(metrics.regularPanelPadding, const EdgeInsets.all(14));
      expect(metrics.compactPanelPadding, const EdgeInsets.all(14));
    });

    test('Property dovah keeps the prototype values', () {
      const DovahPageThemeMetrics metrics = DovahPageThemeMetrics.dovah;

      expect(metrics.regularContentTopPadding, 28);
      expect(metrics.compactContentTopPadding, 18);
      expect(metrics.regularIntroBottomGap, 20);
      expect(metrics.compactIntroBottomGap, 14);
      expect(metrics.introPadding, EdgeInsets.zero);
      expect(metrics.regularPanelPadding, const EdgeInsets.all(18));
      expect(metrics.compactPanelPadding, const EdgeInsets.all(15));
    });

    test('Property hearth keeps the prototype values', () {
      const DovahPageThemeMetrics metrics = DovahPageThemeMetrics.hearth;

      expect(metrics.regularContentTopPadding, 28);
      expect(metrics.compactContentTopPadding, 18);
      expect(metrics.regularIntroBottomGap, 20);
      expect(metrics.compactIntroBottomGap, 14);
      expect(
        metrics.introPadding,
        const EdgeInsets.symmetric(vertical: 10, horizontal: 13),
      );
      expect(metrics.regularPanelPadding, const EdgeInsets.all(18));
      expect(metrics.compactPanelPadding, const EdgeInsets.all(15));
    });

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      test(
        'Property ${preset.name} is the metrics its theme builder installs',
        () {
          final DovahPageThemeMetrics expected = switch (preset) {
            DovahThemePreset.frostbound => DovahPageThemeMetrics.frostbound,
            DovahThemePreset.dovah => DovahPageThemeMetrics.dovah,
            DovahThemePreset.hearth => DovahPageThemeMetrics.hearth,
          };

          expect(
            dovahThemeDataFor(preset).extension<DovahPageThemeMetrics>(),
            expected,
          );
        },
      );
    }
  });

  group('Method lerp behaves correctly', () {
    test('Method lerp returns the receiver at t 0 and the target at t 1', () {
      const DovahPageThemeMetrics from = DovahPageThemeMetrics.dovah;
      const DovahPageThemeMetrics to = DovahPageThemeMetrics.hearth;

      expect(from.lerp(to, 0), from);
      expect(from.lerp(to, 1), to);
    });

    test('Method lerp lands regular values between the endpoints at t 0.5', () {
      final DovahPageThemeMetrics mid = DovahPageThemeMetrics.frostbound.lerp(
        DovahPageThemeMetrics.dovah,
        0.5,
      );

      expect(mid.regularContentTopPadding, isA<double>());
      expect(mid.regularContentTopPadding, 25);
      expect(mid.regularIntroBottomGap, 17);
      expect(mid.regularPanelPadding, const EdgeInsets.all(16));
    });

    test('Method lerp lands compact values between the endpoints at t 0.5', () {
      final DovahPageThemeMetrics mid = DovahPageThemeMetrics.frostbound.lerp(
        DovahPageThemeMetrics.dovah,
        0.5,
      );

      expect(mid.compactContentTopPadding, 20);
      expect(mid.compactIntroBottomGap, 14);
      expect(mid.compactPanelPadding, const EdgeInsets.all(14.5));
    });

    test('Method lerp interpolates the intro padding EdgeInsets at t 0.5', () {
      final DovahPageThemeMetrics mid = DovahPageThemeMetrics.dovah.lerp(
        DovahPageThemeMetrics.hearth,
        0.5,
      );

      expect(
        mid.introPadding,
        const EdgeInsets.symmetric(vertical: 5, horizontal: 6.5),
      );
    });

    test('Method lerp between identical metrics keeps every value', () {
      expect(
        DovahPageThemeMetrics.hearth.lerp(DovahPageThemeMetrics.hearth, 0.37),
        DovahPageThemeMetrics.hearth,
      );
    });

    test('Method lerp returns the receiver when the target is null', () {
      expect(
        DovahPageThemeMetrics.dovah.lerp(null, 0.5),
        DovahPageThemeMetrics.dovah,
      );
    });

    test(
      'Method lerp flows through forWindow so mid-transition metrics resolve per window mode',
      () {
        final DovahPageThemeMetrics mid = DovahPageThemeMetrics.frostbound.lerp(
          DovahPageThemeMetrics.dovah,
          0.5,
        );

        final DovahPageMetrics regular = DovahPageMetrics.forWindow(
          themeMetrics: mid,
          window: const Size(1280, 720),
        );
        final DovahPageMetrics compact = DovahPageMetrics.forWindow(
          themeMetrics: mid,
          window: const Size(1280, 560),
        );

        expect(regular.contentTopPadding, 25);
        expect(regular.introBottomGap, 17);
        expect(regular.panelPadding, const EdgeInsets.all(16));
        expect(compact.contentTopPadding, 20);
        expect(compact.panelPadding, const EdgeInsets.all(14.5));
      },
    );

    test(
      'Method lerp interpolates the metrics installed by ThemeData.lerp',
      () {
        final DovahPageThemeMetrics mid = ThemeData.lerp(
          dovahThemeDataFor(DovahThemePreset.frostbound),
          dovahThemeDataFor(DovahThemePreset.dovah),
          0.5,
        ).extension<DovahPageThemeMetrics>()!;

        expect(mid.regularContentTopPadding, 25);
      },
    );
  });

  group('Method copyWith behaves correctly', () {
    test('Method copyWith replaces every given value', () {
      final DovahPageThemeMetrics copy = DovahPageThemeMetrics.dovah.copyWith(
        regularContentTopPadding: 1,
        compactContentTopPadding: 2,
        regularIntroBottomGap: 3,
        compactIntroBottomGap: 4,
        introPadding: const EdgeInsets.all(5),
        regularPanelPadding: const EdgeInsets.all(6),
        compactPanelPadding: const EdgeInsets.all(7),
      );

      expect(copy.regularContentTopPadding, 1);
      expect(copy.compactContentTopPadding, 2);
      expect(copy.regularIntroBottomGap, 3);
      expect(copy.compactIntroBottomGap, 4);
      expect(copy.introPadding, const EdgeInsets.all(5));
      expect(copy.regularPanelPadding, const EdgeInsets.all(6));
      expect(copy.compactPanelPadding, const EdgeInsets.all(7));
    });

    test('Method copyWith keeps the values it is not given', () {
      final DovahPageThemeMetrics copy = DovahPageThemeMetrics.hearth.copyWith(
        regularIntroBottomGap: 1,
      );

      expect(copy.regularIntroBottomGap, 1);
      expect(copy.regularContentTopPadding, 28);
      expect(copy.introPadding, DovahPageThemeMetrics.hearth.introPadding);
    });

    test('Method copyWith without arguments keeps every value', () {
      expect(
        DovahPageThemeMetrics.hearth.copyWith(),
        DovahPageThemeMetrics.hearth,
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same values', () {
      final DovahPageThemeMetrics copy = DovahPageThemeMetrics.dovah.copyWith();

      expect(copy, DovahPageThemeMetrics.dovah);
      expect(copy.hashCode, DovahPageThemeMetrics.dovah.hashCode);
    });

    test('Behavior equality fails between different presets', () {
      expect(DovahPageThemeMetrics.dovah, isNot(DovahPageThemeMetrics.hearth));
    });

    test('Behavior equality fails when any single value differs', () {
      const DovahPageThemeMetrics base = DovahPageThemeMetrics.dovah;

      for (final DovahPageThemeMetrics changed in <DovahPageThemeMetrics>[
        base.copyWith(regularContentTopPadding: 1),
        base.copyWith(compactContentTopPadding: 1),
        base.copyWith(regularIntroBottomGap: 1),
        base.copyWith(compactIntroBottomGap: 1),
        base.copyWith(introPadding: const EdgeInsets.all(1)),
        base.copyWith(regularPanelPadding: EdgeInsets.zero),
        base.copyWith(compactPanelPadding: EdgeInsets.zero),
      ]) {
        expect(changed, isNot(base));
      }
    });
  });
}
