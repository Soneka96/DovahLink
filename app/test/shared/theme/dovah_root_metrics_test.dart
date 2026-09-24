import 'dart:ui' show Size;

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// One expected resolution: the preset and window it is resolved for, then the side margin,
/// header height, content top padding, hero bottom gap, title size, title top gap, tagline letter
/// spacing, and whether the footer is shown.
typedef _RootCase = (
  DovahThemePreset,
  Size,
  double,
  double,
  double,
  double,
  double,
  double,
  double,
  bool,
);

/// Exercises [DovahRootMetrics]'s prototype constants, breakpoints, window resolution of each
/// preset's theme metrics, and equality.
void main() {
  group('Property breakpoints behave correctly', () {
    test(
      'Property breakpoints are the prototype 900 wide and 620 tall queries',
      () {
        expect(DovahRootMetrics.narrowMaxWindowWidth, isA<double>());
        expect(DovahRootMetrics.narrowMaxWindowWidth, 900);
        expect(DovahRootMetrics.compactMaxWindowHeight, isA<double>());
        expect(DovahRootMetrics.compactMaxWindowHeight, 620);
      },
    );
  });

  group('Property shared constants behave correctly', () {
    test('Property shell constants keep the prototype values', () {
      expect(DovahRootMetrics.minimumWidth, isA<double>());
      expect(DovahRootMetrics.minimumWidth, 720);
      expect(DovahRootMetrics.contentMaxWidth, isA<double>());
      expect(DovahRootMetrics.contentMaxWidth, 1180);
      expect(DovahRootMetrics.contentBottomPadding, isA<double>());
      expect(DovahRootMetrics.contentBottomPadding, 40);
      expect(DovahRootMetrics.headerRuleHeight, isA<double>());
      expect(DovahRootMetrics.headerRuleHeight, 2);
      expect(DovahRootMetrics.headerBackgroundOpacity, isA<double>());
      expect(DovahRootMetrics.headerBackgroundOpacity, 0.88);
      expect(DovahRootMetrics.headerBlurSigma, isA<double>());
      expect(DovahRootMetrics.headerBlurSigma, 11);
    });

    test('Property brand constants keep the prototype values', () {
      expect(DovahRootMetrics.brandMarkSize, isA<double>());
      expect(DovahRootMetrics.brandMarkSize, 44);
      expect(DovahRootMetrics.brandGap, isA<double>());
      expect(DovahRootMetrics.brandGap, 13);
      expect(DovahRootMetrics.brandNameFontSize, isA<double>());
      expect(DovahRootMetrics.brandNameFontSize, 18);
      expect(DovahRootMetrics.brandNameLetterSpacingEm, isA<double>());
      expect(DovahRootMetrics.brandNameLetterSpacingEm, 0.15);
      expect(DovahRootMetrics.brandTaglineFontSize, isA<double>());
      expect(DovahRootMetrics.brandTaglineFontSize, 9);
      expect(DovahRootMetrics.brandTaglineTopGap, isA<double>());
      expect(DovahRootMetrics.brandTaglineTopGap, 3);
    });

    test('Property title-block constants keep the prototype values', () {
      expect(DovahRootMetrics.heroGap, isA<double>());
      expect(DovahRootMetrics.heroGap, 20);
      expect(DovahRootMetrics.eyebrowFontSize, isA<double>());
      expect(DovahRootMetrics.eyebrowFontSize, 10);
      expect(DovahRootMetrics.eyebrowLetterSpacingEm, isA<double>());
      expect(DovahRootMetrics.eyebrowLetterSpacingEm, 0.2);
      expect(DovahRootMetrics.pageTitleLetterSpacingEm, isA<double>());
      expect(DovahRootMetrics.pageTitleLetterSpacingEm, 0.02);
      expect(DovahRootMetrics.pageTitleUppercaseLetterSpacingEm, isA<double>());
      expect(DovahRootMetrics.pageTitleUppercaseLetterSpacingEm, 0.06);
      expect(DovahRootMetrics.pageTitleBottomGap, isA<double>());
      expect(DovahRootMetrics.pageTitleBottomGap, 5);
      expect(DovahRootMetrics.pageDescriptionFontSize, isA<double>());
      expect(DovahRootMetrics.pageDescriptionFontSize, 14);
    });

    test('Property section and footer constants keep the prototype values', () {
      expect(DovahRootMetrics.sectionLabelFontSize, isA<double>());
      expect(DovahRootMetrics.sectionLabelFontSize, 11);
      expect(DovahRootMetrics.sectionLabelLetterSpacingEm, isA<double>());
      expect(DovahRootMetrics.sectionLabelLetterSpacingEm, 0.15);
      expect(DovahRootMetrics.sectionLabelGap, isA<double>());
      expect(DovahRootMetrics.sectionLabelGap, 10);
      expect(DovahRootMetrics.sectionLabelBottomGap, isA<double>());
      expect(DovahRootMetrics.sectionLabelBottomGap, 11);
      expect(DovahRootMetrics.listGap, isA<double>());
      expect(DovahRootMetrics.listGap, 10);
      expect(DovahRootMetrics.footerFontSize, isA<double>());
      expect(DovahRootMetrics.footerFontSize, 12);
      expect(DovahRootMetrics.footerTopGap, isA<double>());
      expect(DovahRootMetrics.footerTopGap, 18);
    });
  });

  group('Method forWindow behaves correctly', () {
    // Values are the prototype's measured computed styles at each window.
    const List<_RootCase> cases = <_RootCase>[
      (
        DovahThemePreset.frostbound,
        Size(1280, 720),
        32,
        70,
        20,
        18,
        31,
        7,
        0.24,
        true,
      ),
      (
        DovahThemePreset.dovah,
        Size(1280, 720),
        32,
        88,
        30,
        28,
        34,
        7,
        0.2,
        true,
      ),
      (
        DovahThemePreset.hearth,
        Size(1280, 720),
        32,
        86,
        30,
        28,
        38,
        7,
        0.14,
        true,
      ),
      (
        DovahThemePreset.frostbound,
        Size(1600, 900),
        32,
        70,
        20,
        18,
        31,
        7,
        0.24,
        true,
      ),
      (
        DovahThemePreset.dovah,
        Size(800, 700),
        18,
        88,
        20,
        20,
        28,
        7,
        0.2,
        true,
      ),
      (
        DovahThemePreset.hearth,
        Size(800, 700),
        18,
        86,
        20,
        20,
        38,
        7,
        0.14,
        true,
      ),
      (
        DovahThemePreset.frostbound,
        Size(800, 700),
        18,
        70,
        20,
        18,
        31,
        7,
        0.24,
        true,
      ),
      (
        DovahThemePreset.dovah,
        Size(1000, 560),
        32,
        62,
        14,
        14,
        25,
        4,
        0.2,
        false,
      ),
      (
        DovahThemePreset.frostbound,
        Size(900, 560),
        18,
        56,
        20,
        18,
        31,
        4,
        0.24,
        false,
      ),
      (
        DovahThemePreset.dovah,
        Size(900, 560),
        18,
        62,
        14,
        14,
        25,
        4,
        0.2,
        false,
      ),
      (
        DovahThemePreset.hearth,
        Size(900, 560),
        18,
        62,
        14,
        14,
        38,
        4,
        0.14,
        false,
      ),
      (
        DovahThemePreset.hearth,
        Size(720, 480),
        18,
        62,
        14,
        14,
        38,
        4,
        0.14,
        false,
      ),
    ];

    for (final _RootCase testCase in cases) {
      test(
        'Method forWindow resolves the prototype measurements for ${testCase.$1.name} at ${testCase.$2}',
        () {
          final DovahRootMetrics metrics = DovahRootMetrics.forWindow(
            themeMetrics: dovahThemeDataFor(
              testCase.$1,
            ).extension<DovahRootThemeMetrics>()!,
            window: testCase.$2,
          );

          expect(metrics.sideMargin, isA<double>());
          expect(metrics.sideMargin, testCase.$3);
          expect(metrics.headerHeight, isA<double>());
          expect(metrics.headerHeight, testCase.$4);
          expect(metrics.contentTopPadding, isA<double>());
          expect(metrics.contentTopPadding, testCase.$5);
          expect(metrics.heroBottomGap, isA<double>());
          expect(metrics.heroBottomGap, testCase.$6);
          expect(metrics.pageTitleFontSize, isA<double>());
          expect(metrics.pageTitleFontSize, testCase.$7);
          expect(metrics.pageTitleTopGap, isA<double>());
          expect(metrics.pageTitleTopGap, testCase.$8);
          expect(metrics.brandTaglineLetterSpacingEm, isA<double>());
          expect(metrics.brandTaglineLetterSpacingEm, testCase.$9);
          expect(metrics.showFooter, isA<bool>());
          expect(metrics.showFooter, testCase.$10);
        },
      );
    }

    test('Method forWindow treats a 900 wide window as narrow', () {
      final DovahRootMetrics narrow = DovahRootMetrics.forWindow(
        themeMetrics: DovahRootThemeMetrics.dovah,
        window: const Size(900, 720),
      );
      final DovahRootMetrics wide = DovahRootMetrics.forWindow(
        themeMetrics: DovahRootThemeMetrics.dovah,
        window: const Size(901, 720),
      );

      expect(narrow.sideMargin, 18);
      expect(narrow.pageTitleFontSize, 28);
      expect(wide.sideMargin, 32);
      expect(wide.pageTitleFontSize, 34);
    });

    test('Method forWindow treats a 620 tall window as compact', () {
      final DovahRootMetrics compact = DovahRootMetrics.forWindow(
        themeMetrics: DovahRootThemeMetrics.dovah,
        window: const Size(1280, 620),
      );
      final DovahRootMetrics regular = DovahRootMetrics.forWindow(
        themeMetrics: DovahRootThemeMetrics.dovah,
        window: const Size(1280, 621),
      );

      expect(compact.headerHeight, 62);
      expect(compact.showFooter, isFalse);
      expect(regular.headerHeight, 88);
      expect(regular.showFooter, isTrue);
    });

    test(
      'Method forWindow lets the compact height override the narrow width',
      () {
        final DovahRootMetrics metrics = DovahRootMetrics.forWindow(
          themeMetrics: DovahRootThemeMetrics.dovah,
          window: const Size(720, 480),
        );

        expect(metrics.pageTitleFontSize, 25);
        expect(metrics.contentTopPadding, 14);
      },
    );

    test('Method forWindow applies both breakpoints at exactly 900 by 620', () {
      final DovahRootMetrics metrics = DovahRootMetrics.forWindow(
        themeMetrics: DovahRootThemeMetrics.dovah,
        window: const Size(900, 620),
      );

      expect(metrics.sideMargin, 18);
      expect(metrics.headerHeight, 62);
      expect(metrics.pageTitleFontSize, 25);
      expect(metrics.showFooter, isFalse);
    });

    test(
      'Method forWindow resolves each window mode from mid-transition theme metrics',
      () {
        final DovahRootThemeMetrics mid = DovahRootThemeMetrics.dovah.lerp(
          DovahRootThemeMetrics.hearth,
          0.5,
        );

        final DovahRootMetrics regular = DovahRootMetrics.forWindow(
          themeMetrics: mid,
          window: const Size(1280, 720),
        );
        final DovahRootMetrics narrow = DovahRootMetrics.forWindow(
          themeMetrics: mid,
          window: const Size(800, 700),
        );
        final DovahRootMetrics compact = DovahRootMetrics.forWindow(
          themeMetrics: mid,
          window: const Size(1280, 560),
        );

        expect(regular.pageTitleFontSize, 36);
        expect(regular.headerHeight, 87);
        expect(narrow.pageTitleFontSize, 33);
        expect(narrow.headerHeight, 87);
        expect(compact.pageTitleFontSize, 31.5);
        expect(compact.headerHeight, 62);
        expect(compact.contentTopPadding, 14);
        expect(regular.brandTaglineLetterSpacingEm, closeTo(0.17, 1e-9));
        expect(compact.brandTaglineLetterSpacingEm, closeTo(0.17, 1e-9));
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same resolved measurements', () {
      final DovahRootMetrics first = DovahRootMetrics.forWindow(
        themeMetrics: DovahRootThemeMetrics.hearth,
        window: const Size(1280, 720),
      );
      final DovahRootMetrics second = DovahRootMetrics.forWindow(
        themeMetrics: DovahRootThemeMetrics.hearth,
        window: const Size(1600, 900),
      );

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality fails between different theme metrics', () {
      final DovahRootMetrics first = DovahRootMetrics.forWindow(
        themeMetrics: DovahRootThemeMetrics.hearth,
        window: const Size(1280, 720),
      );
      final DovahRootMetrics second = DovahRootMetrics.forWindow(
        themeMetrics: DovahRootThemeMetrics.dovah,
        window: const Size(1280, 720),
      );

      expect(first, isNot(second));
    });
  });
}
