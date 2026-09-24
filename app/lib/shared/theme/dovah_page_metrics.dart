import 'package:flutter/foundation.dart';
import 'package:flutter/painting.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/dovah_page_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';

/// The measurements of a game page's content area inside the session shell (the approved
/// prototype's `.session-content`, `.game-page`, `.page-intro`, `.panel`, and `.placeholder-grid`),
/// shared by every game page. Like [DovahRootMetrics] they change at the narrow and compact window
/// breakpoints and several are pinned differently by each theme. The theme-varying values live in
/// [DovahPageThemeMetrics], a theme extension that Flutter interpolates during a theme change, and
/// [forWindow] resolves them for the window; a page never branches on the window size or the
/// preset itself.
@immutable
class DovahPageMetrics extends Equatable {
  /// Maximum width of a page's content column (the prototype's `.game-page` `min(1180px,...)`).
  static const double contentMaxWidth = 1180;

  /// Padding below a page's content (the prototype's `.session-content` `padding-bottom:42px`).
  static const double contentBottomPadding = 42;

  /// Gap between a page title and its description (the prototype's `.page-intro h1` bottom
  /// margin).
  static const double introTitleBottomGap = 5;

  /// Font size of a page description and its sync note (the prototype's `.page-intro p`/`.sync`
  /// as themed).
  static const double introDescriptionFontSize = 13;

  /// Gap between placeholder cards (the prototype's `.placeholder-grid` `gap:12px`).
  static const double placeholderGap = 12;

  /// Minimum height of a placeholder card (the prototype's `.placeholder-card`).
  static const double placeholderCardMinHeight = 145;

  /// Padding of a placeholder card (the prototype's `.placeholder-card` `padding:17px`).
  static const double placeholderCardPadding = 17;

  /// Gap below a placeholder card's title (the prototype's `.placeholder-card b`).
  static const double placeholderCardTitleBottomGap = 8;

  /// Font size of a placeholder card's body (the prototype's `.placeholder-card p`).
  static const double placeholderBodyFontSize = 13;

  /// Line height, as a multiple of font size, of a placeholder card's body.
  static const double placeholderBodyLineHeight = 1.5;

  /// Margin on each side of the page's content column.
  final double sideMargin;

  /// Padding above the page's content.
  final double contentTopPadding;

  /// Gap between the page intro and the content below it.
  final double introBottomGap;

  /// Font size of the page title.
  final double introTitleFontSize;

  /// Padding inside the page intro: only Hearth boxes it, so it is zero elsewhere.
  final EdgeInsets introPadding;

  /// Padding inside a content panel.
  final EdgeInsets panelPadding;

  /// Number of columns in the placeholder grid.
  final int placeholderColumns;

  /// Creates a complete measurement set. Every value is required so a set cannot be assembled
  /// with an accidentally-inherited default.
  const DovahPageMetrics({
    required this.sideMargin,
    required this.contentTopPadding,
    required this.introBottomGap,
    required this.introTitleFontSize,
    required this.introPadding,
    required this.panelPadding,
    required this.placeholderColumns,
  });

  /// Resolves the measurements for a window of size [window] from [themeMetrics], the active
  /// theme's (possibly mid-transition) values for each window mode. The compact height selects
  /// the other theme values; the narrow width only changes the margin and column count, and the
  /// title size follows the compact height alone. Taken from the prototype's `index.html` media
  /// queries.
  factory DovahPageMetrics.forWindow({
    required DovahPageThemeMetrics themeMetrics,
    required Size window,
  }) {
    final bool narrow = window.width <= DovahRootMetrics.narrowMaxWindowWidth;
    final bool compact =
        window.height <= DovahRootMetrics.compactMaxWindowHeight;

    return DovahPageMetrics(
      sideMargin: narrow ? 18 : 32,
      contentTopPadding: compact
          ? themeMetrics.compactContentTopPadding
          : themeMetrics.regularContentTopPadding,
      introBottomGap: compact
          ? themeMetrics.compactIntroBottomGap
          : themeMetrics.regularIntroBottomGap,
      introTitleFontSize: compact ? 26 : 31,
      introPadding: themeMetrics.introPadding,
      panelPadding: compact
          ? themeMetrics.compactPanelPadding
          : themeMetrics.regularPanelPadding,
      placeholderColumns: narrow ? 2 : 3,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    sideMargin,
    contentTopPadding,
    introBottomGap,
    introTitleFontSize,
    introPadding,
    panelPadding,
    placeholderColumns,
  ];
}
