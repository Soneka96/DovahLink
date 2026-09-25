import 'package:flutter/painting.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/dovah_appearance_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_appearance_theme_metrics.dart';

/// Exercises [DovahAppearanceMetrics]'s prototype constants, window resolution, and equality.
void main() {
  group('Property shared constants behave correctly', () {
    test('Property shared constants keep the prototype card values', () {
      expect(DovahAppearanceMetrics.columnCount, isA<int>());
      expect(DovahAppearanceMetrics.columnCount, 3);
      expect(DovahAppearanceMetrics.titleFontSize, isA<double>());
      expect(DovahAppearanceMetrics.titleFontSize, 15);
      expect(DovahAppearanceMetrics.summaryFontSize, 12);
      expect(DovahAppearanceMetrics.detailFontSize, 10);
      expect(DovahAppearanceMetrics.copyLineHeight, isA<double>());
      expect(DovahAppearanceMetrics.copyLineHeight, 1.35);
      expect(DovahAppearanceMetrics.copyGap, 3);
      expect(DovahAppearanceMetrics.selectedRingWidth, 2);
      expect(DovahAppearanceMetrics.hoverOffset, const Offset(0, -2));
      expect(DovahAppearanceMetrics.badgeSize, 23);
      expect(DovahAppearanceMetrics.badgeInset, 9);
      expect(DovahAppearanceMetrics.badgeGlyphSize, 13);
    });

    test('Property shared constants keep the prototype preview values', () {
      expect(DovahAppearanceMetrics.previewAccentHeight, isA<double>());
      expect(DovahAppearanceMetrics.previewAccentHeight, 6);
      expect(DovahAppearanceMetrics.previewSigilSize, 45);
      expect(DovahAppearanceMetrics.previewSigilPadding, 7);
      expect(DovahAppearanceMetrics.previewBarsInset, 12);
      expect(DovahAppearanceMetrics.previewBarsBottom, 10);
      expect(DovahAppearanceMetrics.previewBarsGap, 4);
      expect(DovahAppearanceMetrics.previewBarFlexes, [140, 80, 45]);
    });

    test(
      'Property shared constants keep the prototype introduction values',
      () {
        expect(DovahAppearanceMetrics.introTitleFontSize, isA<double>());
        expect(DovahAppearanceMetrics.introTitleFontSize, 16);
        expect(DovahAppearanceMetrics.introBodyFontSize, 13);
        expect(DovahAppearanceMetrics.introBodyLineHeight, 1.4);
        expect(DovahAppearanceMetrics.introGap, 4);
      },
    );
  });

  group('Method forWindow behaves correctly', () {
    for (final (
          Size window,
          double preview,
          double copy,
          double gap,
          double intro,
          bool detail,
        )
        in [
          (const Size(1280, 720), 112.0, 13.0, 11.0, 16.0, true),
          (const Size(900, 621), 112.0, 13.0, 11.0, 16.0, true),
          (const Size(900, 620), 78.0, 9.0, 8.0, 10.0, false),
          (const Size(720, 480), 78.0, 9.0, 8.0, 10.0, false),
        ]) {
      test('Method forWindow resolves the window measurements at $window', () {
        final DovahAppearanceMetrics metrics = DovahAppearanceMetrics.forWindow(
          themeMetrics: DovahAppearanceThemeMetrics.dovah,
          window: window,
        );

        expect(metrics.previewHeight, isA<double>());
        expect(metrics.previewHeight, preview);
        expect(metrics.copyPadding, copy);
        expect(metrics.gridGap, gap);
        expect(metrics.introBottomGap, intro);
        expect(metrics.showDetail, isA<bool>());
        expect(metrics.showDetail, detail);
      });
    }

    for (final (DovahAppearanceThemeMetrics theme, double cut, double radius)
        in [
          (DovahAppearanceThemeMetrics.frostbound, 9.0, 0.0),
          (DovahAppearanceThemeMetrics.dovah, 10.0, 0.0),
          (DovahAppearanceThemeMetrics.hearth, 0.0, 13.0),
        ]) {
      for (final Size window in const [Size(1280, 720), Size(900, 560)]) {
        test('Method forWindow carries the theme card outline at $window', () {
          final DovahAppearanceMetrics metrics =
              DovahAppearanceMetrics.forWindow(
                themeMetrics: theme,
                window: window,
              );

          expect(metrics.cornerCutSize, cut);
          expect(metrics.cornerRadius, radius);
        });
      }
    }

    test('Method forWindow follows a mid-transition theme outline', () {
      final DovahAppearanceMetrics metrics = DovahAppearanceMetrics.forWindow(
        themeMetrics: DovahAppearanceThemeMetrics.dovah.lerp(
          DovahAppearanceThemeMetrics.hearth,
          0.5,
        ),
        window: const Size(1280, 720),
      );

      expect(metrics.cornerCutSize, 5);
      expect(metrics.cornerRadius, 6.5);
    });
  });

  group('Method columnsFor behaves correctly', () {
    DovahAppearanceMetrics metricsAt(double height) =>
        DovahAppearanceMetrics.forWindow(
          themeMetrics: DovahAppearanceThemeMetrics.dovah,
          window: Size(1280, height),
        );

    test('Property cardMinimumWidth is the 160px readability floor', () {
      expect(DovahAppearanceMetrics.cardMinimumWidth, isA<double>());
      expect(DovahAppearanceMetrics.cardMinimumWidth, 160);
    });

    test('Method columnsFor gives three columns at desktop widths', () {
      expect(metricsAt(720).columnsFor(589), 3);
      expect(metricsAt(720).columnsFor(2000), 3);
    });

    test(
      'Method columnsFor keeps three columns down to three minimum widths and gaps',
      () {
        // 3 * 160 + 2 * 11 = 502
        expect(metricsAt(720).columnsFor(502), 3);
        expect(metricsAt(720).columnsFor(501.9), 2);
      },
    );

    test(
      'Method columnsFor gives two columns and then one as the width narrows',
      () {
        // 2 * 160 + 11 = 331
        expect(metricsAt(720).columnsFor(331), 2);
        expect(metricsAt(720).columnsFor(330.9), 1);
        expect(metricsAt(720).columnsFor(100), 1);
        expect(metricsAt(720).columnsFor(0), 1);
      },
    );

    test('Method columnsFor uses the compact gap at compact heights', () {
      // 3 * 160 + 2 * 8 = 496
      expect(metricsAt(560).columnsFor(496), 3);
      expect(metricsAt(560).columnsFor(495.9), 2);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same resolved measurements', () {
      final DovahAppearanceMetrics first = DovahAppearanceMetrics.forWindow(
        themeMetrics: DovahAppearanceThemeMetrics.dovah,
        window: const Size(1280, 720),
      );
      final DovahAppearanceMetrics second = DovahAppearanceMetrics.forWindow(
        themeMetrics: DovahAppearanceThemeMetrics.dovah,
        window: const Size(1600, 900),
      );

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality fails between regular and compact', () {
      expect(
        DovahAppearanceMetrics.forWindow(
          themeMetrics: DovahAppearanceThemeMetrics.dovah,
          window: const Size(1280, 720),
        ),
        isNot(
          DovahAppearanceMetrics.forWindow(
            themeMetrics: DovahAppearanceThemeMetrics.dovah,
            window: const Size(1280, 500),
          ),
        ),
      );
    });

    test('Behavior equality fails between themes', () {
      expect(
        DovahAppearanceMetrics.forWindow(
          themeMetrics: DovahAppearanceThemeMetrics.dovah,
          window: const Size(1280, 720),
        ),
        isNot(
          DovahAppearanceMetrics.forWindow(
            themeMetrics: DovahAppearanceThemeMetrics.hearth,
            window: const Size(1280, 720),
          ),
        ),
      );
    });
  });
}
