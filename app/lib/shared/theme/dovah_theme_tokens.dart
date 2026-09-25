import 'dart:ui' show lerpDouble;

import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';

/// DovahLink's typed theme identity: the semantic colors, status tones, corner treatment, and
/// typography a [DovahThemePreset] resolves to, beyond what a plain Material [ColorScheme] can
/// express. Layered component textures, the canvas atmosphere, the dialog backdrop, and the
/// appearance preview are recipes, not tokens, and live in [DovahThemeMaterials]. Shared DovahLink
/// surfaces and components read these extensions rather than branching on which concrete preset is
/// active.
@immutable
class DovahThemeTokens extends ThemeExtension<DovahThemeTokens> with Equatable {
  /// Font size for supporting text and compact labels.
  static const double compactFontSize = 13;

  /// Letter spacing, in ems, of labels a theme renders in uppercase.
  static const double uppercaseLetterSpacingEm = 0.045;

  /// Line height, as a multiple of font size, of body and label text.
  static const double bodyLineHeight = 4 / 3;

  /// Border width shared by themed surfaces.
  static const double surfaceBorderWidth = 1;

  /// The body font family of every theme, first in the prototype's `--body` stack
  /// (`Inter,ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif`). The prototype ships no
  /// font file, so no font is bundled: a machine without Inter falls through [bodyFontFamilyFallback]
  /// and then Flutter's platform default, which is the system UI font the stack's `system-ui` names.
  static const String bodyFontFamily = 'Inter';

  /// The families of the body stack after [bodyFontFamily] that a Flutter font fallback can name;
  /// the stack's generic keywords (`ui-sans-serif`, `system-ui`, `-apple-system`, `sans-serif`) have
  /// no Flutter name and resolve to the platform default.
  static const List<String> bodyFontFamilyFallback = ['Segoe UI'];

  /// The canvas behind every surface.
  final Color background;

  /// The base flat surface tone, used by simple non-material elements (icon buttons, form
  /// fields).
  final Color surface;

  /// The raised/hover flat surface tone.
  final Color surfaceRaised;

  /// The strongest flat surface tone.
  final Color surface3;

  /// The ordinary border/divider tone.
  final Color lineSubtle;

  /// The emphasized border tone, used on hover and for stronger separators.
  final Color lineStrong;

  /// The primary text tone.
  final Color textPrimary;

  /// The secondary/muted text tone.
  final Color textMuted;

  /// The tertiary, least emphasized text tone (captions, sub-labels).
  final Color textFaint;

  /// The general interactive/icon accent tone.
  final Color accentPrimary;

  /// The secondary accent tone used for secondary accents; page-title eyebrows use [eyebrow].
  final Color accentSecondary;

  /// The brand's cool/icy signal tone: active-state indicators, gradient underlines, focus
  /// glow.
  final Color signal;

  /// The brand's warm ember tone: paired with [signal] in gradient underlines and accents.
  final Color ember;

  /// The status tone for a reachable/healthy state.
  final Color success;

  /// The status tone for a state that needs attention but is not failed.
  final Color warning;

  /// The status tone for an error or failed state.
  final Color danger;

  /// The approved primary-button label color for this theme.
  final Color primaryActionForeground;

  /// The theme's low-opacity color for focus halos.
  final Color soft;

  /// The stat-bar tone for health.
  final Color health;

  /// The stat-bar tone for magicka.
  final Color magicka;

  /// The stat-bar tone for stamina.
  final Color stamina;

  /// Which corner treatment this theme's panels, surfaces, buttons, and cards use.
  final DovahPanelCornerStyle cornerStyle;

  /// The corner radius applied when [cornerStyle] is [DovahPanelCornerStyle.rounded].
  final double cornerRadius;

  /// The bevel cut size applied when [cornerStyle] is [DovahPanelCornerStyle.singleBevel] or
  /// [DovahPanelCornerStyle.doubleBevel]. The theme's general bevel; a component whose approved
  /// bevel differs (for example a connection card) takes its own from its metrics.
  final double cornerCutSize;

  /// The display/heading font family for this theme, first in the prototype's `--display` stack.
  /// The body font family does not vary by theme in the approved prototype, so it is the shared
  /// [bodyFontFamily]. The prototype ships no font file, so no font is bundled.
  final String displayFontFamily;

