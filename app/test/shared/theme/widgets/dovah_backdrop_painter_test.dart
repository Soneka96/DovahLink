import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_backdrop.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_backdrop_painter.dart';
import '../materials/dovah_material_test_helpers.dart';

/// Exercises [DovahBackdropPainter]'s color treatment, tint, hit behavior, and repaint contract.
void main() {
  const Size size = Size(8, 8);
  const Color red = Color(0xFFFF0000);
  const Color clear = Color(0x00000000);

  Future<Color> paintOverRed(
    WidgetTester tester,
    DovahBackdrop backdrop,
  ) async => (await tester.runAsync(
    () => paintDovahPainter(
      DovahBackdropPainter(backdrop: backdrop),
      size,
      background: red,
    ),
  ))!.first;

  group('Method paint behaves correctly', () {
    testWidgets(
      'Method paint leaves the page unchanged without treatment or tint',
      (WidgetTester tester) async {
        final Color pixel = await paintOverRed(
          tester,
          const DovahBackdrop(tint: clear, blurSigma: 8),
        );

        expect(pixel, red);
      },
    );

    testWidgets('Method paint mixes the page toward gray by the desaturation', (
      WidgetTester tester,
    ) async {
      final Color pixel = await paintOverRed(
        tester,
        const DovahBackdrop(tint: clear, blurSigma: 7, saturation: 0.72),
      );

      expect(redOf(pixel), inInclusiveRange(195, 215));
      expect(greenOf(pixel), inInclusiveRange(15, 30));
      expect(alphaOf(pixel), 255);
    });

    testWidgets('Method paint fully grays the page at zero saturation', (
      WidgetTester tester,
    ) async {
      final Color pixel = await paintOverRed(
        tester,
        const DovahBackdrop(tint: clear, blurSigma: 7, saturation: 0),
      );

      expect((redOf(pixel) - greenOf(pixel)).abs(), lessThan(3));
      expect((redOf(pixel) - blueOf(pixel)).abs(), lessThan(3));
    });

    testWidgets('Method paint shifts the page hue toward sepia', (
      WidgetTester tester,
    ) async {
      final Color untreated = await paintOverRed(
        tester,
        const DovahBackdrop(tint: clear, blurSigma: 9),
      );
      final Color sepia = await paintOverRed(
        tester,
        const DovahBackdrop(tint: clear, blurSigma: 9, sepia: 0.5),
      );

      expect(greenOf(sepia), greaterThan(greenOf(untreated)));
      expect(alphaOf(sepia), 255);
    });

    testWidgets('Method paint applies saturation and sepia together', (
      WidgetTester tester,
    ) async {
      final Color desaturated = await paintOverRed(
        tester,
        const DovahBackdrop(tint: clear, blurSigma: 8, saturation: 0.5),
      );
      final Color both = await paintOverRed(
        tester,
        const DovahBackdrop(
          tint: clear,
          blurSigma: 8,
          saturation: 0.5,
          sepia: 0.5,
        ),
      );

      expect(greenOf(both), greaterThan(greenOf(desaturated)));
      expect(alphaOf(both), 255);
    });

    testWidgets('Method paint covers the treated page with the tint', (
      WidgetTester tester,
    ) async {
      final Color pixel = await paintOverRed(
        tester,
        const DovahBackdrop(tint: Color(0x80000000), blurSigma: 8),
      );

      expect(redOf(pixel), inInclusiveRange(124, 132));
      expect(greenOf(pixel), 0);
    });

    test('Method paint draws without throwing for an empty size', () {
      expect(
        () => const DovahBackdropPainter(
          backdrop: DovahBackdrop(tint: clear, blurSigma: 8, saturation: 0.5),
        ).paint(Canvas(PictureRecorder()), Size.zero),
        returnsNormally,
      );
    });
  });

  group('Method hitTest behaves correctly', () {
    test(
      'Method hitTest never claims a hit so the modal barrier gets taps',
      () {
        expect(
          const DovahBackdropPainter(
            backdrop: DovahBackdrop(tint: clear, blurSigma: 8),
          ).hitTest(const Offset(1, 1)),
          isFalse,
        );
      },
    );
  });

  group('Method shouldRepaint behaves correctly', () {
    const DovahBackdrop backdrop = DovahBackdrop(tint: clear, blurSigma: 8);

    test('Method shouldRepaint returns false for an equal backdrop', () {
      expect(
        const DovahBackdropPainter(
          backdrop: backdrop,
        ).shouldRepaint(const DovahBackdropPainter(backdrop: backdrop)),
        isFalse,
      );
    });

    test('Method shouldRepaint returns true for a different backdrop', () {
      expect(
        const DovahBackdropPainter(backdrop: backdrop).shouldRepaint(
          const DovahBackdropPainter(
            backdrop: DovahBackdrop(tint: clear, blurSigma: 8, sepia: 0.1),
          ),
        ),
        isTrue,
      );
    });
  });
}
