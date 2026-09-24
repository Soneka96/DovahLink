import 'dart:ui' show lerpDouble;

import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// DovahLink's typed visual-theme contract: the complete material and atmosphere boundary a
/// [DovahThemePreset] resolves to, beyond what a plain Material [ColorScheme] can express.
/// Shared DovahLink surfaces and components read this extension rather than branching on which
/// concrete preset is active.
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

  /// Opacity of the environment-background scrim at the top edge.
  static const double environmentTopScrimOpacity = 0.82;

  /// Opacity of the environment-background scrim at the bottom edge.
  static const double environmentBottomScrimOpacity = 0.55;

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

  /// The approved primary-button fill for this theme.
  final Gradient primaryActionGradient;

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
  /// [DovahPanelCornerStyle.doubleBevel]. A single representative size shared by every bevelled
  /// component in the theme; the approved prototype varies this slightly per component, which
  /// this token intentionally simplifies to one value per theme.
  final double cornerCutSize;

  /// The outer drop shadow a raised panel casts. The approved prototype also layers inset
  /// highlight/shadow via CSS `box-shadow: inset`, which Flutter's [BoxShadow] cannot express;
  /// that polish is reproduced by [materialGradient]'s own highlight stops instead.
  final List<BoxShadow> panelShadow;

  /// The base material recipe for a resting panel, connection card, or dialog.
  final Gradient materialGradient;

  /// The material recipe for a raised/hovered panel or connection card.
  final Gradient materialRaisedGradient;

  /// The display/heading font family for this theme. The body font family does not vary by
  /// theme in the approved prototype, so it is not part of this contract.
  final String displayFontFamily;

  /// The asset path for this theme's atmospheric background image, or `null` when the theme
  /// uses a pure gradient atmosphere with no image (Dovah).
  final String? environmentAssetPath;

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

  /// Which preset these tokens belong to. Metrics classes key their prototype-exact per-theme
  /// tables on it; widgets never branch on it.
  final DovahThemePreset preset;

  /// The scrim color, alpha included, drawn behind a dialog (the prototype's per-theme
  /// `.modal-backdrop` background). The prototype's `saturate`/`sepia` backdrop filters have no
  /// direct Flutter equivalent and are not reproduced.
  final Color backdropColor;

  /// The blur strength behind a dialog (the prototype's per-theme `.modal-backdrop`
  /// `backdrop-filter: blur`).
  final double backdropBlurSigma;

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
    required this.primaryActionGradient,
    required this.primaryActionForeground,
    required this.soft,
    required this.health,
    required this.magicka,
    required this.stamina,
    required this.cornerStyle,
    required this.cornerRadius,
    required this.cornerCutSize,
    required this.panelShadow,
    required this.materialGradient,
    required this.materialRaisedGradient,
    required this.displayFontFamily,
    required this.environmentAssetPath,
    required this.eyebrow,
    required this.uppercaseLabels,
    required this.rootHeaderRuleFraction,
    required this.pageTitleLineHeight,
    required this.preset,
    required this.backdropColor,
    required this.backdropBlurSigma,
    required this.panelCornerRadius,
    required this.primaryActionCornerRadius,
    required this.statusOffline,
    required this.brandTagline,
    required this.brandAccent,
    required this.markIcon,
    required this.barTrack,
    required this.panelNote,
    required this.heroScrim,
    required this.heroFloorScrim,
  });

  /// Returns a copy with selected values replaced. [environmentAssetPath] is nullable, so it is
  /// threaded through [Option] to keep "omitted", "cleared to null", and "set" distinct.
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

    /// Replacement primary-button fill.
    Gradient? primaryActionGradient,

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
    List<BoxShadow>? panelShadow,
    Gradient? materialGradient,
    Gradient? materialRaisedGradient,
    String? displayFontFamily,
    Option<String>? environmentAssetPath,
    Color? eyebrow,
    bool? uppercaseLabels,
    double? rootHeaderRuleFraction,
    double? pageTitleLineHeight,
    DovahThemePreset? preset,
    Color? backdropColor,
    double? backdropBlurSigma,
    double? panelCornerRadius,
    double? primaryActionCornerRadius,
    Color? statusOffline,
    Color? brandTagline,
    Color? brandAccent,
    Color? markIcon,
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
    primaryActionGradient: primaryActionGradient ?? this.primaryActionGradient,
    primaryActionForeground:
        primaryActionForeground ?? this.primaryActionForeground,
    soft: soft ?? this.soft,
    health: health ?? this.health,
    magicka: magicka ?? this.magicka,
    stamina: stamina ?? this.stamina,
    cornerStyle: cornerStyle ?? this.cornerStyle,
    cornerRadius: cornerRadius ?? this.cornerRadius,
    cornerCutSize: cornerCutSize ?? this.cornerCutSize,
    panelShadow: panelShadow ?? this.panelShadow,
    materialGradient: materialGradient ?? this.materialGradient,
    materialRaisedGradient:
        materialRaisedGradient ?? this.materialRaisedGradient,
    displayFontFamily: displayFontFamily ?? this.displayFontFamily,
    environmentAssetPath: environmentAssetPath == null
        ? this.environmentAssetPath
        : environmentAssetPath.toNullable(),
    eyebrow: eyebrow ?? this.eyebrow,
    uppercaseLabels: uppercaseLabels ?? this.uppercaseLabels,
    rootHeaderRuleFraction:
        rootHeaderRuleFraction ?? this.rootHeaderRuleFraction,
    pageTitleLineHeight: pageTitleLineHeight ?? this.pageTitleLineHeight,
    preset: preset ?? this.preset,
    backdropColor: backdropColor ?? this.backdropColor,
    backdropBlurSigma: backdropBlurSigma ?? this.backdropBlurSigma,
    panelCornerRadius: panelCornerRadius ?? this.panelCornerRadius,
    primaryActionCornerRadius:
        primaryActionCornerRadius ?? this.primaryActionCornerRadius,
    statusOffline: statusOffline ?? this.statusOffline,
    brandTagline: brandTagline ?? this.brandTagline,
    brandAccent: brandAccent ?? this.brandAccent,
    markIcon: markIcon ?? this.markIcon,
    barTrack: barTrack ?? this.barTrack,
    panelNote: panelNote ?? this.panelNote,
    heroScrim: heroScrim ?? this.heroScrim,
    heroFloorScrim: heroFloorScrim ?? this.heroFloorScrim,
  );

  /// Interpolates colors and continuous numeric values. Discrete values (corner style, font
  /// family, asset path, shadow, gradients, and casing) snap to whichever side of [t] is closer
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
      primaryActionGradient: t < 0.5
          ? primaryActionGradient
          : other.primaryActionGradient,
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
      panelShadow: t < 0.5 ? panelShadow : other.panelShadow,
      materialGradient: t < 0.5 ? materialGradient : other.materialGradient,
      materialRaisedGradient: t < 0.5
          ? materialRaisedGradient
          : other.materialRaisedGradient,
      displayFontFamily: t < 0.5 ? displayFontFamily : other.displayFontFamily,
      environmentAssetPath: t < 0.5
          ? environmentAssetPath
          : other.environmentAssetPath,
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
      backdropColor: Color.lerp(backdropColor, other.backdropColor, t)!,
      backdropBlurSigma: lerpDouble(
        backdropBlurSigma,
        other.backdropBlurSigma,
        t,
      )!,
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
    primaryActionGradient,
    primaryActionForeground,
    soft,
    health,
    magicka,
    stamina,
    cornerStyle,
    cornerRadius,
    cornerCutSize,
    panelShadow,
    materialGradient,
    materialRaisedGradient,
    displayFontFamily,
    environmentAssetPath,
    eyebrow,
    uppercaseLabels,
    rootHeaderRuleFraction,
    pageTitleLineHeight,
    preset,
    backdropColor,
    backdropBlurSigma,
    panelCornerRadius,
    primaryActionCornerRadius,
    statusOffline,
    brandTagline,
    brandAccent,
    markIcon,
    barTrack,
    panelNote,
    heroScrim,
    heroFloorScrim,
  ];
}
