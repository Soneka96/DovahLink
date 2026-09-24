import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil_painter.dart';

/// The DovahLink Linked Sigil, drawn at a square [size]. It is decorative -- the wordmark beside
/// it carries the brand name -- so it is excluded from semantics.
class DovahSigil extends StatelessWidget {
  /// Creates the sigil at [size] logical pixels square.
  const DovahSigil({required this.size, super.key});

  /// The width and height of the sigil.
  final double size;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return ExcludeSemantics(
      child: CustomPaint(
        size: Size.square(size),
        painter: const DovahSigilPainter(),
      ),
    );
  }
}
