import 'dart:ui' show Size;

import 'package:flutter/foundation.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/dovah_overview_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';

/// The measurements of the Overview page's panels (the approved prototype's `.overview-grid`,
/// `.hero-panel`, `.stats`, `.quest`, and `.bars`). The grid ratio changes at the narrow window
/// width, and the grid gap, hero height, and stats gap are pinned differently by each theme and
/// tighten at the compact height. The theme-varying values live in [DovahOverviewThemeMetrics], a
/// theme extension that Flutter interpolates during a theme change, and [forWindow] resolves them
/// for the window; the page never branches on the window size or the preset itself. Panel padding
/// is `DovahPageMetrics.panelPadding`.
@immutable
class DovahOverviewMetrics extends Equatable {
  /// Width of the accent bar down a panel's leading edge (the prototype's `inset 3px 0` shadow).
  static const double panelAccentWidth = 3;

  /// Font size of the hero panel's kicker (the prototype's `.kicker`).
  static const double kickerFontSize = 10;

  /// Letter spacing, in ems, of the hero panel's kicker (the prototype's `.kicker` `.16em`).
  static const double kickerLetterSpacingEm = 0.16;

  /// Font size of the hero panel's title (the prototype's `.hero-panel h2`).
  static const double heroTitleFontSize = 30;

  /// Gap above the hero panel's title (the prototype's `.hero-panel h2` `margin-top:7px`).
  static const double heroTitleTopGap = 7;

  /// Gap below the hero panel's title (the prototype's `.hero-panel h2` bottom margin).
  static const double heroTitleBottomGap = 5;

  /// Font size of the hero panel's description (the prototype's `.hero-panel p`).
  static const double heroDescriptionFontSize = 13;

  /// Gap between stat columns (the prototype's `.stats` `gap:8px`).
  static const double statGap = 8;

  /// Padding above a stat under its rule (the prototype's `.stat` `padding-top:12px`).
  static const double statTopPadding = 12;

  /// Font size of a stat's value (the prototype's `.stat b`).
  static const double statValueFontSize = 21;

  /// Font size of a stat's label (the prototype's `.stat span` as themed).
  static const double statLabelFontSize = 12;

  /// Font size of a panel's title (the prototype's `.panel-title b`).
  static const double panelTitleFontSize = 13;

  /// Font size of a panel title's trailing note (the prototype's `.panel-title span` as themed).
  static const double panelTitleNoteFontSize = 12;

  /// Gap below a panel's title row (the prototype's `.panel-title` `margin-bottom:14px`).
  static const double panelTitleBottomGap = 14;

  /// Indent of a tracked quest's text from its rule (the prototype's `.quest` `padding-left:12px`).
  static const double questIndent = 12;

  /// Width of a tracked quest's leading rule (the prototype's `.quest` `border-left:2px`).
  static const double questRuleWidth = 2;

  /// Font size of a tracked quest's title (the prototype's `.quest b`).
  static const double questTitleFontSize = 13;

  /// Gap below a tracked quest's title (the prototype's `.quest b` `margin-bottom:5px`).
  static const double questTitleBottomGap = 5;

  /// Font size of a tracked quest's description (the prototype's `.quest span` as themed).
  static const double questDescriptionFontSize = 13;

  /// Line height, as a multiple of font size, of a tracked quest's description.
  static const double questDescriptionLineHeight = 1.4;

  /// Gap between stat bar rows (the prototype's `.bars` `gap:9px`).
  static const double barRowGap = 9;

  /// Gap between a stat bar row's label, bar, and value (the prototype's `.bar-row` `gap:9px`).
  static const double barColumnGap = 9;

  /// Width of a stat bar row's label column (the prototype's `.bar-row` `55px`).
  static const double barLabelWidth = 55;

  /// Width of a stat bar row's value column (the prototype's `.bar-row` `34px`).
  static const double barValueWidth = 34;

  /// Font size of a stat bar row (the prototype's `.bar-row` as themed).
  static const double barFontSize = 12;

  /// Thickness of a stat bar (the prototype's `.bar` `height:5px`).
  static const double barHeight = 5;

  /// Corner radius of a stat bar and its fill (the prototype's `.bar` `border-radius:5px`).
  static const double barRadius = 5;

  /// Flex share of the hero column in the overview grid.
  final int mainColumnFlex;

  /// Flex share of the side column in the overview grid.
  final int sideColumnFlex;

  /// Gap between the grid's columns and between the side column's panels.
  final double gridGap;

  /// Minimum height of the hero panel.
  final double heroMinHeight;

  /// Gap above the hero panel's stats.
  final double statsTopGap;

  /// Creates a complete measurement set. Every value is required so a set cannot be assembled
  /// with an accidentally-inherited default.
  const DovahOverviewMetrics({
    required this.mainColumnFlex,
    required this.sideColumnFlex,
    required this.gridGap,
    required this.heroMinHeight,
    required this.statsTopGap,
  });

  /// Resolves the measurements for a window of size [window] from [themeMetrics], the active
  /// theme's (possibly mid-transition) values for each window mode. The compact height selects the
  /// other hero and stats values; the narrow width only changes the column ratio. Taken from the
  /// prototype's `index.html` media queries and `themes.css` per-theme overrides.
  factory DovahOverviewMetrics.forWindow({
    required DovahOverviewThemeMetrics themeMetrics,
    required Size window,
  }) {
    final bool narrow = window.width <= DovahRootMetrics.narrowMaxWindowWidth;
    final bool compact =
        window.height <= DovahRootMetrics.compactMaxWindowHeight;

    return DovahOverviewMetrics(
      mainColumnFlex: narrow ? 115 : 125,
      sideColumnFlex: narrow ? 85 : 75,
      gridGap: themeMetrics.gridGap,
      heroMinHeight: compact
          ? themeMetrics.compactHeroMinHeight
          : themeMetrics.regularHeroMinHeight,
      statsTopGap: compact
          ? themeMetrics.compactStatsTopGap
          : themeMetrics.regularStatsTopGap,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    mainColumnFlex,
    sideColumnFlex,
    gridGap,
    heroMinHeight,
    statsTopGap,
  ];
}
