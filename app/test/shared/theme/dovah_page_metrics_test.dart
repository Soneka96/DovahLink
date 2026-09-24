import 'package:flutter/painting.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_metrics.dart';

/// One expected resolution: the preset and window it is resolved for, then the side margin,
/// content top padding, intro bottom gap, intro title size, intro padding, panel padding, and
/// placeholder column count.
typedef _PageCase = (
  DovahThemePreset,
  Size,
  double,
  double,
  double,
  double,
  EdgeInsets,
  EdgeInsets,
  int,
);

/// Exercises [DovahPageMetrics]'s prototype constants, per-theme tables, breakpoints, and
/// equality.
void main() {
  group('Property shared constants behave correctly', () {
    test('Property shared constants keep the prototype game-page values', () {
      expect(DovahPageMetrics.contentMaxWidth, isA<double>());
      expect(DovahPageMetrics.contentMaxWidth, 1180);
      expect(DovahPageMetrics.contentBottomPadding, isA<double>());
      expect(DovahPageMetrics.contentBottomPadding, 42);
      expect(DovahPageMetrics.introTitleBottomGap, isA<double>());
      expect(DovahPageMetrics.introTitleBottomGap, 5);
      expect(DovahPageMetrics.introDescriptionFontSize, isA<double>());
      expect(DovahPageMetrics.introDescriptionFontSize, 13);
    });

    test('Property placeholder constants keep the prototype values', () {
      expect(DovahPageMetrics.placeholderGap, isA<double>());
      expect(DovahPageMetrics.placeholderGap, 12);
      expect(DovahPageMetrics.placeholderCardMinHeight, isA<double>());
      expect(DovahPageMetrics.placeholderCardMinHeight, 145);
      expect(DovahPageMetrics.placeholderCardPadding, isA<double>());
      expect(DovahPageMetrics.placeholderCardPadding, 17);
      expect(DovahPageMetrics.placeholderCardTitleBottomGap, isA<double>());
      expect(DovahPageMetrics.placeholderCardTitleBottomGap, 8);
      expect(DovahPageMetrics.placeholderBodyFontSize, isA<double>());
      expect(DovahPageMetrics.placeholderBodyFontSize, 13);
      expect(DovahPageMetrics.placeholderBodyLineHeight, isA<double>());
      expect(DovahPageMetrics.placeholderBodyLineHeight, 1.5);
    });
  });

  group('Method forWindow behaves correctly', () {
    const EdgeInsets boxed = EdgeInsets.symmetric(vertical: 10, horizontal: 13);
    // Values are the prototype's measured computed styles at each window.
    const List<_PageCase> cases = <_PageCase>[
      (
        DovahThemePreset.frostbound,
        Size(1280, 720),
        32,
        22,
        14,
        31,
        EdgeInsets.zero,
        EdgeInsets.all(14),
        3,
      ),
      (
        DovahThemePreset.dovah,
        Size(1280, 720),
        32,
        28,
        20,
        31,
        EdgeInsets.zero,
        EdgeInsets.all(18),
        3,
      ),
      (
        DovahThemePreset.hearth,
        Size(1280, 720),
        32,
        28,
        20,
        31,
        boxed,
        EdgeInsets.all(18),
        3,
      ),
      (
        DovahThemePreset.frostbound,
        Size(800, 700),
        18,
        22,
        14,
        31,
        EdgeInsets.zero,
        EdgeInsets.all(14),
        2,
      ),
      (
        DovahThemePreset.dovah,
        Size(800, 700),
        18,
        28,
        20,
        31,
        EdgeInsets.zero,
        EdgeInsets.all(18),
        2,
      ),
      (
        DovahThemePreset.frostbound,
        Size(1000, 560),
        32,
        22,
        14,
        26,
        EdgeInsets.zero,
        EdgeInsets.all(14),
        3,
      ),
      (
        DovahThemePreset.dovah,
        Size(1000, 560),
        32,
        18,
        14,
        26,
        EdgeInsets.zero,
        EdgeInsets.all(15),
        3,
      ),
      (
        DovahThemePreset.hearth,
        Size(1000, 560),
        32,
        18,
        14,
        26,
        boxed,
        EdgeInsets.all(15),
        3,
      ),
      (
        DovahThemePreset.dovah,
        Size(900, 560),
        18,
        18,
        14,
        26,
        EdgeInsets.zero,
        EdgeInsets.all(15),
        2,
      ),
    ];

    for (final _PageCase testCase in cases) {
      test(
        'Method forWindow resolves the prototype page measurements for ${testCase.$1.name} at ${testCase.$2}',
        () {
          final DovahPageMetrics metrics = DovahPageMetrics.forWindow(
            preset: testCase.$1,
            window: testCase.$2,
          );

          expect(metrics.sideMargin, isA<double>());
          expect(metrics.sideMargin, testCase.$3);
          expect(metrics.contentTopPadding, isA<double>());
          expect(metrics.contentTopPadding, testCase.$4);
          expect(metrics.introBottomGap, isA<double>());
          expect(metrics.introBottomGap, testCase.$5);
          expect(metrics.introTitleFontSize, isA<double>());
          expect(metrics.introTitleFontSize, testCase.$6);
          expect(metrics.introPadding, testCase.$7);
          expect(metrics.panelPadding, testCase.$8);
          expect(metrics.placeholderColumns, isA<int>());
          expect(metrics.placeholderColumns, testCase.$9);
        },
      );
    }

    test(
      'Method forWindow treats 900 wide and 620 tall as the last narrow and compact',
      () {
        final DovahPageMetrics edge = DovahPageMetrics.forWindow(
          preset: DovahThemePreset.dovah,
          window: const Size(900, 620),
        );
        final DovahPageMetrics past = DovahPageMetrics.forWindow(
          preset: DovahThemePreset.dovah,
          window: const Size(901, 621),
        );

        expect(edge.placeholderColumns, 2);
        expect(edge.introTitleFontSize, 26);
        expect(past.placeholderColumns, 3);
        expect(past.introTitleFontSize, 31);
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same resolved measurements', () {
      final DovahPageMetrics first = DovahPageMetrics.forWindow(
        preset: DovahThemePreset.dovah,
        window: const Size(1280, 720),
      );
      final DovahPageMetrics second = DovahPageMetrics.forWindow(
        preset: DovahThemePreset.hearth,
        window: const Size(1600, 900),
      );

      expect(first, isNot(second));
      expect(
        first,
        DovahPageMetrics.forWindow(
          preset: DovahThemePreset.dovah,
          window: const Size(1600, 900),
        ),
      );
    });
  });
}
