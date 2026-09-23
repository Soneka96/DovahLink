import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// A DovahLink connection entry styled by [DovahConnectionCardState]. It takes display data and a
/// callback as props without reading connection or host state.
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

    final bool enabled = onTap != null;

    return Semantics(
      button: true,
      enabled: enabled,
      label: '$title, $subtitle, $detail, ${state.label}',
      child: InkWell(
        onTap: onTap,
        mouseCursor: enabled
            ? SystemMouseCursors.click
            : SystemMouseCursors.basic,
        child: Builder(
          builder: (BuildContext context) {
            final bool focused = Focus.of(context).hasPrimaryFocus;

            return Container(
              key: focused
                  ? const Key('dovah-connection-card-focus-outline')
                  : null,
              foregroundDecoration: focused
                  ? BoxDecoration(
                      border: Border.all(
                        color: tokens.signal,
                        width: DovahThemeTokens.focusOutlineWidth,
                      ),
                      borderRadius: BorderRadius.circular(tokens.cornerRadius),
                      boxShadow: <BoxShadow>[
                        BoxShadow(
                          color: tokens.focusRingTint,
                          blurRadius: DovahThemeTokens.focusGlowBlurRadius,
                        ),
                      ],
                    )
                  : null,
              child: DovahSurface(
                padding: EdgeInsets.symmetric(
                  vertical: DovahThemeTokens.spacing16 * tokens.densityScale,
                  horizontal: DovahThemeTokens.spacing18 * tokens.densityScale,
                ),
                child: Row(
                  children: [
                    Container(
                      width:
                          DovahThemeTokens.connectionIconTileSize *
                          tokens.densityScale,
                      height:
                          DovahThemeTokens.connectionIconTileSize *
                          tokens.densityScale,
                      alignment: Alignment.center,
                      decoration: BoxDecoration(
                        color: tokens.surfaceRaised,
                        border: Border.all(color: tokens.lineStrong),
                        borderRadius: BorderRadius.circular(
                          DovahThemeTokens.connectionIconTileRadius *
                              tokens.densityScale,
                        ),
                      ),
                      child: Icon(
                        Icons.desktop_windows_outlined,
                        color: tokens.accentPrimary,
                        size:
                            DovahThemeTokens.connectionIconSize *
                            tokens.densityScale,
                      ),
                    ),
                    SizedBox(
                      width: DovahThemeTokens.spacing16 * tokens.densityScale,
                    ),
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
                            style: TextStyle(
                              color: tokens.textMuted,
                              fontSize: DovahThemeTokens.compactFontSize,
                            ),
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
                        style: TextStyle(
                          color: tokens.textMuted,
                          fontSize: DovahThemeTokens.compactFontSize,
                        ),
                      ),
                    ),
                    Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          Icons.circle,
                          size: DovahThemeTokens.connectionStateMarkerSize,
                          color: statusColor,
                        ),
                        const SizedBox(width: DovahThemeTokens.spacing8),
                        Text(
                          state.label,
                          style: TextStyle(
                            color: statusColor,
                            fontWeight: FontWeight.w700,
                            fontSize: DovahThemeTokens.compactFontSize,
                          ),
                        ),
                      ],
                    ),
                    if (state != DovahConnectionCardState.offline)
                      Icon(Icons.chevron_right, color: tokens.accentPrimary),
                  ],
                ),
              ),
            );
          },
        ),
      ),
    );
  }
}
