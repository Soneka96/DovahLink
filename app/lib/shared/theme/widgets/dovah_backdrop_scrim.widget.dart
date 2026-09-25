import 'dart:ui';

import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_backdrop.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_backdrop_painter.dart';

/// Blurs and re-colors everything behind it as a [DovahBackdrop] describes, covers it with the
/// backdrop's tint, and lays [child] above. It fills the space its parent gives it and passes taps
/// through to whatever modal barrier lies beneath, so a dialog above it still dismisses on an
/// outside tap.
class DovahBackdropScrim extends StatelessWidget {
  /// The treatment to apply to the page behind.
  final DovahBackdrop backdrop;

  /// The content shown above the scrim, normally a dialog.
  final Widget child;

  /// Creates a scrim for [backdrop] around [child].
  const DovahBackdropScrim({
    required this.backdrop,
    required this.child,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return BackdropFilter(
      filter: ImageFilter.blur(
        sigmaX: backdrop.blurSigma,
        sigmaY: backdrop.blurSigma,
      ),
      child: CustomPaint(
        painter: DovahBackdropPainter(backdrop: backdrop),
        child: child,
      ),
    );
  }
}
