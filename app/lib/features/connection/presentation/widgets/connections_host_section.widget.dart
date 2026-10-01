import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_card.widget.dart';

/// The connections screen's Known Hosts section: a labelled gradient rule followed by one
/// [DovahConnectionCard] per durable Host relationship. It reports the selected card and does not
/// read state.
class ConnectionsHostSection extends StatelessWidget {
  /// The Known Host cards to show, in order.
  final List<HostCardViewData> cards;

  /// Called with the card the user taps, including its selection source.
  final void Function(HostCardViewData card) onSelectHost;

  /// Called when the user taps an Offline card to open its informational dialog.
  final void Function(HostCardViewData card)? onShowOfflineHost;

  /// Creates the Host section.
  const ConnectionsHostSection({
    required this.cards,
    required this.onSelectHost,
    this.onShowOfflineHost,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Text(
              'MY SKYRIM PCS',
              style: TextStyle(
                color: tokens.textMuted,
                fontSize: DovahRootMetrics.sectionLabelFontSize,
                height: DovahThemeTokens.bodyLineHeight,
                fontWeight: FontWeight.w800,
                letterSpacing:
                    DovahRootMetrics.sectionLabelLetterSpacingEm *
                    DovahRootMetrics.sectionLabelFontSize,
              ),
            ),
            const SizedBox(width: DovahRootMetrics.sectionLabelGap),
            Expanded(
              child: DecoratedBox(
                decoration: BoxDecoration(
                  gradient: LinearGradient(
                    colors: [
                      tokens.ember,
                      tokens.signal,
                      tokens.signal.withValues(alpha: 0),
                    ],
                  ),
                ),
                child: const SizedBox(
                  height: DovahThemeTokens.surfaceBorderWidth,
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: DovahRootMetrics.sectionLabelBottomGap),
        ListView.separated(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          itemCount: cards.length,
          separatorBuilder: (BuildContext context, int index) =>
              const SizedBox(height: DovahRootMetrics.listGap),
          itemBuilder: (BuildContext context, int index) {
            final HostCardViewData card = cards[index];
            final VoidCallback? onTap = switch (card.state) {
              DovahConnectionCardState.available => () => onSelectHost(card),
              DovahConnectionCardState.offline when onShowOfflineHost != null =>
                () => onShowOfflineHost!(card),
              _ => null,
            };
            return DovahConnectionCard(
              key: Key('host-card-${card.host.hostId}'),
              title: card.title,
              subtitle: card.subtitle,
              detail: card.detail,
              state: card.state,
              onTap: onTap,
            );
          },
        ),
      ],
    );
  }
}
