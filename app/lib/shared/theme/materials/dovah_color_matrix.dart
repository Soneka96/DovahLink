import 'dart:math' as math;

/// Builds the 4x5 row-major color matrix (offsets in 0-255 units, as `ColorFilter.matrix` expects)
/// for a chain of CSS filter functions applied in this fixed order: sepia, hue-rotate,
/// saturate, brightness, contrast. Linear color operations that preserve grays commute, so this one
/// order reproduces every prototype `filter` chain, including `grayscale`, which is a saturation of
/// `1 - amount` and so arrives through [saturation]. The matrices are the ones the CSS Filter
/// Effects specification defines. The alpha channel is left unchanged.
List<double> buildDovahColorMatrix({
  required double sepia,
  required double hueRotateDegrees,
  required double saturation,
  required double brightness,
  required double contrast,
}) {
  const double lumaRed = 0.2126;
  const double lumaGreen = 0.7152;
  const double lumaBlue = 0.0722;

  final double keep = 1 - sepia;
  final List<List<double>> sepiaMatrix = [
    [0.393 + 0.607 * keep, 0.769 - 0.769 * keep, 0.189 - 0.189 * keep],
    [0.349 - 0.349 * keep, 0.686 + 0.314 * keep, 0.168 - 0.168 * keep],
    [0.272 - 0.272 * keep, 0.534 - 0.534 * keep, 0.131 + 0.869 * keep],
  ];

  final double cosine = math.cos(hueRotateDegrees * math.pi / 180);
  final double sine = math.sin(hueRotateDegrees * math.pi / 180);
  final List<List<double>> hueMatrix = [
    [
      0.213 + cosine * 0.787 - sine * 0.213,
      0.715 - cosine * 0.715 - sine * 0.715,
      0.072 - cosine * 0.072 + sine * 0.928,
    ],
    [
      0.213 - cosine * 0.213 + sine * 0.143,
      0.715 + cosine * 0.285 + sine * 0.140,
      0.072 - cosine * 0.072 - sine * 0.283,
    ],
    [
      0.213 - cosine * 0.213 - sine * 0.787,
      0.715 - cosine * 0.715 + sine * 0.715,
      0.072 + cosine * 0.928 + sine * 0.072,
    ],
  ];

  final List<List<double>> saturationMatrix = [
    [
      lumaRed + (1 - lumaRed) * saturation,
      lumaGreen - lumaGreen * saturation,
      lumaBlue - lumaBlue * saturation,
    ],
    [
      lumaRed - lumaRed * saturation,
      lumaGreen + (1 - lumaGreen) * saturation,
      lumaBlue - lumaBlue * saturation,
    ],
    [
      lumaRed - lumaRed * saturation,
      lumaGreen - lumaGreen * saturation,
      lumaBlue + (1 - lumaBlue) * saturation,
    ],
  ];

  List<List<double>> multiply(
    List<List<double>> left,
    List<List<double>> right,
  ) => [
    for (int row = 0; row < 3; row++)
      [
        for (int column = 0; column < 3; column++)
          left[row][0] * right[0][column] +
              left[row][1] * right[1][column] +
              left[row][2] * right[2][column],
      ],
  ];

  final List<List<double>> color = multiply(
    saturationMatrix,
    multiply(hueMatrix, sepiaMatrix),
  );
  // Brightness scales and contrast scales then recenters around mid-gray, so both fold into one
  // gain and one offset.
  final double gain = brightness * contrast;
  final double offset = 0.5 * (1 - contrast) * 255;

  return [
    for (int row = 0; row < 3; row++) ...[
      color[row][0] * gain,
      color[row][1] * gain,
      color[row][2] * gain,
      0,
      offset,
    ],
    0,
    0,
    0,
    1,
    0,
  ];
}
