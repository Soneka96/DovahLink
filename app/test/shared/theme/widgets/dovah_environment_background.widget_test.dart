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
import 'package:dovahlink_client/shared/theme/materials/dovah_materials.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_environment_background.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_layers_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_scene.widget.dart';
import 'dovah_widget_test_helpers.dart';

/// Returns the atmosphere recipe installed by [preset].
DovahAtmosphere atmosphereForPreset(DovahThemePreset preset) =>
    dovahThemeDataFor(preset).extension<DovahThemeMaterials>()!.atmosphere;

/// Checks that [tester] sees [preset]'s recipe in the rendered background.
void expectAtmosphereRecipe(WidgetTester tester, DovahThemePreset preset) {
  final DovahAtmosphere atmosphere = atmosphereForPreset(preset);
  final DovahScene scene = tester.widget(find.byType(DovahScene));
  final DovahThemeTokens tokens = Theme.of(
    tester.element(find.byType(DovahEnvironmentBackground)),
  ).extension<DovahThemeTokens>()!;
  final ColoredBox base = tester.widget(
    find.descendant(
      of: find.byType(DovahEnvironmentBackground),
      matching: find.byType(ColoredBox),
    ),
  );

  expect(base.color, tokens.background);
  expect(scene.imageAssetPath, atmosphere.imageAssetPath);
  expect(scene.layers, same(atmosphere.layers));
  expect(scene.imageFilter, atmosphere.imageFilter);

  final CustomPaint mainPaint = tester.widget(
    find.descendant(
      of: find.byType(DovahScene),
      matching: find.byType(CustomPaint),
    ),
  );
  expect(
    (mainPaint.painter! as DovahLayersPainter).layers,
    same(atmosphere.layers),
  );

  final Finder hazeFinder = find.byWidgetPredicate(
    (Widget widget) =>
        widget is CustomPaint &&
        widget.painter is DovahLayersPainter &&
        identical(
          (widget.painter! as DovahLayersPainter).layers,
          atmosphere.hazeLayers,
        ),
  );
  expect(hazeFinder, findsOneWidget);
  final CustomPaint hazePaint = tester.widget(hazeFinder);
  expect(
    (hazePaint.painter! as DovahLayersPainter).opacity,
    atmosphere.hazeOpacity,
  );

  if (atmosphere.imageFilter.isNeutral) {
    expect(find.byType(ColorFiltered), findsNothing);
  } else {
    final ColorFiltered filtered = tester.widget(find.byType(ColorFiltered));
    expect(filtered.colorFilter, atmosphere.imageFilter.toColorFilter());
    expect(
      find.descendant(
        of: find.byType(ColorFiltered),
        matching: find.byType(Image),
      ),
      atmosphere.imageAssetPath == null ? findsNothing : findsOneWidget,
    );
    expect(
      find.descendant(
        of: find.byType(ColorFiltered),
        matching: find.byType(CustomPaint),
      ),
      findsOneWidget,
    );
    expect(
      find.ancestor(of: hazeFinder, matching: find.byType(ColorFiltered)),
      findsNothing,
    );
  }
}

