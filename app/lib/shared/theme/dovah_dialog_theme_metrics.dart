import 'dart:ui' show lerpDouble;

import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// The dialog-content corner radii each theme pins differently, as a [ThemeExtension] so Flutter's
/// [ThemeData] transition interpolates them alongside the colors instead of snapping them. Every
/// value is the theme's prototype-exact value; `DovahDialogMetrics.forWindow` selects the window
/// mode. Values that vary only by window, never by theme, are not here.
///
/// The pairing mark and the code boxes are plain boxes, never clipped, so their radii are their own
/// and not the theme's `--radius`.
@immutable
class DovahDialogThemeMetrics extends ThemeExtension<DovahDialogThemeMetrics>
    with Equatable {
  /// Frostbound's values: square marks and code boxes (the prototype's `.large-mark` and
  /// `.otp input` `border-radius:0`).
  static const DovahDialogThemeMetrics frostbound = DovahDialogThemeMetrics(
    regularMarkCornerRadius: 0,
    compactMarkCornerRadius: 0,
    codeBoxCornerRadius: 0,
    discoveryCandidateCornerStyle: DovahPanelCornerStyle.singleBevel,
    discoveryCandidateCornerRadius: 0,
    discoveryCandidateCornerCutSize: 9,
  );

  /// The Dovah preset's values: square marks and code boxes.
  static const DovahDialogThemeMetrics dovah = DovahDialogThemeMetrics(
    regularMarkCornerRadius: 0,
    compactMarkCornerRadius: 0,
    codeBoxCornerRadius: 0,
    discoveryCandidateCornerStyle: DovahPanelCornerStyle.rounded,
    discoveryCandidateCornerRadius: 3,
    discoveryCandidateCornerCutSize: 0,
  );

  /// Hearth's values: a circular mark (the prototype's `.large-mark` `border-radius:50%`, half of
  /// its 54px and compact 42px sides) and 9px code boxes (`.otp input` `border-radius:9px`).
  static const DovahDialogThemeMetrics hearth = DovahDialogThemeMetrics(
    regularMarkCornerRadius: 27,
    compactMarkCornerRadius: 21,
    codeBoxCornerRadius: 9,
    discoveryCandidateCornerStyle: DovahPanelCornerStyle.rounded,
    discoveryCandidateCornerRadius: 14,
    discoveryCandidateCornerCutSize: 0,
  );

  /// Corner radius of a pairing state's icon tile in a regular-height window.
  final double regularMarkCornerRadius;

  /// Corner radius of a pairing state's icon tile in a compact-height window.
  final double compactMarkCornerRadius;

  /// Corner radius of a pairing-code digit box in every window mode.
  final double codeBoxCornerRadius;

  /// Corner treatment of a Discover candidate card, as each theme styles `.nearby-card`.
  final DovahPanelCornerStyle discoveryCandidateCornerStyle;

  /// Corner radius of a rounded Discover candidate card.
  final double discoveryCandidateCornerRadius;

  /// Bevel size of a clipped Discover candidate card.
  final double discoveryCandidateCornerCutSize;

  /// Creates a complete set. Every value is required so a set cannot be assembled with an
  /// accidentally-inherited default.
  const DovahDialogThemeMetrics({
    required this.regularMarkCornerRadius,
    required this.compactMarkCornerRadius,
    required this.codeBoxCornerRadius,
    required this.discoveryCandidateCornerStyle,
    required this.discoveryCandidateCornerRadius,
    required this.discoveryCandidateCornerCutSize,
  });

  /// Returns a copy with the given values replaced.
  @override
  DovahDialogThemeMetrics copyWith({
    double? regularMarkCornerRadius,
    double? compactMarkCornerRadius,
    double? codeBoxCornerRadius,
    DovahPanelCornerStyle? discoveryCandidateCornerStyle,
    double? discoveryCandidateCornerRadius,
    double? discoveryCandidateCornerCutSize,
  }) => DovahDialogThemeMetrics(
    regularMarkCornerRadius:
        regularMarkCornerRadius ?? this.regularMarkCornerRadius,
    compactMarkCornerRadius:
        compactMarkCornerRadius ?? this.compactMarkCornerRadius,
    codeBoxCornerRadius: codeBoxCornerRadius ?? this.codeBoxCornerRadius,
    discoveryCandidateCornerStyle:
        discoveryCandidateCornerStyle ?? this.discoveryCandidateCornerStyle,
    discoveryCandidateCornerRadius:
        discoveryCandidateCornerRadius ?? this.discoveryCandidateCornerRadius,
    discoveryCandidateCornerCutSize:
        discoveryCandidateCornerCutSize ?? this.discoveryCandidateCornerCutSize,
  );

  /// Interpolates every value; each is a continuous measurement with no discrete counterpart.
  @override
  DovahDialogThemeMetrics lerp(
    ThemeExtension<DovahDialogThemeMetrics>? other,
    double t,
  ) {
    if (other is! DovahDialogThemeMetrics) {
      return this;
    }
    return DovahDialogThemeMetrics(
      regularMarkCornerRadius: lerpDouble(
        regularMarkCornerRadius,
        other.regularMarkCornerRadius,
        t,
      )!,
      compactMarkCornerRadius: lerpDouble(
        compactMarkCornerRadius,
        other.compactMarkCornerRadius,
        t,
      )!,
      codeBoxCornerRadius: lerpDouble(
        codeBoxCornerRadius,
        other.codeBoxCornerRadius,
        t,
      )!,
      discoveryCandidateCornerStyle: t < 0.5
          ? discoveryCandidateCornerStyle
          : other.discoveryCandidateCornerStyle,
      discoveryCandidateCornerRadius: lerpDouble(
        discoveryCandidateCornerRadius,
        other.discoveryCandidateCornerRadius,
        t,
      )!,
      discoveryCandidateCornerCutSize: lerpDouble(
        discoveryCandidateCornerCutSize,
        other.discoveryCandidateCornerCutSize,
        t,
      )!,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    regularMarkCornerRadius,
    compactMarkCornerRadius,
    codeBoxCornerRadius,
    discoveryCandidateCornerStyle,
    discoveryCandidateCornerRadius,
    discoveryCandidateCornerCutSize,
  ];
}
