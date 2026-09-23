import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_environment_background.widget.dart';

import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahEnvironmentBackground] across every DovahLink theme and representative size.
void main() {
  group('DovahEnvironmentBackground renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahTestSizes) {
        testWidgets(
          'DovahEnvironmentBackground renders its child under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              const DovahEnvironmentBackground(
                child: Text('Foreground content'),
              ),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
            expect(find.text('Foreground content'), findsOneWidget);
          },
        );
      }
    }

    testWidgets(
      'DovahEnvironmentBackground renders an environment image for Frostbound',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: SizedBox.shrink()),
          preset: DovahThemePreset.frostbound,
          size: dovahTestSizes.first,
        );

        expect(find.byType(Image), findsOneWidget);
      },
    );

    testWidgets(
      'DovahEnvironmentBackground renders an environment image for Hearth',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: SizedBox.shrink()),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        expect(find.byType(Image), findsOneWidget);
      },
    );

    testWidgets(
      'DovahEnvironmentBackground renders no environment image for Dovah',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: SizedBox.shrink()),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.byType(Image), findsNothing);
      },
    );
  });
}