  /// The families of this theme's `--display` stack after [displayFontFamily] that a Flutter font
  /// fallback can name (Frostbound `Impact`; Dovah and Hearth `"Times New Roman"`). The stack's
  /// generic keywords (`ui-sans-serif`, `sans-serif`, `serif`) have no Flutter name and resolve to
  /// the platform default.
  final List<String> displayFontFamilyFallback;

  /// The tone of the eyebrow label above a page title (the prototype's `.eyebrow`), which differs
  /// per theme rather than following [accentSecondary].
  final Color eyebrow;

  /// Whether page titles, connection names, and connection states render in uppercase, as the
  /// prototype's Frostbound theme does.
  final bool uppercaseLabels;

  /// Fraction of the root header's width its gradient rule spans.
  final double rootHeaderRuleFraction;

  /// Line height, as a multiple of font size, of a page title.
  final double pageTitleLineHeight;

  /// Which preset these tokens belong to. It snaps at the midpoint of a theme transition, so
  /// neither metrics classes nor widgets resolve visual values from it; theme-varying geometry
  /// lives in the `Dovah*ThemeMetrics` extensions.
  final DovahThemePreset preset;

  /// The corner radius of a panel, card group, or dialog when [cornerStyle] is
  /// [DovahPanelCornerStyle.rounded] (the prototype's `.panel`/`.modal` `border-radius`), which
  /// differs from [cornerRadius] in Hearth.
  final double panelCornerRadius;

  /// The corner radius of a primary button when [cornerStyle] is
  /// [DovahPanelCornerStyle.rounded] (the prototype's `.primary` `border-radius`), which differs
  /// from [cornerRadius] in Hearth.
  final double primaryActionCornerRadius;

  /// The status tone for an offline connection (the prototype's `.status.offline`), which is
  /// neither a healthy nor an attention state and so has its own tone rather than [textFaint].
  final Color statusOffline;

  /// The tone of the wordmark's tagline (the prototype's `.brand-sub`), which differs per theme
  /// rather than following [textFaint].
  final Color brandTagline;

  /// The tone of the wordmark's "LINK" half (the prototype's `.brand-name span`).
  final Color brandAccent;

  /// The tone of the icon inside a large icon tile (the prototype's `.large-mark`), which differs
  /// per theme rather than following [accentPrimary].
  final Color markIcon;

  /// The tone of the glyph inside a leading icon tile (the prototype's `.pc-icon` `color`), which
  /// follows [accentPrimary] in Frostbound and Dovah but not in Hearth.
  final Color iconTileForeground;

  /// The empty track of a stat bar (the prototype's `.bar` background), which differs per theme.
  final Color barTrack;

  /// The tone of a panel title's trailing note (the prototype's `.panel-title span`), which
  /// differs from [textFaint] in Frostbound.
  final Color panelNote;

  /// The horizontal scrim over the character image in a hero panel (the prototype's
  /// `.hero-panel` leading `linear-gradient(90deg, ...)`), which keeps its text legible.
  final Gradient heroScrim;

  /// The scrim rising from a hero panel's floor (the prototype's `.hero-panel:before`
  /// `linear-gradient(0deg, ...)`).
  final Gradient heroFloorScrim;

  /// Creates a complete token set. Every field is required so no theme can be assembled with an
  /// accidentally-inherited default.
  const DovahThemeTokens({
    required this.background,
    required this.surface,
    required this.surfaceRaised,
    required this.surface3,
    required this.lineSubtle,
    required this.lineStrong,
    required this.textPrimary,
    required this.textMuted,
    required this.textFaint,
    required this.accentPrimary,
    required this.accentSecondary,
    required this.signal,
    required this.ember,
    required this.success,
    required this.warning,
    required this.danger,
    required this.primaryActionForeground,
    required this.soft,
    required this.health,
    required this.magicka,
    required this.stamina,
    required this.cornerStyle,
    required this.cornerRadius,
    required this.cornerCutSize,
    required this.displayFontFamily,
    required this.displayFontFamilyFallback,
    required this.eyebrow,
    required this.uppercaseLabels,
    required this.rootHeaderRuleFraction,
    required this.pageTitleLineHeight,
    required this.preset,
    required this.panelCornerRadius,
    required this.primaryActionCornerRadius,
    required this.statusOffline,
    required this.brandTagline,
    required this.brandAccent,
    required this.markIcon,
    required this.iconTileForeground,
    required this.barTrack,
    required this.panelNote,
    required this.heroScrim,
    required this.heroFloorScrim,
  });

