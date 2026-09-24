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
  /// Smallest shared spacing unit.
  static const double spacing4 = 4;

  /// Shared spacing for compact card gutters.
  static const double spacing6 = 6;

  /// Shared spacing for small layout gaps.
  static const double spacing8 = 8;

  /// Shared spacing for button padding and compact surfaces.
  static const double spacing12 = 12;

  /// Shared spacing for section and connection-card gaps.
  static const double spacing16 = 16;

  /// Shared horizontal button padding.
  static const double spacing17 = 17;

  /// Shared panel and connection-card padding.
  static const double spacing18 = 18;

  /// Shared dialog-header padding.
  static const double spacing19 = 19;

  /// Shared dialog-content padding.
  static const double spacing22 = 22;

  /// Shared dialog-backdrop padding.
  static const double spacing24 = 24;

  /// Font size for supporting text and compact labels.
  static const double compactFontSize = 13;

  /// Font size for dialog titles.
  static const double dialogTitleFontSize = 23;

  /// Diameter of the connection-state marker.
  static const double connectionStateMarkerSize = 8;

  /// Size of the connection-card icon tile before density scaling.
  static const double connectionIconTileSize = 43;

  /// Corner radius of the connection-card icon tile before density scaling.
  static const double connectionIconTileRadius = 9;

  /// Size of the icon inside a connection-card icon tile before density scaling.
  static const double connectionIconSize = 21;

  /// Height of an appearance-preset preview.
  static const double appearancePreviewHeight = 48;

  /// Minimum width of an appearance-preset card before it wraps to another row.
  static const double appearancePresetCardMinimumWidth = 160;

  /// Height of the accent strip in an appearance-preset preview.
  static const double appearancePreviewAccentHeight = 6;

  /// Size of the selected-preset indicator.
  static const double appearanceSelectionIconSize = 20;

  /// Size of the icon inside a themed button.
  static const double buttonIconSize = 17;

  /// Gap between a themed button's icon and its label.
  static const double buttonIconGap = 9;

  /// Width and height of a themed icon-only button's visible surface.
  static const double iconButtonSize = 40;

  /// Size of the icon inside a themed icon-only button.
  static const double iconButtonIconSize = 19;

  /// Letter spacing, in ems, of labels a theme renders in uppercase.
  static const double uppercaseLetterSpacingEm = 0.045;

  /// Width below which the root screen stops shrinking and scrolls horizontally.
  static const double rootMinimumWidth = 720;

  /// Maximum width of the root screen's content column.
  static const double rootContentMaxWidth = 1180;

  /// Margin on each side of the root screen's content column.
  static const double rootContentSideMargin = 32;

  /// Padding below the root screen's content.
  static const double rootContentBottomPadding = 40;

  /// Height of the gradient rule under the root header.
  static const double rootHeaderRuleHeight = 2;

  /// Opacity of the root header's surface fill.
  static const double rootHeaderBackgroundOpacity = 0.88;

  /// Blur strength behind the root header.
  static const double rootHeaderBlurSigma = 11;

  /// Width and height of the root header's brand mark.
  static const double rootBrandMarkSize = 44;

  /// Gap between the brand mark and the wordmark.
  static const double rootBrandGap = 13;

  /// Font size of the DovahLink wordmark.
  static const double brandNameFontSize = 18;

  /// Letter spacing, in ems, of the DovahLink wordmark.
  static const double brandNameLetterSpacingEm = 0.15;

  /// Font size of the wordmark's tagline.
  static const double brandTaglineFontSize = 9;

  /// Letter spacing, in ems, of the wordmark's tagline.
  static const double brandTaglineLetterSpacingEm = 0.2;

  /// Gap between the wordmark and its tagline.
  static const double brandTaglineTopGap = 3;

  /// Gap between the page title block and its action.
  static const double rootHeroGap = 20;

  /// Font size of a page-title eyebrow.
  static const double eyebrowFontSize = 10;

  /// Letter spacing, in ems, of a page-title eyebrow.
  static const double eyebrowLetterSpacingEm = 0.2;

  /// Letter spacing, in ems, of a page title.
  static const double pageTitleLetterSpacingEm = 0.02;

  /// Letter spacing, in ems, of a page title a theme renders in uppercase.
  static const double pageTitleUppercaseLetterSpacingEm = 0.06;

  /// Gap between an eyebrow and its page title.
  static const double pageTitleTopGap = 7;

  /// Gap between a page title and its description.
  static const double pageTitleBottomGap = 5;

  /// Font size of a page description.
  static const double pageDescriptionFontSize = 14;

  /// Font size of a section label.
  static const double sectionLabelFontSize = 11;

  /// Letter spacing, in ems, of a section label.
  static const double sectionLabelLetterSpacingEm = 0.15;

  /// Gap between a section label and its rule.
  static const double sectionLabelGap = 10;

  /// Gap between a section label and its content.
  static const double sectionLabelBottomGap = 11;

  /// Gap between connection cards.
  static const double rootListGap = 10;

  /// Font size of the root screen's footer note.
  static const double rootFooterFontSize = 12;

  /// Line height, as a multiple of font size, of body and label text.
  static const double bodyLineHeight = 4 / 3;

  /// Font size of a themed button's label.
  static const double buttonFontSize = 16;

  /// Font size of a connection card's title.
  static const double connectionTitleFontSize = 16;

  /// Width of themed focus outlines.
  static const double focusOutlineWidth = 2;

  /// Minimum interactive target width and height for themed controls.
  static const double minimumTapTargetSize = 48;

  /// Blur radius of themed focus glows.
  static const double focusGlowBlurRadius = 8;

  /// Disabled-control opacity.
  static const double disabledControlOpacity = 0.46;

  /// Width of one pairing-code digit box.
  static const double pairingCodeBoxWidth = 45;

  /// Height of one pairing-code digit box.
  static const double pairingCodeBoxHeight = 48;

  /// Gap between pairing-code digit boxes.
  static const double pairingCodeBoxGap = 8;

  /// Font size of a digit inside a pairing-code digit box.
  static const double pairingCodeBoxFontSize = 22;

  /// Width of the halo around the focused pairing-code digit box.
  static const double pairingCodeBoxFocusRingWidth = 3;

  /// Font size of an inline form error message.
  static const double formErrorFontSize = 12;

  /// Height reserved for an inline form error message, so showing one does not shift the layout.
  static const double formErrorMinHeight = 14;

  /// Gap between adjacent action buttons in a dialog.
  static const double dialogActionGap = 10;

  /// Maximum width of a pairing state's content column.
  static const double pairingContentMaxWidth = 520;

  /// Maximum width of a pairing state's body copy.
  static const double pairingBodyMaxWidth = 430;

  /// Width and height of a pairing state's icon tile.
  static const double pairingMarkSize = 42;

  /// Size of the icon inside a pairing state's icon tile.
  static const double pairingMarkIconSize = 20;

  /// Gap below a pairing state's icon tile.
  static const double pairingMarkBottomGap = 8;

  /// Font size of a pairing state's heading.
  static const double pairingHeadingFontSize = 22;

  /// Gap below a pairing state's heading.
  static const double pairingHeadingBottomGap = 5;

  /// Font size of a pairing state's body copy.
  static const double pairingBodyFontSize = 14;

  /// Line height, as a multiple of font size, of a pairing state's body copy.
  static const double pairingBodyLineHeight = 1.35;

  /// Gap below a pairing state's body copy.
  static const double pairingBodyBottomGap = 10;

  /// Font size of a pairing state's footnote.
  static const double pairingNoteFontSize = 12;

  /// Gap above a pairing state's footnote.
  static const double pairingNoteTopGap = 8;

  /// Width and height of a pairing success mark.
  static const double pairingSuccessMarkSize = 62;

  /// Size of the check glyph inside a pairing success mark.
  static const double pairingSuccessGlyphSize = 29;

  /// Opacity of the status tone filling a pairing success mark.
  static const double statusMarkFillOpacity = 0.1;

  /// Opacity of the status tone outlining a pairing success mark.
  static const double statusMarkBorderOpacity = 0.36;

  /// Width and height of an inline progress spinner.
  static const double progressIndicatorSize = 15;

  /// Stroke width of an inline progress spinner.
  static const double progressIndicatorStrokeWidth = 2;

  /// Gap between an inline progress spinner and its status text.
  static const double progressStatusGap = 10;

  /// Brightness increase applied to hovered primary buttons.
  static const double primaryButtonHoverBrightness = 0.07;

  /// Duration of the primary-button hover transition.
  static const Duration buttonHoverDuration = Duration(milliseconds: 160);

  /// Border width shared by themed surfaces.
  static const double surfaceBorderWidth = 1;

  /// Blur strength applied behind themed dialogs.
  static const double dialogBackdropBlurSigma = 8;

  /// Opacity of the themed-dialog backdrop scrim.
  static const double dialogBackdropOpacity = 0.35;

  /// Color of the themed-dialog backdrop scrim.
  static const Color dialogBackdropColor = Colors.black;

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

  /// A multiplier applied to shared base spacing/sizing constants to express this theme's
  /// overall visual density (frostbound tightest, hearth roomiest).
  final double densityScale;

  /// The display/heading font family for this theme. The body font family does not vary by
  /// theme in the approved prototype, so it is not part of this contract.
  final String displayFontFamily;

  /// The asset path for this theme's atmospheric background image, or `null` when the theme
  /// uses a pure gradient atmosphere with no image (Dovah).
  final String? environmentAssetPath;

  /// The tone of the eyebrow label above a page title (the prototype's `.eyebrow`), which differs
  /// per theme rather than following [accentSecondary].
  final Color eyebrow;

  /// The height of the root screen's header bar before it is placed in the layout.
  final double rootHeaderHeight;

  /// The font size of a page title such as the root screen's "Connections".
  final double pageTitleFontSize;

  /// The minimum height of a connection card.
  final double connectionCardMinHeight;

  /// Whether page titles, connection names, and connection states render in uppercase, as the
  /// prototype's Frostbound theme does.
  final bool uppercaseLabels;

  /// Padding above the root screen's title row.
  final double rootContentTopPadding;

  /// Gap between the root screen's title row and its first section.
  final double rootHeroBottomGap;

  /// Fraction of the root header's width its gradient rule spans.
  final double rootHeaderRuleFraction;

  /// Line height, as a multiple of font size, of a page title.
  final double pageTitleLineHeight;

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
    required this.densityScale,
    required this.displayFontFamily,
    required this.environmentAssetPath,
    required this.eyebrow,
    required this.rootHeaderHeight,
    required this.pageTitleFontSize,
    required this.connectionCardMinHeight,
    required this.uppercaseLabels,
    required this.rootContentTopPadding,
    required this.rootHeroBottomGap,
    required this.rootHeaderRuleFraction,
    required this.pageTitleLineHeight,
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
    double? densityScale,
    String? displayFontFamily,
    Option<String>? environmentAssetPath,
    Color? eyebrow,
    double? rootHeaderHeight,
    double? pageTitleFontSize,
    double? connectionCardMinHeight,
    bool? uppercaseLabels,
    double? rootContentTopPadding,
    double? rootHeroBottomGap,
    double? rootHeaderRuleFraction,
    double? pageTitleLineHeight,
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
    densityScale: densityScale ?? this.densityScale,
    displayFontFamily: displayFontFamily ?? this.displayFontFamily,
    environmentAssetPath: environmentAssetPath == null
        ? this.environmentAssetPath
        : environmentAssetPath.toNullable(),
    eyebrow: eyebrow ?? this.eyebrow,
    rootHeaderHeight: rootHeaderHeight ?? this.rootHeaderHeight,
    pageTitleFontSize: pageTitleFontSize ?? this.pageTitleFontSize,
    connectionCardMinHeight:
        connectionCardMinHeight ?? this.connectionCardMinHeight,
    uppercaseLabels: uppercaseLabels ?? this.uppercaseLabels,
    rootContentTopPadding: rootContentTopPadding ?? this.rootContentTopPadding,
    rootHeroBottomGap: rootHeroBottomGap ?? this.rootHeroBottomGap,
    rootHeaderRuleFraction:
        rootHeaderRuleFraction ?? this.rootHeaderRuleFraction,
    pageTitleLineHeight: pageTitleLineHeight ?? this.pageTitleLineHeight,
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
      densityScale: lerpDouble(densityScale, other.densityScale, t)!,
      displayFontFamily: t < 0.5 ? displayFontFamily : other.displayFontFamily,
      environmentAssetPath: t < 0.5
          ? environmentAssetPath
          : other.environmentAssetPath,
      eyebrow: Color.lerp(eyebrow, other.eyebrow, t)!,
      rootHeaderHeight: lerpDouble(
        rootHeaderHeight,
        other.rootHeaderHeight,
        t,
      )!,
      pageTitleFontSize: lerpDouble(
        pageTitleFontSize,
        other.pageTitleFontSize,
        t,
      )!,
      connectionCardMinHeight: lerpDouble(
        connectionCardMinHeight,
        other.connectionCardMinHeight,
        t,
      )!,
      uppercaseLabels: t < 0.5 ? uppercaseLabels : other.uppercaseLabels,
      rootContentTopPadding: lerpDouble(
        rootContentTopPadding,
        other.rootContentTopPadding,
        t,
      )!,
      rootHeroBottomGap: lerpDouble(
        rootHeroBottomGap,
        other.rootHeroBottomGap,
        t,
      )!,
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
    densityScale,
    displayFontFamily,
    environmentAssetPath,
    eyebrow,
    rootHeaderHeight,
    pageTitleFontSize,
    connectionCardMinHeight,
    uppercaseLabels,
    rootContentTopPadding,
    rootHeroBottomGap,
    rootHeaderRuleFraction,
    pageTitleLineHeight,
  ];
}
