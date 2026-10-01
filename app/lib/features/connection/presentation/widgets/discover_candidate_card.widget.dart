import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_icon_tile.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// A selectable, ephemeral Host candidate in the Discover Skyrim dialog.
class DiscoverCandidateCard extends StatefulWidget {
  /// The candidate's prototype-visible title.
  final String title;

  /// The candidate's prototype-visible subtitle.
  final String subtitle;

  /// Whether a real authentication check is active for this candidate.
  final bool isChecking;

  /// Called when the candidate is activated, or `null` while checking.
  final VoidCallback? onTap;

  /// Creates a discovery candidate card.
  const DiscoverCandidateCard({
    required this.title,
    required this.subtitle,
    this.isChecking = false,
    required this.onTap,
    super.key,
  });

  /// Creates the state used for the prototype's candidate hover treatment.
  @override
  State<DiscoverCandidateCard> createState() => _DiscoverCandidateCardState();
}

/// Tracks hover so the candidate can use the theme's raised surface.
class _DiscoverCandidateCardState extends State<DiscoverCandidateCard> {
  /// Whether the pointer is over the card.
  bool _hovered = false;

  /// See [State.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    final DovahConnectionCardMetrics cardMetrics =
        context.dovahConnectionCardMetrics;
    const double cornerRadius =
        DovahDialogMetrics.discoveryCandidateCardCornerRadius;
    final String visibleSubtitle = widget.isChecking
        ? 'Checking trusted connection…'
        : widget.subtitle;
    final bool enabled = widget.onTap != null && !widget.isChecking;
    final bool hovered = enabled && _hovered;

    return MouseRegion(
      onEnter: enabled ? (_) => setState(() => _hovered = true) : null,
      onExit: enabled ? (_) => setState(() => _hovered = false) : null,
      child: Semantics(
        excludeSemantics: true,
        button: true,
        enabled: enabled,
        label: '${widget.title}, $visibleSubtitle',
        onTap: enabled ? widget.onTap : null,
        child: InkWell(
          onTap: enabled ? widget.onTap : null,
          splashFactory: NoSplash.splashFactory,
          overlayColor: const WidgetStatePropertyAll(Colors.transparent),
          mouseCursor: widget.isChecking
              ? SystemMouseCursors.wait
              : enabled
              ? SystemMouseCursors.click
              : SystemMouseCursors.basic,
          child: Builder(
            builder: (BuildContext context) {
              final bool focused = Focus.of(context).hasPrimaryFocus;

              return DovahFocusRing(
                focused: focused,
                cornerRadius: cornerRadius,
                child: Opacity(
                  opacity: widget.isChecking
                      ? DovahDialogMetrics.discoveryCandidateCheckingOpacity
                      : 1,
                  child: DovahSurface(
                    role: hovered
                        ? DovahMaterialRole.raised
                        : DovahMaterialRole.surface,
                    borderColor: hovered ? tokens.accentPrimary : null,
                    cornerStyle: DovahPanelCornerStyle.rounded,
                    cornerRadius: cornerRadius,
                    padding: const EdgeInsets.all(
                      DovahDialogMetrics.discoveryCandidateCardPadding,
                    ),
                    child: Row(
                      children: [
                        DovahIconTile(
                          size: cardMetrics.iconTileSize,
                          cornerRadius: cardMetrics.iconTileRadius,
                          rotation: cardMetrics.iconTileRotation,
                          child: Icon(
                            Icons.desktop_windows_outlined,
                            color: tokens.iconTileForeground,
                            size: DovahConnectionCardMetrics.iconSize,
                          ),
                        ),
                        const SizedBox(
                          width:
                              DovahDialogMetrics.discoveryCandidateContentGap,
                        ),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Text(
                                widget.title,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: TextStyle(
                                  color: tokens.textPrimary,
                                  fontSize: DovahDialogMetrics
                                      .discoveryCandidateTitleFontSize,
                                  fontWeight: FontWeight.w700,
                                  height: DovahThemeTokens.bodyLineHeight,
                                ),
                              ),
                              const SizedBox(height: 4),
                              Text(
                                visibleSubtitle,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: TextStyle(
                                  color: tokens.textMuted,
                                  fontSize: DovahDialogMetrics
                                      .discoveryCandidateSubtitleFontSize,
                                  height: DovahThemeTokens.bodyLineHeight,
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(
                          width:
                              DovahDialogMetrics.discoveryCandidateContentGap,
                        ),
                        if (widget.isChecking)
                          SizedBox.square(
                            dimension: DovahDialogMetrics.progressIndicatorSize,
                            child: CircularProgressIndicator(
                              key: const Key('candidate-checking-spinner'),
                              strokeWidth: DovahDialogMetrics
                                  .progressIndicatorStrokeWidth,
                              color: tokens.accentPrimary,
                              backgroundColor: tokens.lineSubtle,
                            ),
                          )
                        else
                          Text(
                            '›',
                            style: TextStyle(
                              color: tokens.accentPrimary,
                              fontSize: DovahDialogMetrics
                                  .discoveryCandidateArrowFontSize,
                              height: 1,
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
