import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahSurface] across every DovahLink theme and representative landscape size.
void main() {
  group('DovahSurface renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahTestSizes) {
        testWidgets(
          'DovahSurface renders its child under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              const DovahSurface(
                padding: EdgeInsets.all(16),
                child: Text('Surface content'),
              ),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
            expect(find.text('Surface content'), findsOneWidget);
          },
        );
      }
    }

    testWidgets(
      'DovahSurface renders with the raised material when raised is true',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(raised: true, child: Text('Raised content')),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(tester.takeException(), isNull);
        expect(find.text('Raised content'), findsOneWidget);
      },
    );

    testWidgets(
      'DovahSurface renders with a gradient override when given one',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(
            gradient: LinearGradient(colors: [Colors.red, Colors.blue]),
            child: Text('Overridden gradient content'),
          ),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        expect(tester.takeException(), isNull);
        expect(find.text('Overridden gradient content'), findsOneWidget);
      },
    );
  });
}
