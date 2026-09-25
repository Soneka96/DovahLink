import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring_painter.dart';
import '../materials/dovah_material_test_helpers.dart';

/// Exercises [DovahFocusRingPainter]'s outline geometry and repaint contract.
void main() {
  const Size boxSize = Size(100, 60);
  const double margin = 20;
  const Color accent = Color(0xFF8ED6FF);

  Future<List<Color>> paint(
    WidgetTester tester,
    DovahFocusRingPainter painter,
  ) async => (await tester.runAsync(
    () => paintDovahPainter(painter, boxSize, margin: margin),
  ))!;

  Color pixelAt(List<Color> pixels, int x, int y) =>
      pixels[(y + margin.toInt()) * 140 + x + margin.toInt()];

  group('Method paint behaves correctly', () {
    testWidgets(
      'Method paint draws a 2px line starting 3px outside the box edge',
      (WidgetTester tester) async {
        final List<Color> pixels = await paint(
          tester,
          const DovahFocusRingPainter(color: accent, cornerRadius: 0),
        );

        expect(pixelAt(pixels, 50, -4), accent);
        expect(pixelAt(pixels, 50, -5), accent);
        expect(alphaOf(pixelAt(pixels, 50, -3)), 0);
        expect(alphaOf(pixelAt(pixels, 50, -6)), 0);
        expect(pixelAt(pixels, 103, 30), accent);
        expect(alphaOf(pixelAt(pixels, 50, 30)), 0);
      },
    );

    testWidgets('Method paint keeps a square outline sharp at the corners', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        const DovahFocusRingPainter(color: accent, cornerRadius: 0),
      );

      expect(pixelAt(pixels, -5, -5), accent);
    });

    testWidgets('Method paint rounds the outline corner for a rounded box', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        const DovahFocusRingPainter(color: accent, cornerRadius: 13),
      );

      expect(alphaOf(pixelAt(pixels, -5, -5)), 0);
      expect(pixelAt(pixels, 50, -5), accent);
    });
  });

  group('Method shouldRepaint behaves correctly', () {
    const DovahFocusRingPainter painter = DovahFocusRingPainter(
      color: accent,
      cornerRadius: 4,
    );

    test('Method shouldRepaint returns false when nothing changed', () {
      expect(
        painter.shouldRepaint(
          const DovahFocusRingPainter(color: accent, cornerRadius: 4),
        ),
        isFalse,
      );
    });

    test('Method shouldRepaint returns true when the color differs', () {
      expect(
        painter.shouldRepaint(
          const DovahFocusRingPainter(
            color: Color(0xFF000000),
            cornerRadius: 4,
          ),
        ),
        isTrue,
      );
    });

    test('Method shouldRepaint returns true when the radius differs', () {
      expect(
        painter.shouldRepaint(
          const DovahFocusRingPainter(color: accent, cornerRadius: 5),
        ),
        isTrue,
      );
    });
  });

  group('Method hitTest behaves correctly', () {
    test('Method hitTest never takes hits', () {
      expect(
        const DovahFocusRingPainter(
          color: accent,
          cornerRadius: 0,
        ).hitTest(const Offset(1, 1)),
        isFalse,
      );
    });
  });
}
