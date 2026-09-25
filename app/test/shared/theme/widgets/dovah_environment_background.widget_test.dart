import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_atmosphere.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_environment_background.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_layers_painter.dart';
import 'dovah_widget_test_helpers.dart';

/// Returns the atmosphere of [preset]'s theme.
DovahAtmosphere atmosphereOf(DovahThemePreset preset) =>
    dovahThemeDataFor(preset).extension<DovahThemeMaterials>()!.atmosphere;

/// Returns every [DovahLayersPainter] under the background, in paint order.
List<DovahLayersPainter> findLayerPainters(WidgetTester tester) => [
  for (final CustomPaint paint in tester.widgetList<CustomPaint>(
    find.descendant(
      of: find.byType(DovahEnvironmentBackground),
      matching: find.byType(CustomPaint),
    ),
  ))
    if (paint.painter is DovahLayersPainter)
      paint.painter! as DovahLayersPainter,
];

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
  });

  group('DovahEnvironmentBackground uses each theme environment', () {
    for (final (DovahThemePreset preset, String asset) in [
      (DovahThemePreset.frostbound, frostboundEnvironmentAsset),
      (DovahThemePreset.hearth, hearthEnvironmentAsset),
    ]) {
      testWidgets(
        'DovahEnvironmentBackground renders the $preset environment image',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const DovahEnvironmentBackground(child: SizedBox.shrink()),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final Image image = tester.widget(find.byType(Image));

          expect(find.byType(Image), findsOneWidget);
          expect((image.image as AssetImage).assetName, asset);
          expect(image.fit, BoxFit.cover);
          expect(image.excludeFromSemantics, isTrue);
        },
      );
    }

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
      'DovahEnvironmentBackground paints the theme base color under the atmosphere',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: SizedBox.shrink()),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );
        final ColoredBox base = tester.widget(
          find.descendant(
            of: find.byType(DovahEnvironmentBackground),
            matching: find.byType(ColoredBox),
          ),
        );

        expect(
          base.color,
          dovahThemeDataFor(
            DovahThemePreset.hearth,
          ).extension<DovahThemeTokens>()!.background,
        );
      },
    );
  });

  group('DovahEnvironmentBackground applies the atmosphere recipe', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahEnvironmentBackground paints the $preset gradient layers, then its haze at the haze opacity',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const DovahEnvironmentBackground(child: SizedBox.shrink()),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final DovahAtmosphere atmosphere = atmosphereOf(preset);
          final List<DovahLayersPainter> painters = findLayerPainters(tester);

          expect(painters, hasLength(2));
          expect(painters.first.layers, atmosphere.layers);
          expect(painters.first.opacity, 1);
          expect(painters.last.layers, atmosphere.hazeLayers);
          expect(painters.last.opacity, atmosphere.hazeOpacity);
        },
      );
    }

    for (final DovahThemePreset preset in [
      DovahThemePreset.frostbound,
      DovahThemePreset.hearth,
    ]) {
      testWidgets(
        'DovahEnvironmentBackground applies the $preset color treatment to the image and gradients',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const DovahEnvironmentBackground(child: SizedBox.shrink()),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final ColorFiltered filtered = tester.widget(
            find.byType(ColorFiltered),
          );

          expect(
            filtered.colorFilter,
            atmosphereOf(preset).imageFilter.toColorFilter(),
          );
          expect(
            find.descendant(
              of: find.byType(ColorFiltered),
              matching: find.byType(Image),
            ),
            findsOneWidget,
          );
          expect(
            find.descendant(
              of: find.byType(ColorFiltered),
              matching: find.byWidgetPredicate(
                (Widget widget) =>
                    widget is CustomPaint &&
                    widget.painter is DovahLayersPainter,
              ),
            ),
            findsOneWidget,
          );
        },
      );
    }

    testWidgets(
      'DovahEnvironmentBackground applies no color treatment for Dovah',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: SizedBox.shrink()),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.byType(ColorFiltered), findsNothing);
      },
    );

    testWidgets(
      'DovahEnvironmentBackground isolates the static atmosphere in a repaint boundary',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: Text('Foreground content')),
          preset: DovahThemePreset.frostbound,
          size: dovahTestSizes.first,
        );

        expect(
          find.descendant(
            of: find.byType(DovahEnvironmentBackground),
            matching: find.byType(RepaintBoundary),
          ),
          findsWidgets,
        );
        expect(
          find.ancestor(
            of: find.byType(ColorFiltered),
            matching: find.byType(RepaintBoundary),
          ),
          findsWidgets,
        );
      },
    );
  });

  group('DovahEnvironmentBackground keeps foreground content usable', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahEnvironmentBackground lets a tap reach a foreground button under $preset',
        (WidgetTester tester) async {
          int tapCount = 0;
          await pumpDovahThemedWidget(
            tester,
            DovahEnvironmentBackground(
              child: Center(
                child: TextButton(
                  onPressed: () => tapCount++,
                  child: const Text('Tap me'),
                ),
              ),
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );

          await tester.tap(find.text('Tap me'));
          await tester.pump();

          expect(tapCount, 1);
        },
      );
    }

    testWidgets(
      'DovahEnvironmentBackground paints its child above every atmosphere layer',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: Text('Foreground content')),
          preset: DovahThemePreset.frostbound,
          size: dovahTestSizes.first,
        );
        final Stack stack = tester.widget(
          find
              .descendant(
                of: find.byType(DovahEnvironmentBackground),
                matching: find.byType(Stack),
              )
              .first,
        );

        expect(stack.children.last, isA<Text>());
        expect(stack.children.first, isA<Positioned>());
      },
    );
  });

  group('DovahEnvironmentBackground fades its haze down the canvas', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahEnvironmentBackground masks the $preset haze from visible at the top to gone at 80%',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const DovahEnvironmentBackground(child: SizedBox.shrink()),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final ShaderMask mask = tester.widget(find.byType(ShaderMask));

          expect(mask.blendMode, BlendMode.dstIn);
          expect(
            find.descendant(
              of: find.byType(ShaderMask),
              matching: find.byWidgetPredicate(
                (Widget widget) =>
                    widget is CustomPaint &&
                    widget.painter is DovahLayersPainter,
              ),
            ),
            findsOneWidget,
          );
          expect(
            find.descendant(
              of: find.byType(ShaderMask),
              matching: find.byType(Image),
            ),
            findsNothing,
          );
        },
      );
    }

    testWidgets(
      'DovahEnvironmentBackground adds no mask for an atmosphere without haze',
      (WidgetTester tester) async {
        final ThemeData base = dovahThemeDataFor(DovahThemePreset.dovah);
        setDovahTestWindow(tester, dovahTestSizes.first);
        await tester.pumpWidget(
          MaterialApp(
            theme: base.copyWith(
              extensions: [
                for (final ThemeExtension<dynamic> extension
                    in base.extensions.values)
                  if (extension is DovahThemeMaterials)
                    extension.copyWith(atmosphere: const DovahAtmosphere())
                  else
                    extension,
              ],
            ),
            home: const DovahEnvironmentBackground(child: SizedBox.shrink()),
          ),
        );

        expect(find.byType(ShaderMask), findsNothing);
      },
    );

    testWidgets('DovahEnvironmentBackground fades a solid haze by height', (
      WidgetTester tester,
    ) async {
      const Key boundaryKey = Key('background-boundary');
      final ThemeData base = dovahThemeDataFor(DovahThemePreset.dovah);
      setDovahTestWindow(tester, const Size(400, 500));
      await tester.pumpWidget(
        MaterialApp(
          theme: base.copyWith(
            extensions: [
              for (final ThemeExtension<dynamic> extension
                  in base.extensions.values)
                if (extension is DovahThemeMaterials)
                  extension.copyWith(
                    atmosphere: const DovahAtmosphere(
                      hazeLayers: [
                        DovahLinearLayer(
                          angleDegrees: 180,
                          colors: [Color(0xFFFFFFFF), Color(0xFFFFFFFF)],
                          stops: [0, 1],
                        ),
                      ],
                    ),
                  )
                else
                  extension,
            ],
          ),
          home: const RepaintBoundary(
            key: boundaryKey,
            child: DovahEnvironmentBackground(child: SizedBox.shrink()),
          ),
        ),
      );
      final RenderRepaintBoundary boundary = tester.renderObject(
        find.byKey(boundaryKey),
      );
      final ByteData data = (await tester.runAsync(() async {
        final ui.Image image = await boundary.toImage();
        return (await image.toByteData())!;
      }))!;
      int red(int y) => data.getUint8((y * 400 + 200) * 4);

      // The Dovah base is nearly black, so red tracks how much white haze shows through.
      expect(red(2), greaterThan(240));
      expect(red(200), inInclusiveRange(110, 150));
      expect(red(350), inInclusiveRange(20, 45));
      expect(red(410), lessThan(10));
      expect(red(499), lessThan(10));
    });
  });
}
