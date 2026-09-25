import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_connection_accent.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_accent_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_hover_lift.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_icon_tile.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// A DovahLink connection entry styled by [DovahConnectionCardState]. It takes display data and a
/// callback as props without reading connection or host state.
///
/// The card paints the theme's connection decoration (the prototype's `.connection:before`,
/// `.connection:after`, and `.connection.available` rules) over its surface: Frostbound's fracture
/// lines and available edge, Dovah's link line, both bevelled themes' corner outline, and Hearth's
/// darker resting border.
///
/// While hovered, an interactive card takes the theme's raised material and slides by the theme's
/// hover offset, as the prototype's `.connection:hover` does.
class DovahConnectionCard extends StatelessWidget {
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

  /// Creates a themed connection card.
  const DovahConnectionCard({
    required this.title,
    required this.subtitle,
    required this.detail,
    required this.state,
    this.onTap,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    final Color statusColor = switch (state) {
      DovahConnectionCardState.available => tokens.success,
      DovahConnectionCardState.unknown => tokens.textMuted,
      DovahConnectionCardState.offline => tokens.statusOffline,
      DovahConnectionCardState.repair => tokens.warning,
    };

    final bool enabled = onTap != null;
    final DovahConnectionCardMetrics metrics =
        context.dovahConnectionCardMetrics;
    final double focusRadius =
        tokens.cornerStyle == DovahPanelCornerStyle.rounded
        ? metrics.cornerRadius
        : 0;
    final DovahConnectionAccent accent =
        context.dovahMaterials.connectionAccent;
    final bool available = state == DovahConnectionCardState.available;
    final bool uppercase = tokens.uppercaseLabels;
    final double? uppercaseSpacing = uppercase
        ? DovahThemeTokens.uppercaseLetterSpacingEm *
              DovahThemeTokens.compactFontSize
        : null;
    final TextStyle detailStyle = TextStyle(
      color: tokens.textMuted,
      fontSize: DovahThemeTokens.compactFontSize,
      height: DovahThemeTokens.bodyLineHeight,
    );

    return Semantics(
      excludeSemantics: true,
      button: true,
      enabled: enabled,
      label: '$title, $subtitle, $detail, ${state.label}',
      onTap: onTap,
      child: DovahHoverLift(
        enabled: enabled,
        offset: metrics.hoverOffset,
        builder: (BuildContext context, bool hovered) => InkWell(
          onTap: onTap,
          mouseCursor: enabled
              ? SystemMouseCursors.click
              : SystemMouseCursors.basic,
          child: Builder(
            builder: (BuildContext context) {
              final bool focused = Focus.of(context).hasPrimaryFocus;

              return DovahFocusRing(
                focused: focused,
                cornerRadius: focusRadius,
                child: DovahSurface(
                  role: hovered
                      ? DovahMaterialRole.raised
                      : DovahMaterialRole.surface,
                  borderColor: hovered ? null : accent.restingBorder,
                  underlay: accent.drawsNothing
                      ? null
                      : DovahConnectionAccentPainter(
                          accent: accent,
                          available: available,
                          showLinkLine: metrics.showLinkLine,
                          aboveContent: false,
                        ),
                  overlay: accent.drawsNothing || !accent.overContent
                      ? null
                      : DovahConnectionAccentPainter(
                          accent: accent,
                          available: available,
                          showLinkLine: metrics.showLinkLine,
                          aboveContent: true,
                        ),
                  padding: metrics.padding,
                  cornerRadius: metrics.cornerRadius,
                  cornerCutSize: metrics.cornerCutSize,
                  child: ConstrainedBox(
                    constraints: BoxConstraints(
                      minHeight: math.max(
                        0,
                        metrics.minHeight - metrics.padding.vertical,
                      ),
                    ),
                    child: Row(
                      children: [
                        DovahIconTile(
                          size: metrics.iconTileSize,
                          cornerRadius: metrics.iconTileRadius,
                          rotation: metrics.iconTileRotation,
                          child: Icon(
                            Icons.desktop_windows_outlined,
                            color: tokens.iconTileForeground,
                            size: DovahConnectionCardMetrics.iconSize,
                          ),
                        ),
                        const SizedBox(
                          width: DovahConnectionCardMetrics.columnGap,
                        ),
                        Expanded(
                          flex: DovahConnectionCardMetrics.mainColumnFlex,
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Text(
                                uppercase ? title.toUpperCase() : title,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: TextStyle(
                                  color: tokens.textPrimary,
                                  fontSize:
                                      DovahConnectionCardMetrics.titleFontSize,
                                  fontWeight: FontWeight.w700,
                                  height: DovahThemeTokens.bodyLineHeight,
                                  letterSpacing: uppercaseSpacing,
                                ),
                              ),
                              const SizedBox(
                                height:
                                    DovahConnectionCardMetrics.titleBottomGap,
                              ),
                              Text(
                                subtitle,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: detailStyle,
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(
                          width: DovahConnectionCardMetrics.columnGap,
                        ),
                        if (metrics.showDetail) ...[
                          Expanded(
                            flex: DovahConnectionCardMetrics.detailColumnFlex,
                            child: Text(
                              detail,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: detailStyle,
                            ),
                          ),
                          const SizedBox(
                            width: DovahConnectionCardMetrics.columnGap,
                          ),
                        ],
                        ConstrainedBox(
                          constraints: const BoxConstraints(
                            minWidth: DovahConnectionCardMetrics.statusMinWidth,
                          ),
                          child: Row(
                            mainAxisSize: MainAxisSize.min,
                            mainAxisAlignment: MainAxisAlignment.end,
                            children: [
                              Icon(
                                Icons.circle,
                                size:
                                    DovahConnectionCardMetrics.stateMarkerSize,
                                color: statusColor,
                              ),
                              const SizedBox(
                                width:
                                    DovahConnectionCardMetrics.stateMarkerGap,
                              ),
                              Text(
                                uppercase
                                    ? state.label.toUpperCase()
                                    : state.label,
                                style: TextStyle(
                                  color: statusColor,
                                  fontWeight: FontWeight.w700,
                                  fontSize: DovahThemeTokens.compactFontSize,
                                  height: DovahThemeTokens.bodyLineHeight,
                                  letterSpacing: uppercaseSpacing,
                                ),
                              ),
                            ],
                          ),
                        ),
                        if (state != DovahConnectionCardState.offline) ...[
                          const SizedBox(
                            width: DovahConnectionCardMetrics.statusArrowGap,
                          ),
                          Icon(
                            Icons.chevron_right,
                            size: DovahConnectionCardMetrics.arrowSize,
                            color: tokens.accentPrimary,
                          ),
                        ],
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
