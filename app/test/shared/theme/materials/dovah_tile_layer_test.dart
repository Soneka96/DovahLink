import 'dart:typed_data';
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

/// Builds a one-pixel tile with a unique color for cache-order tests.
///
/// [index] identifies the tile's color.
DovahTileLayer buildCacheProbeTile(int index) => DovahTileLayer(
  tileSize: const Size(1, 1),
  content: DovahLinearLayer(
    angleDegrees: 0,
    colors: [Color(0xFF800000 | index), const Color(0xFFFFFFFF)],
    stops: const [0, 1],
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

    test(
      'Method createShader reuses equal recipes and keeps different recipes separate',
      () {
        const DovahTileLayer recipe = DovahTileLayer(
          tileSize: Size(3, 2),
          content: DovahLinearLayer(
            angleDegrees: 90,
            colors: [Color(0xFF124578), Color(0xFF90ABCD)],
            stops: [0, 1],
          ),
        );
        const DovahTileLayer equivalentRecipe = DovahTileLayer(
          tileSize: Size(3, 2),
          content: DovahLinearLayer(
            angleDegrees: 90,
            colors: [Color(0xFF124578), Color(0xFF90ABCD)],
            stops: [0, 1],
          ),
        );
        const DovahTileLayer differentRecipe = DovahTileLayer(
          tileSize: Size(3, 2),
          content: DovahLinearLayer(
            angleDegrees: 90,
            colors: [Color(0xFF124578), Color(0xFF90ABCE)],
            stops: [0, 1],
          ),
        );
        final int before = DovahTileLayer.tileRasterizationCount;

        final Shader firstShader = recipe.createShader(const Size(30, 20));
        final int afterFirst = DovahTileLayer.tileRasterizationCount;
        final Shader equivalentShader = equivalentRecipe.createShader(
          const Size(30, 20),
        );

        expect(afterFirst, before + 1);
        expect(DovahTileLayer.tileRasterizationCount, afterFirst);
        expect(identical(firstShader, equivalentShader), isFalse);
        firstShader.dispose();
        equivalentShader.dispose();

        differentRecipe.createShader(const Size(30, 20)).dispose();

        expect(DovahTileLayer.tileRasterizationCount, afterFirst + 1);
      },
    );

    test(
      'Method createShader keeps the raster cache within its fixed limit',
      () {
        for (int index = 0; index < 40; index++) {
          DovahTileLayer(
            tileSize: const Size(1, 1),
            content: DovahLinearLayer(
              angleDegrees: 0,
              colors: [Color(0xFF000000 | index), const Color(0xFFFFFFFF)],
              stops: const [0, 1],
            ),
          ).createShader(const Size(2, 2)).dispose();
        }

        expect(DovahTileLayer.cachedTileCount, 32);
      },
    );

    testWidgets(
      'Method createShader keeps a shader usable after its cached image is evicted',
      (WidgetTester tester) async {
        final List<Color> pixels = (await tester.runAsync(() async {
          final Shader retainedShader = redBlueTile.createShader(
            const Size(8, 1),
          );
          for (int index = 0; index < 40; index++) {
            final Shader shader = DovahTileLayer(
              tileSize: const Size(1, 1),
              content: DovahLinearLayer(
                angleDegrees: 0,
                colors: [Color(0xFF010000 | index), const Color(0xFFFFFFFF)],
                stops: const [0, 1],
              ),
            ).createShader(const Size(2, 2));
            shader.dispose();
          }

          final PictureRecorder recorder = PictureRecorder();
          Canvas(recorder).drawRect(
            const Rect.fromLTWH(0, 0, 8, 1),
            Paint()..shader = retainedShader,
          );
          final Picture picture = recorder.endRecording();
          try {
            final Image image = picture.toImageSync(8, 1);
            try {
              final ByteData bytes = (await image.toByteData(
                format: ImageByteFormat.rawStraightRgba,
              ))!;
              return List<Color>.generate(
                8,
                (int index) => Color.fromARGB(
                  bytes.getUint8(index * 4 + 3),
                  bytes.getUint8(index * 4),
                  bytes.getUint8(index * 4 + 1),
                  bytes.getUint8(index * 4 + 2),
                ),
              );
            } finally {
              image.dispose();
            }
          } finally {
            picture.dispose();
            retainedShader.dispose();
          }
        }))!;

        expect(redOf(pixels[0]), 255);
        expect(blueOf(pixels[3]), 255);
        expect(redOf(pixels[4]), 255);
        expect(blueOf(pixels[7]), 255);
      },
    );

    test('Method createShader promotes recent entries before eviction', () {
      for (int index = 100; index < 132; index++) {
        buildCacheProbeTile(index).createShader(const Size(2, 2)).dispose();
      }
      final int afterFill = DovahTileLayer.tileRasterizationCount;

      buildCacheProbeTile(100).createShader(const Size(2, 2)).dispose();
      buildCacheProbeTile(132).createShader(const Size(2, 2)).dispose();
      expect(DovahTileLayer.tileRasterizationCount, afterFill + 1);

      buildCacheProbeTile(100).createShader(const Size(2, 2)).dispose();
      expect(DovahTileLayer.tileRasterizationCount, afterFill + 1);

      buildCacheProbeTile(101).createShader(const Size(2, 2)).dispose();
      expect(DovahTileLayer.tileRasterizationCount, afterFill + 2);
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
