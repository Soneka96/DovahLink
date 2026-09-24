import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_metrics.dart';
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
  });

  group('DovahPanel uses shared padding', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'DovahPanel uses the prototype panel padding for $preset at $size',
          (WidgetTester tester) async {
            const Key panelKey = Key('dovah-panel-padding');
            await pumpDovahThemedWidget(
              tester,
              const DovahPanel(key: panelKey, child: Text('Panel content')),
              preset: preset,
              size: size,
            );
            final DovahThemeTokens tokens = Theme.of(
              tester.element(find.text('Panel content')),
            ).extension<DovahThemeTokens>()!;
            final DovahPageMetrics metrics = DovahPageMetrics.forWindow(
              preset: preset,
              window: size,
            );
            final Rect panelRect = tester.getRect(find.byKey(panelKey));
            final Rect contentRect = tester.getRect(find.text('Panel content'));
            final double borderInset =
                tokens.cornerStyle == DovahPanelCornerStyle.rounded
                ? DovahThemeTokens.surfaceBorderWidth
                : 0;
            final double expectedInset =
                metrics.panelPadding.left + borderInset;

            expect(
              contentRect.left - panelRect.left,
              closeTo(expectedInset, 0.001),
            );
            expect(
              contentRect.top - panelRect.top,
              closeTo(expectedInset, 0.001),
            );
            expect(
              panelRect.right - contentRect.right,
              closeTo(expectedInset, 0.001),
            );
            expect(
              panelRect.bottom - contentRect.bottom,
              closeTo(expectedInset, 0.001),
            );
          },
        );
      }
    }

    for (final (DovahThemePreset preset, Size size, double inset) in [
      (DovahThemePreset.frostbound, const Size(1280, 720), 14),
      (DovahThemePreset.dovah, const Size(1280, 720), 18),
      (DovahThemePreset.hearth, const Size(1280, 720), 18),
      (DovahThemePreset.frostbound, const Size(900, 560), 14),
      (DovahThemePreset.dovah, const Size(900, 560), 15),
      (DovahThemePreset.hearth, const Size(900, 560), 15),
    ]) {
      testWidgets(
        'DovahPanel pads its content $inset under ${preset.name} at $size',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const DovahPanel(child: Text('Panel content')),
            preset: preset,
            size: size,
          );

          expect(
            tester.widget<DovahSurface>(find.byType(DovahSurface)).padding,
            EdgeInsets.all(inset),
          );
        },
      );
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

  group('DovahPanel uses the prototype panel corner radius', () {
    testWidgets('DovahPanel rounds Hearth panels by 14 rather than 13', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahPanel(child: Text('Hearth panel')),
        preset: DovahThemePreset.hearth,
        size: dovahTestSizes.first,
      );

      final Container container = tester.widget<Container>(
        find
            .descendant(
              of: find.byType(DovahPanel),
              matching: find.byType(Container),
            )
            .first,
      );
      final BoxDecoration decoration = container.decoration! as BoxDecoration;
      expect(decoration.borderRadius, BorderRadius.circular(14));
    });
  });
}
