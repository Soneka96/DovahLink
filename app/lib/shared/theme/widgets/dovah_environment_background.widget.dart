import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The DovahLink application canvas's atmospheric background: the theme's environment image
/// (Frostbound, Hearth) or a pure gradient atmosphere (Dovah has no environment image), with a
/// scrim toward the theme's background color so foreground content stays legible. Translates the
/// approved prototype's `body::before`/`::after` atmosphere layers into an explicit background
/// [Stack] layer rather than CSS pseudo-elements. This uses one representative scrim gradient per
/// theme's background color rather than the prototype's bespoke per-theme radial-gradient
/// recipe and `grayscale`/`saturate`/`contrast` image filters, which have no direct Flutter
/// equivalent; a disclosed simplification, not a redesign.
class DovahEnvironmentBackground extends StatelessWidget {
  /// Creates the atmospheric background behind [child].
  const DovahEnvironmentBackground({required this.child, super.key});

  /// The foreground content rendered above the atmosphere.
  final Widget child;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    final String? environmentAssetPath =
        context.dovahMaterials.atmosphere.imageAssetPath;

    return Stack(
      fit: StackFit.expand,
      children: [
        ColoredBox(color: tokens.background),
        if (environmentAssetPath != null)
          Positioned.fill(
            child: Image.asset(environmentAssetPath, fit: BoxFit.cover),
          ),
        Positioned.fill(
          child: DecoratedBox(
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topCenter,
                end: Alignment.bottomCenter,
                colors: [
                  tokens.background.withValues(
                    alpha: DovahThemeTokens.environmentTopScrimOpacity,
                  ),
                  tokens.background.withValues(
                    alpha: DovahThemeTokens.environmentBottomScrimOpacity,
                  ),
                ],
              ),
            ),
          ),
        ),
        child,
      ],
    );
  }
}
