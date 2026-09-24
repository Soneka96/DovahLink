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

  group('DovahSurface honors a corner radius override', () {
    testWidgets(
      'DovahSurface uses the override radius instead of the theme radius under Hearth',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(cornerRadius: 20, child: Text('Override')),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        final Container container = tester.widget<Container>(
          find
              .descendant(
                of: find.byType(DovahSurface),
                matching: find.byType(Container),
              )
              .first,
        );
        final BoxDecoration decoration = container.decoration! as BoxDecoration;
        expect(decoration.borderRadius, BorderRadius.circular(20));
      },
    );

    testWidgets(
      'DovahSurface uses the theme radius under Hearth when no override is given',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(child: Text('Default')),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        final Container container = tester.widget<Container>(
          find
              .descendant(
                of: find.byType(DovahSurface),
                matching: find.byType(Container),
              )
              .first,
        );
        final BoxDecoration decoration = container.decoration! as BoxDecoration;
        expect(decoration.borderRadius, BorderRadius.circular(13));
      },
    );
  });
}
