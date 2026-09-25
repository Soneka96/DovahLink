import 'package:flutter/foundation.dart';
import 'package:flutter/painting.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/dovah_appearance_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';

/// The measurements of the appearance picker (the approved prototype's `.preset-intro`,
/// `.preset-grid`, `.preset-card`, `.preset-scene`, and `.preset-copy`). The grid, the card's
/// spacing, and the preview shrink at the compact window height; the card's outline comes from the
/// theme and is resolved through [DovahAppearanceThemeMetrics]. [forWindow] resolves both, so no
/// widget branches on the window size or the preset. The breakpoint is [DovahRootMetrics]'s, the
/// prototype's `max-height:620px`.
@immutable
class DovahAppearanceMetrics extends Equatable {
  /// Number of preset cards per row (the prototype's `.preset-grid` `repeat(3,1fr)`).
  static const int columnCount = 3;

  /// The narrowest a card may get before the grid gives it fewer columns. An accessibility and
  /// readability floor, not a prototype value: the prototype's screen never gets narrower than
  /// 720px, so its grid always has [columnCount] columns.
  static const double cardMinimumWidth = 160;

  /// Font size of a card's title (the prototype's `.preset-copy>b`).
  static const double titleFontSize = 15;

  /// Font size of a card's summary line (the prototype's `.preset-copy>span`).
  static const double summaryFontSize = 12;

  /// Font size of a card's materials line (the prototype's `.preset-copy>small`).
  static const double detailFontSize = 10;

  /// Line height, as a multiple of font size, of a card's summary and materials lines (the
  /// prototype's `line-height:1.35`).
  static const double copyLineHeight = 1.35;

  /// Gap between the lines of a card's copy (the prototype's `.preset-copy` `gap:3px`).
  static const double copyGap = 3;

  /// Width of the ring around the selected card (the prototype's `.preset-card.selected`
  /// `box-shadow:0 0 0 2px`).
  static const double selectedRingWidth = 2;

  /// How far a hovered card rises (the prototype's `.preset-card:hover` `translateY(-2px)`).
  static const Offset hoverOffset = Offset(0, -2);

  /// Width and height of the selected badge (the prototype's `.preset-check`).
  static const double badgeSize = 23;

  /// Distance of the selected badge from the card's top and right edges (the prototype's
  /// `.preset-check` `right:9px;top:9px`).
  static const double badgeInset = 9;

  /// Size of the check glyph in the selected badge (the prototype's `.preset-check` `font-size:13px`).
  static const double badgeGlyphSize = 13;

  /// Height of each accent bar in a card's preview (the prototype's `.preset-ui i`).
  static const double previewAccentHeight = 6;

  /// Width and height of the sigil tile in a card's preview, border and padding included (the
  /// prototype's `.preset-sigil`).
  static const double previewSigilSize = 45;

  /// Padding between the sigil tile's border and its mark (the prototype's `.preset-sigil`).
  static const double previewSigilPadding = 7;

  /// Distance of the accent bars from the preview's left and right edges (the prototype's
  /// `.preset-ui`).
  static const double previewBarsInset = 12;

  /// Distance of the accent bars from the preview's bottom edge (the prototype's `.preset-ui`).
  static const double previewBarsBottom = 10;

  /// Gap between adjacent accent bars (the prototype's `.preset-ui`).
  static const double previewBarsGap = 4;

  /// The relative widths of the three accent bars, in hundredths (the prototype's `.preset-ui`
  /// `grid-template-columns:1.4fr .8fr .45fr`).
  static const List<int> previewBarFlexes = [140, 80, 45];

  /// Font size of the picker's heading (the prototype's `.preset-intro>b`).
  static const double introTitleFontSize = 16;

  /// Font size of the picker's description (the prototype's `.preset-intro>span`).
  static const double introBodyFontSize = 13;

  /// Line height, as a multiple of font size, of the picker's description (the prototype's
  /// `.preset-intro>span` `line-height:1.4`).
  static const double introBodyLineHeight = 1.4;

  /// Gap between the picker's heading and description (the prototype's `.preset-intro` `gap:4px`).
  static const double introGap = 4;

  /// Height of a card's preview scene (the prototype's `.preset-card` `grid-template-rows`, first
  /// row).
  final double previewHeight;

  /// Padding around a card's copy (the prototype's `.preset-copy`).
  final double copyPadding;

  /// Gap between cards, in both directions (the prototype's `.preset-grid` `gap`).
  final double gridGap;

  /// Gap between the picker's introduction and its cards (the prototype's `.preset-intro`
  /// `margin-bottom`).
  final double introBottomGap;

  /// Whether a card shows its materials line (the prototype hides `.preset-copy>small` at compact
  /// heights).
  final bool showDetail;

  /// Bevel cut size of a card when its corner style is a bevel; unused by Hearth.
  final double cornerCutSize;

  /// Corner radius of a card when its corner style is rounded; unused by the bevelled themes.
  final double cornerRadius;

  /// Creates a complete measurement set. Every value is required so a set cannot be assembled with
  /// an accidentally-inherited default.
  const DovahAppearanceMetrics({
    required this.previewHeight,
    required this.copyPadding,
    required this.gridGap,
    required this.introBottomGap,
    required this.showDetail,
    required this.cornerCutSize,
    required this.cornerRadius,
  });

  /// Returns how many cards fit in a row of [width]: [columnCount] whenever each card keeps at
  /// least [cardMinimumWidth], and fewer only in a very narrow or heavily scaled surface.
  int columnsFor(double width) {
    final int fitting = ((width + gridGap) / (cardMinimumWidth + gridGap))
        .floor();

    return fitting.clamp(1, columnCount);
  }

  /// Resolves the measurements for a window of size [window] from [themeMetrics], the active
  /// theme's (possibly mid-transition) card outline. Only the compact height selects other
  /// window values.
  factory DovahAppearanceMetrics.forWindow({
    required DovahAppearanceThemeMetrics themeMetrics,
    required Size window,
  }) {
    final bool compact =
        window.height <= DovahRootMetrics.compactMaxWindowHeight;

    return DovahAppearanceMetrics(
      previewHeight: compact ? 78 : 112,
      copyPadding: compact ? 9 : 13,
      gridGap: compact ? 8 : 11,
      introBottomGap: compact ? 10 : 16,
      showDetail: !compact,
      cornerCutSize: themeMetrics.cornerCutSize,
      cornerRadius: themeMetrics.cornerRadius,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    previewHeight,
    copyPadding,
    gridGap,
    introBottomGap,
    showDetail,
    cornerCutSize,
    cornerRadius,
  ];
}
