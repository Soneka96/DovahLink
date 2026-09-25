import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_appearance_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_materials.dart';

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
    primaryActionForeground: Color(0xFF1A0E04),
    soft: Color(0x2174BDE8),
    health: Color(0xFFD16F62),
    magicka: Color(0xFF65B8E7),
    stamina: Color(0xFF78A984),
    cornerStyle: DovahPanelCornerStyle.doubleBevel,
    cornerRadius: 3,
    cornerCutSize: 12,
    displayFontFamily: 'Georgia',
    displayFontFamilyFallback: ['Times New Roman'],
    eyebrow: Color(0xFFE2A55E),
    pageTitleLineHeight: 1.14,
    rootHeaderRuleFraction: 0.36,
    uppercaseLabels: false,
    preset: DovahThemePreset.dovah,
    panelCornerRadius: 0,
    primaryActionCornerRadius: 0,
    statusOffline: Color(0xFF7C8993),
    brandTagline: Color(0xFF72899A),
    brandAccent: Color(0xFF74BDE8),
    barTrack: Color(0xFF202B34),
    markIcon: Color(0xFFE2A55E),
    iconTileForeground: Color(0xFF8ED6FF),
    panelNote: Color(0xFF667C8B),
    heroScrim: LinearGradient(
      begin: Alignment.centerLeft,
      end: Alignment.centerRight,
      colors: [Color(0xE0050A0F), Color(0x5C050A0F), Color(0x0F050A0F)],
      stops: [0, 0.52, 1],
    ),
    heroFloorScrim: LinearGradient(
      begin: Alignment.bottomCenter,
      end: Alignment.topCenter,
      colors: [Color(0xE00B141D), Color(0x000B141D)],
      stops: [0, 0.72],
    ),
  );

  return ThemeData(
    brightness: Brightness.dark,
    fontFamily: DovahThemeTokens.bodyFontFamily,
    fontFamilyFallback: DovahThemeTokens.bodyFontFamilyFallback,
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
      DovahRootThemeMetrics.dovah,
      DovahConnectionCardThemeMetrics.dovah,
      DovahDialogThemeMetrics.dovah,
      DovahAppearanceThemeMetrics.dovah,
      DovahPageThemeMetrics.dovah,
      DovahSessionThemeMetrics.dovah,
      DovahOverviewThemeMetrics.dovah,
      dovahMaterials,
    ],
  );
}
