import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
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

        final Image image = tester.widget(find.byType(Image));
        expect(
          (image.image as AssetImage).assetName,
          frostboundEnvironmentAsset,
        );
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

        final Image image = tester.widget(find.byType(Image));
        expect((image.image as AssetImage).assetName, hearthEnvironmentAsset);
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

    testWidgets(
      'DovahEnvironmentBackground uses the shared environment scrim opacities',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: SizedBox.shrink()),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );
        final DovahThemeTokens tokens = Theme.of(
          tester.element(find.byType(DovahEnvironmentBackground)),
        ).extension<DovahThemeTokens>()!;
        final DecoratedBox scrim = tester.widget(
          find.descendant(
            of: find.byType(DovahEnvironmentBackground),
            matching: find.byType(DecoratedBox),
          ),
        );
        final LinearGradient gradient =
            (scrim.decoration as BoxDecoration).gradient! as LinearGradient;

        expect(gradient.colors, [
          tokens.background.withValues(
            alpha: DovahThemeTokens.environmentTopScrimOpacity,
          ),
          tokens.background.withValues(
            alpha: DovahThemeTokens.environmentBottomScrimOpacity,
          ),
        ]);
      },
    );
  });
}
