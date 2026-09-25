import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_color_matrix.dart';

/// Builds the matrix with every amount at its neutral value, overridable per function.
List<double> buildMatrix({
  double sepia = 0,
  double hueRotateDegrees = 0,
  double saturation = 1,
  double brightness = 1,
  double contrast = 1,
}) => buildDovahColorMatrix(
  sepia: sepia,
  hueRotateDegrees: hueRotateDegrees,
  saturation: saturation,
  brightness: brightness,
  contrast: contrast,
);

/// Applies [matrix] to the RGB color ([red], [green], [blue]) in 0-1 units, as the CSS filter
/// would, and returns the resulting channels.
List<double> applyMatrix(
  List<double> matrix,
  double red,
  double green,
  double blue,
) => [
  for (int row = 0; row < 3; row++)
    matrix[row * 5] * red +
        matrix[row * 5 + 1] * green +
        matrix[row * 5 + 2] * blue +
        matrix[row * 5 + 4] / 255,
];

/// Exercises [buildDovahColorMatrix] against the CSS Filter Effects definitions.
void main() {
  group('Method buildDovahColorMatrix behaves correctly', () {
    test(
      'Method buildDovahColorMatrix returns the identity for neutral amounts',
      () {
        expect(buildMatrix(), [
          for (int row = 0; row < 3; row++)
            for (int column = 0; column < 5; column++)
              if (column == row) 1.0 else 0.0,
          0.0,
          0.0,
          0.0,
          1.0,
          0.0,
        ]);
      },
    );

    test('Method buildDovahColorMatrix leaves the alpha channel unchanged', () {
      final List<double> matrix = buildMatrix(
        sepia: 0.4,
        hueRotateDegrees: 30,
        saturation: 0.5,
        brightness: 0.8,
        contrast: 1.2,
      );

      expect(matrix.sublist(15), [0, 0, 0, 1, 0]);
    });

    test(
      'Method buildDovahColorMatrix maps every color to its luma at zero saturation',
      () {
        final List<double> matrix = buildMatrix(saturation: 0);

        for (int row = 0; row < 3; row++) {
          expect(matrix[row * 5], closeTo(0.2126, 1e-9));
          expect(matrix[row * 5 + 1], closeTo(0.7152, 1e-9));
          expect(matrix[row * 5 + 2], closeTo(0.0722, 1e-9));
        }
      },
    );

    test(
      'Method buildDovahColorMatrix uses the CSS sepia matrix at full sepia',
      () {
        final List<double> matrix = buildMatrix(sepia: 1);

        expect(matrix.sublist(0, 3), [0.393, 0.769, 0.189]);
        expect(matrix.sublist(5, 8), [0.349, 0.686, 0.168]);
        expect(matrix.sublist(10, 13), [0.272, 0.534, 0.131]);
      },
    );

    test(
      'Method buildDovahColorMatrix returns the identity for a full hue turn',
      () {
        final List<double> matrix = buildMatrix(hueRotateDegrees: 360);

        for (int row = 0; row < 3; row++) {
          for (int column = 0; column < 3; column++) {
            expect(
              matrix[row * 5 + column],
              closeTo(row == column ? 1 : 0, 1e-9),
            );
          }
        }
      },
    );

    test(
      'Method buildDovahColorMatrix keeps grays gray through a hue rotation',
      () {
        final List<double> gray = applyMatrix(
          buildMatrix(hueRotateDegrees: 345),
          0.5,
          0.5,
          0.5,
        );

        expect(gray[0], closeTo(0.5, 1e-3));
        expect(gray[1], closeTo(0.5, 1e-3));
        expect(gray[2], closeTo(0.5, 1e-3));
      },
    );

    test(
      'Method buildDovahColorMatrix scales every color by the brightness',
      () {
        final List<double> matrix = buildMatrix(brightness: 0.5);

        expect(matrix[0], 0.5);
        expect(matrix[6], 0.5);
        expect(matrix[12], 0.5);
        expect(matrix[4], 0);
      },
    );

    test(
      'Method buildDovahColorMatrix recenters on mid-gray for the contrast',
      () {
        final List<double> matrix = buildMatrix(contrast: 0.5);

        expect(matrix[0], 0.5);
        expect(matrix[4], 63.75);
        expect(applyMatrix(matrix, 0.5, 0.5, 0.5)[0], closeTo(0.5, 1e-9));
      },
    );

    test(
      'Method buildDovahColorMatrix folds brightness and contrast into one gain',
      () {
        final List<double> matrix = buildMatrix(
          brightness: 0.92,
          contrast: 1.06,
        );

        expect(matrix[0], closeTo(0.92 * 1.06, 1e-9));
        expect(matrix[4], closeTo(0.5 * (1 - 1.06) * 255, 1e-9));
      },
    );

    test('Method buildDovahColorMatrix applies sepia before saturation', () {
      final List<double> sepiaThenSaturate = buildMatrix(
        sepia: 1,
        saturation: 0.5,
      );
      final List<double> sepiaOnly = applyMatrix(
        buildMatrix(sepia: 1),
        0.8,
        0.4,
        0.2,
      );
      final double luma =
          0.2126 * sepiaOnly[0] + 0.7152 * sepiaOnly[1] + 0.0722 * sepiaOnly[2];
      final List<double> expected = [
        for (final double channel in sepiaOnly) luma + (channel - luma) * 0.5,
      ];
      final List<double> actual = applyMatrix(sepiaThenSaturate, 0.8, 0.4, 0.2);

      for (int channel = 0; channel < 3; channel++) {
        expect(actual[channel], closeTo(expected[channel], 1e-9));
      }
    });
  });
}
