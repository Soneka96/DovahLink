import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring_painter.dart';

/// Draws the prototype's keyboard-focus outline around [child] while [focused]: a 2px line in the
/// theme's accent, 3px outside the box. Every focusable DovahLink control wraps its visible surface
/// in this one widget, so the outline is the same everywhere.
///
/// The prototype's `clip-path` also clips the outline of a bevelled component (Frostbound and Dovah
/// cards and primary buttons), which leaves it invisible there. The ring stays visible on those
/// components on purpose: a keyboard user must be able to see where focus is.
class DovahFocusRing extends StatelessWidget {
  /// The key of the outline while it is showing, so tests can tell a focused control apart.
  static const Key ringKey = Key('dovah-focus-ring');

  /// Whether the outline is showing.
  final bool focused;

  /// The corner radius of [child]'s box; `0` for a square or bevelled box.
  final double cornerRadius;

  /// The control's visible surface.
  final Widget child;

  /// Creates an outline around [child] that shows while [focused].
  const DovahFocusRing({
    required this.focused,
    required this.cornerRadius,
    required this.child,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      key: focused ? ringKey : null,
      foregroundPainter: focused
          ? DovahFocusRingPainter(
              color: context.dovahTokens.accentPrimary,
              cornerRadius: cornerRadius,
            )
          : null,
      child: child,
    );
  }
}
