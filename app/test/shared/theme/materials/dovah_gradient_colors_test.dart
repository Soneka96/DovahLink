import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_gradient_colors.dart';

/// Exercises [matchTransparentStops] against CSS premultiplied-alpha fade semantics.
void main() {
  const Color red = Color(0xFFFF0000);
  const Color blue = Color(0xFF0000FF);
  const Color clear = Color(0x00000000);

  group('Method matchTransparentStops behaves correctly', () {
    test(
      'Method matchTransparentStops gives a trailing transparent stop the previous color',
      () {
        expect(matchTransparentStops([red, clear]), [
          red,
          red.withValues(alpha: 0),
        ]);
      },
    );

    test(
      'Method matchTransparentStops gives a leading transparent stop the next color',
      () {
        expect(matchTransparentStops([clear, clear, red]), [
          red.withValues(alpha: 0),
          red.withValues(alpha: 0),
          red,
        ]);
      },
    );

    test(
      'Method matchTransparentStops prefers the previous color for a transparent stop between colors',
      () {
        expect(matchTransparentStops([red, clear, blue]), [
          red,
          red.withValues(alpha: 0),
          blue,
        ]);
      },
    );

    test('Method matchTransparentStops keeps visible stops unchanged', () {
      const List<Color> colors = [red, blue];

      expect(matchTransparentStops(colors), colors);
    });

    test(
      'Method matchTransparentStops keeps a list with no visible stop unchanged',
      () {
        expect(matchTransparentStops([clear, clear]), [clear, clear]);
      },
    );

    test('Method matchTransparentStops does not modify its input', () {
      final List<Color> colors = [red, clear];

      matchTransparentStops(colors);

      expect(colors, [red, clear]);
    });
  });
}
