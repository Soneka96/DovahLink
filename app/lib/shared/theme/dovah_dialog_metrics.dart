import 'package:flutter/foundation.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The measurements a themed dialog and the content it hosts change with the window height: the
/// approved prototype has a regular layout and a tighter one for short landscape windows, and
/// every measurement that differs between them lives here so no widget branches on the window
/// size itself. Resolve the set for a window with [forWindowHeight].
@immutable
class DovahDialogMetrics extends Equatable {
  /// The tallest window, in logical pixels, that still gets the [compact] measurements.
  static const double compactMaxWindowHeight = 620;

  /// The measurements for windows taller than [compactMaxWindowHeight].
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
  );

  /// The measurements for windows no taller than [compactMaxWindowHeight].
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
  );

  /// Vertical padding of a dialog's header, before theme density scaling.
  final double headerVerticalPadding;

  /// Horizontal padding of a dialog's header, before theme density scaling.
  final double headerHorizontalPadding;

  /// Vertical padding around a dialog's content, before theme density scaling.
  final double bodyVerticalPadding;

  /// Horizontal padding around a dialog's content, before theme density scaling.
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
  });

  /// The total width of a row of [pairingCodeLength] digit boxes and the gaps between them.
  double get codeRowWidth =>
      pairingCodeLength * codeBoxWidth +
      (pairingCodeLength - 1) * DovahThemeTokens.pairingCodeBoxGap;

  /// Returns [compact] when [windowHeight] is at most [compactMaxWindowHeight], and [regular] for
  /// a taller window.
  static DovahDialogMetrics forWindowHeight(double windowHeight) =>
      windowHeight <= compactMaxWindowHeight ? compact : regular;

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
  ];
}
