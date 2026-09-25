import 'dart:ui' show Size;

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// One expected resolution: the preset and window it is resolved for, then the main and side
/// column flex, grid gap, hero minimum height, and stats top gap.
typedef _OverviewCase = (
  DovahThemePreset,
  Size,
  int,
  int,
  double,
  double,
  double,
);

/// Exercises [DovahOverviewMetrics]'s prototype constants, window resolution of each preset's theme metrics, breakpoints, and
/// equality.
void main() {
  group('Property hero and stats constants behave correctly', () {
    test('Property hero constants keep the prototype values', () {
      expect(DovahOverviewMetrics.panelAccentWidth, isA<double>());
      expect(DovahOverviewMetrics.panelAccentWidth, 3);
      expect(DovahOverviewMetrics.kickerFontSize, isA<double>());
      expect(DovahOverviewMetrics.kickerFontSize, 10);
      expect(DovahOverviewMetrics.kickerLetterSpacingEm, isA<double>());
      expect(DovahOverviewMetrics.kickerLetterSpacingEm, 0.16);
      expect(DovahOverviewMetrics.heroTitleFontSize, isA<double>());
      expect(DovahOverviewMetrics.heroTitleFontSize, 30);
      expect(DovahOverviewMetrics.heroTitleTopGap, isA<double>());
      expect(DovahOverviewMetrics.heroTitleTopGap, 7);
      expect(DovahOverviewMetrics.heroTitleBottomGap, isA<double>());
      expect(DovahOverviewMetrics.heroTitleBottomGap, 5);
      expect(DovahOverviewMetrics.heroDescriptionFontSize, isA<double>());
      expect(DovahOverviewMetrics.heroDescriptionFontSize, 13);
    });

    test('Property stat constants keep the prototype values', () {
      expect(DovahOverviewMetrics.statGap, isA<double>());
      expect(DovahOverviewMetrics.statGap, 8);
      expect(DovahOverviewMetrics.statTopPadding, isA<double>());
      expect(DovahOverviewMetrics.statTopPadding, 12);
      expect(DovahOverviewMetrics.statValueFontSize, isA<double>());
      expect(DovahOverviewMetrics.statValueFontSize, 21);
      expect(DovahOverviewMetrics.statLabelFontSize, isA<double>());
      expect(DovahOverviewMetrics.statLabelFontSize, 12);
    });
  });

  group('Property side panel constants behave correctly', () {
    test('Property panel title constants keep the prototype values', () {
      expect(DovahOverviewMetrics.panelTitleFontSize, isA<double>());
      expect(DovahOverviewMetrics.panelTitleFontSize, 13);
      expect(DovahOverviewMetrics.panelTitleNoteFontSize, isA<double>());
      expect(DovahOverviewMetrics.panelTitleNoteFontSize, 12);
      expect(DovahOverviewMetrics.panelTitleBottomGap, isA<double>());
      expect(DovahOverviewMetrics.panelTitleBottomGap, 14);
    });

    test('Property tracked quest constants keep the prototype values', () {
      expect(DovahOverviewMetrics.questIndent, isA<double>());
      expect(DovahOverviewMetrics.questIndent, 12);
      expect(DovahOverviewMetrics.questRuleWidth, isA<double>());
      expect(DovahOverviewMetrics.questRuleWidth, 2);
      expect(DovahOverviewMetrics.questTitleFontSize, isA<double>());
      expect(DovahOverviewMetrics.questTitleFontSize, 13);
      expect(DovahOverviewMetrics.questTitleBottomGap, isA<double>());
      expect(DovahOverviewMetrics.questTitleBottomGap, 5);
      expect(DovahOverviewMetrics.questDescriptionFontSize, isA<double>());
      expect(DovahOverviewMetrics.questDescriptionFontSize, 13);
      expect(DovahOverviewMetrics.questDescriptionLineHeight, isA<double>());
      expect(DovahOverviewMetrics.questDescriptionLineHeight, 1.4);
    });

    test('Property stat bar constants keep the prototype values', () {
      expect(DovahOverviewMetrics.barRowGap, isA<double>());
      expect(DovahOverviewMetrics.barRowGap, 9);
      expect(DovahOverviewMetrics.barColumnGap, isA<double>());
      expect(DovahOverviewMetrics.barColumnGap, 9);
      expect(DovahOverviewMetrics.barLabelWidth, isA<double>());
      expect(DovahOverviewMetrics.barLabelWidth, 55);
      expect(DovahOverviewMetrics.barValueWidth, isA<double>());
      expect(DovahOverviewMetrics.barValueWidth, 34);
      expect(DovahOverviewMetrics.barFontSize, isA<double>());
      expect(DovahOverviewMetrics.barFontSize, 12);
      expect(DovahOverviewMetrics.barHeight, isA<double>());
      expect(DovahOverviewMetrics.barHeight, 5);
      expect(DovahOverviewMetrics.barRadius, isA<double>());
      expect(DovahOverviewMetrics.barRadius, 5);
    });
  });

  group('Method forWindow behaves correctly', () {
    // Values are the prototype's measured computed styles at each window; the hero height is its
    // declared min-height.
    const List<_OverviewCase> cases = <_OverviewCase>[
      (DovahThemePreset.frostbound, Size(1280, 720), 125, 75, 10, 226, 14),
      (DovahThemePreset.dovah, Size(1280, 720), 125, 75, 14, 270, 20),
      (DovahThemePreset.hearth, Size(1600, 900), 125, 75, 14, 278, 20),
      (DovahThemePreset.frostbound, Size(800, 700), 115, 85, 10, 226, 14),
      (DovahThemePreset.dovah, Size(800, 700), 115, 85, 14, 270, 20),
      (DovahThemePreset.frostbound, Size(1000, 560), 125, 75, 10, 205, 14),
      (DovahThemePreset.dovah, Size(1000, 560), 125, 75, 14, 210, 14),
      (DovahThemePreset.hearth, Size(1000, 560), 125, 75, 14, 215, 14),
      (DovahThemePreset.hearth, Size(900, 560), 115, 85, 14, 215, 14),
    ];

    for (final _OverviewCase testCase in cases) {
      test(
        'Method forWindow resolves the prototype overview measurements for ${testCase.$1.name} at ${testCase.$2}',
        () {
          final DovahOverviewMetrics metrics = DovahOverviewMetrics.forWindow(
            themeMetrics: dovahThemeDataFor(
              testCase.$1,
            ).extension<DovahOverviewThemeMetrics>()!,
            window: testCase.$2,
          );

          expect(metrics.mainColumnFlex, isA<int>());
          expect(metrics.mainColumnFlex, testCase.$3);
          expect(metrics.sideColumnFlex, isA<int>());
          expect(metrics.sideColumnFlex, testCase.$4);
          expect(metrics.gridGap, isA<double>());
          expect(metrics.gridGap, testCase.$5);
          expect(metrics.heroMinHeight, isA<double>());
          expect(metrics.heroMinHeight, testCase.$6);
          expect(metrics.statsTopGap, isA<double>());
          expect(metrics.statsTopGap, testCase.$7);
        },
      );
    }

    test(
      'Method forWindow treats 900 wide and 620 tall as the last narrow and compact',
      () {
        final DovahOverviewMetrics edge = DovahOverviewMetrics.forWindow(
          themeMetrics: DovahOverviewThemeMetrics.dovah,
          window: const Size(900, 620),
        );
        final DovahOverviewMetrics past = DovahOverviewMetrics.forWindow(
          themeMetrics: DovahOverviewThemeMetrics.dovah,
          window: const Size(901, 621),
        );

        expect(edge.mainColumnFlex, 115);
        expect(edge.heroMinHeight, 210);
        expect(past.mainColumnFlex, 125);
        expect(past.heroMinHeight, 270);
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same resolved measurements', () {
      expect(
        DovahOverviewMetrics.forWindow(
          themeMetrics: DovahOverviewThemeMetrics.dovah,
          window: const Size(1280, 720),
        ),
        DovahOverviewMetrics.forWindow(
          themeMetrics: DovahOverviewThemeMetrics.dovah,
          window: const Size(1600, 900),
        ),
      );
    });

    test('Behavior equality fails between different presets', () {
      expect(
        DovahOverviewMetrics.forWindow(
          themeMetrics: DovahOverviewThemeMetrics.dovah,
          window: const Size(1280, 720),
        ),
        isNot(
          DovahOverviewMetrics.forWindow(
            themeMetrics: DovahOverviewThemeMetrics.hearth,
            window: const Size(1280, 720),
          ),
        ),
      );
    });
  });
}
