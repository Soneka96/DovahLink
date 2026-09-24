import 'dart:typed_data';
import 'dart:ui';

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

  group('Method shouldRepaint behaves correctly', () {
    test('Method shouldRepaint returns false for another sigil painter', () {
      expect(
        const DovahSigilPainter().shouldRepaint(const DovahSigilPainter()),
        isFalse,
      );
    });
  });
}
