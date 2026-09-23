import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// A DovahLink content panel: a [DovahSurface] with the theme's standard panel padding, scaled
/// by the theme's density. The everyday building block for grouped content (hero sections,
/// side-stack sections, settings groups).
class DovahPanel extends StatelessWidget {
  /// Creates a themed panel around [child].
  const DovahPanel({
    required this.child,
    this.raised = false,
    this.padding,
    super.key,
  });

  /// The panel's content.
  final Widget child;

  /// Whether to use the theme's raised material instead of its resting material.
  final bool raised;

  /// Padding inside the panel, or the theme's standard density-scaled panel padding when
  /// omitted.
  final EdgeInsetsGeometry? padding;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    return DovahSurface(
      raised: raised,
      padding:
          padding ??
          EdgeInsets.all(DovahThemeTokens.spacing18 * tokens.densityScale),
      child: child,
    );
  }
}
