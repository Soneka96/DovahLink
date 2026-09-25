import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// The icon tile that opens a pairing state, the approved prototype's `.large-mark`: a box on the
/// theme's control material (`--material-raised`, the same as `.secondary` and `.otp input`), square
/// or circular by theme. Decorative: the state's heading carries its meaning, so the tile is hidden
/// from semantics.
class PairingMark extends StatelessWidget {
  /// The icon drawn in the tile.
  final IconData icon;

  /// Creates an icon tile showing [icon].
  const PairingMark({required this.icon, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final DovahDialogMetrics metrics = context.dovahDialogMetrics;

    return ExcludeSemantics(
      child: SizedBox(
        width: metrics.markSize,
        height: metrics.markSize,
        child: DovahSurface(
          role: DovahMaterialRole.control,
          cornerRadius: metrics.markCornerRadius,
          child: Center(
            child: Icon(
              icon,
              size: metrics.markIconSize,
              color: tokens.markIcon,
            ),
          ),
        ),
      ),
    );
  }
}
