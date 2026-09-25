import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_radial_layer.dart';
import 'dovah_material_test_helpers.dart';

/// Exercises [DovahRadialLayer]'s circle and farthest-corner ellipse shaders, construction
/// contract, and equality.
void main() {
  const List<Color> whiteToClear = [Color(0xFFFFFFFF), Color(0x00FFFFFF)];

  group('Method createShader behaves correctly', () {
    testWidgets(
      'Method createShader paints a circle of the given radius around the center',
      (WidgetTester tester) async {
        const DovahRadialLayer speck = DovahRadialLayer(
          center: Offset(0.5, 0.5),
          colors: whiteToClear,
          stops: [0, 1],
          radius: 10,
        );
        final List<Color> pixels = (await tester.runAsync(
          () => paintDovahLayer(speck, const Size(40, 40)),
        ))!;

        expect(alphaOf(pixels[20 * 40 + 20]), greaterThan(235));
        expect(alphaOf(pixels[20 * 40 + 35]), 0);
        expect(alphaOf(pixels[35 * 40 + 20]), 0);
      },
    );

    testWidgets(
      'Method createShader fades an ellipse evenly to the farthest corner without a radius',
      (WidgetTester tester) async {
        const DovahRadialLayer glow = DovahRadialLayer(
          center: Offset(0.5, 0.5),
          colors: whiteToClear,
          stops: [0, 1],
        );
        final List<Color> pixels = (await tester.runAsync(
          () => paintDovahLayer(glow, const Size(100, 50)),
        ))!;
        final int alongWidth = alphaOf(pixels[25 * 100 + 85]);
        final int alongHeight = alphaOf(pixels[42 * 100 + 50]);

        expect(alongWidth, inInclusiveRange(120, 140));
        expect((alongWidth - alongHeight).abs(), lessThan(8));
      },
    );

    testWidgets(
      'Method createShader reaches the farthest corner from an off-center origin',
      (WidgetTester tester) async {
        const DovahRadialLayer glow = DovahRadialLayer(
          center: Offset(0.25, 0.5),
          colors: whiteToClear,
          stops: [0, 1],
        );
        final List<Color> pixels = (await tester.runAsync(
          () => paintDovahLayer(glow, const Size(100, 50)),
        ))!;

        expect(alphaOf(pixels[25 * 100 + 25]), greaterThan(235));
        expect(alphaOf(pixels[25 * 100 + 78]), inInclusiveRange(120, 140));
        expect(alphaOf(pixels[25 * 100 + 99]), lessThan(90));
      },
    );

    testWidgets(
      'Method createShader fades a circular gradient evenly in every direction',
      (WidgetTester tester) async {
        const DovahRadialLayer glow = DovahRadialLayer(
          center: Offset(0.5, 0.5),
          colors: whiteToClear,
          stops: [0, 1],
          circular: true,
        );
        final List<Color> pixels = (await tester.runAsync(
          () => paintDovahLayer(glow, const Size(100, 50)),
        ))!;
        final int alongWidth = alphaOf(pixels[25 * 100 + 75]);
        final int alongHeight = alphaOf(pixels[49 * 100 + 50]);

        expect(alongWidth, inInclusiveRange(100, 200));
        expect(alongHeight, greaterThan(alongWidth));
      },
    );

    testWidgets(
      'Method createShader repeats rings every radius when repeating',
      (WidgetTester tester) async {
        const DovahRadialLayer rings = DovahRadialLayer(
          center: Offset(0, 0.5),
          colors: [
            Color(0xFFFFFFFF),
            Color(0xFFFFFFFF),
            Color(0x00FFFFFF),
            Color(0x00FFFFFF),
          ],
          stops: [0, 0.2, 0.2, 1],
          radius: 10,
          repeating: true,
        );
        final List<Color> pixels = (await tester.runAsync(
          () => paintDovahLayer(rings, const Size(40, 1)),
        ))!;

        expect(alphaOf(pixels[0]), 255);
        expect(alphaOf(pixels[5]), 0);
        expect(alphaOf(pixels[10]), 255);
        expect(alphaOf(pixels[15]), 0);
      },
    );

    testWidgets(
      'Method createShader anchors the gradient at the fractional center',
      (WidgetTester tester) async {
        const DovahRadialLayer corner = DovahRadialLayer(
          center: Offset.zero,
          colors: whiteToClear,
          stops: [0, 1],
          radius: 12,
        );
        final List<Color> pixels = (await tester.runAsync(
          () => paintDovahLayer(corner, const Size(40, 40)),
        ))!;

        expect(alphaOf(pixels[0]), greaterThan(235));
        expect(alphaOf(pixels[39 * 40 + 39]), 0);
      },
    );

    test(
      'Method createShader rejects colors and stops of different lengths',
      () {
        const DovahRadialLayer mismatched = DovahRadialLayer(
          center: Offset(0.5, 0.5),
          colors: whiteToClear,
          stops: [0],
        );

        expect(
          () => mismatched.createShader(const Size(10, 10)),
          throwsArgumentError,
        );
      },
    );

    test('Method createShader returns normally for an empty size', () {
      const DovahRadialLayer glow = DovahRadialLayer(
        center: Offset(0.5, 0.5),
        colors: whiteToClear,
        stops: [0, 1],
      );

      expect(() => glow.createShader(Size.zero), returnsNormally);
    });
  });

  group('Behavior equality behaves correctly', () {
    const DovahRadialLayer glow = DovahRadialLayer(
      center: Offset(0.5, 0.5),
      colors: whiteToClear,
      stops: [0, 1],
    );

    test('Behavior equality holds for identical recipes', () {
      const DovahRadialLayer same = DovahRadialLayer(
        center: Offset(0.5, 0.5),
        colors: whiteToClear,
        stops: [0, 1],
      );

      expect(glow == same, isTrue);
      expect(glow.hashCode, same.hashCode);
    });

    test('Behavior equality fails when the radius differs', () {
      const DovahRadialLayer other = DovahRadialLayer(
        center: Offset(0.5, 0.5),
        colors: whiteToClear,
        stops: [0, 1],
        radius: 4,
      );

      expect(glow == other, isFalse);
    });

    test('Behavior equality fails when circular differs', () {
      const DovahRadialLayer other = DovahRadialLayer(
        center: Offset(0.5, 0.5),
        colors: whiteToClear,
        stops: [0, 1],
        circular: true,
      );

      expect(glow == other, isFalse);
    });

    test('Behavior equality fails when repeating differs', () {
      const DovahRadialLayer other = DovahRadialLayer(
        center: Offset(0.5, 0.5),
        colors: whiteToClear,
        stops: [0, 1],
        repeating: true,
      );

      expect(glow == other, isFalse);
    });

    test('Behavior equality fails when the center differs', () {
      const DovahRadialLayer other = DovahRadialLayer(
        center: Offset(0.25, 0.5),
        colors: whiteToClear,
        stops: [0, 1],
      );

      expect(glow == other, isFalse);
    });

    test('Behavior equality fails when the colors differ', () {
      const DovahRadialLayer other = DovahRadialLayer(
        center: Offset(0.5, 0.5),
        colors: [Color(0xFF000000), Color(0x00000000)],
        stops: [0, 1],
      );

      expect(glow == other, isFalse);
    });

    test('Behavior equality fails when the stops differ', () {
      const DovahRadialLayer other = DovahRadialLayer(
        center: Offset(0.5, 0.5),
        colors: whiteToClear,
        stops: [0, 0.5],
      );

      expect(glow == other, isFalse);
    });
  });
}
