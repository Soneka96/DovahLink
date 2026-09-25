import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'dovah_material_test_helpers.dart';

/// Exercises [DovahLinearLayer]'s shader, construction contract, and equality.
void main() {
  const DovahLinearLayer redToBlue = DovahLinearLayer(
    angleDegrees: 90,
    colors: [Color(0xFFFF0000), Color(0xFF0000FF)],
    stops: [0, 1],
  );

  group('Method createShader behaves correctly', () {
    testWidgets(
      'Method createShader paints the first stop at the start and the last at the end',
      (WidgetTester tester) async {
        const Size size = Size(100, 10);
        final List<Color> pixels = (await tester.runAsync(
          () => paintDovahLayer(redToBlue, size),
        ))!;

        expect(redOf(pixels[2]), greaterThan(240));
        expect(blueOf(pixels[2]), lessThan(15));
        expect(redOf(pixels[97]), lessThan(15));
        expect(blueOf(pixels[97]), greaterThan(240));
      },
    );

    testWidgets(
      'Method createShader places stops as fractions of the gradient line',
      (WidgetTester tester) async {
        const DovahLinearLayer hardSplit = DovahLinearLayer(
          angleDegrees: 90,
          colors: [
            Color(0xFFFF0000),
            Color(0xFFFF0000),
            Color(0xFF0000FF),
            Color(0xFF0000FF),
          ],
          stops: [0, 0.25, 0.25, 1],
        );
        final List<Color> pixels = (await tester.runAsync(
          () => paintDovahLayer(hardSplit, const Size(100, 4)),
        ))!;

        expect(redOf(pixels[20]), 255);
        expect(blueOf(pixels[30]), 255);
      },
    );

    testWidgets(
      'Method createShader fades to transparent without darkening the color',
      (WidgetTester tester) async {
        const DovahLinearLayer fade = DovahLinearLayer(
          angleDegrees: 90,
          colors: [Color(0xFFFFFFFF), Color(0x00000000)],
          stops: [0, 1],
        );
        final List<Color> pixels = (await tester.runAsync(
          () => paintDovahLayer(fade, const Size(100, 4)),
        ))!;

        expect(redOf(pixels[50]), greaterThan(240));
        expect(alphaOf(pixels[50]), inInclusiveRange(120, 135));
      },
    );

    test(
      'Method createShader rejects colors and stops of different lengths',
      () {
        const DovahLinearLayer mismatched = DovahLinearLayer(
          angleDegrees: 90,
          colors: [Color(0xFFFF0000), Color(0xFF0000FF)],
          stops: [0],
        );

        expect(
          () => mismatched.createShader(const Size(10, 10)),
          throwsArgumentError,
        );
      },
    );

    test('Method createShader returns normally for an empty size', () {
      expect(() => redToBlue.createShader(Size.zero), returnsNormally);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for identical recipes', () {
      const DovahLinearLayer same = DovahLinearLayer(
        angleDegrees: 90,
        colors: [Color(0xFFFF0000), Color(0xFF0000FF)],
        stops: [0, 1],
      );

      expect(redToBlue == same, isTrue);
      expect(redToBlue.hashCode, same.hashCode);
    });

    test('Behavior equality fails when the angle differs', () {
      const DovahLinearLayer other = DovahLinearLayer(
        angleDegrees: 91,
        colors: [Color(0xFFFF0000), Color(0xFF0000FF)],
        stops: [0, 1],
      );

      expect(redToBlue == other, isFalse);
    });

    test('Behavior equality fails when the colors differ', () {
      const DovahLinearLayer other = DovahLinearLayer(
        angleDegrees: 90,
        colors: [Color(0xFF00FF00), Color(0xFF0000FF)],
        stops: [0, 1],
      );

      expect(redToBlue == other, isFalse);
    });

    test('Behavior equality fails when the stops differ', () {
      const DovahLinearLayer other = DovahLinearLayer(
        angleDegrees: 90,
        colors: [Color(0xFFFF0000), Color(0xFF0000FF)],
        stops: [0, 0.5],
      );

      expect(redToBlue == other, isFalse);
    });
  });
}
