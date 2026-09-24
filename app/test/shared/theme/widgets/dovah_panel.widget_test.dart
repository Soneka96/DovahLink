import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel.widget.dart';
import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahPanel] across every DovahLink theme and representative landscape size.
void main() {
  group('DovahPanel renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahTestSizes) {
        testWidgets(
          'DovahPanel renders its child under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              const DovahPanel(child: Text('Panel content')),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
            expect(find.text('Panel content'), findsOneWidget);
          },
        );
      }
    }
  });

  group('DovahPanel uses shared padding', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets('DovahPanel uses shared padding scaled for $preset', (
        WidgetTester tester,
      ) async {
        const Key panelKey = Key('dovah-panel-padding');
        await pumpDovahThemedWidget(
          tester,
          const DovahPanel(key: panelKey, child: Text('Panel content')),
          preset: preset,
          size: dovahTestSizes.first,
        );
        final DovahThemeTokens tokens = Theme.of(
          tester.element(find.text('Panel content')),
        ).extension<DovahThemeTokens>()!;
        final Rect panelRect = tester.getRect(find.byKey(panelKey));
        final Rect contentRect = tester.getRect(find.text('Panel content'));
        final double borderInset =
            tokens.cornerStyle == DovahPanelCornerStyle.rounded
            ? DovahThemeTokens.surfaceBorderWidth
            : 0;
        final double expectedInset =
            DovahThemeTokens.spacing18 * tokens.densityScale + borderInset;

        expect(
          contentRect.left - panelRect.left,
          closeTo(expectedInset, 0.001),
        );
        expect(contentRect.top - panelRect.top, closeTo(expectedInset, 0.001));
        expect(
          panelRect.right - contentRect.right,
          closeTo(expectedInset, 0.001),
        );
        expect(
          panelRect.bottom - contentRect.bottom,
          closeTo(expectedInset, 0.001),
        );
      });
    }
  });

  group('DovahPanel respects explicit padding overrides', () {
    testWidgets('DovahPanel respects an explicit padding override', (
      WidgetTester tester,
    ) async {
      const Key panelKey = Key('dovah-panel-explicit-padding');
      await pumpDovahThemedWidget(
        tester,
        const DovahPanel(
          key: panelKey,
          padding: EdgeInsets.all(4),
          child: Text('Tightly padded content'),
        ),
        preset: DovahThemePreset.frostbound,
        size: dovahTestSizes.first,
      );

      expect(tester.takeException(), isNull);
      expect(find.text('Tightly padded content'), findsOneWidget);
      final Rect panelRect = tester.getRect(find.byKey(panelKey));
      final Rect contentRect = tester.getRect(
        find.text('Tightly padded content'),
      );
      expect(contentRect.left - panelRect.left, closeTo(4, 0.001));
      expect(contentRect.top - panelRect.top, closeTo(4, 0.001));
      expect(panelRect.right - contentRect.right, closeTo(4, 0.001));
      expect(panelRect.bottom - contentRect.bottom, closeTo(4, 0.001));
    });
  });
}
