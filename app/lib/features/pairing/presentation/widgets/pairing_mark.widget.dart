import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The icon tile that opens a pairing state, the approved prototype's `.large-mark`. Decorative:
/// the state's heading carries its meaning, so the tile is hidden from semantics.
class PairingMark extends StatelessWidget {
  /// The icon drawn in the tile.
  final IconData icon;

  /// Creates an icon tile showing [icon].
  const PairingMark({required this.icon, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;

    return ExcludeSemantics(
      child: Container(
        width: DovahThemeTokens.pairingMarkSize,
        height: DovahThemeTokens.pairingMarkSize,
        decoration: BoxDecoration(
          color: tokens.surfaceRaised,
          borderRadius: BorderRadius.circular(tokens.cornerRadius),
          border: Border.all(color: tokens.lineStrong),
        ),
        child: Icon(
          icon,
          size: DovahThemeTokens.pairingMarkIconSize,
          color: tokens.accentPrimary,
        ),
      ),
    );
  }
}
