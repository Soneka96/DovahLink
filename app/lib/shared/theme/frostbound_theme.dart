import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';

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
    primaryActionForeground: Color(0xFFE9F0F2),
    soft: Color(0x1F9AC9DC),
    health: Color(0xFFB65256),
    magicka: Color(0xFF83B9D1),
    stamina: Color(0xFF789779),
    cornerStyle: DovahPanelCornerStyle.singleBevel,
    cornerRadius: 0,
    cornerCutSize: 9,
    displayFontFamily: 'Arial Narrow',
    eyebrow: Color(0xFFBD5559),
    pageTitleLineHeight: 1.0,
    rootHeaderRuleFraction: 0.2,
    uppercaseLabels: true,
    preset: DovahThemePreset.frostbound,
    backdropColor: Color(0xC7000204),
    backdropBlurSigma: 7,
    panelCornerRadius: 0,
    primaryActionCornerRadius: 0,
    statusOffline: Color(0xFF7C8993),
    brandTagline: Color(0xFF82919A),
    brandAccent: Color(0xFFA9C7D1),
    barTrack: Color(0xFF1B2931),
    markIcon: Color(0xFFBD5559),
    panelNote: Color(0xFF929DA2),
    heroScrim: LinearGradient(
      begin: Alignment.centerLeft,
      end: Alignment.centerRight,
      colors: [Color(0xF0010406), Color(0x8A020609), Color(0x2B020609)],
      stops: [0, 0.54, 1],
    ),
    heroFloorScrim: LinearGradient(
      begin: Alignment.bottomCenter,
      end: Alignment.topCenter,
      colors: [Color(0xEB030709), Color(0x00030709)],
      stops: [0, 0.66],
    ),
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
    extensions: const [
      tokens,
      DovahRootThemeMetrics.frostbound,
      DovahConnectionCardThemeMetrics.frostbound,
      DovahPageThemeMetrics.frostbound,
      DovahSessionThemeMetrics.frostbound,
      DovahOverviewThemeMetrics.frostbound,
      DovahThemeMaterials.frostbound,
    ],
  );
}
