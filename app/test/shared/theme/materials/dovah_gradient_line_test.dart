import 'dart:math' as math;
import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_gradient_line.dart';

/// Exercises [buildDovahGradientLine] against the CSS gradient-line definition.
void main() {
  group('Method buildDovahGradientLine behaves correctly', () {
    test('Method buildDovahGradientLine runs left to right at 90 degrees', () {
      final ({Offset start, Offset end}) line = buildDovahGradientLine(
        const Size(200, 100),
        90,
      );

      expect(line.start.dx, closeTo(0, 1e-9));
      expect(line.start.dy, closeTo(50, 1e-9));
      expect(line.end.dx, closeTo(200, 1e-9));
      expect(line.end.dy, closeTo(50, 1e-9));
    });

    test('Method buildDovahGradientLine runs top to bottom at 180 degrees', () {
      final ({Offset start, Offset end}) line = buildDovahGradientLine(
        const Size(200, 100),
        180,
      );

      expect(line.start.dx, closeTo(100, 1e-9));
      expect(line.start.dy, closeTo(0, 1e-9));
      expect(line.end.dx, closeTo(100, 1e-9));
      expect(line.end.dy, closeTo(100, 1e-9));
    });

    test('Method buildDovahGradientLine runs bottom to top at 0 degrees', () {
      final ({Offset start, Offset end}) line = buildDovahGradientLine(
        const Size(200, 100),
        0,
      );

      expect(line.start.dy, closeTo(100, 1e-9));
      expect(line.end.dy, closeTo(0, 1e-9));
    });

    test(
      'Method buildDovahGradientLine reaches the corners of a square at 45 degrees',
      () {
        final ({Offset start, Offset end}) line = buildDovahGradientLine(
          const Size(100, 100),
          45,
        );

        expect(line.start.dx, closeTo(0, 1e-9));
        expect(line.start.dy, closeTo(100, 1e-9));
        expect(line.end.dx, closeTo(100, 1e-9));
        expect(line.end.dy, closeTo(0, 1e-9));
      },
    );

    test(
      'Method buildDovahGradientLine uses the CSS length and passes through the center at 116 degrees',
      () {
        const Size size = Size(200, 100);
        const double radians = 116 * math.pi / 180;
        final double expectedLength =
            size.width * math.sin(radians).abs() +
            size.height * math.cos(radians).abs();
        final ({Offset start, Offset end}) line = buildDovahGradientLine(
          size,
          116,
        );
        final Offset midpoint = (line.start + line.end) / 2;

        expect((line.end - line.start).distance, closeTo(expectedLength, 1e-9));
        expect(midpoint.dx, closeTo(100, 1e-9));
        expect(midpoint.dy, closeTo(50, 1e-9));
      },
    );

    test(
      'Method buildDovahGradientLine collapses to the center for an empty size',
      () {
        final ({Offset start, Offset end}) line = buildDovahGradientLine(
          Size.zero,
          116,
        );

        expect(line.start, Offset.zero);
        expect(line.end, Offset.zero);
      },
    );
  });
}
