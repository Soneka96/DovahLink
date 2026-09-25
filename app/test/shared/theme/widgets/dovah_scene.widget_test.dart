import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_layers_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_scene.widget.dart';

/// A layer that fills the scene with a translucent black.
const DovahLinearLayer scrim = DovahLinearLayer(
  angleDegrees: 180,
  colors: [Color(0x80000000), Color(0xC0000000)],
  stops: [0, 1],
);

/// Pumps a [DovahScene] with the given parts inside a fixed-size box.
Future<void> pumpScene(
  WidgetTester tester, {
  String? imageAssetPath,
  DovahColorFilter imageFilter = DovahColorFilter.none,
  List<DovahMaterialLayer> layers = const [scrim],
}) => tester.pumpWidget(
  MaterialApp(
    home: Center(
      child: SizedBox(
        width: 200,
        height: 100,
        child: DovahScene(
          imageAssetPath: imageAssetPath,
          imageFilter: imageFilter,
          layers: layers,
        ),
      ),
    ),
  ),
);

/// Exercises [DovahScene]'s image, gradient layers, color treatment, and sizing.
void main() {
  group('DovahScene renders correctly', () {
    testWidgets('DovahScene fills the space it is given', (
      WidgetTester tester,
    ) async {
      await pumpScene(tester);

      expect(tester.getSize(find.byType(DovahScene)), const Size(200, 100));
      expect(tester.takeException(), isNull);
    });

    testWidgets(
      'DovahScene draws the image covering its area when it has one',
      (WidgetTester tester) async {
        await pumpScene(tester, imageAssetPath: hearthEnvironmentAsset);
        final Image image = tester.widget(find.byType(Image));

        expect((image.image as AssetImage).assetName, hearthEnvironmentAsset);
        expect(image.fit, BoxFit.cover);
        expect(image.excludeFromSemantics, isTrue);
      },
    );

    testWidgets('DovahScene draws no image when it has no asset path', (
      WidgetTester tester,
    ) async {
      await pumpScene(tester);

      expect(find.byType(Image), findsNothing);
    });

    testWidgets('DovahScene paints its layers over the image', (
      WidgetTester tester,
    ) async {
      await pumpScene(tester, imageAssetPath: hearthEnvironmentAsset);
      final Stack stack = tester.widget(
        find.descendant(
          of: find.byType(DovahScene),
          matching: find.byType(Stack),
        ),
      );

      expect(stack.children.first, isA<Image>());
      final CustomPaint paint = stack.children.last as CustomPaint;
      expect((paint.painter! as DovahLayersPainter).layers, const [scrim]);
    });

    testWidgets('DovahScene paints its layers alone when it has no image', (
      WidgetTester tester,
    ) async {
      await pumpScene(tester);
      final Stack stack = tester.widget(
        find.descendant(
          of: find.byType(DovahScene),
          matching: find.byType(Stack),
        ),
      );

      expect(stack.children, hasLength(1));
      expect(stack.children.single, isA<CustomPaint>());
    });
  });

  group('DovahScene applies its color treatment', () {
    testWidgets('DovahScene wraps the image and layers in the color filter', (
      WidgetTester tester,
    ) async {
      const DovahColorFilter filter = DovahColorFilter(
        grayscale: 0.35,
        saturate: 0.55,
        contrast: 1.15,
      );
      await pumpScene(
        tester,
        imageAssetPath: hearthEnvironmentAsset,
        imageFilter: filter,
      );
      final ColorFiltered filtered = tester.widget(find.byType(ColorFiltered));

      expect(filtered.colorFilter, filter.toColorFilter());
      expect(
        find.descendant(
          of: find.byType(ColorFiltered),
          matching: find.byType(Image),
        ),
        findsOneWidget,
      );
    });

    testWidgets('DovahScene adds no color filter for a neutral treatment', (
      WidgetTester tester,
    ) async {
      await pumpScene(tester, imageAssetPath: hearthEnvironmentAsset);

      expect(find.byType(ColorFiltered), findsNothing);
    });
  });
}
