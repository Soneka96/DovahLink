import 'dart:ui';

import 'package:flutter/painting.dart' show BoxShadow;

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
import '../materials/dovah_material_test_helpers.dart';

/// The size of the painted surface every pixel probe uses.
const Size surfaceSize = Size(100, 60);

/// Transparent pixels around the surface, so paint outside it is visible.
const double margin = 20;

/// The image width of a probe: the surface plus its margins.
const int imageWidth = 140;

/// A layer that fills the surface with [color].
DovahLinearLayer solid(Color color) => DovahLinearLayer(
  angleDegrees: 90,
  colors: [color, color],
  stops: const [0, 1],
);

/// Builds a painter for [material] on the given geometry.
DovahMaterialPainter buildPainter({
  required DovahMaterial material,
  DovahPanelCornerStyle cornerStyle = DovahPanelCornerStyle.singleBevel,
  double cornerRadius = 0,
  double cutSize = 10,
}) => DovahMaterialPainter(
  cornerStyle: cornerStyle,
  cornerRadius: cornerRadius,
  cutSize: cutSize,
  material: material,
);

/// Returns the pixel at the surface-relative point ([x], [y]) of a [paintDovahPainter] image.
Color pixelAt(List<Color> pixels, int x, int y) =>
    pixels[(y + margin.toInt()) * imageWidth + x + margin.toInt()];

