import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';

/// Exercises [DovahDialogMetrics]'s window-height resolution, prototype values, and equality.
void main() {
  group('Method forWindowHeight behaves correctly', () {
    test('Method forWindowHeight returns compact at the 620 breakpoint', () {
      expect(
        DovahDialogMetrics.forWindowHeight(620),
        DovahDialogMetrics.compact,
      );
    });

    test('Method forWindowHeight returns compact below the breakpoint', () {
      expect(
        DovahDialogMetrics.forWindowHeight(480),
        DovahDialogMetrics.compact,
      );
      expect(
        DovahDialogMetrics.forWindowHeight(560),
        DovahDialogMetrics.compact,
      );
    });

    test(
      'Method forWindowHeight returns regular just above the breakpoint',
      () {
        expect(
          DovahDialogMetrics.forWindowHeight(621),
          DovahDialogMetrics.regular,
        );
      },
    );

    test('Method forWindowHeight returns regular for a fraction above 620', () {
      expect(
        DovahDialogMetrics.forWindowHeight(620.0001),
        DovahDialogMetrics.regular,
      );
    });

    test('Method forWindowHeight returns regular on tall windows', () {
      expect(
        DovahDialogMetrics.forWindowHeight(720),
        DovahDialogMetrics.regular,
      );
      expect(
        DovahDialogMetrics.forWindowHeight(900),
        DovahDialogMetrics.regular,
      );
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
      expect(
        DovahDialogMetrics.forWindowHeight(500),
        DovahDialogMetrics.compact,
      );
      expect(
        DovahDialogMetrics.forWindowHeight(500).hashCode,
        DovahDialogMetrics.compact.hashCode,
      );
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
