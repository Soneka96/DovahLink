import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Exercises [DovahConnectionCardThemeMetrics]'s prototype values per preset, its interpolation,
/// and its value semantics.
void main() {
  group('Property preset values behave correctly', () {
    test('Property frostbound keeps the prototype values', () {
      const DovahConnectionCardThemeMetrics metrics =
          DovahConnectionCardThemeMetrics.frostbound;

      expect(
        metrics.regularPadding,
        const EdgeInsets.symmetric(vertical: 10, horizontal: 14),
      );
      expect(
        metrics.compactPadding,
        const EdgeInsets.symmetric(vertical: 7, horizontal: 12),
      );
      expect(metrics.regularMinHeight, isA<double>());
      expect(metrics.regularMinHeight, 68);
      expect(metrics.compactMinHeight, isA<double>());
      expect(metrics.compactMinHeight, 62);
      expect(metrics.regularIconTileSize, 37);
      expect(metrics.compactIconTileSize, 37);
      expect(metrics.regularIconTileRadius, 0);
      expect(metrics.compactIconTileRadius, 0);
      expect(metrics.cornerCutSize, isA<double>());
      expect(metrics.cornerCutSize, 11);
      expect(metrics.cornerRadius, isA<double>());
      expect(metrics.cornerRadius, 0);
      expect(metrics.iconTileRotation, isA<double>());
      expect(metrics.iconTileRotation, 0);
    });

    test('Property dovah keeps the prototype values', () {
      const DovahConnectionCardThemeMetrics metrics =
          DovahConnectionCardThemeMetrics.dovah;

      expect(
        metrics.regularPadding,
        const EdgeInsets.symmetric(vertical: 16, horizontal: 18),
      );
      expect(
        metrics.compactPadding,
        const EdgeInsets.symmetric(vertical: 10, horizontal: 14),
      );
      expect(metrics.regularMinHeight, 80);
      expect(metrics.compactMinHeight, 68);
      expect(metrics.regularIconTileSize, 43);
      expect(metrics.compactIconTileSize, 37);
      expect(metrics.regularIconTileRadius, 0);
      expect(metrics.compactIconTileRadius, 0);
      expect(metrics.cornerCutSize, 16);
      expect(metrics.cornerRadius, 0);
      expect(metrics.iconTileRotation, isA<double>());
      expect(metrics.iconTileRotation, math.pi / 4);
    });

    test('Property hearth keeps the prototype values', () {
      const DovahConnectionCardThemeMetrics metrics =
          DovahConnectionCardThemeMetrics.hearth;

      expect(
        metrics.regularPadding,
        const EdgeInsets.symmetric(vertical: 16, horizontal: 18),
      );
      expect(
        metrics.compactPadding,
        const EdgeInsets.symmetric(vertical: 10, horizontal: 14),
      );
      expect(metrics.regularMinHeight, 82);
      expect(metrics.compactMinHeight, 68);
      expect(metrics.regularIconTileSize, 43);
      expect(metrics.compactIconTileSize, 37);
      expect(metrics.regularIconTileRadius, 21.5);
      expect(metrics.compactIconTileRadius, 18.5);
      expect(metrics.cornerCutSize, 0);
      expect(metrics.cornerRadius, 12);
      expect(metrics.iconTileRotation, 0);
    });

    test('Property hearth icon tile radius is half its tile size', () {
      const DovahConnectionCardThemeMetrics metrics =
          DovahConnectionCardThemeMetrics.hearth;

      expect(metrics.regularIconTileRadius, metrics.regularIconTileSize / 2);
      expect(metrics.compactIconTileRadius, metrics.compactIconTileSize / 2);
    });

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      test(
        'Property ${preset.name} is the metrics its theme builder installs',
        () {
          final DovahConnectionCardThemeMetrics expected = switch (preset) {
            DovahThemePreset.frostbound =>
              DovahConnectionCardThemeMetrics.frostbound,
            DovahThemePreset.dovah => DovahConnectionCardThemeMetrics.dovah,
            DovahThemePreset.hearth => DovahConnectionCardThemeMetrics.hearth,
          };

          expect(
            dovahThemeDataFor(
              preset,
            ).extension<DovahConnectionCardThemeMetrics>(),
            expected,
          );
        },
      );
    }
  });

  group('Method lerp behaves correctly', () {
    test('Method lerp returns the receiver at t 0 and the target at t 1', () {
      const DovahConnectionCardThemeMetrics from =
          DovahConnectionCardThemeMetrics.dovah;
      const DovahConnectionCardThemeMetrics to =
          DovahConnectionCardThemeMetrics.hearth;

      expect(from.lerp(to, 0), from);
      expect(from.lerp(to, 1), to);
    });

    test(
      'Method lerp lands the card height between the endpoints at t 0.5',
      () {
        final DovahConnectionCardThemeMetrics mid =
            DovahConnectionCardThemeMetrics.dovah.lerp(
              DovahConnectionCardThemeMetrics.hearth,
              0.5,
            );

        expect(mid.regularMinHeight, isA<double>());
        expect(mid.regularMinHeight, 81);
        expect(mid.compactMinHeight, 68);
      },
    );

    test('Method lerp interpolates the padding EdgeInsets at t 0.5', () {
      final DovahConnectionCardThemeMetrics regular =
          DovahConnectionCardThemeMetrics.frostbound.lerp(
            DovahConnectionCardThemeMetrics.dovah,
            0.5,
          );

      expect(
        regular.regularPadding,
        const EdgeInsets.symmetric(vertical: 13, horizontal: 16),
      );
      expect(
        regular.compactPadding,
        const EdgeInsets.symmetric(vertical: 8.5, horizontal: 13),
      );
    });

    test('Method lerp interpolates the corner values at t 0.5', () {
      final DovahConnectionCardThemeMetrics dovahToHearth =
          DovahConnectionCardThemeMetrics.dovah.lerp(
            DovahConnectionCardThemeMetrics.hearth,
            0.5,
          );

      expect(dovahToHearth.cornerCutSize, 8);
      expect(dovahToHearth.cornerRadius, 6);
    });

    test('Method lerp interpolates the icon tile size and radius at t 0.5', () {
      final DovahConnectionCardThemeMetrics mid =
          DovahConnectionCardThemeMetrics.frostbound.lerp(
            DovahConnectionCardThemeMetrics.hearth,
            0.5,
          );

      expect(mid.regularIconTileSize, 40);
      expect(mid.regularIconTileRadius, 10.75);
      expect(mid.compactIconTileSize, 37);
      expect(mid.compactIconTileRadius, 9.25);
    });

    test('Method lerp turns the icon tile between the endpoints at t 0.5', () {
      final DovahConnectionCardThemeMetrics mid =
          DovahConnectionCardThemeMetrics.dovah.lerp(
            DovahConnectionCardThemeMetrics.hearth,
            0.5,
          );

      expect(mid.iconTileRotation, math.pi / 8);
    });

    test('Method lerp between identical metrics keeps every value', () {
      expect(
        DovahConnectionCardThemeMetrics.hearth.lerp(
          DovahConnectionCardThemeMetrics.hearth,
          0.37,
        ),
        DovahConnectionCardThemeMetrics.hearth,
      );
    });

    test('Method lerp returns the receiver when the target is null', () {
      expect(
        DovahConnectionCardThemeMetrics.dovah.lerp(null, 0.5),
        DovahConnectionCardThemeMetrics.dovah,
      );
    });

    test(
      'Method lerp flows through forWindow so mid-transition metrics resolve per window mode',
      () {
        final DovahConnectionCardThemeMetrics mid =
            DovahConnectionCardThemeMetrics.dovah.lerp(
              DovahConnectionCardThemeMetrics.hearth,
              0.5,
            );

        final DovahConnectionCardMetrics regular =
            DovahConnectionCardMetrics.forWindow(
              themeMetrics: mid,
              window: const Size(1280, 720),
            );
        final DovahConnectionCardMetrics compact =
            DovahConnectionCardMetrics.forWindow(
              themeMetrics: mid,
              window: const Size(1280, 560),
            );

        expect(regular.minHeight, 81);
        expect(regular.iconTileRadius, 10.75);
        expect(regular.cornerCutSize, 8);
        expect(compact.minHeight, 68);
        expect(compact.iconTileRadius, 9.25);
      },
    );

    test(
      'Method lerp interpolates the metrics installed by ThemeData.lerp',
      () {
        final DovahConnectionCardThemeMetrics mid = ThemeData.lerp(
          dovahThemeDataFor(DovahThemePreset.dovah),
          dovahThemeDataFor(DovahThemePreset.hearth),
          0.5,
        ).extension<DovahConnectionCardThemeMetrics>()!;

        expect(mid.regularMinHeight, 81);
      },
    );
  });

  group('Method copyWith behaves correctly', () {
    test('Method copyWith replaces every given value', () {
      final DovahConnectionCardThemeMetrics copy =
          DovahConnectionCardThemeMetrics.dovah.copyWith(
            regularPadding: const EdgeInsets.all(1),
            compactPadding: const EdgeInsets.all(2),
            regularMinHeight: 3,
            compactMinHeight: 4,
            regularIconTileSize: 5,
            compactIconTileSize: 6,
            regularIconTileRadius: 7,
            compactIconTileRadius: 8,
            cornerCutSize: 9,
            cornerRadius: 10,
            iconTileRotation: 11,
          );

      expect(copy.regularPadding, const EdgeInsets.all(1));
      expect(copy.compactPadding, const EdgeInsets.all(2));
      expect(copy.regularMinHeight, 3);
      expect(copy.compactMinHeight, 4);
      expect(copy.regularIconTileSize, 5);
      expect(copy.compactIconTileSize, 6);
      expect(copy.regularIconTileRadius, 7);
      expect(copy.compactIconTileRadius, 8);
      expect(copy.cornerCutSize, 9);
      expect(copy.cornerRadius, 10);
      expect(copy.iconTileRotation, 11);
    });

    test('Method copyWith keeps the values it is not given', () {
      final DovahConnectionCardThemeMetrics copy =
          DovahConnectionCardThemeMetrics.hearth.copyWith(cornerRadius: 1);

      expect(copy.cornerRadius, 1);
      expect(copy.cornerCutSize, 0);
      expect(copy.regularMinHeight, 82);
      expect(
        copy,
        DovahConnectionCardThemeMetrics.hearth.copyWith(cornerRadius: 1),
      );
      expect(copy, isNot(DovahConnectionCardThemeMetrics.hearth));
    });

    test('Method copyWith without arguments keeps every value', () {
      expect(
        DovahConnectionCardThemeMetrics.hearth.copyWith(),
        DovahConnectionCardThemeMetrics.hearth,
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same values', () {
      final DovahConnectionCardThemeMetrics copy =
          DovahConnectionCardThemeMetrics.dovah.copyWith();

      expect(copy, DovahConnectionCardThemeMetrics.dovah);
      expect(copy.hashCode, DovahConnectionCardThemeMetrics.dovah.hashCode);
    });

    test('Behavior equality fails between different presets', () {
      expect(
        DovahConnectionCardThemeMetrics.dovah,
        isNot(DovahConnectionCardThemeMetrics.hearth),
      );
    });

    test('Behavior equality fails when any single value differs', () {
      const DovahConnectionCardThemeMetrics base =
          DovahConnectionCardThemeMetrics.dovah;

      for (final DovahConnectionCardThemeMetrics changed
          in <DovahConnectionCardThemeMetrics>[
            base.copyWith(regularPadding: EdgeInsets.zero),
            base.copyWith(compactPadding: EdgeInsets.zero),
            base.copyWith(regularMinHeight: 1),
            base.copyWith(compactMinHeight: 1),
            base.copyWith(regularIconTileSize: 1),
            base.copyWith(compactIconTileSize: 1),
            base.copyWith(regularIconTileRadius: 1),
            base.copyWith(compactIconTileRadius: 1),
            base.copyWith(cornerCutSize: 1),
            base.copyWith(cornerRadius: 1),
            base.copyWith(iconTileRotation: 1),
          ]) {
        expect(changed, isNot(base));
      }
    });
  });
}
