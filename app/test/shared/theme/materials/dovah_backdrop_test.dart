import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_backdrop.dart';

/// Exercises [DovahBackdrop]'s defaults and equality.
void main() {
  const Color tint = Color(0xC7000204);

  group('Behavior construction behaves correctly', () {
    test('Behavior construction defaults to an unchanged page color', () {
      const DovahBackdrop backdrop = DovahBackdrop(tint: tint, blurSigma: 7);

      expect(backdrop.tint, tint);
      expect(backdrop.blurSigma, isA<double>());
      expect(backdrop.blurSigma, 7);
      expect(backdrop.saturation, isA<double>());
      expect(backdrop.saturation, 1);
      expect(backdrop.sepia, isA<double>());
      expect(backdrop.sepia, 0);
    });
  });

  group('Behavior equality behaves correctly', () {
    const DovahBackdrop backdrop = DovahBackdrop(
      tint: tint,
      blurSigma: 7,
      saturation: 0.72,
      sepia: 0.12,
    );

    test('Behavior equality holds for identical backdrops', () {
      const DovahBackdrop same = DovahBackdrop(
        tint: tint,
        blurSigma: 7,
        saturation: 0.72,
        sepia: 0.12,
      );

      expect(backdrop == same, isTrue);
      expect(backdrop.hashCode, same.hashCode);
    });

    test('Behavior equality fails when any field differs', () {
      const List<DovahBackdrop> others = [
        DovahBackdrop(
          tint: Color(0xC2020407),
          blurSigma: 7,
          saturation: 0.72,
          sepia: 0.12,
        ),
        DovahBackdrop(tint: tint, blurSigma: 8, saturation: 0.72, sepia: 0.12),
        DovahBackdrop(tint: tint, blurSigma: 7, saturation: 0.5, sepia: 0.12),
        DovahBackdrop(tint: tint, blurSigma: 7, saturation: 0.72, sepia: 0.2),
      ];

      for (final DovahBackdrop other in others) {
        expect(backdrop == other, isFalse);
      }
    });
  });
}
