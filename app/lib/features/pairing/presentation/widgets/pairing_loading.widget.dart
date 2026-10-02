import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The small rotating ring shown while a pairing step is in progress, the prototype's `.spinner`.
class PairingLoadingIndicator extends StatefulWidget {
  /// Creates a pairing spinner.
  const PairingLoadingIndicator({super.key});

  /// Creates the state that rotates the ring while animations are enabled.
  @override
  State<PairingLoadingIndicator> createState() =>
      _PairingLoadingIndicatorState();
}

/// Runs the prototype's one-second linear spinner rotation.
class _PairingLoadingIndicatorState extends State<PairingLoadingIndicator>
    with SingleTickerProviderStateMixin {
  /// The ring's CSS `border` animation.
  late final AnimationController _rotation = AnimationController(
    vsync: this,
    duration: DovahDialogMetrics.progressIndicatorRotationDuration,
  );

  /// Starts or stops rotation when the reduced-motion preference changes.
  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (MediaQuery.disableAnimationsOf(context)) {
      _rotation
        ..stop()
        ..value = 0;
    } else if (!_rotation.isAnimating) {
      _rotation.repeat();
    }
  }

  /// Releases the ticker when the spinner is removed.
  @override
  void dispose() {
    _rotation.dispose();
    super.dispose();
  }

  /// See [State.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;

    return RotationTransition(
      turns: _rotation,
      child: SizedBox.square(
        key: const Key('pairing-loading'),
        dimension: DovahDialogMetrics.progressIndicatorSize,
        child: DecoratedBox(
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            border: Border.all(
              color: DovahDialogMetrics.progressIndicatorTrackColor,
              width: DovahDialogMetrics.progressIndicatorStrokeWidth,
            ),
          ),
          child: CustomPaint(
            key: const Key('pairing-loading-accent'),
            foregroundPainter: _PairingSpinnerAccentPainter(
              color: tokens.accentPrimary,
              strokeWidth: DovahDialogMetrics.progressIndicatorStrokeWidth,
            ),
            child: const SizedBox.expand(),
          ),
        ),
      ),
    );
  }
}

/// Paints the prototype spinner's accent segment over its full muted track.
class _PairingSpinnerAccentPainter extends CustomPainter {
  /// The theme's primary accent.
  final Color color;

  /// The prototype spinner border width.
  final double strokeWidth;

  /// Creates the spinner's top accent segment.
  const _PairingSpinnerAccentPainter({
    required this.color,
    required this.strokeWidth,
  });

  /// See [CustomPainter.paint].
  @override
  void paint(Canvas canvas, Size size) {
    final double radius = (size.shortestSide - strokeWidth) / 2;
    canvas.drawArc(
      Rect.fromCircle(center: size.center(Offset.zero), radius: radius),
      -math.pi * 0.75,
      math.pi / 2,
      false,
      Paint()
        ..color = color
        ..style = PaintingStyle.stroke
        ..strokeWidth = strokeWidth,
    );
  }

  /// See [CustomPainter.shouldRepaint].
  @override
  bool shouldRepaint(covariant _PairingSpinnerAccentPainter oldDelegate) =>
      color != oldDelegate.color || strokeWidth != oldDelegate.strokeWidth;
}
