import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Builds the Dovah preset: the balanced DovahLink identity -- midnight steel, ember-to-ice
/// accents, and a double diagonal bevel on opposite corners. Named `Dovah` after the approved
/// prototype's own preset name, distinct from the app-wide `DovahLink`/`Dovah*` type prefix.
ThemeData buildDovahPresetTheme() {
  const DovahThemeTokens tokens = DovahThemeTokens(
    background: Color(0xFF05090E),
    surface: Color(0xFF0B141D),
    surfaceRaised: Color(0xFF101D28),
    surface3: Color(0xFF162735),
    lineSubtle: Color(0xFF294052),
    lineStrong: Color(0xFF4A6B84),
    textPrimary: Color(0xFFF1F6F9),
    textMuted: Color(0xFF9AABB7),
    textFaint: Color(0xFF667C8B),
    accentPrimary: Color(0xFF8ED6FF),
    accentSecondary: Color(0xFF54AEE0),
    signal: Color(0xFF74BDE8),
    ember: Color(0xFFE2A55E),
    success: Color(0xFF8ED6FF),
    warning: Color(0xFFE2A55E),
    danger: Color(0xFFE18080),
    primaryActionGradient: LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [Color(0xFFF0BD73), Color(0xFFC77D38)],
    ),
    primaryActionForeground: Color(0xFF1A0E04),
    soft: Color(0x2174BDE8),
    health: Color(0xFFD16F62),
    magicka: Color(0xFF65B8E7),
    stamina: Color(0xFF78A984),
    cornerStyle: DovahPanelCornerStyle.doubleBevel,
    cornerRadius: 3,
    cornerCutSize: 12,
    panelShadow: [
      BoxShadow(
        color: Color(0x4A000000),
        blurRadius: 38,
        offset: Offset(0, 17),
      ),
    ],
    materialGradient: LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [Color(0xFF11212D), Color(0xFF071018)],
    ),
    materialRaisedGradient: LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [Color(0xFF162A38), Color(0xFF09141D)],
    ),
    densityScale: 1,
    displayFontFamily: 'Georgia',
    environmentAssetPath: null,
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