  /// Returns a copy with selected values replaced.
  @override
  DovahThemeTokens copyWith({
    Color? background,
    Color? surface,
    Color? surfaceRaised,

    /// Replacement strongest flat surface tone.
    Color? surface3,
    Color? lineSubtle,
    Color? lineStrong,
    Color? textPrimary,
    Color? textMuted,
    Color? textFaint,
    Color? accentPrimary,
    Color? accentSecondary,
    Color? signal,
    Color? ember,
    Color? success,
    Color? warning,
    Color? danger,

    /// Replacement primary-button label color.
    Color? primaryActionForeground,

    /// Replacement low-opacity focus halo color.
    Color? soft,
    Color? health,
    Color? magicka,
    Color? stamina,
    DovahPanelCornerStyle? cornerStyle,
    double? cornerRadius,
    double? cornerCutSize,
    String? displayFontFamily,
    List<String>? displayFontFamilyFallback,
    Color? eyebrow,
    bool? uppercaseLabels,
    double? rootHeaderRuleFraction,
    double? pageTitleLineHeight,
    DovahThemePreset? preset,
    double? panelCornerRadius,
    double? primaryActionCornerRadius,
    Color? statusOffline,
    Color? brandTagline,
    Color? brandAccent,
    Color? markIcon,
    Color? iconTileForeground,
    Color? barTrack,
    Color? panelNote,
    Gradient? heroScrim,
    Gradient? heroFloorScrim,
  }) => DovahThemeTokens(
    background: background ?? this.background,
    surface: surface ?? this.surface,
    surfaceRaised: surfaceRaised ?? this.surfaceRaised,
    surface3: surface3 ?? this.surface3,
    lineSubtle: lineSubtle ?? this.lineSubtle,
    lineStrong: lineStrong ?? this.lineStrong,
    textPrimary: textPrimary ?? this.textPrimary,
    textMuted: textMuted ?? this.textMuted,
    textFaint: textFaint ?? this.textFaint,
    accentPrimary: accentPrimary ?? this.accentPrimary,
    accentSecondary: accentSecondary ?? this.accentSecondary,
    signal: signal ?? this.signal,
    ember: ember ?? this.ember,
    success: success ?? this.success,
    warning: warning ?? this.warning,
    danger: danger ?? this.danger,
    primaryActionForeground:
        primaryActionForeground ?? this.primaryActionForeground,
    soft: soft ?? this.soft,
    health: health ?? this.health,
    magicka: magicka ?? this.magicka,
    stamina: stamina ?? this.stamina,
    cornerStyle: cornerStyle ?? this.cornerStyle,
    cornerRadius: cornerRadius ?? this.cornerRadius,
    cornerCutSize: cornerCutSize ?? this.cornerCutSize,
    displayFontFamily: displayFontFamily ?? this.displayFontFamily,
    displayFontFamilyFallback:
        displayFontFamilyFallback ?? this.displayFontFamilyFallback,
    eyebrow: eyebrow ?? this.eyebrow,
    uppercaseLabels: uppercaseLabels ?? this.uppercaseLabels,
    rootHeaderRuleFraction:
        rootHeaderRuleFraction ?? this.rootHeaderRuleFraction,
    pageTitleLineHeight: pageTitleLineHeight ?? this.pageTitleLineHeight,
    preset: preset ?? this.preset,
    panelCornerRadius: panelCornerRadius ?? this.panelCornerRadius,
    primaryActionCornerRadius:
        primaryActionCornerRadius ?? this.primaryActionCornerRadius,
    statusOffline: statusOffline ?? this.statusOffline,
    brandTagline: brandTagline ?? this.brandTagline,
    brandAccent: brandAccent ?? this.brandAccent,
    markIcon: markIcon ?? this.markIcon,
    iconTileForeground: iconTileForeground ?? this.iconTileForeground,
    barTrack: barTrack ?? this.barTrack,
    panelNote: panelNote ?? this.panelNote,
    heroScrim: heroScrim ?? this.heroScrim,
    heroFloorScrim: heroFloorScrim ?? this.heroFloorScrim,
  );

