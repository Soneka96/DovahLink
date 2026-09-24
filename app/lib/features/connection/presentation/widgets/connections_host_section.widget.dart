import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/host_card.viewmodel.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_card.widget.dart';

/// The connections screen's "My Skyrim PCs" section: a labelled gradient rule followed by one
/// [DovahConnectionCard] per Host. It shows the supplied display data and reports which Host a
/// card selects; it does not read state.
class ConnectionsHostSection extends StatelessWidget {
  /// Creates the Host section.
  const ConnectionsHostSection({
    required this.cards,
    required this.onSelectHost,
    super.key,
  });

  /// The cards to show, in order.
  final List<HostCardViewModel> cards;

  /// Called with the Host of the card the user taps.
  final void Function(HostEntity host) onSelectHost;

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
                fontSize: DovahThemeTokens.sectionLabelFontSize,
                fontWeight: FontWeight.w800,
                letterSpacing:
                    DovahThemeTokens.sectionLabelLetterSpacingEm *
                    DovahThemeTokens.sectionLabelFontSize,
              ),
            ),
            const SizedBox(width: DovahThemeTokens.sectionLabelGap),
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
        const SizedBox(height: DovahThemeTokens.sectionLabelBottomGap),
        ListView.separated(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          itemCount: cards.length,
          separatorBuilder: (BuildContext context, int index) =>
              const SizedBox(height: DovahThemeTokens.rootListGap),
          itemBuilder: (BuildContext context, int index) {
            final HostCardViewModel card = cards[index];
            return DovahConnectionCard(
              key: Key('host-card-${card.host.displayName}'),
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
