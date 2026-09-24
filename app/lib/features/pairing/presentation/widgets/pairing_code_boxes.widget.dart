import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Draws a pairing code as one box per digit, the approved prototype's `.otp` row. Display only:
/// the entered [code] is owned and edited by the text field a caller lays over it, and this row
/// is hidden from semantics because that field is the accessible control.
class PairingCodeBoxes extends StatelessWidget {
  /// The total width of a row of [pairingCodeLength] boxes.
  static const double width =
      pairingCodeLength * DovahThemeTokens.pairingCodeBoxWidth +
      (pairingCodeLength - 1) * DovahThemeTokens.pairingCodeBoxGap;

  /// The digits entered so far, at most [pairingCodeLength] of them.
  final String code;

  /// Whether the field this row displays has focus, which halos the box the next digit lands in.
  final bool isFocused;

  /// Creates a row of digit boxes showing [code].
  const PairingCodeBoxes({
    required this.code,
    required this.isFocused,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final int activeIndex = code.length < pairingCodeLength
        ? code.length
        : pairingCodeLength - 1;

    return ExcludeSemantics(
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (int index = 0; index < pairingCodeLength; index++) ...[
            if (index > 0)
              const SizedBox(width: DovahThemeTokens.pairingCodeBoxGap),
            Container(
              key: Key('pairing-code-box-$index'),
              width: DovahThemeTokens.pairingCodeBoxWidth,
              height: DovahThemeTokens.pairingCodeBoxHeight,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: tokens.background,
                borderRadius: BorderRadius.circular(tokens.cornerRadius),
                border: Border.all(
                  color: isFocused && index == activeIndex
                      ? tokens.accentPrimary
                      : tokens.lineStrong,
                ),
                boxShadow: isFocused && index == activeIndex
                    ? [
                        BoxShadow(
                          color: tokens.soft,
                          spreadRadius:
                              DovahThemeTokens.pairingCodeBoxFocusRingWidth,
                        ),
                      ]
                    : null,
              ),
              child: Text(
                index < code.length ? code[index] : '',
                style: TextStyle(
                  color: tokens.textPrimary,
                  fontSize: DovahThemeTokens.pairingCodeBoxFontSize,
                  fontWeight: FontWeight.w800,
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}
