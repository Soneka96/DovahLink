import 'package:flutter/foundation.dart';
import 'package:flutter/painting.dart' show Size;

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_theme_metrics.dart';

/// The measurements a themed dialog and the content it hosts change with the window height: the
/// approved prototype has a regular layout and a tighter one for short landscape windows, and
/// every measurement that differs between them lives here so no widget branches on the window
/// size itself. The corner radii a theme pins for its marks and code boxes come from
/// [DovahDialogThemeMetrics]; resolve the set for a window with [forWindow].
@immutable
class DovahDialogMetrics extends Equatable {
  /// The tallest window, in logical pixels, that still gets the [compact] measurements.
  static const double compactMaxWindowHeight = 620;

  /// Padding between the window edge and the dialog (the prototype's `.modal-backdrop`
  /// `padding:24px`).
  static const double backdropPadding = 24;

  /// Maximum width of a themed dialog (the prototype's `.modal` `width:min(720px,88vw)`).
  static const double maxWidth = 720;

  /// Fraction of the window width a themed dialog may fill, up to [maxWidth].
  static const double widthFraction = 0.88;

  /// Font size of a dialog title (the prototype's `.modal-head h2`).
  static const double titleFontSize = 23;

  /// Gap between adjacent action buttons in a dialog (the prototype's `.pair-actions`
  /// `gap:10px`).
  static const double actionGap = 10;

  /// Gap between pairing-code digit boxes (the prototype's `.otp` `gap:8px`).
  static const double codeBoxGap = 8;

  /// Font size of a digit inside a pairing-code digit box (the prototype's `.otp input`).
  static const double codeBoxFontSize = 22;

  /// Width of the halo around the focused pairing-code digit box (the prototype's
  /// `.otp input:focus` `0 0 0 3px`).
  static const double codeBoxFocusRingWidth = 3;

  /// Font size of an inline form message (the prototype's `.error`).
  static const double messageFontSize = 12;

  /// Maximum width of a pairing state's content column (the prototype's `.pairing`).
  static const double contentMaxWidth = 520;

  /// Maximum width of a pairing state's body copy (the prototype's `.pairing p`).
  static const double bodyMaxWidth = 430;

  /// Font size of a pairing state's body copy (the prototype's `.pairing p`).
  static const double bodyFontSize = 14;

  /// Font size of a pairing state's footnote (the prototype's `.pairing .pair-note`).
  static const double noteFontSize = 12;

  /// Width and height of a pairing success mark (the prototype's `.success-icon`).
  static const double successMarkSize = 62;

  /// Gap below a pairing success mark (the prototype's `.success-icon` bottom margin).
  static const double successMarkBottomGap = 17;

  /// Size of the check glyph inside a pairing success mark (the prototype's `.success-icon`).
  static const double successGlyphSize = 29;

  /// Opacity of the status tone filling a pairing success mark (the prototype's `.success-icon`
  /// `rgba(...,.1)` fill).
  static const double statusMarkFillOpacity = 0.1;

  /// Opacity of the status tone outlining a pairing success mark (the prototype's
  /// `.success-icon` `rgba(...,.36)` border).
  static const double statusMarkBorderOpacity = 0.36;

  /// Width and height of an inline progress spinner (the prototype's `.spinner`).
  static const double progressIndicatorSize = 15;

  /// Stroke width of an inline progress spinner (the prototype's `.spinner` border).
  static const double progressIndicatorStrokeWidth = 2;

  /// Gap between an inline progress spinner and its status text (the prototype's `.searching`
  /// `gap:10px`).
  static const double progressStatusGap = 10;

  /// The measurements for windows taller than [compactMaxWindowHeight], before a theme's own corner
  /// radii: square marks and code boxes.
  static const DovahDialogMetrics regular = DovahDialogMetrics(
    headerVerticalPadding: 19,
    headerHorizontalPadding: 22,
    bodyVerticalPadding: 22,
    bodyHorizontalPadding: 22,
    heightFraction: 0.86,
    markSize: 54,
    markIconSize: 24,
    markBottomGap: 16,
    headingFontSize: 24,
    headingBottomGap: 8,
    bodyLineHeight: 1.5,
    bodyBottomGap: 20,
    codeBoxWidth: 49,
    codeBoxHeight: 56,
    codeRowTopGap: 6,
    codeRowBottomGap: 12,
    messageMinHeight: 18,
    actionsTopGap: 18,
    noteTopGap: 17,
    markCornerRadius: 0,
    codeBoxCornerRadius: 0,
  );

  /// The measurements for windows no taller than [compactMaxWindowHeight], before a theme's own
  /// corner radii: square marks and code boxes.
  static const DovahDialogMetrics compact = DovahDialogMetrics(
    headerVerticalPadding: 12,
    headerHorizontalPadding: 19,
    bodyVerticalPadding: 13,
    bodyHorizontalPadding: 17,
    heightFraction: 0.92,
    markSize: 42,
    markIconSize: 20,
    markBottomGap: 8,
    headingFontSize: 22,
    headingBottomGap: 5,
    bodyLineHeight: 1.35,
    bodyBottomGap: 10,
    codeBoxWidth: 45,
    codeBoxHeight: 48,
    codeRowTopGap: 3,
    codeRowBottomGap: 6,
    messageMinHeight: 14,
    actionsTopGap: 7,
    noteTopGap: 8,
    markCornerRadius: 0,
    codeBoxCornerRadius: 0,
  );

