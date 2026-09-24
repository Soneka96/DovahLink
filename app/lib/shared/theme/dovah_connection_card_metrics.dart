import 'package:flutter/foundation.dart';
import 'package:flutter/painting.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/dovah_connection_card_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';

/// The measurements of a connection card (the approved prototype's `.connection`). Its padding,
/// height, and icon tile are pinned differently by each theme and shrink at the compact window
/// height, and its detail column disappears at the narrow window width. The theme-varying values
/// live in [DovahConnectionCardThemeMetrics], a theme extension that Flutter interpolates during a
/// theme change, and [forWindow] resolves them for the window; the card never branches on the
/// window size or the preset itself. The breakpoints are [DovahRootMetrics]'s.
///
/// [minHeight] is the height the prototype's card actually renders at (measured in the
/// prototype), not its `min-height` declaration: with the prototype's fonts the card's content is
/// taller than the declared minimum in most themes.
@immutable
class DovahConnectionCardMetrics extends Equatable {
  /// Diameter of the connection-state marker (the prototype's `.dot`).
  static const double stateMarkerSize = 8;

  /// Gap between the state marker and its label (the prototype's `.status` `gap:8px`).
  static const double stateMarkerGap = 8;

  /// Minimum width of the state label, which right-aligns inside it (the prototype's `.status`
  /// `min-width:112px`).
  static const double statusMinWidth = 112;

  /// Gap between the state label and the arrow (the prototype's `.connection .right`
  /// `gap:13px` plus the `.arrow` `margin-left:4px`).
  static const double statusArrowGap = 17;

  /// Size of the arrow (the prototype's `.arrow` 24px glyph).
  static const double arrowSize = 24;

  /// Gap between the card's columns (the prototype's `.connection` `gap:16px`).
  static const double columnGap = 16;

  /// Size of the icon inside the icon tile (the prototype's `.pc-icon svg`).
  static const double iconSize = 21;

  /// Font size of the card's title (the prototype's `.connection-main b`).
  static const double titleFontSize = 16;

  /// Gap between the card's title and its subtitle (the prototype's `.connection-main b`
  /// `margin-bottom:4px`).
  static const double titleBottomGap = 4;

  /// Flex share of the title column (the prototype's `minmax(190px,1fr)`).
  static const int mainColumnFlex = 10;

  /// Flex share of the detail column (the prototype's `minmax(190px,.7fr)`).
  static const int detailColumnFlex = 7;

  /// The card's padding.
  final EdgeInsets padding;

  /// The height the card renders at.
  final double minHeight;

  /// Width and height of the icon tile.
  final double iconTileSize;

  /// Corner radius of the icon tile: none in Frostbound and Dovah, a full circle in Hearth.
  final double iconTileRadius;

  /// Bevel cut size of the card when the theme's corner style is a bevel: 11 in Frostbound and
  /// 16 in Dovah, differing from the theme's general bevel; unused by Hearth.
  final double cornerCutSize;

  /// Corner radius of the card when the theme's corner style is rounded: 12 in Hearth, differing
  /// from the theme's general radius; unused by the bevelled themes.
  final double cornerRadius;

  /// Whether the detail column is shown (the prototype hides it at narrow widths).
  final bool showDetail;

  /// Creates a complete measurement set. Every value is required so a set cannot be assembled
  /// with an accidentally-inherited default.
  const DovahConnectionCardMetrics({
    required this.padding,
    required this.minHeight,
    required this.iconTileSize,
    required this.iconTileRadius,
    required this.cornerCutSize,
    required this.cornerRadius,
    required this.showDetail,
  });

  /// Resolves the measurements for a window of size [window] from [themeMetrics], the active
  /// theme's (possibly mid-transition) values for each window mode. The card has no narrow-only
  /// geometry, so only the compact height selects other values, taken from the prototype's
  /// `index.html` media queries.
  factory DovahConnectionCardMetrics.forWindow({
    required DovahConnectionCardThemeMetrics themeMetrics,
    required Size window,
  }) {
    final bool compact =
        window.height <= DovahRootMetrics.compactMaxWindowHeight;

    return DovahConnectionCardMetrics(
      padding: compact
          ? themeMetrics.compactPadding
          : themeMetrics.regularPadding,
      minHeight: compact
          ? themeMetrics.compactMinHeight
          : themeMetrics.regularMinHeight,
      iconTileSize: compact
          ? themeMetrics.compactIconTileSize
          : themeMetrics.regularIconTileSize,
      iconTileRadius: compact
          ? themeMetrics.compactIconTileRadius
          : themeMetrics.regularIconTileRadius,
      cornerCutSize: themeMetrics.cornerCutSize,
      cornerRadius: themeMetrics.cornerRadius,
      showDetail: window.width > DovahRootMetrics.narrowMaxWindowWidth,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    padding,
    minHeight,
    iconTileSize,
    iconTileRadius,
    cornerCutSize,
    cornerRadius,
    showDetail,
  ];
}
