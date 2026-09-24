import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The centered column every pairing state shares, the approved prototype's `.pairing`: a mark, a
/// heading, body copy that may emphasize a name, and the state's own content below.
class PairingStateLayout extends StatelessWidget {
  /// The mark shown above the heading, such as a [PairingMark].
  final Widget mark;

  /// The state's heading, announced when the state appears.
  final String heading;

  /// The body copy before [highlight].
  final String body;

  /// A name emphasized inside the body copy, such as the Host's, or `null` for none.
  final String? highlight;

  /// The body copy after [highlight].
  final String bodyEnd;

  /// The state's own content, below the body copy.
  final List<Widget> children;

  /// Creates a pairing state layout.
  const PairingStateLayout({
    required this.mark,
    required this.heading,
    required this.body,
    this.highlight,
    this.bodyEnd = '',
    this.children = const <Widget>[],
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;

    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(
          maxWidth: DovahThemeTokens.pairingContentMaxWidth,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            mark,
            const SizedBox(height: DovahThemeTokens.pairingMarkBottomGap),
            Semantics(
              header: true,
              liveRegion: true,
              child: Text(
                heading,
                key: const Key('pairing-heading'),
                textAlign: TextAlign.center,
                style: TextStyle(
                  fontFamily: tokens.displayFontFamily,
                  fontSize: DovahThemeTokens.pairingHeadingFontSize,
                  fontWeight: FontWeight.w500,
                  color: tokens.textPrimary,
                ),
              ),
            ),
            const SizedBox(height: DovahThemeTokens.pairingHeadingBottomGap),
            ConstrainedBox(
              constraints: const BoxConstraints(
                maxWidth: DovahThemeTokens.pairingBodyMaxWidth,
              ),
              child: Text.rich(
                TextSpan(
                  text: body,
                  children: [
                    if (highlight != null)
                      TextSpan(
                        text: highlight,
                        style: TextStyle(
                          color: tokens.textPrimary,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    if (bodyEnd.isNotEmpty) TextSpan(text: bodyEnd),
                  ],
                ),
                key: const Key('pairing-body'),
                textAlign: TextAlign.center,
                style: TextStyle(
                  color: tokens.textMuted,
                  fontSize: DovahThemeTokens.pairingBodyFontSize,
                  height: DovahThemeTokens.pairingBodyLineHeight,
                ),
              ),
            ),
            const SizedBox(height: DovahThemeTokens.pairingBodyBottomGap),
            ...children,
          ],
        ),
      ),
    );
  }
}