  /// Vertical padding of a dialog's header.
  final double headerVerticalPadding;

  /// Horizontal padding of a dialog's header.
  final double headerHorizontalPadding;

  /// Vertical padding around a dialog's content.
  final double bodyVerticalPadding;

  /// Horizontal padding around a dialog's content.
  final double bodyHorizontalPadding;

  /// Fraction of the window height a dialog may fill.
  final double heightFraction;

  /// Width and height of a pairing state's icon tile.
  final double markSize;

  /// Size of the icon inside a pairing state's icon tile.
  final double markIconSize;

  /// Gap below a pairing state's icon tile.
  final double markBottomGap;

  /// Font size of a pairing state's heading.
  final double headingFontSize;

  /// Gap below a pairing state's heading.
  final double headingBottomGap;

  /// Line height, as a multiple of font size, of a pairing state's body copy.
  final double bodyLineHeight;

  /// Gap below a pairing state's body copy, before its content.
  final double bodyBottomGap;

  /// Width of one pairing-code digit box.
  final double codeBoxWidth;

  /// Height of one pairing-code digit box.
  final double codeBoxHeight;

  /// Gap above the row of pairing-code digit boxes.
  final double codeRowTopGap;

  /// Gap below the row of pairing-code digit boxes.
  final double codeRowBottomGap;

  /// Height reserved for an inline message, so showing one does not shift the layout.
  final double messageMinHeight;

  /// Gap above a pairing state's action buttons.
  final double actionsTopGap;

  /// Gap above a pairing state's footnote.
  final double noteTopGap;

  /// Corner radius of a pairing state's icon tile, which the theme pins.
  final double markCornerRadius;

  /// Corner radius of a pairing-code digit box, which the theme pins.
  final double codeBoxCornerRadius;

  /// Creates a complete measurement set. Every value is required so a set cannot be assembled
  /// with an accidentally-inherited default.
  const DovahDialogMetrics({
    required this.headerVerticalPadding,
    required this.headerHorizontalPadding,
    required this.bodyVerticalPadding,
    required this.bodyHorizontalPadding,
    required this.heightFraction,
    required this.markSize,
    required this.markIconSize,
    required this.markBottomGap,
    required this.headingFontSize,
    required this.headingBottomGap,
    required this.bodyLineHeight,
    required this.bodyBottomGap,
    required this.codeBoxWidth,
    required this.codeBoxHeight,
    required this.codeRowTopGap,
    required this.codeRowBottomGap,
    required this.messageMinHeight,
    required this.actionsTopGap,
    required this.noteTopGap,
    required this.markCornerRadius,
    required this.codeBoxCornerRadius,
  });

  /// The total width of a row of [pairingCodeLength] digit boxes and the gaps between them.
  double get codeRowWidth =>
      pairingCodeLength * codeBoxWidth + (pairingCodeLength - 1) * codeBoxGap;

  /// Resolves the measurements for a window of size [window] from [themeMetrics], the active
  /// theme's (possibly mid-transition) corner radii: [compact] when the window is at most
  /// [compactMaxWindowHeight] tall and [regular] above it, with the theme's radii for that mode.
  factory DovahDialogMetrics.forWindow({
    required DovahDialogThemeMetrics themeMetrics,
    required Size window,
  }) {
    final bool isCompact = window.height <= compactMaxWindowHeight;

    return (isCompact ? compact : regular).withCornerRadii(
      markCornerRadius: isCompact
          ? themeMetrics.compactMarkCornerRadius
          : themeMetrics.regularMarkCornerRadius,
      codeBoxCornerRadius: themeMetrics.codeBoxCornerRadius,
    );
  }

  /// Returns these measurements with the theme-pinned corner radii replaced.
  DovahDialogMetrics withCornerRadii({
    required double markCornerRadius,
    required double codeBoxCornerRadius,
  }) => DovahDialogMetrics(
    headerVerticalPadding: headerVerticalPadding,
    headerHorizontalPadding: headerHorizontalPadding,
    bodyVerticalPadding: bodyVerticalPadding,
    bodyHorizontalPadding: bodyHorizontalPadding,
    heightFraction: heightFraction,
    markSize: markSize,
    markIconSize: markIconSize,
    markBottomGap: markBottomGap,
    headingFontSize: headingFontSize,
    headingBottomGap: headingBottomGap,
    bodyLineHeight: bodyLineHeight,
    bodyBottomGap: bodyBottomGap,
    codeBoxWidth: codeBoxWidth,
    codeBoxHeight: codeBoxHeight,
    codeRowTopGap: codeRowTopGap,
    codeRowBottomGap: codeRowBottomGap,
    messageMinHeight: messageMinHeight,
    actionsTopGap: actionsTopGap,
    noteTopGap: noteTopGap,
    markCornerRadius: markCornerRadius,
    codeBoxCornerRadius: codeBoxCornerRadius,
  );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    headerVerticalPadding,
    headerHorizontalPadding,
    bodyVerticalPadding,
    bodyHorizontalPadding,
    heightFraction,
    markSize,
    markIconSize,
    markBottomGap,
    headingFontSize,
    headingBottomGap,
    bodyLineHeight,
    bodyBottomGap,
    codeBoxWidth,
    codeBoxHeight,
    codeRowTopGap,
    codeRowBottomGap,
    messageMinHeight,
    actionsTopGap,
    noteTopGap,
    markCornerRadius,
    codeBoxCornerRadius,
  ];
}
