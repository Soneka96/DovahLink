import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_brand_mark_treatment.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil_painter.dart';

/// The DovahLink sigil as the header shows it (the prototype's `.brand-mark`): the sigil dressed in
/// the theme's [DovahBrandMarkTreatment]. Its color treatment covers the sigil and its optional
/// backing disc together, and its glow sits beneath them untreated and may reach past [size]. It is
/// decorative, like the sigil itself, so it adds no semantics.
class DovahBrandMark extends StatelessWidget {
  /// The width and height of the mark, not counting a glow.
  final double size;

  /// Creates the mark at [size] logical pixels square.
  const DovahBrandMark({required this.size, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahBrandMarkTreatment treatment = context.dovahMaterials.brandMark;
    final Color? glow = treatment.glowColor;
    final Color? backing = treatment.backingColor;

    final Widget mark = SizedBox.square(
      dimension: size,
      child: Stack(
        fit: StackFit.expand,
        children: [
          if (backing != null)
            DecoratedBox(
              decoration: BoxDecoration(color: backing, shape: BoxShape.circle),
            ),
          DovahSigil(size: size),
        ],
      ),
    );

    return SizedBox.square(
      dimension: size,
      child: Stack(
        clipBehavior: Clip.none,
        fit: StackFit.expand,
        children: [
          if (glow != null)
            ExcludeSemantics(
              child: CustomPaint(
                painter: DovahSigilPainter.glow(
                  color: glow,
                  blurRadius: treatment.glowBlurRadius,
                ),
              ),
            ),
          if (treatment.filter.isNeutral)
            mark
          else
            ColorFiltered(
              colorFilter: treatment.filter.toColorFilter(),
              child: mark,
            ),
        ],
      ),
    );
  }
}
