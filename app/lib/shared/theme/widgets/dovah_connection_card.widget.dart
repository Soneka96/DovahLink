import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// A DovahLink connection entry, styled per its [DovahConnectionCardState]. Presentation-only:
/// it takes display data and a callback as props and does not read connection/host domain state
/// itself -- see the class's own Stage 10/11 note on [DovahConnectionCardState] for why all three
/// visual states exist here even though only `available` can be produced by the live app today.
class DovahConnectionCard extends StatelessWidget {
  /// Creates a themed connection card.
  const DovahConnectionCard({
    required this.title,
    required this.subtitle,
    required this.detail,
    required this.state,
    this.onTap,
    super.key,
  });

  /// The connection's display name (for example a Host's name).
  final String title;

  /// The secondary line under the title (for example the Skyrim edition).
  final String subtitle;

  /// The trailing detail text (for example "Last connected yesterday").
  final String detail;

  /// The card's visual state.
  final DovahConnectionCardState state;

  /// Called when the card is tapped, or `null` to render it non-interactive.
  final VoidCallback? onTap;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    final Color statusColor = switch (state) {
      DovahConnectionCardState.available => tokens.success,
      DovahConnectionCardState.offline => tokens.textFaint,
      DovahConnectionCardState.repair => tokens.warning,
    };

    return MouseRegion(
      cursor: onTap != null
          ? SystemMouseCursors.click
          : SystemMouseCursors.basic,
      child: GestureDetector(
        onTap: onTap,
        child: DovahSurface(
          padding: EdgeInsets.symmetric(
            vertical: 16 * tokens.densityScale,
            horizontal: 18 * tokens.densityScale,
          ),
          child: Row(
            children: [
              Container(
                width: 43 * tokens.densityScale,
                height: 43 * tokens.densityScale,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: tokens.surfaceRaised,
                  border: Border.all(color: tokens.lineStrong),
                  borderRadius: BorderRadius.circular(9 * tokens.densityScale),
                ),
                child: Icon(
                  Icons.desktop_windows_outlined,
                  color: tokens.accentPrimary,
                  size: 21 * tokens.densityScale,
                ),
              ),
              SizedBox(width: 16 * tokens.densityScale),
              Expanded(
                flex: 3,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        color: tokens.textPrimary,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    Text(
                      subtitle,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(color: tokens.textMuted, fontSize: 13),
                    ),
                  ],
                ),
              ),
              Expanded(
                flex: 2,
                child: Text(
                  detail,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(color: tokens.textMuted, fontSize: 13),
                ),
              ),
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(Icons.circle, size: 8, color: statusColor),
                  const SizedBox(width: 8),
                  Text(
                    state.label,
                    style: TextStyle(
                      color: statusColor,
                      fontWeight: FontWeight.w700,
                      fontSize: 13,
                    ),
                  ),
                ],
              ),
              if (state != DovahConnectionCardState.offline)
                Icon(Icons.chevron_right, color: tokens.accentPrimary),
            ],
          ),
        ),
      ),
    );
  }
}
