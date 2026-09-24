import 'package:flutter/painting.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';

/// One expected resolution: the preset and window it is resolved for, then the bar height,
/// navigation height, bar side margin, tab padding, and whether the first action is shown.
typedef _SessionCase = (
  DovahThemePreset,
  Size,
  double,
  double,
  double,
  double,
  bool,
);

/// Exercises [DovahSessionMetrics]'s prototype constants, per-theme tables, breakpoints, and
/// equality.
void main() {
  group('Property shared constants behave correctly', () {
    test('Property header bar constants keep the prototype values', () {
      expect(DovahSessionMetrics.barMaxWidth, isA<double>());
      expect(DovahSessionMetrics.barMaxWidth, 1240);
      expect(DovahSessionMetrics.barGap, isA<double>());
      expect(DovahSessionMetrics.barGap, 18);
      expect(DovahSessionMetrics.backGap, isA<double>());
      expect(DovahSessionMetrics.backGap, 8);
      expect(DovahSessionMetrics.backIconSize, isA<double>());
      expect(DovahSessionMetrics.backIconSize, 18);
      expect(DovahSessionMetrics.dividerWidth, isA<double>());
      expect(DovahSessionMetrics.dividerWidth, 1);
      expect(DovahSessionMetrics.dividerHeight, isA<double>());
      expect(DovahSessionMetrics.dividerHeight, 28);
    });

    test(
      'Property identity and status constants keep the prototype values',
      () {
        expect(DovahSessionMetrics.identityGap, isA<double>());
        expect(DovahSessionMetrics.identityGap, 11);
        expect(DovahSessionMetrics.glyphSize, isA<double>());
        expect(DovahSessionMetrics.glyphSize, 30);
        expect(DovahSessionMetrics.nameFontSize, isA<double>());
        expect(DovahSessionMetrics.nameFontSize, 14);
        expect(DovahSessionMetrics.metaFontSize, isA<double>());
        expect(DovahSessionMetrics.metaFontSize, 12);
        expect(DovahSessionMetrics.metaTopGap, isA<double>());
        expect(DovahSessionMetrics.metaTopGap, 2);
        expect(DovahSessionMetrics.statusGap, isA<double>());
        expect(DovahSessionMetrics.statusGap, 8);
        expect(DovahSessionMetrics.statusFontSize, isA<double>());
        expect(DovahSessionMetrics.statusFontSize, 12);
        expect(DovahSessionMetrics.actionsLeadingGap, isA<double>());
        expect(DovahSessionMetrics.actionsLeadingGap, 8);
        expect(DovahSessionMetrics.actionsGap, isA<double>());
        expect(DovahSessionMetrics.actionsGap, 7);
        expect(DovahSessionMetrics.actionButtonSize, isA<double>());
        expect(DovahSessionMetrics.actionButtonSize, 36);
      },
    );

    test('Property navigation constants keep the prototype values', () {
      expect(DovahSessionMetrics.tabGap, isA<double>());
      expect(DovahSessionMetrics.tabGap, 6);
      expect(DovahSessionMetrics.tabFontSize, isA<double>());
      expect(DovahSessionMetrics.tabFontSize, 14);
      expect(DovahSessionMetrics.tabIconSize, isA<double>());
      expect(DovahSessionMetrics.tabIconSize, 17);
      expect(DovahSessionMetrics.tabIconGap, isA<double>());
      expect(DovahSessionMetrics.tabIconGap, 8);
      expect(DovahSessionMetrics.tabUppercaseLetterSpacingEm, isA<double>());
      expect(DovahSessionMetrics.tabUppercaseLetterSpacingEm, 0.08);
      expect(DovahSessionMetrics.activeRuleHeight, isA<double>());
      expect(DovahSessionMetrics.activeRuleHeight, 2);
      expect(DovahSessionMetrics.activeRuleInset, isA<double>());
      expect(DovahSessionMetrics.activeRuleInset, 19);
      expect(DovahSessionMetrics.activeRuleGlowBlurRadius, isA<double>());
      expect(DovahSessionMetrics.activeRuleGlowBlurRadius, 14);
      expect(DovahSessionMetrics.navRuleColor, const Color(0xA6293640));
    });
  });

  group('Method forWindow behaves correctly', () {
    // Values are the prototype's measured computed styles at each window.
    const List<_SessionCase> cases = <_SessionCase>[
      (DovahThemePreset.frostbound, Size(1280, 720), 65, 45, 24, 22, true),
      (DovahThemePreset.dovah, Size(1280, 720), 65, 53, 24, 22, true),
      (DovahThemePreset.hearth, Size(1600, 900), 65, 53, 24, 22, true),
      (DovahThemePreset.frostbound, Size(800, 700), 65, 45, 14, 16, false),
      (DovahThemePreset.dovah, Size(800, 700), 65, 53, 14, 16, false),
      (DovahThemePreset.frostbound, Size(1000, 560), 54, 45, 24, 22, true),
      (DovahThemePreset.dovah, Size(1000, 560), 54, 45, 24, 22, true),
      (DovahThemePreset.hearth, Size(900, 560), 54, 45, 14, 16, false),
    ];

    for (final _SessionCase testCase in cases) {
      test(
        'Method forWindow resolves the prototype session measurements for ${testCase.$1.name} at ${testCase.$2}',
        () {
          final DovahSessionMetrics metrics = DovahSessionMetrics.forWindow(
            preset: testCase.$1,
            window: testCase.$2,
          );

          expect(metrics.barHeight, isA<double>());
          expect(metrics.barHeight, testCase.$3);
          expect(metrics.navHeight, isA<double>());
          expect(metrics.navHeight, testCase.$4);
          expect(metrics.barSideMargin, isA<double>());
          expect(metrics.barSideMargin, testCase.$5);
          expect(metrics.tabHorizontalPadding, isA<double>());
          expect(metrics.tabHorizontalPadding, testCase.$6);
          expect(metrics.showFirstAction, isA<bool>());
          expect(metrics.showFirstAction, testCase.$7);
        },
      );
    }

    test(
      'Method forWindow treats 900 wide and 620 tall as the last narrow and compact',
      () {
        final DovahSessionMetrics edge = DovahSessionMetrics.forWindow(
          preset: DovahThemePreset.dovah,
          window: const Size(900, 620),
        );
        final DovahSessionMetrics past = DovahSessionMetrics.forWindow(
          preset: DovahThemePreset.dovah,
          window: const Size(901, 621),
        );

        expect(edge.showFirstAction, isFalse);
        expect(edge.barHeight, 54);
        expect(past.showFirstAction, isTrue);
        expect(past.barHeight, 65);
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds across presets that share a table row', () {
      expect(
        DovahSessionMetrics.forWindow(
          preset: DovahThemePreset.dovah,
          window: const Size(1280, 720),
        ),
        DovahSessionMetrics.forWindow(
          preset: DovahThemePreset.hearth,
          window: const Size(1600, 900),
        ),
      );
    });

    test(
      'Behavior equality fails when Frostbound pins its navigation height',
      () {
        expect(
          DovahSessionMetrics.forWindow(
            preset: DovahThemePreset.frostbound,
            window: const Size(1280, 720),
          ),
          isNot(
            DovahSessionMetrics.forWindow(
              preset: DovahThemePreset.dovah,
              window: const Size(1280, 720),
            ),
          ),
        );
      },
    );
  });
}
