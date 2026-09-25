import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_layers_painter.dart';
import '../materials/dovah_material_test_helpers.dart';

/// A layer that fills the surface with [color].
DovahLinearLayer solid(Color color) => DovahLinearLayer(
  angleDegrees: 90,
  colors: [color, color],
  stops: const [0, 1],
);

/// Exercises [DovahLayersPainter]'s layer order, opacity, and repaint contract.
void main() {
  const Size size = Size(20, 10);
  const Color red = Color(0xFFFF0000);
  const Color blue = Color(0xFF0000FF);

  Future<List<Color>> paint(
    WidgetTester tester,
    DovahLayersPainter painter,
  ) async => (await tester.runAsync(() => paintDovahPainter(painter, size)))!;

  group('Method paint behaves correctly', () {
    testWidgets('Method paint fills the whole area with the layer', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        DovahLayersPainter(layers: [solid(red)]),
      );

      expect(pixels.first, red);
      expect(pixels.last, red);
    });

    testWidgets('Method paint paints later layers over earlier ones', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        DovahLayersPainter(layers: [solid(red), solid(blue)]),
      );

      expect(pixels.first, blue);
    });

    testWidgets('Method paint scales every layer by the opacity', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        DovahLayersPainter(layers: [solid(red)], opacity: 0.5),
      );

      expect(alphaOf(pixels.first), inInclusiveRange(126, 130));
      expect(redOf(pixels.first), 255);
    });

    testWidgets('Method paint paints nothing visible at zero opacity', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        DovahLayersPainter(layers: [solid(red)], opacity: 0),
      );

      expect(alphaOf(pixels.first), 0);
    });

    testWidgets('Method paint paints at full strength at full opacity', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        DovahLayersPainter(layers: [solid(red)]),
      );

      expect(alphaOf(pixels.first), 255);
    });

    testWidgets('Method paint paints nothing without layers', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        const DovahLayersPainter(layers: []),
      );

      expect(alphaOf(pixels.first), 0);
    });

    test('Method paint draws nothing for an empty size', () {
      for (final Size empty in const [Size.zero, Size(0, 10), Size(20, 0)]) {
        expect(
          () => DovahLayersPainter(
            layers: [solid(red)],
          ).paint(Canvas(PictureRecorder()), empty),
          returnsNormally,
        );
      }
    });
  });

  group('Method shouldRepaint behaves correctly', () {
    test('Method shouldRepaint returns false for equal layers and opacity', () {
      expect(
        DovahLayersPainter(
          layers: [solid(red)],
        ).shouldRepaint(DovahLayersPainter(layers: [solid(red)])),
        isFalse,
      );
    });

    test('Method shouldRepaint returns true when the layers differ', () {
      expect(
        DovahLayersPainter(
          layers: [solid(red)],
        ).shouldRepaint(DovahLayersPainter(layers: [solid(blue)])),
        isTrue,
      );
    });

    test('Method shouldRepaint returns true when the opacity differs', () {
      expect(
        DovahLayersPainter(
          layers: [solid(red)],
        ).shouldRepaint(DovahLayersPainter(layers: [solid(red)], opacity: 0.5)),
        isTrue,
      );
    });
  });
}
