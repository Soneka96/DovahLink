import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';

/// Exercises the prototype values [DovahControlMetrics] pins for themed controls.
void main() {
  group('Property button geometry behaves correctly', () {
    test('Property button geometry keeps the prototype .primary values', () {
      expect(DovahControlMetrics.buttonVerticalPadding, isA<double>());
      expect(DovahControlMetrics.buttonVerticalPadding, 12);
      expect(DovahControlMetrics.buttonHorizontalPadding, isA<double>());
      expect(DovahControlMetrics.buttonHorizontalPadding, 17);
      expect(DovahControlMetrics.buttonFontSize, isA<double>());
      expect(DovahControlMetrics.buttonFontSize, 16);
      expect(DovahControlMetrics.buttonIconSize, isA<double>());
      expect(DovahControlMetrics.buttonIconSize, 17);
      expect(DovahControlMetrics.buttonIconGap, isA<double>());
      expect(DovahControlMetrics.buttonIconGap, 9);
    });
  });

  group('Property icon button geometry behaves correctly', () {
    test(
      'Property icon button geometry keeps the prototype .icon-btn values',
      () {
        expect(DovahControlMetrics.iconButtonSize, isA<double>());
        expect(DovahControlMetrics.iconButtonSize, 40);
        expect(DovahControlMetrics.iconButtonIconSize, isA<double>());
        expect(DovahControlMetrics.iconButtonIconSize, 19);
      },
    );
  });

  group('Property interaction constants behave correctly', () {
    test(
      'Property hover constants keep the prototype primary-button values',
      () {
        expect(DovahControlMetrics.primaryButtonHoverBrightness, isA<double>());
        expect(DovahControlMetrics.primaryButtonHoverBrightness, 0.07);
        expect(DovahControlMetrics.buttonHoverDuration, isA<Duration>());
        expect(
          DovahControlMetrics.buttonHoverDuration,
          const Duration(milliseconds: 160),
        );
      },
    );

    test('Property disabled and focus constants keep the approved values', () {
      expect(DovahControlMetrics.disabledControlOpacity, isA<double>());
      expect(DovahControlMetrics.disabledControlOpacity, 0.46);
      expect(DovahControlMetrics.disabledPrimarySaturation, isA<double>());
      expect(DovahControlMetrics.disabledPrimarySaturation, 0.45);
      expect(
        DovahControlMetrics.liftDuration,
        const Duration(milliseconds: 180),
      );
      expect(DovahControlMetrics.focusOutlineWidth, isA<double>());
      expect(DovahControlMetrics.focusOutlineWidth, 2);
      expect(DovahControlMetrics.focusOutlineOffset, isA<double>());
      expect(DovahControlMetrics.focusOutlineOffset, 3);
    });

    test('Property minimumTapTargetSize is the 48 logical-pixel floor', () {
      expect(DovahControlMetrics.minimumTapTargetSize, isA<double>());
      expect(DovahControlMetrics.minimumTapTargetSize, 48);
    });
  });
}
