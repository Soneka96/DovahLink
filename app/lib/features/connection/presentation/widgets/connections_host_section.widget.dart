import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_card.widget.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkCompatibilityException,
        DovahLinkConnectionException,
        DovahLinkProtocolException;

/// The connections screen's "My Skyrim PCs" section: a labelled gradient rule followed by one
/// [DovahConnectionCard] per Host. It shows the supplied display data and reports which Host a
/// card selects; it does not read state.
class ConnectionsHostSection extends StatelessWidget {
  /// The cards to show, in order.
  final List<HostCardViewData> cards;

  /// The latest Host discovery operation's state.
  final ConnectionDiscoveryStatus discoveryStatus;

  /// The latest typed discovery error, or `null` when it did not fail.
  final Object? discoveryError;

  /// Called with the Host of the card the user taps.
  final void Function(Host host) onSelectHost;

  /// Creates the Host section.
  const ConnectionsHostSection({
    required this.cards,
    this.discoveryStatus = ConnectionDiscoveryStatus.idle,
    this.discoveryError,
    required this.onSelectHost,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final String? discoveryMessage = switch (discoveryStatus) {
      ConnectionDiscoveryStatus.idle ||
      ConnectionDiscoveryStatus.available => null,
      ConnectionDiscoveryStatus.discovering => 'Searching for Skyrim PCs…',
      ConnectionDiscoveryStatus.empty => 'No local Hosts found.',
      ConnectionDiscoveryStatus.failed => switch (discoveryError) {
        DovahLinkConnectionException _ =>
          'Could not reach the local Host. Check that it is running and try again.',
        DovahLinkProtocolException _ =>
          'The local Host returned an invalid response. Try again.',
        DovahLinkCompatibilityException _ =>
          'This local Host version is not compatible with the app.',
        _ => 'Host discovery failed. Try again.',
      },
    };

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
        if (discoveryMessage != null) ...[
          Semantics(
            label: discoveryMessage,
            liveRegion: true,
            excludeSemantics: true,
            child: Text(
              discoveryMessage,
              key: const Key('connection-discovery-status'),
              style: TextStyle(
                color: discoveryStatus == ConnectionDiscoveryStatus.failed
                    ? tokens.danger
                    : tokens.textMuted,
                fontSize: DovahRootMetrics.pageDescriptionFontSize,
                height: DovahThemeTokens.bodyLineHeight,
              ),
            ),
          ),
          const SizedBox(height: DovahRootMetrics.listGap),
        ],
        ListView.separated(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          itemCount: cards.length,
          separatorBuilder: (BuildContext context, int index) =>
              const SizedBox(height: DovahRootMetrics.listGap),
          itemBuilder: (BuildContext context, int index) {
            final HostCardViewData card = cards[index];
            return DovahConnectionCard(
              key: Key('host-card-${card.host.uri}'),
              title: card.title,
              subtitle: card.subtitle,
              detail: card.detail,
              state: card.state,
              onTap: () => onSelectHost(card.host),
            );
          },
        ),
      ],
    );
  }
}
