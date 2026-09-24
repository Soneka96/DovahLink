import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Builds the Hearth preset: warm, spacious, and storybook-like -- parchment, walnut, bronze,
/// and plain rounded corners with no bevel.
ThemeData buildHearthTheme() {
  const DovahThemeTokens tokens = DovahThemeTokens(
    background: Color(0xFFD8C09A),
    surface: Color(0xFFEEDBBB),
    surfaceRaised: Color(0xFFDFC399),
    surface3: Color(0xFFCFAA76),
    lineSubtle: Color(0xFF9B7344),
    lineStrong: Color(0xFF79542F),
    textPrimary: Color(0xFF271B12),
    textMuted: Color(0xFF594431),
    textFaint: Color(0xFF765B3E),
    accentPrimary: Color(0xFF965923),
    accentSecondary: Color(0xFFB87230),
    signal: Color(0xFFA96328),
    ember: Color(0xFFA96328),
    success: Color(0xFF35684C),
    warning: Color(0xFF99541F),
    danger: Color(0xFF913B34),
    primaryActionGradient: LinearGradient(
      begin: Alignment.topCenter,
      end: Alignment.bottomCenter,
      colors: [Color(0xFFA96932), Color(0xFF82491E)],
    ),
    primaryActionForeground: Color(0xFFFFF9EE),
    soft: Color(0x24965923),
    health: Color(0xFFA74F3E),
    magicka: Color(0xFF557B98),
    stamina: Color(0xFF58785B),
    cornerStyle: DovahPanelCornerStyle.rounded,
    cornerRadius: 13,
    cornerCutSize: 0,
    panelShadow: [
      BoxShadow(
        color: Color(0x3B452C15),
        blurRadius: 26,
        offset: Offset(0, 12),
      ),
    ],
    materialGradient: LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [Color(0xFFF7E6C9), Color(0xFFD9B681)],
    ),
    materialRaisedGradient: LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [Color(0xFFFAE9CD), Color(0xFFDDB985)],
    ),
    densityScale: 1.15,
    displayFontFamily: 'Georgia',
    environmentAssetPath: 'assets/themes/hearth/hearth-environment.png',
    eyebrow: Color(0xFF945720),
    rootHeaderHeight: 86,
    pageTitleFontSize: 38,
    connectionCardMinHeight: 82,
    rootContentTopPadding: 30,
    rootHeroBottomGap: 28,
    pageTitleLineHeight: 1.14,
    rootHeaderRuleFraction: 0.52,
    uppercaseLabels: false,
    preset: DovahThemePreset.hearth,
    backdropColor: Color(0x8A2F1F12),
    backdropBlurSigma: 9,
    panelCornerRadius: 14,
    primaryActionCornerRadius: 9,
  );

  return ThemeData(
    brightness: Brightness.light,
    scaffoldBackgroundColor: tokens.background,
    colorScheme: ColorScheme.light(
      surface: tokens.surface,
      primary: tokens.signal,
      secondary: tokens.ember,
      error: tokens.danger,
      onSurface: tokens.textPrimary,
    ),
    extensions: const [tokens],
  );
}
