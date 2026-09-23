import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

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

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets('DovahPanel uses shared padding scaled for $preset', (
        WidgetTester tester,
      ) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahPanel(child: Text('Panel content')),
          preset: preset,
          size: dovahTestSizes.first,
        );
        final DovahThemeTokens tokens = Theme.of(
          tester.element(find.text('Panel content')),
        ).extension<DovahThemeTokens>()!;
        final Rect panelRect = tester.getRect(find.byType(DovahSurface));
        final Rect contentRect = tester.getRect(find.text('Panel content'));

        expect(
          contentRect.left - panelRect.left,
          DovahThemeTokens.spacing18 * tokens.densityScale,
        );
        expect(
          contentRect.top - panelRect.top,
          DovahThemeTokens.spacing18 * tokens.densityScale,
        );
      });
    }

    testWidgets('DovahPanel respects an explicit padding override', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahPanel(
          padding: EdgeInsets.all(4),
          child: Text('Tightly padded content'),
        ),
        preset: DovahThemePreset.frostbound,
        size: dovahTestSizes.first,
      );

      expect(tester.takeException(), isNull);
      expect(find.text('Tightly padded content'), findsOneWidget);
    });
  });
}
