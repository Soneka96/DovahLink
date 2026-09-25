import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring_painter.dart';
import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahFocusRing] across every DovahLink theme.
void main() {
  group('DovahFocusRing renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahFocusRing paints the accent outline around its child under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const Center(
              child: DovahFocusRing(
                focused: true,
                cornerRadius: 4,
                child: SizedBox(width: 40, height: 40),
              ),
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;
          final DovahFocusRingPainter painter =
              tester
                      .widget<CustomPaint>(find.byKey(DovahFocusRing.ringKey))
                      .foregroundPainter!
                  as DovahFocusRingPainter;

          expect(painter.color, tokens.accentPrimary);
          expect(painter.cornerRadius, 4);
        },
      );
    }

    testWidgets('DovahFocusRing paints nothing while unfocused', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const Center(
          child: DovahFocusRing(
            focused: false,
            cornerRadius: 0,
            child: SizedBox(width: 40, height: 40),
          ),
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(find.byKey(DovahFocusRing.ringKey), findsNothing);
      expect(tester.getSize(find.byType(DovahFocusRing)), const Size(40, 40));
    });

    testWidgets('DovahFocusRing does not change its child size while focused', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const Center(
          child: DovahFocusRing(
            focused: true,
            cornerRadius: 0,
            child: SizedBox(width: 40, height: 40),
          ),
        ),
        preset: DovahThemePreset.hearth,
        size: dovahTestSizes.first,
      );

      expect(tester.getSize(find.byType(DovahFocusRing)), const Size(40, 40));
    });
  });
}
