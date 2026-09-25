import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_stripe_layer.dart';
import 'dovah_material_test_helpers.dart';

/// Exercises [DovahStripeLayer]'s repeating shader, period, construction contract, and equality.
void main() {
  DovahStripeLayer buildStripes({double angleDegrees = 90}) => DovahStripeLayer(
    angleDegrees: angleDegrees,
    colors: const [
      Color(0xFFFF0000),
      Color(0xFFFF0000),
      Color(0xFF0000FF),
      Color(0xFF0000FF),
    ],
    stopsPx: const [0, 4, 4, 8],
  );

  group('Property period behaves correctly', () {
    test('Property period is the last stop in logical pixels', () {
      expect(buildStripes().period, isA<double>());
      expect(buildStripes().period, 8);
    });
  });

  group('Method createShader behaves correctly', () {
    testWidgets(
      'Method createShader repeats the stripes horizontally at 90 degrees',
      (WidgetTester tester) async {
        final List<Color> pixels = (await tester.runAsync(
          () => paintDovahLayer(buildStripes(), const Size(16, 2)),
        ))!;

        expect(redOf(pixels[1]), 255);
        expect(blueOf(pixels[5]), 255);
        expect(redOf(pixels[9]), 255);
        expect(blueOf(pixels[13]), 255);
      },
    );

    testWidgets(
      'Method createShader repeats the stripes vertically at 180 degrees',
      (WidgetTester tester) async {
        final List<Color> pixels = (await tester.runAsync(
          () => paintDovahLayer(
            buildStripes(angleDegrees: 180),
            const Size(2, 16),
          ),
        ))!;

        expect(redOf(pixels[1 * 2]), 255);
        expect(blueOf(pixels[5 * 2]), 255);
        expect(redOf(pixels[9 * 2]), 255);
      },
    );

    test(
      'Method createShader rejects colors and stops of different lengths',
      () {
        const DovahStripeLayer mismatched = DovahStripeLayer(
          angleDegrees: 90,
          colors: [Color(0xFFFF0000)],
          stopsPx: [0, 4],
        );

        expect(
          () => mismatched.createShader(const Size(10, 10)),
          throwsArgumentError,
        );
      },
    );

    test('Method createShader returns normally for an empty size', () {
      expect(() => buildStripes().createShader(Size.zero), returnsNormally);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for identical recipes', () {
      expect(buildStripes() == buildStripes(), isTrue);
      expect(buildStripes().hashCode, buildStripes().hashCode);
    });

    test('Behavior equality fails when the angle differs', () {
      expect(buildStripes() == buildStripes(angleDegrees: 91), isFalse);
    });

    test('Behavior equality fails when the colors differ', () {
      const DovahStripeLayer other = DovahStripeLayer(
        angleDegrees: 90,
        colors: [
          Color(0xFF00FF00),
          Color(0xFFFF0000),
          Color(0xFF0000FF),
          Color(0xFF0000FF),
        ],
        stopsPx: [0, 4, 4, 8],
      );

      expect(buildStripes() == other, isFalse);
    });

    test('Behavior equality fails when the stops differ', () {
      const DovahStripeLayer other = DovahStripeLayer(
        angleDegrees: 90,
        colors: [
          Color(0xFFFF0000),
          Color(0xFFFF0000),
          Color(0xFF0000FF),
          Color(0xFF0000FF),
        ],
        stopsPx: [0, 5, 5, 10],
      );

      expect(buildStripes() == other, isFalse);
    });
  });
}
