import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahButton] across every DovahLink theme, both variants, and interaction.
void main() {
  group('DovahButton renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final DovahButtonVariant variant in DovahButtonVariant.values) {
        for (final Size size in dovahTestSizes) {
          testWidgets(
            'DovahButton renders $variant under $preset at $size without overflow',
            (WidgetTester tester) async {
              await pumpDovahThemedWidget(
                tester,
                DovahButton(
                  label: 'Confirm',
                  onPressed: () {},
                  variant: variant,
                ),
                preset: preset,
                size: size,
              );

              expect(tester.takeException(), isNull);
              expect(find.text('Confirm'), findsOneWidget);
            },
          );
        }
      }
    }
  });

  group('DovahButton calls onPressed', () {
    testWidgets('DovahButton calls onPressed when tapped and enabled', (
      WidgetTester tester,
    ) async {
      int tapCount = 0;

      await pumpDovahThemedWidget(
        tester,
        DovahButton(label: 'Confirm', onPressed: () => tapCount++),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      await tester.tap(find.text('Confirm'));
      await tester.pump();

      expect(tapCount, 1);
    });

    testWidgets('DovahButton does not call onPressed when disabled', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahButton(label: 'Confirm', onPressed: null),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      await tester.tap(find.text('Confirm'), warnIfMissed: false);
      await tester.pump();

      expect(tester.takeException(), isNull);
    });
  });

  group('DovahButton contains a tap target of at least 48x48', () {
    for (final DovahButtonVariant variant in DovahButtonVariant.values) {
      testWidgets(
        'DovahButton contains a $variant tap target of at least 48x48',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(label: 'OK', onPressed: () {}, variant: variant),
            preset: DovahThemePreset.frostbound,
            size: dovahTestSizes.first,
          );

          final Size size = tester.getSize(find.byType(DovahButton));

          expect(size.width, greaterThanOrEqualTo(48));
          expect(size.height, greaterThanOrEqualTo(48));
        },
      );
    }
  });

  group('DovahButton preserves the prototype\'s visible geometry', () {
    testWidgets(
      'DovahButton does not stretch its visible surface to the 48dp tap target',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          DovahButton(label: 'X', onPressed: () {}),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final Size interactiveSize = tester.getSize(find.byType(DovahButton));
        final Size visibleSurfaceSize = tester.getSize(
          find.descendant(
            of: find.byType(DovahButton),
            matching: find.byType(DovahSurface),
          ),
        );

        expect(interactiveSize.height, greaterThanOrEqualTo(48));
        expect(visibleSurfaceSize.height, lessThan(48));
      },
    );

    testWidgets(
      'DovahButton calls onPressed when tapped in the transparent tap-target margin',
      (WidgetTester tester) async {
        int tapCount = 0;

        await pumpDovahThemedWidget(
          tester,
          DovahButton(label: 'X', onPressed: () => tapCount++),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final Rect bounds = tester.getRect(find.byType(DovahButton));
        // A point at the very top edge of the >=48dp interactive region: outside the small
        // painted surface (which is centered and shorter than 48dp) but inside DovahButton's
        // own bounds.
        await tester.tapAt(Offset(bounds.center.dx, bounds.top + 1));
        await tester.pump();

        expect(tapCount, 1);
      },
    );
  });

  group('DovahButton displays contrasting label text', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahButton displays a readable label color for the primary variant under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const DovahButton(label: 'Confirm', onPressed: null),
            preset: preset,
            size: dovahTestSizes.first,
          );

          final Text text = tester.widget<Text>(find.text('Confirm'));

          expect(text.style?.color, isNotNull);
          expect(
            text.style!.color,
            anyOf(equals(Colors.white), equals(Colors.black87)),
          );
        },
      );
    }
  });
}
