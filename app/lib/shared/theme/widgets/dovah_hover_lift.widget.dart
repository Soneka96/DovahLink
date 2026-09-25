import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';

/// Tracks the pointer over its child and slides the child by [offset] while it is hovered (the
/// prototype's `.connection:hover{transform:translateX(...)}` and `.preset-card:hover{transform:
/// translateY(-2px)}`), telling [builder] whether the pointer is over it so the child can also take
/// the raised material. The slide eases over [DovahControlMetrics.liftDuration] and is immediate
/// when the platform asks for reduced motion; the hovered state itself still applies.
///
/// Like the CSS transform, the slide moves the child's hit area with it. Nothing is hovered while
/// [enabled] is `false`.
class DovahHoverLift extends StatefulWidget {
  /// Whether hovering has any effect; `false` for a non-interactive child.
  final bool enabled;

  /// How far the child slides while hovered.
  final Offset offset;

  /// Builds the child for the current hover state.
  final Widget Function(BuildContext context, bool hovered) builder;

  /// Creates a hover slide of [offset] around what [builder] builds.
  const DovahHoverLift({
    required this.enabled,
    required this.offset,
    required this.builder,
    super.key,
  });

  /// Creates the state that tracks the pointer.
  @override
  State<DovahHoverLift> createState() => _DovahHoverLiftState();
}

/// Tracks whether a mouse pointer is over a [DovahHoverLift].
class _DovahHoverLiftState extends State<DovahHoverLift> {
  /// Whether a pointer is currently over the child.
  bool _pointerOver = false;

  /// See [State.build].
  @override
  Widget build(BuildContext context) {
    final bool hovered = widget.enabled && _pointerOver;

    return MouseRegion(
      onEnter: (_) => setState(() => _pointerOver = true),
      onExit: (_) => setState(() => _pointerOver = false),
      child: TweenAnimationBuilder<Offset>(
        tween: Tween<Offset>(
          begin: Offset.zero,
          end: hovered ? widget.offset : Offset.zero,
        ),
        duration: MediaQuery.disableAnimationsOf(context)
            ? Duration.zero
            : DovahControlMetrics.liftDuration,
        curve: Curves.ease,
        builder: (BuildContext context, Offset offset, Widget? child) =>
            Transform.translate(offset: offset, child: child),
        child: Builder(
          builder: (BuildContext context) => widget.builder(context, hovered),
        ),
      ),
    );
  }
}
