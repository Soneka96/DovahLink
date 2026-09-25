import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_brand_mark_treatment.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';

/// Exercises [DovahBrandMarkTreatment]'s defaults and equality.
void main() {
  group('Behavior construction behaves correctly', () {
    test('Behavior construction defaults to an untouched mark', () {
      const DovahBrandMarkTreatment treatment = DovahBrandMarkTreatment();

      expect(treatment.filter, DovahColorFilter.none);
      expect(treatment.glowColor, isNull);
      expect(treatment.glowBlurRadius, isA<double>());
      expect(treatment.glowBlurRadius, 0);
      expect(treatment.backingColor, isNull);
    });
  });

  group('Behavior equality behaves correctly', () {
    const DovahBrandMarkTreatment base = DovahBrandMarkTreatment(
      filter: DovahColorFilter(grayscale: 0.65),
      glowColor: Color(0x2B9AC9DC),
      glowBlurRadius: 8,
      backingColor: Color(0x4DFFF8E6),
    );

    test('Behavior equality holds for identical treatments', () {
      const DovahBrandMarkTreatment same = DovahBrandMarkTreatment(
        filter: DovahColorFilter(grayscale: 0.65),
        glowColor: Color(0x2B9AC9DC),
        glowBlurRadius: 8,
        backingColor: Color(0x4DFFF8E6),
      );

      expect(base == same, isTrue);
      expect(base.hashCode, same.hashCode);
    });

    test('Behavior equality fails when any single part differs', () {
      for (final DovahBrandMarkTreatment other in const [
        DovahBrandMarkTreatment(
          filter: DovahColorFilter(grayscale: 0.5),
          glowColor: Color(0x2B9AC9DC),
          glowBlurRadius: 8,
          backingColor: Color(0x4DFFF8E6),
        ),
        DovahBrandMarkTreatment(
          filter: DovahColorFilter(grayscale: 0.65),
          glowColor: Color(0x00000000),
          glowBlurRadius: 8,
          backingColor: Color(0x4DFFF8E6),
        ),
        DovahBrandMarkTreatment(
          filter: DovahColorFilter(grayscale: 0.65),
          glowColor: Color(0x2B9AC9DC),
          glowBlurRadius: 9,
          backingColor: Color(0x4DFFF8E6),
        ),
        DovahBrandMarkTreatment(
          filter: DovahColorFilter(grayscale: 0.65),
          glowColor: Color(0x2B9AC9DC),
          glowBlurRadius: 8,
        ),
      ]) {
        expect(base == other, isFalse);
      }
    });
  });
}
