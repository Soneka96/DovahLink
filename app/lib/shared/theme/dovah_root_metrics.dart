import 'dart:ui' show Size;

import 'package:flutter/foundation.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// The measurements of the root (Connections) screen: its shell, header, and title block. The
/// approved prototype changes them at two independent window breakpoints -- narrow
/// (`max-width:900px`) and compact (`max-height:620px`) -- and each theme pins some of them
/// differently, so every value that varies lives in a prototype-exact table resolved by
/// [forWindow] and no widget branches on the window size or the preset itself. Where both
/// breakpoints apply, the compact (height) value wins, as it does in the prototype's cascade. The
/// connection card's own geometry is in `DovahConnectionCardMetrics`.
@immutable
class DovahRootMetrics extends Equatable {
  /// The widest window, in logical pixels, that still gets the narrow measurements (the
  /// prototype's `@media(max-width:900px)`).
  static const double narrowMaxWindowWidth = 900;

  /// The tallest window, in logical pixels, that still gets the compact measurements (the
  /// prototype's `@media(max-height:620px)`).
  static const double compactMaxWindowHeight = 620;

  /// Width below which the screen stops shrinking and scrolls horizontally (the prototype's
  /// `.screen{min-width:720px}`).
  static const double minimumWidth = 720;

  /// Maximum width of the content column (the prototype's `.shell` `min(1180px,...)`).
  static const double contentMaxWidth = 1180;

  /// Padding below the content (the prototype's `.root-content` `padding-bottom:40px`).
  static const double contentBottomPadding = 40;

  /// Height of the gradient rule under the header (the prototype's `.root-header:after`).
  static const double headerRuleHeight = 2;

  /// Opacity of the header's surface fill (the prototype's `color-mix(... 88%, transparent)`).
  static const double headerBackgroundOpacity = 0.88;

  /// Blur strength behind the header (the prototype's `backdrop-filter:blur(11px)`).
  static const double headerBlurSigma = 11;

  /// Width and height of the brand mark (the prototype's `.brand-mark` 44px sigil).
  static const double brandMarkSize = 44;

  /// Gap between the brand mark and the wordmark (the prototype's `.brand` `gap:13px`).
  static const double brandGap = 13;

  /// Font size of the DovahLink wordmark (the prototype's `.brand-name`).
  static const double brandNameFontSize = 18;

  /// Letter spacing, in ems, of the wordmark (the prototype's `.brand-name` `.15em`).
  static const double brandNameLetterSpacingEm = 0.15;

  /// Font size of the wordmark's tagline (the prototype's `.brand-sub` 9px).
  static const double brandTaglineFontSize = 9;

  /// Gap between the wordmark and its tagline (the prototype's `.brand-sub` `margin-top:3px`).
  static const double brandTaglineTopGap = 3;

  /// Gap between the title block and its action (the prototype's `.title-row` `gap:20px`).
  static const double heroGap = 20;

  /// Font size of a page-title eyebrow (the prototype's `.eyebrow`).
  static const double eyebrowFontSize = 10;

  /// Letter spacing, in ems, of a page-title eyebrow (the prototype's `.eyebrow` `.2em`).
  static const double eyebrowLetterSpacingEm = 0.2;

  /// Letter spacing, in ems, of a page title (the prototype's `.title-row h1` `.02em`).
  static const double pageTitleLetterSpacingEm = 0.02;

  /// Letter spacing, in ems, of a page title a theme renders in uppercase (the prototype's
  /// Frostbound `.title-row h1` `.06em`).
  static const double pageTitleUppercaseLetterSpacingEm = 0.06;

  /// Gap between a page title and its description (the prototype's `.title-row h1` bottom
  /// margin).
  static const double pageTitleBottomGap = 5;

  /// Font size of a page description (the prototype's `.title-row p`).
  static const double pageDescriptionFontSize = 14;

  /// Font size of a section label (the prototype's `.section-label`).
  static const double sectionLabelFontSize = 11;

