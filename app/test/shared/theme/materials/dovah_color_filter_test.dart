import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_matrix.dart';

/// Exercises [DovahColorFilter]'s neutrality, Flutter conversion, and equality.
void main() {
  group('Property isNeutral behaves correctly', () {
    test('Property isNeutral is true for the default filter', () {
      expect(const DovahColorFilter().isNeutral, isTrue);
      expect(DovahColorFilter.none.isNeutral, isTrue);
    });

    test('Property isNeutral is false when any amount is set', () {
      const List<DovahColorFilter> filters = [
        DovahColorFilter(grayscale: 0.1),
        DovahColorFilter(sepia: 0.1),
        DovahColorFilter(hueRotateDegrees: 10),
        DovahColorFilter(saturate: 0.9),
        DovahColorFilter(brightness: 0.9),
        DovahColorFilter(contrast: 1.1),
      ];

      for (final DovahColorFilter filter in filters) {
        expect(filter.isNeutral, isFalse);
      }
    });
  });

  group('Method toColorFilter behaves correctly', () {
    test(
      'Method toColorFilter builds the matrix of the CSS function chain',
      () {
        const DovahColorFilter filter = DovahColorFilter(
          sepia: 0.38,
          hueRotateDegrees: 345,
          saturate: 0.75,
          brightness: 0.92,
          contrast: 1.06,
        );

        expect(
          filter.toColorFilter(),
          ColorFilter.matrix(
            buildDovahColorMatrix(
              sepia: 0.38,
              hueRotateDegrees: 345,
              saturation: 0.75,
              brightness: 0.92,
              contrast: 1.06,
            ),
          ),
        );
      },
    );

    test('Method toColorFilter folds grayscale into the saturation', () {
      const DovahColorFilter filter = DovahColorFilter(
        grayscale: 0.34,
        saturate: 0.48,
        contrast: 1.16,
      );

      expect(
        filter.toColorFilter(),
        ColorFilter.matrix(
          buildDovahColorMatrix(
            sepia: 0,
            hueRotateDegrees: 0,
            saturation: (1 - 0.34) * 0.48,
            brightness: 1,
            contrast: 1.16,
          ),
        ),
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for identical filters', () {
      const DovahColorFilter first = DovahColorFilter(
        grayscale: 0.34,
        contrast: 1.16,
      );
      const DovahColorFilter second = DovahColorFilter(
        grayscale: 0.34,
        contrast: 1.16,
      );

      expect(first == second, isTrue);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality fails when any amount differs', () {
      const DovahColorFilter base = DovahColorFilter(
        grayscale: 0.3,
        sepia: 0.3,
        hueRotateDegrees: 30,
        saturate: 0.3,
        brightness: 0.3,
        contrast: 0.3,
      );
      const List<DovahColorFilter> others = [
        DovahColorFilter(
          grayscale: 0.4,
          sepia: 0.3,
          hueRotateDegrees: 30,
          saturate: 0.3,
          brightness: 0.3,
          contrast: 0.3,
        ),
        DovahColorFilter(
          grayscale: 0.3,
          sepia: 0.4,
          hueRotateDegrees: 30,
          saturate: 0.3,
          brightness: 0.3,
          contrast: 0.3,
        ),
        DovahColorFilter(
          grayscale: 0.3,
          sepia: 0.3,
          hueRotateDegrees: 40,
          saturate: 0.3,
          brightness: 0.3,
          contrast: 0.3,
        ),
        DovahColorFilter(
          grayscale: 0.3,
          sepia: 0.3,
          hueRotateDegrees: 30,
          saturate: 0.4,
          brightness: 0.3,
          contrast: 0.3,
        ),
        DovahColorFilter(
          grayscale: 0.3,
          sepia: 0.3,
          hueRotateDegrees: 30,
          saturate: 0.3,
          brightness: 0.4,
          contrast: 0.3,
        ),
        DovahColorFilter(
          grayscale: 0.3,
          sepia: 0.3,
          hueRotateDegrees: 30,
          saturate: 0.3,
          brightness: 0.3,
          contrast: 0.4,
        ),
      ];

      for (final DovahColorFilter other in others) {
        expect(base == other, isFalse);
      }
    });
  });
}
