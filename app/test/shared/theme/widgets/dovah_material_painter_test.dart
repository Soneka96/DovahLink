import 'dart:ui';

import 'package:flutter/material.dart' show LinearGradient, BoxShadow, Colors;
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';

/// Exercises [DovahMaterialPainter]'s `paint` and `shouldRepaint` contracts.
void main() {
  DovahMaterialPainter buildPainter({
    DovahPanelCornerStyle cornerStyle = DovahPanelCornerStyle.singleBevel,
    double cornerRadius = 0,
    double cutSize = 9,
    List<BoxShadow> shadow = const [
      BoxShadow(color: Color(0x80000000), blurRadius: 32),
    ],
  }) => DovahMaterialPainter(
    cornerStyle: cornerStyle,
    cornerRadius: cornerRadius,
    cutSize: cutSize,
    gradient: const LinearGradient(colors: [Colors.black, Colors.white]),
    borderColor: Colors.grey,
    shadow: shadow,
  );

  group('Method paint behaves correctly', () {
    test('Method paint draws without throwing for every corner style', () {
      final PictureRecorder recorder = PictureRecorder();
      final Canvas canvas = Canvas(recorder);

      for (final DovahPanelCornerStyle cornerStyle
          in DovahPanelCornerStyle.values) {
        expect(
          () => buildPainter(
            cornerStyle: cornerStyle,
          ).paint(canvas, const Size(100, 60)),
          returnsNormally,
        );
      }
    });

    test(
      'Method paint draws without throwing when the shadow list is empty',
      () {
        final PictureRecorder recorder = PictureRecorder();
        final Canvas canvas = Canvas(recorder);

        expect(
          () =>
              buildPainter(shadow: const []).paint(canvas, const Size(100, 60)),
          returnsNormally,
        );
      },
    );
  });

  group('Method shouldRepaint behaves correctly', () {
    test('Method shouldRepaint returns false when nothing changed', () {
      expect(buildPainter().shouldRepaint(buildPainter()), isFalse);
    });

    test('Method shouldRepaint returns true when the gradient differs', () {
      final DovahMaterialPainter first = buildPainter();
      final DovahMaterialPainter second = DovahMaterialPainter(
        cornerStyle: DovahPanelCornerStyle.singleBevel,
        cornerRadius: 0,
        cutSize: 9,
        gradient: const LinearGradient(colors: [Colors.red, Colors.blue]),
        borderColor: Colors.grey,
        shadow: const [BoxShadow(color: Color(0x80000000), blurRadius: 32)],
      );

      expect(first.shouldRepaint(second), isTrue);
    });

    test('Method shouldRepaint returns true when the border color differs', () {
      final DovahMaterialPainter first = buildPainter();
      final DovahMaterialPainter second = DovahMaterialPainter(
        cornerStyle: DovahPanelCornerStyle.singleBevel,
        cornerRadius: 0,
        cutSize: 9,
        gradient: const LinearGradient(colors: [Colors.black, Colors.white]),
        borderColor: Colors.red,
        shadow: const [BoxShadow(color: Color(0x80000000), blurRadius: 32)],
      );

      expect(first.shouldRepaint(second), isTrue);
    });
  });
}
