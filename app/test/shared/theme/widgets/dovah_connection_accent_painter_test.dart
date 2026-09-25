import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_connection_accent.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_accent_painter.dart';
import '../materials/dovah_material_test_helpers.dart';

/// The size of the card every probe paints.
const Size cardSize = Size(400, 80);

/// Returns the pixel at ([x], [y]) of an image [cardSize] wide.
Color pixelAt(List<Color> pixels, int x, int y) =>
    pixels[y * cardSize.width.toInt() + x];

/// Whether any pixel in the 3x3 block around ([x], [y]) is painted.
bool paintedNear(List<Color> pixels, int x, int y) => [
  for (int dy = -1; dy <= 1; dy++)
    for (int dx = -1; dx <= 1; dx++) pixelAt(pixels, x + dx, y + dy),
].any((Color color) => alphaOf(color) > 0);

/// How many pixels of [pixels] are painted.
int paintedCount(List<Color> pixels) =>
    pixels.where((Color color) => alphaOf(color) > 0).length;

/// Exercises [DovahConnectionAccentPainter]'s parts, passes, and repaint contract.
void main() {
  final DovahConnectionAccent frostbound =
      DovahThemeMaterials.frostbound.connectionAccent;
  final DovahConnectionAccent dovah =
      DovahThemeMaterials.dovah.connectionAccent;

  Future<List<Color>> paint(
    WidgetTester tester, {
    required DovahConnectionAccent accent,
    bool available = false,
    bool showLinkLine = true,
    bool aboveContent = false,
  }) async => (await tester.runAsync(
    () => paintDovahPainter(
      DovahConnectionAccentPainter(
        accent: accent,
        available: available,
        showLinkLine: showLinkLine,
        aboveContent: aboveContent,
      ),
      cardSize,
    ),
  ))!;

  group('Method paint draws the available edge', () {
    testWidgets('Method paint fills the right edge of an available card', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        accent: frostbound,
        available: true,
      );

      expect(pixelAt(pixels, 397, 40), const Color(0xFF86B4C7));
      expect(pixelAt(pixels, 398, 40), const Color(0xFF86B4C7));
      expect(alphaOf(pixelAt(pixels, 396, 40)), 0);
    });

    testWidgets('Method paint leaves the border column of the edge clear', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        accent: frostbound,
        available: true,
      );

      expect(alphaOf(pixelAt(pixels, 399, 40)), 0);
    });

    testWidgets('Method paint draws no edge for a card that is not available', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(tester, accent: frostbound);

      expect(alphaOf(pixelAt(pixels, 398, 40)), 0);
    });

    testWidgets('Method paint keeps the edge beneath the content only', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        accent: frostbound,
        available: true,
        aboveContent: true,
      );

      expect(alphaOf(pixelAt(pixels, 398, 40)), 0);
    });
  });

  group('Method paint draws the overlay', () {
    testWidgets('Method paint draws Frostbound fracture lines above content', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        accent: frostbound,
        aboveContent: true,
      );

      expect(paintedCount(pixels), greaterThan(0));
      expect(alphaOf(pixelAt(pixels, 0, 0)), 0);
    });

    testWidgets('Method paint draws no fracture lines beneath the content', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(tester, accent: frostbound);

      expect(
        pixels.where((Color color) => color == const Color(0x00000000)).length,
        pixels.length,
      );
    });

    testWidgets('Method paint draws nothing above the content for Dovah', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        accent: dovah,
        aboveContent: true,
      );

      expect(paintedCount(pixels), 0);
    });

    testWidgets('Method paint keeps the overlay inside the border', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        accent: frostbound,
        aboveContent: true,
      );

      for (int x = 0; x < 400; x++) {
        expect(alphaOf(pixelAt(pixels, x, 0)), 0);
        expect(alphaOf(pixelAt(pixels, x, 79)), 0);
      }
    });
  });

  group('Method paint draws the link line', () {
    testWidgets('Method paint draws a one-pixel line at half the card height', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(tester, accent: dovah);

      expect(alphaOf(pixelAt(pixels, 100, 40)), greaterThan(0));
      expect(alphaOf(pixelAt(pixels, 100, 38)), 0);
      expect(alphaOf(pixelAt(pixels, 100, 42)), 0);
    });

    testWidgets('Method paint starts the line 55px in and ends it 90px short', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(tester, accent: dovah);

      expect(alphaOf(pixelAt(pixels, 55, 40)), 0);
      expect(alphaOf(pixelAt(pixels, 58, 40)), greaterThan(0));
      expect(alphaOf(pixelAt(pixels, 306, 40)), greaterThan(0));
      expect(alphaOf(pixelAt(pixels, 312, 40)), 0);
    });

    testWidgets('Method paint runs the line from ember to ice', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(tester, accent: dovah);

      expect(
        redOf(pixelAt(pixels, 58, 40)),
        greaterThan(blueOf(pixelAt(pixels, 58, 40))),
      );
      expect(
        blueOf(pixelAt(pixels, 306, 40)),
        greaterThan(redOf(pixelAt(pixels, 306, 40))),
      );
    });

    testWidgets('Method paint fades the line by its opacity', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(tester, accent: dovah);

      // The line's end is 0.75 alpha at 0.6 opacity: about 0.45 of 255.
      expect(alphaOf(pixelAt(pixels, 58, 40)), inInclusiveRange(105, 125));
    });

    testWidgets('Method paint hides the line at narrow widths', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        accent: dovah,
        showLinkLine: false,
      );

      expect(alphaOf(pixelAt(pixels, 100, 40)), 0);
    });

    testWidgets('Method paint draws no line for a theme without one', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        accent: frostbound,
        aboveContent: true,
      );

      expect(alphaOf(pixelAt(pixels, 100, 40)), lessThan(20));
    });
  });

  group('Method paint draws the corner outline', () {
    testWidgets(
      'Method paint draws the diamond outline reaching in from the corner',
      (WidgetTester tester) async {
        final List<Color> pixels = await paint(tester, accent: dovah);

        // The outline's upper-left edge passes through its midpoint at (378, 62).
        expect(paintedNear(pixels, 378, 62), isTrue);
        expect(paintedNear(pixels, 390, 50), isTrue);
      },
    );

    testWidgets('Method paint leaves the diamond hollow', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(tester, accent: dovah);

      expect(alphaOf(pixelAt(pixels, 397, 75)), 0);
      expect(alphaOf(pixelAt(pixels, 200, 60)), 0);
    });

    testWidgets(
      'Method paint draws the outline on the same pass as the overlay',
      (WidgetTester tester) async {
        final List<Color> beneath = await paint(tester, accent: frostbound);
        final List<Color> above = await paint(
          tester,
          accent: frostbound,
          aboveContent: true,
        );

        expect(paintedNear(beneath, 378, 62), isFalse);
        expect(paintedNear(above, 378, 62), isTrue);
      },
    );

    testWidgets('Method paint draws no outline when the accent has none', (
      WidgetTester tester,
    ) async {
      final List<Color> pixels = await paint(
        tester,
        accent: DovahConnectionAccent.none,
      );

      expect(paintedCount(pixels), 0);
    });
  });

  group('Method paint handles degenerate sizes', () {
    test(
      'Method paint draws nothing into a card no larger than its border',
      () {
        expect(
          () => const DovahConnectionAccentPainter(
            accent: DovahConnectionAccent(
              cornerOutline: Color(0xFFFFFFFF),
              availableEdge: Color(0xFFFFFFFF),
            ),
            available: true,
            showLinkLine: true,
            aboveContent: false,
          ).paint(Canvas(PictureRecorder()), const Size(2, 2)),
          returnsNormally,
        );
      },
    );

    test('Method paint skips the link line on a card too narrow for it', () {
      expect(
        () => const DovahConnectionAccentPainter(
          accent: DovahConnectionAccent(
            linkLayer: DovahLinearLayer(
              angleDegrees: 90,
              colors: [Color(0xFF000000), Color(0xFFFFFFFF)],
              stops: [0, 1],
            ),
          ),
          available: false,
          showLinkLine: true,
          aboveContent: false,
        ).paint(Canvas(PictureRecorder()), const Size(100, 60)),
        returnsNormally,
      );
    });
  });

  group('Method shouldRepaint behaves correctly', () {
    const DovahConnectionAccentPainter painter = DovahConnectionAccentPainter(
      accent: DovahConnectionAccent(),
      available: false,
      showLinkLine: true,
      aboveContent: false,
    );

    test('Method shouldRepaint returns false when nothing changed', () {
      expect(
        painter.shouldRepaint(
          const DovahConnectionAccentPainter(
            accent: DovahConnectionAccent(),
            available: false,
            showLinkLine: true,
            aboveContent: false,
          ),
        ),
        isFalse,
      );
    });

    test('Method shouldRepaint returns true when any input changed', () {
      for (final DovahConnectionAccentPainter other in const [
        DovahConnectionAccentPainter(
          accent: DovahConnectionAccent(overContent: true),
          available: false,
          showLinkLine: true,
          aboveContent: false,
        ),
        DovahConnectionAccentPainter(
          accent: DovahConnectionAccent(),
          available: true,
          showLinkLine: true,
          aboveContent: false,
        ),
        DovahConnectionAccentPainter(
          accent: DovahConnectionAccent(),
          available: false,
          showLinkLine: false,
          aboveContent: false,
        ),
        DovahConnectionAccentPainter(
          accent: DovahConnectionAccent(),
          available: false,
          showLinkLine: true,
          aboveContent: true,
        ),
      ]) {
        expect(painter.shouldRepaint(other), isTrue);
      }
    });
  });

  group('Method hitTest behaves correctly', () {
    test('Method hitTest never takes hits', () {
      expect(
        const DovahConnectionAccentPainter(
          accent: DovahConnectionAccent(),
          available: false,
          showLinkLine: true,
          aboveContent: false,
        ).hitTest(const Offset(1, 1)),
        isFalse,
      );
    });
  });
}
