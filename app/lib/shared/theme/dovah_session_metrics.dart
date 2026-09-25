import 'package:flutter/foundation.dart';
import 'package:flutter/painting.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_theme_metrics.dart';

/// The measurements of the session shell's chrome: the session header bar and the game navigation
/// under it (the approved prototype's `.session-header`, `.session-top`, and `.game-nav`). They
/// change at the narrow and compact window breakpoints and Frostbound pins the navigation height.
/// The theme-varying navigation height lives in [DovahSessionThemeMetrics], a theme extension that
/// Flutter interpolates during a theme change, and [forWindow] resolves it for the window; the
/// shell never branches on the window size or the preset itself. The header's blurred translucent
/// fill is the root header's, [DovahRootMetrics.headerBackgroundOpacity] and
/// [DovahRootMetrics.headerBlurSigma].
@immutable
class DovahSessionMetrics extends Equatable {
  /// Maximum width of the header bar and navigation (the prototype's `min(1240px,...)`).
  static const double barMaxWidth = 1240;

  /// Gap between the header bar's items (the prototype's `.session-top` `gap:18px`).
  static const double barGap = 18;

  /// Gap between the back arrow and its label (the prototype's `.back` `gap:8px`).
  static const double backGap = 8;

  /// Size of the back arrow (the prototype's `.back svg`).
  static const double backIconSize = 18;

  /// Width of the divider after the back button (the prototype's `.divider`).
  static const double dividerWidth = 1;

  /// Height of the divider after the back button (the prototype's `.divider`).
  static const double dividerHeight = 28;

  /// Gap between the session glyph and the session name (the prototype's `.session-identity`
  /// `gap:11px`).
  static const double identityGap = 11;

  /// Width and height of the session glyph (the prototype's `.session-glyph` sigil).
  static const double glyphSize = 30;

  /// Font size of the session name (the prototype's `.session-name`).
  static const double nameFontSize = 14;

  /// Font size of the session detail line (the prototype's `.session-meta` as themed).
  static const double metaFontSize = 12;

  /// Gap between the session name and its detail line (the prototype's `.session-meta`
  /// `margin-top:2px`).
  static const double metaTopGap = 2;

  /// Gap between the connection dot and its label (the prototype's `.session-status`
  /// `gap:8px`).
  static const double statusGap = 8;

  /// Font size of the connection label (the prototype's `.session-status`).
  static const double statusFontSize = 12;

  /// Gap before the action buttons (the prototype's `.session-actions` `margin-left:8px`).
  static const double actionsLeadingGap = 8;

  /// Gap between the action buttons (the prototype's `.session-actions` `gap:7px`).
  static const double actionsGap = 7;

  /// Width and height of a session action button (the prototype's `.session-actions .icon-btn`).
  static const double actionButtonSize = 36;

  /// Gap between navigation tabs (the prototype's `.game-nav` `gap:6px`).
  static const double tabGap = 6;

  /// Font size of a navigation tab (the prototype's `.game-tab`).
  static const double tabFontSize = 14;

  /// Size of an icon inside a navigation tab (the prototype's `.game-tab svg`).
  static const double tabIconSize = 17;

  /// Gap between a navigation tab's icon and its label (the prototype's `.game-tab` `gap:8px`).
  static const double tabIconGap = 8;

  /// Letter spacing, in ems, of a navigation tab a theme renders in uppercase (the prototype's
  /// Frostbound `.game-tab` `.08em`).
  static const double tabUppercaseLetterSpacingEm = 0.08;

  /// Height of the active tab's underline (the prototype's `.game-tab.active:after`).
  static const double activeRuleHeight = 2;

  /// Inset of the active tab's underline from the tab's left and right edges (the prototype's
  /// `.game-tab.active:after` `left:19px;right:19px`).
  static const double activeRuleInset = 19;

  /// Blur radius of the active tab underline's glow (the prototype's `box-shadow:0 0 14px`).
  static const double activeRuleGlowBlurRadius = 14;

  /// The rule above the navigation (the prototype's `.game-nav-wrap`
  /// `rgba(41,54,64,.65)`), which the prototype does not theme.
  static const Color navRuleColor = Color(0xA6293640);

  /// Height of the header bar.
  final double barHeight;

  /// Height of the navigation.
  final double navHeight;

  /// Margin on each side of the header bar and navigation.
  final double barSideMargin;

  /// Horizontal padding of a navigation tab.
  final double tabHorizontalPadding;

  /// Whether the first action button (notifications) is shown; the prototype hides it at narrow
  /// widths.
  final bool showFirstAction;

  /// Creates a complete measurement set. Every value is required so a set cannot be assembled
  /// with an accidentally-inherited default.
  const DovahSessionMetrics({
    required this.barHeight,
    required this.navHeight,
    required this.barSideMargin,
    required this.tabHorizontalPadding,
    required this.showFirstAction,
  });

  /// Resolves the measurements for a window of size [window] from [themeMetrics], the active
  /// theme's (possibly mid-transition) navigation heights. The compact height selects the other
  /// navigation height; the remaining measurements vary by window alone. Taken from the
  /// prototype's `index.html` media queries.
  factory DovahSessionMetrics.forWindow({
    required DovahSessionThemeMetrics themeMetrics,
    required Size window,
  }) {
    final bool narrow = window.width <= DovahRootMetrics.narrowMaxWindowWidth;
    final bool compact =
        window.height <= DovahRootMetrics.compactMaxWindowHeight;

    return DovahSessionMetrics(
      barHeight: compact ? 54 : 65,
      navHeight: compact
          ? themeMetrics.compactNavHeight
          : themeMetrics.regularNavHeight,
      barSideMargin: narrow ? 14 : 24,
      tabHorizontalPadding: narrow ? 16 : 22,
      showFirstAction: !narrow,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    barHeight,
    navHeight,
    barSideMargin,
    tabHorizontalPadding,
    showFirstAction,
  ];
}