/// Exercises [DovahEnvironmentBackground] across every theme and representative size.
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

  group('DovahEnvironmentBackground renders the Frostbound recipe', () {
    testWidgets(
      'DovahEnvironmentBackground renders the image, filter, atmosphere layers, and haze',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: SizedBox.shrink()),
          preset: DovahThemePreset.frostbound,
          size: dovahTestSizes.first,
        );

        expectAtmosphereRecipe(tester, DovahThemePreset.frostbound);
        final Image image = tester.widget(find.byType(Image));
        expect(
          (image.image as AssetImage).assetName,
          frostboundEnvironmentAsset,
        );
      },
    );
  });

  group('DovahEnvironmentBackground renders the Hearth recipe', () {
    testWidgets(
      'DovahEnvironmentBackground renders the image, filter, atmosphere layers, and haze',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: SizedBox.shrink()),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        expectAtmosphereRecipe(tester, DovahThemePreset.hearth);
        final Image image = tester.widget(find.byType(Image));
        expect((image.image as AssetImage).assetName, hearthEnvironmentAsset);
      },
    );
  });

  group('DovahEnvironmentBackground renders the Dovah recipe', () {
    testWidgets(
      'DovahEnvironmentBackground paints atmosphere layers and haze without an image',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: SizedBox.shrink()),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expectAtmosphereRecipe(tester, DovahThemePreset.dovah);
        expect(find.byType(Image), findsNothing);
      },
    );
  });

  group('DovahEnvironmentBackground preserves atmosphere and content order', () {
    testWidgets(
      'DovahEnvironmentBackground paints main layers before haze and content above both',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahEnvironmentBackground(child: Text('Foreground content')),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final Stack stack = tester.widget(
          find.ancestor(
            of: find.text('Foreground content'),
            matching: find.byType(Stack),
          ),
        );
        expect(stack.children, hasLength(4));
        expect(stack.children.first, isA<ColoredBox>());
        expect((stack.children[1] as Positioned).child, isA<DovahScene>());
        expect((stack.children[2] as Positioned).child, isA<CustomPaint>());
        expect(stack.children.last, isA<Text>());

        final CustomPaint haze = tester.widget(
          find.byWidgetPredicate(
            (Widget widget) =>
                widget is CustomPaint &&
                widget.painter is DovahLayersPainter &&
                identical(
                  (widget.painter! as DovahLayersPainter).layers,
                  dovahMaterials.atmosphere.hazeLayers,
                ),
          ),
        );
        expect(
          find.ancestor(
            of: find.byWidget(haze),
            matching: find.byType(ColorFiltered),
          ),
          findsNothing,
        );
      },
    );
  });

  group('DovahEnvironmentBackground responds to theme changes', () {
    testWidgets(
      'DovahEnvironmentBackground updates its image and recipe for the active theme',
      (WidgetTester tester) async {
        setDovahTestWindow(tester, dovahTestSizes.first);
        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            themeAnimationDuration: Duration.zero,
            home: const Scaffold(
              body: DovahEnvironmentBackground(child: SizedBox.shrink()),
            ),
          ),
        );
        expectAtmosphereRecipe(tester, DovahThemePreset.dovah);

        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.frostbound),
            themeAnimationDuration: Duration.zero,
            home: const Scaffold(
              body: DovahEnvironmentBackground(child: SizedBox.shrink()),
            ),
          ),
        );

        expectAtmosphereRecipe(tester, DovahThemePreset.frostbound);
        final Image image = tester.widget(find.byType(Image));
        expect(
          (image.image as AssetImage).assetName,
          frostboundEnvironmentAsset,
        );

        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            themeAnimationDuration: Duration.zero,
            home: const Scaffold(
              body: DovahEnvironmentBackground(child: SizedBox.shrink()),
            ),
          ),
        );

        expectAtmosphereRecipe(tester, DovahThemePreset.dovah);
        expect(find.byType(Image), findsNothing);
      },
    );
  });

  group('DovahEnvironmentBackground supports an atmosphere without haze', () {
    testWidgets(
      'DovahEnvironmentBackground omits the haze painter when the recipe has no haze layers',
      (WidgetTester tester) async {
        final ThemeData baseTheme = dovahThemeDataFor(DovahThemePreset.dovah);
        final DovahThemeTokens tokens = baseTheme
            .extension<DovahThemeTokens>()!;
        final ThemeData noHazeTheme = ThemeData(
          extensions: [
            tokens,
            dovahMaterials.copyWith(atmosphere: const DovahAtmosphere()),
          ],
        );
        setDovahTestWindow(tester, dovahTestSizes.first);
        await tester.pumpWidget(
          MaterialApp(
            theme: noHazeTheme,
            home: const Scaffold(
              body: DovahEnvironmentBackground(child: SizedBox.shrink()),
            ),
          ),
        );

        final Stack stack = tester.widget(
          find
              .descendant(
                of: find.byType(DovahEnvironmentBackground),
                matching: find.byType(Stack),
              )
              .first,
        );
        expect(stack.children, hasLength(3));
        expect(stack.children[1], isA<Positioned>());
        expect((stack.children[1] as Positioned).child, isA<DovahScene>());
        expect(stack.children.last, isA<SizedBox>());
      },
    );
  });

  group('DovahEnvironmentBackground changes rendered pixels', () {
    testWidgets(
      'DovahEnvironmentBackground atmosphere changes the output from the plain background',
      (WidgetTester tester) async {
        const Key boundaryKey = ValueKey<String>('atmosphere-output');
        await pumpDovahThemedWidget(
          tester,
          const RepaintBoundary(
            key: boundaryKey,
            child: DovahEnvironmentBackground(child: SizedBox.expand()),
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(100, 100),
        );

        final RenderRepaintBoundary boundary = tester.renderObject(
          find.byKey(boundaryKey),
        );
        final Color? atmospherePixel = await tester.runAsync<Color>(() async {
          final ui.Image image = await boundary.toImage(pixelRatio: 1);
          try {
            final ByteData? bytes = await image.toByteData(
              format: ui.ImageByteFormat.rawRgba,
            );
            if (bytes == null) {
              throw StateError('The atmosphere image has no pixel data.');
            }
            final ByteData pixels = bytes;
            const int x = 88;
            const int y = 5;
            final int offset = (y * image.width + x) * 4;
            return Color.fromARGB(
              pixels.getUint8(offset + 3),
              pixels.getUint8(offset),
              pixels.getUint8(offset + 1),
              pixels.getUint8(offset + 2),
            );
          } finally {
            image.dispose();
          }
        });
        if (atmospherePixel == null) {
          fail('The atmosphere pixel capture did not complete.');
        }
        final Color plainBackground = Theme.of(
          tester.element(find.byType(DovahEnvironmentBackground)),
        ).extension<DovahThemeTokens>()!.background;

        expect(atmospherePixel, isNot(plainBackground));
      },
    );
  });
}
