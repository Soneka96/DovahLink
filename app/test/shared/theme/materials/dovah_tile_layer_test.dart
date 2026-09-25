import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_tile_layer.dart';
import 'dovah_material_test_helpers.dart';

/// A 4x1 tile whose left half is red and right half is blue.
const DovahTileLayer redBlueTile = DovahTileLayer(
  tileSize: Size(4, 1),
  content: DovahLinearLayer(
    angleDegrees: 90,
    colors: [
      Color(0xFFFF0000),
      Color(0xFFFF0000),
      Color(0xFF0000FF),
      Color(0xFF0000FF),
    ],
    stops: [0, 0.5, 0.5, 1],
  ),
);

/// Exercises [DovahTileLayer]'s repeated shader, tile-local geometry, and equality.
void main() {
  group('Method createShader behaves correctly', () {
    testWidgets('Method createShader repeats the tile across the surface', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = (await tester.runAsync(
        () => paintDovahLayer(redBlueTile, const Size(16, 2)),
      ))!;

      expect(redOf(pixels[0]), 255);
      expect(redOf(pixels[1]), 255);
      expect(blueOf(pixels[2]), 255);
      expect(blueOf(pixels[3]), 255);
      expect(redOf(pixels[4]), 255);
      expect(blueOf(pixels[7]), 255);
      expect(redOf(pixels[12]), 255);
      expect(blueOf(pixels[15]), 255);
    });

    testWidgets('Method createShader repeats the tile down the surface', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = (await tester.runAsync(
        () => paintDovahLayer(redBlueTile, const Size(4, 6)),
      ))!;

      for (int row = 0; row < 6; row++) {
        expect(redOf(pixels[row * 4]), 255, reason: 'row $row');
        expect(blueOf(pixels[row * 4 + 3]), 255, reason: 'row $row');
      }
    });

    testWidgets('Method createShader lays the content out inside the tile', (
      WidgetTester tester,
    ) async {
      final List<Color> narrow = (await tester.runAsync(
        () => paintDovahLayer(redBlueTile, const Size(8, 1)),
      ))!;
      final List<Color> wide = (await tester.runAsync(
        () => paintDovahLayer(redBlueTile, const Size(40, 1)),
      ))!;

      expect(narrow.sublist(0, 8), wide.sublist(0, 8));
    });

    test('Method createShader returns normally for an empty size', () {
      expect(() => redBlueTile.createShader(Size.zero), returnsNormally);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for identical tiles', () {
      const DovahTileLayer same = DovahTileLayer(
        tileSize: Size(4, 1),
        content: DovahLinearLayer(
          angleDegrees: 90,
          colors: [
            Color(0xFFFF0000),
            Color(0xFFFF0000),
            Color(0xFF0000FF),
            Color(0xFF0000FF),
          ],
          stops: [0, 0.5, 0.5, 1],
        ),
      );

      expect(redBlueTile == same, isTrue);
      expect(redBlueTile.hashCode, same.hashCode);
    });

    test('Behavior equality fails when the tile size differs', () {
      const DovahTileLayer other = DovahTileLayer(
        tileSize: Size(8, 1),
        content: DovahLinearLayer(
          angleDegrees: 90,
          colors: [
            Color(0xFFFF0000),
            Color(0xFFFF0000),
            Color(0xFF0000FF),
            Color(0xFF0000FF),
          ],
          stops: [0, 0.5, 0.5, 1],
        ),
      );

      expect(redBlueTile == other, isFalse);
    });

    test('Behavior equality fails when the content differs', () {
      const DovahTileLayer other = DovahTileLayer(
        tileSize: Size(4, 1),
        content: DovahLinearLayer(
          angleDegrees: 0,
          colors: [Color(0xFFFF0000), Color(0xFF0000FF)],
          stops: [0, 1],
        ),
      );

      expect(redBlueTile == other, isFalse);
    });
  });
}
