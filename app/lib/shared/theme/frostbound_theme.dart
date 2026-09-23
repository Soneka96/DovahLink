import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Builds the Frostbound preset: cold, severe, and compact -- fractured stone, scratched iron,
/// and a single sharp bevel with no rounding.
ThemeData buildFrostboundTheme() {
  const DovahThemeTokens tokens = DovahThemeTokens(
    background: Color(0xFF020405),
    surface: Color(0xFF070B0D),
    surfaceRaised: Color(0xFF0B1115),
    surface3: Color(0xFF11191E),
    lineSubtle: Color(0xFF344048),
    lineStrong: Color(0xFF71808A),
    textPrimary: Color(0xFFEDF1F2),
    textMuted: Color(0xFFB0B9BD),
    textFaint: Color(0xFF879399),
    accentPrimary: Color(0xFFA9C7D1),
    accentSecondary: Color(0xFF7FA5B3),
    signal: Color(0xFFA9C7D1),
    ember: Color(0xFFA43B40),
    success: Color(0xFF9AC9DC),
    warning: Color(0xFFC0575B),
    danger: Color(0xFFD36A6E),
    primaryActionGradient: LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [Color(0xFF263239), Color(0xFF11191D)],
    ),
    primaryActionForeground: Color(0xFFE9F0F2),
    soft: Color(0x1F9AC9DC),
    health: Color(0xFFB65256),
    magicka: Color(0xFF83B9D1),
    stamina: Color(0xFF789779),
    cornerStyle: DovahPanelCornerStyle.singleBevel,
    cornerRadius: 0,
    cornerCutSize: 9,
    panelShadow: [
      BoxShadow(
        color: Color(0x80000000),
        blurRadius: 32,
        offset: Offset(0, 15),
      ),
    ],
    materialGradient: LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [Color(0xFF11181C), Color(0xFF040708)],
    ),
    materialRaisedGradient: LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [Color(0xFF151E23), Color(0xFF070B0D)],
    ),
    densityScale: 0.85,
    displayFontFamily: 'Arial Narrow',
    environmentAssetPath: 'assets/themes/frostbound/frostbound-environment.png',
  );

  return ThemeData(
    brightness: Brightness.dark,
    scaffoldBackgroundColor: tokens.background,
    colorScheme: ColorScheme.dark(
      surface: tokens.surface,
      primary: tokens.signal,
      secondary: tokens.ember,
      error: tokens.danger,
      onSurface: tokens.textPrimary,
    ),
    extensions: const [tokens],
  );
}