  /// Interpolates colors and continuous numeric values. Discrete values (corner style, font
  /// families, gradients, and casing) snap to whichever side of [t] is closer
  /// because they have no meaningful halfway point.
  @override
  DovahThemeTokens lerp(ThemeExtension<DovahThemeTokens>? other, double t) {
    if (other is! DovahThemeTokens) {
      return this;
    }
    return DovahThemeTokens(
      background: Color.lerp(background, other.background, t)!,
      surface: Color.lerp(surface, other.surface, t)!,
      surfaceRaised: Color.lerp(surfaceRaised, other.surfaceRaised, t)!,
      surface3: Color.lerp(surface3, other.surface3, t)!,
      lineSubtle: Color.lerp(lineSubtle, other.lineSubtle, t)!,
      lineStrong: Color.lerp(lineStrong, other.lineStrong, t)!,
      textPrimary: Color.lerp(textPrimary, other.textPrimary, t)!,
      textMuted: Color.lerp(textMuted, other.textMuted, t)!,
      textFaint: Color.lerp(textFaint, other.textFaint, t)!,
      accentPrimary: Color.lerp(accentPrimary, other.accentPrimary, t)!,
      accentSecondary: Color.lerp(accentSecondary, other.accentSecondary, t)!,
      signal: Color.lerp(signal, other.signal, t)!,
      ember: Color.lerp(ember, other.ember, t)!,
      success: Color.lerp(success, other.success, t)!,
      warning: Color.lerp(warning, other.warning, t)!,
      danger: Color.lerp(danger, other.danger, t)!,
      primaryActionForeground: Color.lerp(
        primaryActionForeground,
        other.primaryActionForeground,
        t,
      )!,
      soft: Color.lerp(soft, other.soft, t)!,
      health: Color.lerp(health, other.health, t)!,
      magicka: Color.lerp(magicka, other.magicka, t)!,
      stamina: Color.lerp(stamina, other.stamina, t)!,
      cornerStyle: t < 0.5 ? cornerStyle : other.cornerStyle,
      cornerRadius: lerpDouble(cornerRadius, other.cornerRadius, t)!,
      cornerCutSize: lerpDouble(cornerCutSize, other.cornerCutSize, t)!,
      displayFontFamily: t < 0.5 ? displayFontFamily : other.displayFontFamily,
      displayFontFamilyFallback: t < 0.5
          ? displayFontFamilyFallback
          : other.displayFontFamilyFallback,
      eyebrow: Color.lerp(eyebrow, other.eyebrow, t)!,
      uppercaseLabels: t < 0.5 ? uppercaseLabels : other.uppercaseLabels,
      rootHeaderRuleFraction: lerpDouble(
        rootHeaderRuleFraction,
        other.rootHeaderRuleFraction,
        t,
      )!,
      pageTitleLineHeight: lerpDouble(
        pageTitleLineHeight,
        other.pageTitleLineHeight,
        t,
      )!,
      preset: t < 0.5 ? preset : other.preset,
      panelCornerRadius: lerpDouble(
        panelCornerRadius,
        other.panelCornerRadius,
        t,
      )!,
      primaryActionCornerRadius: lerpDouble(
        primaryActionCornerRadius,
        other.primaryActionCornerRadius,
        t,
      )!,
      statusOffline: Color.lerp(statusOffline, other.statusOffline, t)!,
      brandTagline: Color.lerp(brandTagline, other.brandTagline, t)!,
      brandAccent: Color.lerp(brandAccent, other.brandAccent, t)!,
      markIcon: Color.lerp(markIcon, other.markIcon, t)!,
      iconTileForeground: Color.lerp(
        iconTileForeground,
        other.iconTileForeground,
        t,
      )!,
      barTrack: Color.lerp(barTrack, other.barTrack, t)!,
      panelNote: Color.lerp(panelNote, other.panelNote, t)!,
      heroScrim: t < 0.5 ? heroScrim : other.heroScrim,
      heroFloorScrim: t < 0.5 ? heroFloorScrim : other.heroFloorScrim,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    background,
    surface,
    surfaceRaised,
    surface3,
    lineSubtle,
    lineStrong,
    textPrimary,
    textMuted,
    textFaint,
    accentPrimary,
    accentSecondary,
    signal,
    ember,
    success,
    warning,
    danger,
    primaryActionForeground,
    soft,
    health,
    magicka,
    stamina,
    cornerStyle,
    cornerRadius,
    cornerCutSize,
    displayFontFamily,
    displayFontFamilyFallback,
    eyebrow,
    uppercaseLabels,
    rootHeaderRuleFraction,
    pageTitleLineHeight,
    preset,
    panelCornerRadius,
    primaryActionCornerRadius,
    statusOffline,
    brandTagline,
    brandAccent,
    markIcon,
    iconTileForeground,
    barTrack,
    panelNote,
    heroScrim,
    heroFloorScrim,
  ];
}
