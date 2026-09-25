import 'dart:typed_data';
import 'dart:ui';

import 'package:flutter/rendering.dart' show CustomPainter;

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil_painter.dart';

/// Exercises [DovahSigilPainter]'s `paint` and `shouldRepaint` contracts.
void main() {
  /// Renders the sigil at [side] pixels square and returns its RGBA bytes.
  Future<ByteData> renderSigil(WidgetTester tester, int side) async {
    final PictureRecorder recorder = PictureRecorder();
    const DovahSigilPainter().paint(
      Canvas(recorder),
      Size.square(side.toDouble()),
    );
    final Picture picture = recorder.endRecording();
    final ByteData? bytes = await tester.runAsync(() async {
      final Image image = await picture.toImage(side, side);
      return (await image.toByteData())!;
    });
    return bytes!;
  }

  /// Reads the pixel at ([x], [y]) of a [side]-wide RGBA image as a [Color].
  Color pixelAt(ByteData bytes, int side, int x, int y) {
    final int offset = (y * side + x) * 4;
    return Color.fromARGB(
      bytes.getUint8(offset + 3),
      bytes.getUint8(offset),
      bytes.getUint8(offset + 1),
      bytes.getUint8(offset + 2),
    );
  }

  group('Method paint behaves correctly', () {
    testWidgets(
      'Method paint fills the upper arrow with the upper brand color',
      (WidgetTester tester) async {
        final ByteData bytes = await renderSigil(tester, 100);

        expect(pixelAt(bytes, 100, 17, 50), DovahSigilPainter.upperColor);
      },
    );

    testWidgets(
      'Method paint fills the lower arrow with the lower brand color',
      (WidgetTester tester) async {
        final ByteData bytes = await renderSigil(tester, 100);

        expect(pixelAt(bytes, 100, 83, 50), DovahSigilPainter.lowerColor);
      },
    );

    testWidgets(
      'Method paint leaves the space between the arrows transparent',
      (WidgetTester tester) async {
        final ByteData bytes = await renderSigil(tester, 100);

        expect(pixelAt(bytes, 100, 50, 50).a, 0);
        expect(pixelAt(bytes, 100, 2, 2).a, 0);
      },
    );

    testWidgets('Method paint scales the view box to the canvas size', (
      WidgetTester tester,
    ) async {
      final ByteData bytes = await renderSigil(tester, 200);

      expect(pixelAt(bytes, 200, 34, 100), DovahSigilPainter.upperColor);
      expect(pixelAt(bytes, 200, 166, 100), DovahSigilPainter.lowerColor);
      expect(pixelAt(bytes, 200, 100, 100).a, 0);
    });
  });

  group('Method paint draws the glow constructor', () {
    /// Renders the glow of [blurRadius] and [color] at [side] pixels square.
    Future<ByteData> renderGlow(
      WidgetTester tester,
      int side, {
      double blurRadius = 15,
      Color color = const Color(0xFF74BDE8),
    }) async {
      final PictureRecorder recorder = PictureRecorder();
      DovahSigilPainter.glow(
        color: color,
        blurRadius: blurRadius,
      ).paint(Canvas(recorder), Size.square(side.toDouble()));
      final Picture picture = recorder.endRecording();
      final ByteData? bytes = await tester.runAsync(() async {
        final Image image = await picture.toImage(side, side);
        return (await image.toByteData())!;
      });
      return bytes!;
    }

    testWidgets('Method paint glows outside the silhouette in the glow color', (
      WidgetTester tester,
    ) async {
      final ByteData bytes = await renderGlow(tester, 100);
      final Color outside = pixelAt(bytes, 100, 4, 50);

      expect(outside.a, greaterThan(0));
      expect(outside.b, greaterThan(outside.r));
    });

    testWidgets('Method paint draws the glow instead of the sigil', (
      WidgetTester tester,
    ) async {
      final ByteData bytes = await renderGlow(tester, 100);

      expect(pixelAt(bytes, 100, 17, 50), isNot(DovahSigilPainter.upperColor));
      expect(pixelAt(bytes, 100, 83, 50), isNot(DovahSigilPainter.lowerColor));
    });

    testWidgets('Method paint softens a wider glow further from the sigil', (
      WidgetTester tester,
    ) async {
      final ByteData narrow = await renderGlow(tester, 100, blurRadius: 4);
      final ByteData wide = await renderGlow(tester, 100, blurRadius: 24);

      expect(
        pixelAt(wide, 100, 1, 50).a,
        greaterThan(pixelAt(narrow, 100, 1, 50).a),
      );
    });

    testWidgets('Method paint keeps the glow radius when the canvas scales', (
      WidgetTester tester,
    ) async {
      final ByteData small = await renderGlow(tester, 50, blurRadius: 8);
      final ByteData large = await renderGlow(tester, 100, blurRadius: 8);

      // A given distance from the silhouette glows equally at either scale: 3px left of the
      // upper arrow's left edge (5px at 50, 10px at 100).
      expect(
        pixelAt(small, 50, 2, 25).a,
        closeTo(pixelAt(large, 100, 7, 50).a, 30),
      );
    });
  });

  group('Method paint handles degenerate input', () {
    test('Method paint draws nothing into an empty canvas', () {
      for (final CustomPainter painter in const [
        DovahSigilPainter(),
        DovahSigilPainter.glow(color: Color(0xFF74BDE8), blurRadius: 8),
      ]) {
        expect(
          () => painter.paint(Canvas(PictureRecorder()), Size.zero),
          returnsNormally,
        );
      }
    });

    test('Method paint draws a glow of no blur without throwing', () {
      expect(
        () => const DovahSigilPainter.glow(
          color: Color(0xFF74BDE8),
          blurRadius: 0,
        ).paint(Canvas(PictureRecorder()), const Size.square(44)),
        returnsNormally,
      );
    });
  });

  group('Method shouldRepaint behaves correctly', () {
    test('Method shouldRepaint returns true when the glow differs', () {
      const DovahSigilPainter glow = DovahSigilPainter.glow(
        color: Color(0xFF74BDE8),
        blurRadius: 8,
      );

      expect(glow.shouldRepaint(const DovahSigilPainter()), isTrue);
      expect(
        glow.shouldRepaint(
          const DovahSigilPainter.glow(color: Color(0xFF000000), blurRadius: 8),
        ),
        isTrue,
      );
      expect(
        glow.shouldRepaint(
          const DovahSigilPainter.glow(color: Color(0xFF74BDE8), blurRadius: 9),
        ),
        isTrue,
      );
      expect(
        glow.shouldRepaint(
          const DovahSigilPainter.glow(color: Color(0xFF74BDE8), blurRadius: 8),
        ),
        isFalse,
      );
    });

    test('Method shouldRepaint returns false for another sigil painter', () {
      expect(
        const DovahSigilPainter().shouldRepaint(const DovahSigilPainter()),
        isFalse,
      );
    });
  });
}
