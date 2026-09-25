import 'dart:ui';

/// Returns [colors] with every fully transparent stop given the RGB of its nearest non-transparent
/// neighbor, looking back first and then forward. CSS interpolates gradients in premultiplied
/// alpha, so fading to `transparent` never darkens or shifts the visible hue; Flutter interpolates
/// straight RGB, where a black transparent stop would tint the fade. Stops that are not fully
/// transparent, and a list with no visible stop at all, are returned unchanged.
List<Color> matchTransparentStops(List<Color> colors) {
  final List<Color> matched = List<Color>.of(colors);

  for (int index = 0; index < colors.length; index++) {
    if (colors[index].a != 0) {
      continue;
    }

    int neighbor = index - 1;
    while (neighbor >= 0 && colors[neighbor].a == 0) {
      neighbor--;
    }
    if (neighbor < 0) {
      neighbor = index + 1;
      while (neighbor < colors.length && colors[neighbor].a == 0) {
        neighbor++;
      }
    }
    if (neighbor >= 0 && neighbor < colors.length) {
      matched[index] = colors[neighbor].withValues(alpha: 0);
    }
  }

  return matched;
}
