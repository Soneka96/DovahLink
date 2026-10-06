import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// A theme-aware tab in the Session Shell's prototype navigation.
class SessionNavigationTab extends StatefulWidget {
  /// The visible page name.
  final String label;

  /// Whether this destination is currently selected.
  final bool selected;

  /// The current theme's visual tokens.
  final DovahThemeTokens tokens;

  /// The current window's session measurements.
  final DovahSessionMetrics metrics;

  /// Selects this destination.
  final VoidCallback onTap;

  /// Creates a tab for the Session Shell navigation.
  const SessionNavigationTab({
    required this.label,
    required this.selected,
    required this.tokens,
    required this.metrics,
    required this.onTap,
    super.key,
  });

  @override
  State<SessionNavigationTab> createState() => _SessionNavigationTabState();
}

class _SessionNavigationTabState extends State<SessionNavigationTab> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = widget.tokens;
    final DovahSessionMetrics metrics = widget.metrics;
    return Semantics(
      button: true,
      selected: widget.selected,
      child: MouseRegion(
        cursor: SystemMouseCursors.click,
        child: InkWell(
          onTap: widget.onTap,
          onHover: (bool hovering) {
            if (_hovered != hovering) {
              setState(() => _hovered = hovering);
            }
          },
          hoverColor: tokens.textPrimary.withValues(alpha: 0.018),
          focusColor: tokens.textPrimary.withValues(alpha: 0.035),
          child: SizedBox(
            key: Key('session-shell-${widget.label}-tab'),
            height: metrics.navTapTargetHeight,
            child: Align(
              alignment: Alignment.topCenter,
              child: SizedBox(
                height: metrics.navHeight,
                child: Stack(
                  alignment: Alignment.center,
                  children: [
                    Padding(
                      padding: EdgeInsets.symmetric(
                        horizontal: metrics.tabHorizontalPadding,
                      ),
                      child: Text(
                        tokens.uppercaseLabels
                            ? widget.label.toUpperCase()
                            : widget.label,
                        style: TextStyle(
                          color: widget.selected || _hovered
                              ? tokens.textPrimary
                              : tokens.textMuted,
                          fontSize: DovahSessionMetrics.tabFontSize,
                          fontWeight: FontWeight.w700,
                          letterSpacing: tokens.uppercaseLabels
                              ? DovahSessionMetrics.tabUppercaseLetterSpacingEm
                              : null,
                        ),
                      ),
                    ),
                    if (widget.selected)
                      Positioned(
                        left: DovahSessionMetrics.activeRuleInset,
                        right: DovahSessionMetrics.activeRuleInset,
                        bottom: 0,
                        child: Container(
                          height: DovahSessionMetrics.activeRuleHeight,
                          decoration: BoxDecoration(
                            gradient: LinearGradient(
                              colors: [tokens.ember, tokens.signal],
                            ),
                            boxShadow: [
                              BoxShadow(
                                color: tokens.signal.withValues(alpha: 0.4),
                                blurRadius: DovahSessionMetrics
                                    .activeRuleGlowBlurRadius,
                              ),
                            ],
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
