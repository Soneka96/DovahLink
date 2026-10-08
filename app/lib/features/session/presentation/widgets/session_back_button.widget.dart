import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';

/// The prototype-styled Connections action in the Session Shell header.
class SessionBackButton extends StatefulWidget {
  /// The action invoked when the player returns to Connections.
  final VoidCallback onPressed;

  /// Creates the Session Shell's back action.
  const SessionBackButton({required this.onPressed, super.key});

  /// Creates the hover state for the back action.
  @override
  State<SessionBackButton> createState() => _SessionBackButtonState();
}

/// Tracks the prototype's text-color change while the pointer is over the action.
class _SessionBackButtonState extends State<SessionBackButton> {
  /// Whether the pointer is currently over the action.
  bool _hovered = false;

  /// Builds the back action with its accessible target and visible keyboard focus.
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    return MouseRegion(
      cursor: SystemMouseCursors.click,
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      child: Semantics(
        key: const Key('dovah-button-semantics'),
        excludeSemantics: true,
        button: true,
        label: 'Connections',
        onTap: widget.onPressed,
        child: InkWell(
          onTap: widget.onPressed,
          splashFactory: NoSplash.splashFactory,
          overlayColor: const WidgetStatePropertyAll(Colors.transparent),
          child: Builder(
            builder: (BuildContext context) {
              final bool focused = Focus.of(context).hasPrimaryFocus;
              return ConstrainedBox(
                constraints: const BoxConstraints(
                  minWidth: DovahControlMetrics.minimumTapTargetSize,
                  minHeight: DovahControlMetrics.minimumTapTargetSize,
                ),
                child: DovahFocusRing(
                  focused: focused,
                  cornerRadius: 0,
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(
                      0,
                      DovahSessionMetrics.backVerticalPadding,
                      DovahSessionMetrics.backRightPadding,
                      DovahSessionMetrics.backVerticalPadding,
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          Icons.chevron_left,
                          size: DovahSessionMetrics.backIconSize,
                          color: _hovered
                              ? tokens.textPrimary
                              : tokens.textMuted,
                        ),
                        const SizedBox(width: DovahSessionMetrics.backGap),
                        Text(
                          'Connections',
                          style: TextStyle(
                            color: _hovered
                                ? tokens.textPrimary
                                : tokens.textMuted,
                            fontSize: DovahSessionMetrics.backFontSize,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
