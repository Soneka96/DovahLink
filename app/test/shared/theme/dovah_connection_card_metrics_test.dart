import 'package:flutter/painting.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_metrics.dart';

/// One expected resolution: the preset and window it is resolved for, then the padding, rendered
/// height, icon tile size and radius, bevel cut, corner radius, and whether the detail is shown.
typedef _CardCase = (
  DovahThemePreset,
  Size,
  EdgeInsets,
  double,
  double,
  double,
  double,
  double,
  bool,
);

/// Exercises [DovahConnectionCardMetrics]'s prototype constants, per-theme tables, breakpoints,
/// and equality.
void main() {
  group('Property shared constants behave correctly', () {
    test('Property shared constants keep the prototype .connection values', () {
      expect(DovahConnectionCardMetrics.stateMarkerSize, isA<double>());
      expect(DovahConnectionCardMetrics.stateMarkerSize, 8);
      expect(DovahConnectionCardMetrics.stateMarkerGap, isA<double>());
      expect(DovahConnectionCardMetrics.stateMarkerGap, 8);
      expect(DovahConnectionCardMetrics.statusMinWidth, isA<double>());
      expect(DovahConnectionCardMetrics.statusMinWidth, 112);
      expect(DovahConnectionCardMetrics.statusArrowGap, isA<double>());
      expect(DovahConnectionCardMetrics.statusArrowGap, 17);
      expect(DovahConnectionCardMetrics.arrowSize, isA<double>());
      expect(DovahConnectionCardMetrics.arrowSize, 24);
      expect(DovahConnectionCardMetrics.columnGap, isA<double>());
      expect(DovahConnectionCardMetrics.columnGap, 16);
      expect(DovahConnectionCardMetrics.iconSize, isA<double>());
      expect(DovahConnectionCardMetrics.iconSize, 21);
      expect(DovahConnectionCardMetrics.titleFontSize, isA<double>());
      expect(DovahConnectionCardMetrics.titleFontSize, 16);
      expect(DovahConnectionCardMetrics.titleBottomGap, isA<double>());
      expect(DovahConnectionCardMetrics.titleBottomGap, 4);
      expect(DovahConnectionCardMetrics.mainColumnFlex, isA<int>());
      expect(DovahConnectionCardMetrics.mainColumnFlex, 10);
      expect(DovahConnectionCardMetrics.detailColumnFlex, isA<int>());
      expect(DovahConnectionCardMetrics.detailColumnFlex, 7);
    });
  });

  group('Method forWindow behaves correctly', () {
    // Values are the prototype's measured computed styles at each window.
    const List<_CardCase> cases = <_CardCase>[
      (
        DovahThemePreset.frostbound,
        Size(1280, 720),
        EdgeInsets.symmetric(vertical: 10, horizontal: 14),
        68,
        37,
        0,
        11,
        0,
        true,
      ),
      (
        DovahThemePreset.dovah,
        Size(1280, 720),
        EdgeInsets.symmetric(vertical: 16, horizontal: 18),
        80,
        43,
        0,
        16,
        0,
        true,
      ),
      (
        DovahThemePreset.hearth,
        Size(1280, 720),
        EdgeInsets.symmetric(vertical: 16, horizontal: 18),
        82,
        43,
        21.5,
        0,
        12,
        true,
      ),
      (
        DovahThemePreset.dovah,
        Size(800, 700),
        EdgeInsets.symmetric(vertical: 16, horizontal: 18),
        80,
        43,
        0,
        16,
        0,
        false,
      ),
      (
        DovahThemePreset.frostbound,
        Size(900, 560),
        EdgeInsets.symmetric(vertical: 7, horizontal: 12),
        62,
        37,
        0,
        11,
        0,
        false,
      ),
      (
        DovahThemePreset.dovah,
        Size(900, 560),
        EdgeInsets.symmetric(vertical: 10, horizontal: 14),
        68,
        37,
        0,
        16,
        0,
        false,
      ),
      (
        DovahThemePreset.hearth,
        Size(1000, 560),
        EdgeInsets.symmetric(vertical: 10, horizontal: 14),
        68,
        37,
        18.5,
        0,
        12,
        true,
      ),
    ];

    for (final _CardCase testCase in cases) {
      test(
        'Method forWindow resolves the prototype card measurements for ${testCase.$1.name} at ${testCase.$2}',
        () {
          final DovahConnectionCardMetrics metrics =
              DovahConnectionCardMetrics.forWindow(
                preset: testCase.$1,
                window: testCase.$2,
              );

          expect(metrics.padding, testCase.$3);
          expect(metrics.minHeight, isA<double>());
          expect(metrics.minHeight, testCase.$4);
          expect(metrics.iconTileSize, isA<double>());
          expect(metrics.iconTileSize, testCase.$5);
          expect(metrics.iconTileRadius, isA<double>());
          expect(metrics.iconTileRadius, testCase.$6);
          expect(metrics.cornerCutSize, isA<double>());
          expect(metrics.cornerCutSize, testCase.$7);
          expect(metrics.cornerRadius, isA<double>());
          expect(metrics.cornerRadius, testCase.$8);
          expect(metrics.showDetail, isA<bool>());
          expect(metrics.showDetail, testCase.$9);
        },
      );
    }

    test('Method forWindow shows the detail column just above 900 wide', () {
      expect(
        DovahConnectionCardMetrics.forWindow(
          preset: DovahThemePreset.dovah,
          window: const Size(901, 720),
        ).showDetail,
        isTrue,
      );
      expect(
        DovahConnectionCardMetrics.forWindow(
          preset: DovahThemePreset.dovah,
          window: const Size(900, 720),
        ).showDetail,
        isFalse,
      );
    });

    test('Method forWindow switches to compact geometry at 620 tall', () {
      expect(
        DovahConnectionCardMetrics.forWindow(
          preset: DovahThemePreset.dovah,
          window: const Size(1280, 620),
        ).iconTileSize,
        37,
      );
      expect(
        DovahConnectionCardMetrics.forWindow(
          preset: DovahThemePreset.dovah,
          window: const Size(1280, 621),
        ).iconTileSize,
        43,
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same resolved measurements', () {
      final DovahConnectionCardMetrics first =
          DovahConnectionCardMetrics.forWindow(
            preset: DovahThemePreset.dovah,
            window: const Size(1280, 720),
          );
      final DovahConnectionCardMetrics second =
          DovahConnectionCardMetrics.forWindow(
            preset: DovahThemePreset.dovah,
            window: const Size(1600, 900),
          );

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality fails between regular and compact', () {
      final DovahConnectionCardMetrics regular =
          DovahConnectionCardMetrics.forWindow(
            preset: DovahThemePreset.dovah,
            window: const Size(1280, 720),
          );
      final DovahConnectionCardMetrics compact =
          DovahConnectionCardMetrics.forWindow(
            preset: DovahThemePreset.dovah,
            window: const Size(1280, 500),
          );

      expect(regular, isNot(compact));
    });
  });
}