/// Exercises [DovahMaterialPainter]'s layered painting, geometry, and repaint contract.
void main() {
  const Color red = Color(0xFFFF0000);
  const Color green = Color(0xFF00FF00);
  const Color white = Color(0xFFFFFFFF);
  const Color black = Color(0xFF000000);

  Future<List<Color>> paint(
    WidgetTester tester,
    DovahMaterialPainter painter,
  ) async => (await tester.runAsync(
    () => paintDovahPainter(painter, surfaceSize, margin: margin),
  ))!;

  group('Method paint behaves correctly', () {
    testWidgets('Method paint fills the outline with the material layers', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(material: DovahMaterial(layers: [solid(red)])),
      );

      expect(pixelAt(pixels, 50, 30), red);
      expect(alphaOf(pixelAt(pixels, -5, 30)), 0);
    });

    testWidgets('Method paint paints later layers over earlier ones', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(
            layers: [solid(red), solid(const Color(0x800000FF))],
          ),
        ),
      );
      final Color blended = pixelAt(pixels, 50, 30);

      expect(redOf(blended), inInclusiveRange(120, 135));
      expect(blueOf(blended), inInclusiveRange(120, 135));
    });

    testWidgets('Method paint lets an opaque top layer hide the layers below', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(
            layers: [solid(const Color(0xFF0000FF)), solid(red)],
          ),
        ),
      );

      expect(pixelAt(pixels, 50, 30), red);
    });

    testWidgets('Method paint cuts the top-right corner for singleBevel', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(material: DovahMaterial(layers: [solid(red)])),
      );

      expect(alphaOf(pixelAt(pixels, 98, 1)), 0);
      expect(pixelAt(pixels, 1, 1), red);
      expect(pixelAt(pixels, 1, 58), red);
    });

    testWidgets('Method paint cuts opposite corners for doubleBevel', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(layers: [solid(red)]),
          cornerStyle: DovahPanelCornerStyle.doubleBevel,
        ),
      );

      expect(alphaOf(pixelAt(pixels, 98, 1)), 0);
      expect(alphaOf(pixelAt(pixels, 1, 58)), 0);
      expect(pixelAt(pixels, 1, 1), red);
      expect(pixelAt(pixels, 98, 58), red);
    });

    testWidgets('Method paint rounds every corner for rounded', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(layers: [solid(red)]),
          cornerStyle: DovahPanelCornerStyle.rounded,
          cornerRadius: 20,
        ),
      );

      expect(alphaOf(pixelAt(pixels, 1, 1)), 0);
      expect(alphaOf(pixelAt(pixels, 98, 58)), 0);
      expect(pixelAt(pixels, 50, 30), red);
    });

    testWidgets('Method paint draws the border inside the outline', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(layers: [solid(red)], borderColor: green),
        ),
      );

      expect(pixelAt(pixels, 50, 0), green);
      expect(pixelAt(pixels, 0, 30), green);
      expect(pixelAt(pixels, 50, 1), red);
      expect(alphaOf(pixelAt(pixels, 50, -1)), 0);
    });

    testWidgets(
      'Method paint leaves the singleBevel diagonal without a border',
      (WidgetTester tester) async {
        final List<Color> pixels = await paint(
          tester,
          buildPainter(
            material: DovahMaterial(layers: [solid(red)], borderColor: green),
          ),
        );

        expect(pixelAt(pixels, 95, 6), red);
        expect(pixelAt(pixels, 99, 30), green);
        expect(pixelAt(pixels, 50, 0), green);
        expect(pixelAt(pixels, 0, 30), green);
      },
    );

    testWidgets(
      'Method paint leaves both doubleBevel diagonals without a border',
      (WidgetTester tester) async {
        final List<Color> pixels = await paint(
          tester,
          buildPainter(
            material: DovahMaterial(layers: [solid(red)], borderColor: green),
            cornerStyle: DovahPanelCornerStyle.doubleBevel,
          ),
        );

        expect(pixelAt(pixels, 95, 6), red);
        expect(pixelAt(pixels, 6, 55), red);
        expect(pixelAt(pixels, 50, 59), green);
        expect(pixelAt(pixels, 0, 30), green);
      },
    );

    testWidgets('Method paint clips the edge lines and border at the bevel', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(
            layers: [solid(red)],
            topEdgeHighlight: white,
            borderColor: green,
          ),
        ),
      );

      expect(alphaOf(pixelAt(pixels, 98, 1)), 0);
      expect(alphaOf(pixelAt(pixels, 98, 0)), 0);
      expect(pixelAt(pixels, 50, 1), white);
    });

    testWidgets('Method paint follows the rounded corner with the border', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(layers: [solid(red)], borderColor: green),
          cornerStyle: DovahPanelCornerStyle.rounded,
          cornerRadius: 20,
        ),
      );

      // The point on the rounded corner arc, 20 * (1 - cos 45) from each edge.
      expect(
        greenOf(pixelAt(pixels, 6, 6)),
        greaterThan(redOf(pixelAt(pixels, 6, 6))),
      );
      expect(pixelAt(pixels, 50, 0), green);
    });

    testWidgets('Method paint draws no border when the material has none', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(material: DovahMaterial(layers: [solid(red)])),
      );

      expect(pixelAt(pixels, 50, 0), red);
    });

    testWidgets('Method paint draws the top edge line under the border', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(
            layers: [solid(red)],
            topEdgeHighlight: white,
            borderColor: green,
          ),
        ),
      );

      expect(pixelAt(pixels, 50, 0), green);
      expect(pixelAt(pixels, 50, 1), white);
      expect(pixelAt(pixels, 50, 2), red);
    });

    testWidgets(
      'Method paint draws the top edge line at the outline without a border',
      (WidgetTester tester) async {
        final List<Color> pixels = await paint(
          tester,
          buildPainter(
            material: DovahMaterial(
              layers: [solid(red)],
              topEdgeHighlight: white,
            ),
          ),
        );

        expect(pixelAt(pixels, 50, 0), white);
        expect(pixelAt(pixels, 50, 1), red);
      },
    );

    testWidgets('Method paint draws the bottom edge line above the border', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(
            layers: [solid(red)],
            bottomEdgeShade: black,
            borderColor: green,
          ),
        ),
      );

      expect(pixelAt(pixels, 50, 59), green);
      expect(pixelAt(pixels, 50, 58), black);
      expect(pixelAt(pixels, 50, 57), red);
    });

    testWidgets('Method paint draws no edge lines when the material has none', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(layers: [solid(red)], borderColor: green),
        ),
      );

      expect(pixelAt(pixels, 50, 1), red);
      expect(pixelAt(pixels, 50, 58), red);
    });

    testWidgets('Method paint casts the shadow outside the outline', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(
            layers: [solid(red)],
            shadow: const [
              BoxShadow(color: black, blurRadius: 10, offset: Offset(0, 8)),
            ],
          ),
        ),
      );

      expect(alphaOf(pixelAt(pixels, 50, 64)), greaterThan(0));
      expect(pixelAt(pixels, 50, 30), red);
    });

    testWidgets('Method paint casts every shadow in the list', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(
          material: DovahMaterial(
            layers: [solid(red)],
            shadow: const [
              BoxShadow(color: black, blurRadius: 4, offset: Offset(0, 6)),
              BoxShadow(color: green, blurRadius: 4, offset: Offset(0, -6)),
            ],
          ),
        ),
      );

      expect(alphaOf(pixelAt(pixels, 50, 64)), greaterThan(0));
      expect(alphaOf(pixelAt(pixels, 50, -4)), greaterThan(0));
    });

    testWidgets(
      'Method paint draws the bottom edge line at the outline without a border',
      (WidgetTester tester) async {
        final List<Color> pixels = await paint(
          tester,
          buildPainter(
            material: DovahMaterial(
              layers: [solid(red)],
              bottomEdgeShade: black,
            ),
          ),
        );

        expect(pixelAt(pixels, 50, 59), black);
        expect(pixelAt(pixels, 50, 58), red);
      },
    );

    testWidgets('Method paint casts no shadow when the material has none', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        buildPainter(material: DovahMaterial(layers: [solid(red)])),
      );

      expect(alphaOf(pixelAt(pixels, 50, 64)), 0);
    });

    test('Method paint draws nothing for an empty size', () {
      for (final Size size in const [Size.zero, Size(0, 60), Size(100, 0)]) {
        expect(
          () => buildPainter(
            material: DovahMaterial(layers: [solid(red)]),
          ).paint(Canvas(PictureRecorder()), size),
          returnsNormally,
        );
      }
    });

    test(
      'Method paint draws without throwing when the bevel exceeds the size',
      () {
        expect(
          () => buildPainter(
            material: DovahMaterial(layers: [solid(red)], borderColor: green),
            cornerStyle: DovahPanelCornerStyle.doubleBevel,
            cutSize: 40,
          ).paint(Canvas(PictureRecorder()), const Size(8, 8)),
          returnsNormally,
        );
      },
    );
  });

  group('Method shouldRepaint behaves correctly', () {
    final DovahMaterial material = DovahMaterial(layers: [solid(red)]);

    test('Method shouldRepaint returns false when nothing changed', () {
      expect(
        buildPainter(
          material: material,
        ).shouldRepaint(buildPainter(material: material)),
        isFalse,
      );
    });

    test('Method shouldRepaint returns true when the material differs', () {
      expect(
        buildPainter(material: material).shouldRepaint(
          buildPainter(material: DovahMaterial(layers: [solid(green)])),
        ),
        isTrue,
      );
    });

    test('Method shouldRepaint returns true when the corner style differs', () {
      expect(
        buildPainter(material: material).shouldRepaint(
          buildPainter(
            material: material,
            cornerStyle: DovahPanelCornerStyle.doubleBevel,
          ),
        ),
        isTrue,
      );
    });

    test(
      'Method shouldRepaint returns true when the corner radius differs',
      () {
        expect(
          buildPainter(
            material: material,
          ).shouldRepaint(buildPainter(material: material, cornerRadius: 4)),
          isTrue,
        );
      },
    );

    test('Method shouldRepaint returns true when the cut size differs', () {
      expect(
        buildPainter(
          material: material,
        ).shouldRepaint(buildPainter(material: material, cutSize: 12)),
        isTrue,
      );
    });
  });
}
