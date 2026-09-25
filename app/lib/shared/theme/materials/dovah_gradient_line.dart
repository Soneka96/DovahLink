import 'dart:math' as math;
import 'dart:ui';

/// Returns the CSS gradient line for a box of [size] and a gradient of [angleDegrees]: `0deg`
/// points up and angles grow clockwise. The line passes through the box's center and is just long
/// enough that the box's corners receive the first and last color stops, which is what CSS
/// `linear-gradient(<angle>, ...)` does and what Flutter's alignment-based gradients cannot
/// express.
({Offset start, Offset end}) buildDovahGradientLine(
  Size size,
  double angleDegrees,
) {
  final Offset direction = Offset.fromDirection(
    angleDegrees * math.pi / 180 - math.pi / 2,
  );
  final double halfLength =
      (size.width * direction.dx.abs() + size.height * direction.dy.abs()) / 2;
  final Offset center = size.center(Offset.zero);
  final Offset half = direction * halfLength;

  return (start: center - half, end: center + half);
}