  /// Letter spacing, in ems, of a section label (the prototype's `.section-label` `.15em`).
  static const double sectionLabelLetterSpacingEm = 0.15;

  /// Gap between a section label and its rule (the prototype's `.section-label` `gap:10px`).
  static const double sectionLabelGap = 10;

  /// Gap between a section label and its content (the prototype's `.section-label` bottom
  /// margin).
  static const double sectionLabelBottomGap = 11;

  /// Gap between connection cards (the prototype's `.connection-list` `gap:10px`).
  static const double listGap = 10;

  /// Font size of the footer note (the prototype's `.root-note`).
  static const double footerFontSize = 12;

  /// Gap above the footer note (the prototype's `.root-note` `margin-top:18px`).
  static const double footerTopGap = 18;

  /// Margin on each side of the content column.
  final double sideMargin;

  /// Height of the header bar.
  final double headerHeight;

  /// Padding above the title block.
  final double contentTopPadding;

  /// Gap between the title block and the first section.
  final double heroBottomGap;

  /// Font size of the page title.
  final double pageTitleFontSize;

  /// Gap between an eyebrow and its page title.
  final double pageTitleTopGap;

  /// Letter spacing, in ems, of the wordmark's tagline, which differs per theme.
  final double brandTaglineLetterSpacingEm;

  /// Whether the footer note is shown (the prototype hides it at compact heights).
  final bool showFooter;

  /// Creates a complete measurement set. Every value is required so a set cannot be assembled
  /// with an accidentally-inherited default.
  const DovahRootMetrics({
    required this.sideMargin,
    required this.headerHeight,
    required this.contentTopPadding,
    required this.heroBottomGap,
    required this.pageTitleFontSize,
    required this.pageTitleTopGap,
    required this.brandTaglineLetterSpacingEm,
    required this.showFooter,
  });

  /// Resolves the measurements for [preset] in a window of size [window]. Each table row lists
  /// the regular, narrow-only, and compact values in that order, taken from the prototype's
  /// `index.html` media queries and `themes.css` per-theme overrides.
  factory DovahRootMetrics.forWindow({
    required DovahThemePreset preset,
    required Size window,
  }) {
    final bool narrow = window.width <= narrowMaxWindowWidth;
    final bool compact = window.height <= compactMaxWindowHeight;

    double level((double, double, double) row) =>
        compact ? row.$3 : (narrow ? row.$2 : row.$1);

    return DovahRootMetrics(
      sideMargin: narrow ? 18 : 32,
      headerHeight: level(switch (preset) {
        DovahThemePreset.frostbound => (70, 70, 56),
        DovahThemePreset.dovah => (88, 88, 62),
        DovahThemePreset.hearth => (86, 86, 62),
      }),
      contentTopPadding: level(switch (preset) {
        DovahThemePreset.frostbound => (20, 20, 20),
        DovahThemePreset.dovah => (30, 20, 14),
        DovahThemePreset.hearth => (30, 20, 14),
      }),
      heroBottomGap: level(switch (preset) {
        DovahThemePreset.frostbound => (18, 18, 18),
        DovahThemePreset.dovah => (28, 20, 14),
        DovahThemePreset.hearth => (28, 20, 14),
      }),
      pageTitleFontSize: level(switch (preset) {
        DovahThemePreset.frostbound => (31, 31, 31),
        DovahThemePreset.dovah => (34, 28, 25),
        DovahThemePreset.hearth => (38, 38, 38),
      }),
      pageTitleTopGap: compact ? 4 : 7,
      brandTaglineLetterSpacingEm: switch (preset) {
        DovahThemePreset.frostbound => 0.24,
        DovahThemePreset.dovah => 0.2,
        DovahThemePreset.hearth => 0.14,
      },
      showFooter: !compact,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    sideMargin,
    headerHeight,
    contentTopPadding,
    heroBottomGap,
    pageTitleFontSize,
    pageTitleTopGap,
    brandTaglineLetterSpacingEm,
    showFooter,
  ];
}
