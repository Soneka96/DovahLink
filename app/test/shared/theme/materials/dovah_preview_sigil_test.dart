import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_sigil.dart';

/// Exercises [DovahPreviewSigil]'s defaults and equality.
void main() {
  const DovahPreviewSigil sigil = DovahPreviewSigil(
    fill: Color(0xFF090E12),
    border: Color(0xFF71808A),
    shape: DovahPreviewSigilShape.square,
    markFilter: DovahColorFilter(grayscale: 0.72),
  );

  group('Behavior construction behaves correctly', () {
    test('Behavior construction leaves the mark untreated by default', () {
      const DovahPreviewSigil plain = DovahPreviewSigil(
        fill: Color(0xFF090E12),
        border: Color(0xFF71808A),
        shape: DovahPreviewSigilShape.circle,
      );

      expect(plain.markFilter, DovahColorFilter.none);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for identical sigils', () {
      const DovahPreviewSigil same = DovahPreviewSigil(
        fill: Color(0xFF090E12),
        border: Color(0xFF71808A),
        shape: DovahPreviewSigilShape.square,
        markFilter: DovahColorFilter(grayscale: 0.72),
      );

      expect(sigil == same, isTrue);
      expect(sigil.hashCode, same.hashCode);
    });

    test('Behavior equality fails when any field differs', () {
      const List<DovahPreviewSigil> others = [
        DovahPreviewSigil(
          fill: Color(0xFF000000),
          border: Color(0xFF71808A),
          shape: DovahPreviewSigilShape.square,
          markFilter: DovahColorFilter(grayscale: 0.72),
        ),
        DovahPreviewSigil(
          fill: Color(0xFF090E12),
          border: Color(0xFF000000),
          shape: DovahPreviewSigilShape.square,
          markFilter: DovahColorFilter(grayscale: 0.72),
        ),
        DovahPreviewSigil(
          fill: Color(0xFF090E12),
          border: Color(0xFF71808A),
          shape: DovahPreviewSigilShape.diamond,
          markFilter: DovahColorFilter(grayscale: 0.72),
        ),
        DovahPreviewSigil(
          fill: Color(0xFF090E12),
          border: Color(0xFF71808A),
          shape: DovahPreviewSigilShape.square,
        ),
      ];

      for (final DovahPreviewSigil other in others) {
        expect(sigil == other, isFalse);
      }
    });
  });
}
