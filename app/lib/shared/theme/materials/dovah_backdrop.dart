import 'dart:ui';

import 'package:equatable/equatable.dart';

/// The treatment a theme puts over the page behind a modal dialog (the prototype's per-theme
/// `.modal-backdrop`): the page is blurred and re-colored, then covered by a translucent [tint].
/// The re-colored page and the tint are separate, as they are in CSS, where `backdrop-filter` never
/// touches the element's own background.
class DovahBackdrop extends Equatable {
  /// The translucent scrim color, alpha included, drawn over the treated page.
  final Color tint;

  /// The blur strength applied to the page, as a Gaussian sigma in logical pixels.
  final double blurSigma;

  /// The CSS `saturate()` amount applied to the page; `1` leaves its colors as they are.
  final double saturation;

  /// The CSS `sepia()` amount applied to the page; `0` leaves its colors as they are.
  final double sepia;

  /// Creates a backdrop from its [tint] and [blurSigma], and optional page [saturation] and
  /// [sepia].
  const DovahBackdrop({
    required this.tint,
    required this.blurSigma,
    this.saturation = 1,
    this.sepia = 0,
  });

  /// The fields that define this backdrop's value equality.
  @override
  List<Object?> get props => [tint, blurSigma, saturation, sepia];
}
