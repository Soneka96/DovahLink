import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// Draws a pairing code as one box per digit, the approved prototype's `.otp` row, each on the
/// theme's control material (`.otp input` uses `--material-raised`). The focused box keeps its
/// border, gains a soft halo (`.otp input:focus`), and takes the shared focus ring. Display only:
/// the entered [code] is owned and edited by the text field a caller lays over it, and this row
/// is hidden from semantics because that field is the accessible control.
class PairingCodeBoxes extends StatelessWidget {
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
    final DovahDialogMetrics metrics = context.dovahDialogMetrics;
    final int activeIndex = code.length < pairingCodeLength
        ? code.length
        : pairingCodeLength - 1;

    return ExcludeSemantics(
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (int index = 0; index < pairingCodeLength; index++) ...[
            if (index > 0) const SizedBox(width: DovahDialogMetrics.codeBoxGap),
            DovahFocusRing(
              key: Key('pairing-code-box-$index'),
              focused: isFocused && index == activeIndex,
              cornerRadius: metrics.codeBoxCornerRadius,
              child: DecoratedBox(
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(
                    metrics.codeBoxCornerRadius,
                  ),
                  boxShadow: isFocused && index == activeIndex
                      ? [
                          BoxShadow(
                            color: tokens.soft,
                            spreadRadius:
                                DovahDialogMetrics.codeBoxFocusRingWidth,
                          ),
                        ]
                      : null,
                ),
                child: SizedBox(
                  width: metrics.codeBoxWidth,
                  height: metrics.codeBoxHeight,
                  child: DovahSurface(
                    role: DovahMaterialRole.control,
                    cornerRadius: metrics.codeBoxCornerRadius,
                    child: Center(
                      child: Text(
                        index < code.length ? code[index] : '',
                        style: TextStyle(
                          color: tokens.textPrimary,
                          fontSize: DovahDialogMetrics.codeBoxFontSize,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}
