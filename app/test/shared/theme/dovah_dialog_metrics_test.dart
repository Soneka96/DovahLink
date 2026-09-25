import 'package:flutter/painting.dart' show Size;

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_theme_metrics.dart';

/// Exercises [DovahDialogMetrics]'s window and theme resolution, prototype values, and equality.
void main() {
  /// Resolves the metrics of a Dovah window [height] tall, whose marks and code boxes are square.
  DovahDialogMetrics resolve(double height) => DovahDialogMetrics.forWindow(
    themeMetrics: DovahDialogThemeMetrics.dovah,
    window: Size(1280, height),
  );

  group('Method forWindow behaves correctly', () {
    test('Method forWindow returns compact at the 620 breakpoint', () {
      expect(resolve(620), DovahDialogMetrics.compact);
    });

    test('Method forWindow returns compact below the breakpoint', () {
      expect(resolve(480), DovahDialogMetrics.compact);
      expect(resolve(560), DovahDialogMetrics.compact);
    });

    test('Method forWindow returns regular just above the breakpoint', () {
      expect(resolve(621), DovahDialogMetrics.regular);
    });

    test('Method forWindow returns regular for a fraction above 620', () {
      expect(resolve(620.0001), DovahDialogMetrics.regular);
    });

    test('Method forWindow returns regular on tall windows', () {
      expect(resolve(720), DovahDialogMetrics.regular);
      expect(resolve(900), DovahDialogMetrics.regular);
    });

    for (final (
          DovahDialogThemeMetrics theme,
          String name,
          double regular,
          double compact,
          double codeBox,
        )
        in [
          (DovahDialogThemeMetrics.frostbound, 'frostbound', 0.0, 0.0, 0.0),
          (DovahDialogThemeMetrics.dovah, 'dovah', 0.0, 0.0, 0.0),
          (DovahDialogThemeMetrics.hearth, 'hearth', 27.0, 21.0, 9.0),
        ]) {
      test(
        'Method forWindow resolves the $name corner radii per window mode',
        () {
          final DovahDialogMetrics tall = DovahDialogMetrics.forWindow(
            themeMetrics: theme,
            window: const Size(1280, 720),
          );
          final DovahDialogMetrics short = DovahDialogMetrics.forWindow(
            themeMetrics: theme,
            window: const Size(1280, 560),
          );

          expect(tall.markCornerRadius, isA<double>());
          expect(tall.markCornerRadius, regular);
          expect(short.markCornerRadius, compact);
          expect(tall.codeBoxCornerRadius, isA<double>());
          expect(tall.codeBoxCornerRadius, codeBox);
          expect(short.codeBoxCornerRadius, codeBox);
        },
      );
    }

    test(
      'Method forWindow keeps the window measurements whatever the theme',
      () {
        final DovahDialogMetrics hearth = DovahDialogMetrics.forWindow(
          themeMetrics: DovahDialogThemeMetrics.hearth,
          window: const Size(1280, 720),
        );

        expect(hearth.markSize, DovahDialogMetrics.regular.markSize);
        expect(hearth.codeBoxWidth, DovahDialogMetrics.regular.codeBoxWidth);
      },
    );
  });

  group('Method withCornerRadii behaves correctly', () {
    test('Method withCornerRadii replaces only the two corner radii', () {
      final DovahDialogMetrics changed = DovahDialogMetrics.regular
          .withCornerRadii(markCornerRadius: 5, codeBoxCornerRadius: 6);

      expect(changed.markCornerRadius, 5);
      expect(changed.codeBoxCornerRadius, 6);
      expect(changed.markSize, DovahDialogMetrics.regular.markSize);
      expect(changed.headerVerticalPadding, 19);
      expect(changed, isNot(DovahDialogMetrics.regular));
    });
  });

  group('Property compactMaxWindowHeight behaves correctly', () {
    test('Property compactMaxWindowHeight is the prototype 620 breakpoint', () {
      expect(DovahDialogMetrics.compactMaxWindowHeight, isA<double>());
      expect(DovahDialogMetrics.compactMaxWindowHeight, 620);
    });
  });

  group('Property regular behaves correctly', () {
    test('Property regular keeps the prototype normal-height measurements', () {
      const DovahDialogMetrics metrics = DovahDialogMetrics.regular;

      expect(metrics.headerVerticalPadding, 19);
      expect(metrics.headerHorizontalPadding, 22);
      expect(metrics.bodyVerticalPadding, 22);
      expect(metrics.bodyHorizontalPadding, 22);
      expect(metrics.heightFraction, 0.86);
      expect(metrics.markSize, 54);
      expect(metrics.markIconSize, 24);
      expect(metrics.markBottomGap, 16);
      expect(metrics.headingFontSize, 24);
      expect(metrics.headingBottomGap, 8);
      expect(metrics.bodyLineHeight, 1.5);
      expect(metrics.bodyBottomGap, 20);
      expect(metrics.codeBoxWidth, 49);
      expect(metrics.codeBoxHeight, 56);
      expect(metrics.codeRowTopGap, 6);
      expect(metrics.codeRowBottomGap, 12);
      expect(metrics.messageMinHeight, 18);
      expect(metrics.actionsTopGap, 18);
      expect(metrics.noteTopGap, 17);
    });
  });

  group('Property compact behaves correctly', () {
    test('Property compact keeps the prototype short-window measurements', () {
      const DovahDialogMetrics metrics = DovahDialogMetrics.compact;

      expect(metrics.headerVerticalPadding, 12);
      expect(metrics.headerHorizontalPadding, 19);
      expect(metrics.bodyVerticalPadding, 13);
      expect(metrics.bodyHorizontalPadding, 17);
      expect(metrics.heightFraction, 0.92);
      expect(metrics.markSize, 42);
      expect(metrics.markIconSize, 20);
      expect(metrics.markBottomGap, 8);
      expect(metrics.headingFontSize, 22);
      expect(metrics.headingBottomGap, 5);
      expect(metrics.bodyLineHeight, 1.35);
      expect(metrics.bodyBottomGap, 10);
      expect(metrics.codeBoxWidth, 45);
      expect(metrics.codeBoxHeight, 48);
      expect(metrics.codeRowTopGap, 3);
      expect(metrics.codeRowBottomGap, 6);
      expect(metrics.messageMinHeight, 14);
      expect(metrics.actionsTopGap, 7);
      expect(metrics.noteTopGap, 8);
    });
  });

  group('Property shared dialog and pairing constants behave correctly', () {
    test(
      'Property shared dialog constants keep the prototype modal values',
      () {
        expect(DovahDialogMetrics.backdropPadding, isA<double>());
        expect(DovahDialogMetrics.backdropPadding, 24);
        expect(DovahDialogMetrics.maxWidth, isA<double>());
        expect(DovahDialogMetrics.maxWidth, 720);
        expect(DovahDialogMetrics.widthFraction, isA<double>());
        expect(DovahDialogMetrics.widthFraction, 0.88);
        expect(DovahDialogMetrics.titleFontSize, isA<double>());
        expect(DovahDialogMetrics.titleFontSize, 23);
        expect(DovahDialogMetrics.actionGap, isA<double>());
        expect(DovahDialogMetrics.actionGap, 10);
      },
    );

    test('Property shared pairing constants keep the prototype values', () {
      expect(DovahDialogMetrics.codeBoxGap, isA<double>());
      expect(DovahDialogMetrics.codeBoxGap, 8);
      expect(DovahDialogMetrics.codeBoxFontSize, isA<double>());
      expect(DovahDialogMetrics.codeBoxFontSize, 22);
      expect(DovahDialogMetrics.codeBoxFocusRingWidth, isA<double>());
      expect(DovahDialogMetrics.codeBoxFocusRingWidth, 3);
      expect(DovahDialogMetrics.messageFontSize, isA<double>());
      expect(DovahDialogMetrics.messageFontSize, 12);
      expect(DovahDialogMetrics.contentMaxWidth, isA<double>());
      expect(DovahDialogMetrics.contentMaxWidth, 520);
      expect(DovahDialogMetrics.bodyMaxWidth, isA<double>());
      expect(DovahDialogMetrics.bodyMaxWidth, 430);
      expect(DovahDialogMetrics.bodyFontSize, isA<double>());
      expect(DovahDialogMetrics.bodyFontSize, 14);
      expect(DovahDialogMetrics.noteFontSize, isA<double>());
      expect(DovahDialogMetrics.noteFontSize, 12);
    });

    test(
      'Property shared success and progress constants keep prototype values',
      () {
        expect(DovahDialogMetrics.successMarkSize, isA<double>());
        expect(DovahDialogMetrics.successMarkSize, 62);
        expect(DovahDialogMetrics.successMarkBottomGap, isA<double>());
        expect(DovahDialogMetrics.successMarkBottomGap, 17);
        expect(DovahDialogMetrics.successGlyphSize, isA<double>());
        expect(DovahDialogMetrics.successGlyphSize, 29);
        expect(DovahDialogMetrics.statusMarkFillOpacity, isA<double>());
        expect(DovahDialogMetrics.statusMarkFillOpacity, 0.1);
        expect(DovahDialogMetrics.statusMarkBorderOpacity, isA<double>());
        expect(DovahDialogMetrics.statusMarkBorderOpacity, 0.36);
        expect(DovahDialogMetrics.progressIndicatorSize, isA<double>());
        expect(DovahDialogMetrics.progressIndicatorSize, 15);
        expect(DovahDialogMetrics.progressIndicatorStrokeWidth, isA<double>());
        expect(DovahDialogMetrics.progressIndicatorStrokeWidth, 2);
        expect(DovahDialogMetrics.progressStatusGap, isA<double>());
        expect(DovahDialogMetrics.progressStatusGap, 10);
      },
    );
  });

  group('Property codeRowWidth behaves correctly', () {
    test('Property codeRowWidth spans six regular boxes and five gaps', () {
      expect(DovahDialogMetrics.regular.codeRowWidth, 6 * 49 + 5 * 8);
    });

    test('Property codeRowWidth spans six compact boxes and five gaps', () {
      expect(DovahDialogMetrics.compact.codeRowWidth, 6 * 45 + 5 * 8);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same measurement set', () {
      expect(resolve(500), DovahDialogMetrics.compact);
      expect(resolve(500).hashCode, DovahDialogMetrics.compact.hashCode);
    });

    test('Behavior equality fails between regular and compact', () {
      expect(DovahDialogMetrics.regular, isNot(DovahDialogMetrics.compact));
      expect(
        DovahDialogMetrics.regular.hashCode,
        isNot(DovahDialogMetrics.compact.hashCode),
      );
    });
  });
}
