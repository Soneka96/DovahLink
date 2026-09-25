import 'dart:ui';

import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_brand_mark.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_icon_button.widget.dart';

/// The root screen's header bar: the DovahLink brand mark and wordmark on the left and the appearance
/// action on the right, over a translucent blurred surface with a gradient rule along its bottom
/// edge. Takes its one callback as a prop.
class RootHeader extends StatelessWidget {
  /// Called when the appearance action is tapped.
  final VoidCallback onOpenAppearance;

  /// Creates the root header.
  const RootHeader({required this.onOpenAppearance, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final DovahRootMetrics metrics = context.dovahRootMetrics;

    return ClipRect(
      child: BackdropFilter(
        filter: ImageFilter.blur(
          sigmaX: DovahRootMetrics.headerBlurSigma,
          sigmaY: DovahRootMetrics.headerBlurSigma,
        ),
        child: Container(
          constraints: BoxConstraints(minHeight: metrics.headerHeight),
          decoration: BoxDecoration(
            color: tokens.surface.withValues(
              alpha: DovahRootMetrics.headerBackgroundOpacity,
            ),
            border: Border(bottom: BorderSide(color: tokens.lineSubtle)),
          ),
          child: Stack(
            alignment: Alignment.centerLeft,
            children: [
              Row(
                children: [
                  const DovahBrandMark(size: DovahRootMetrics.brandMarkSize),
                  const SizedBox(width: DovahRootMetrics.brandGap),
                  Expanded(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text.rich(
                          TextSpan(
                            text: 'DOVAH',
                            children: [
                              TextSpan(
                                text: 'LINK',
                                style: TextStyle(color: tokens.brandAccent),
                              ),
                            ],
                          ),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            color: tokens.textPrimary,
                            fontSize: DovahRootMetrics.brandNameFontSize,
                            height: DovahThemeTokens.bodyLineHeight,
                            fontWeight: FontWeight.w800,
                            letterSpacing:
                                DovahRootMetrics.brandNameLetterSpacingEm *
                                DovahRootMetrics.brandNameFontSize,
                          ),
                        ),
                        const SizedBox(
                          height: DovahRootMetrics.brandTaglineTopGap,
                        ),
                        Text(
                          'SKYRIM COMPANION',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            color: tokens.brandTagline,
                            fontSize: DovahRootMetrics.brandTaglineFontSize,
                            height: DovahThemeTokens.bodyLineHeight,
                            fontWeight: FontWeight.w700,
                            letterSpacing:
                                metrics.brandTaglineLetterSpacingEm *
                                DovahRootMetrics.brandTaglineFontSize,
                          ),
                        ),
                      ],
                    ),
                  ),
                  // The button's tap target is wider than its 40px surface; shift it so the
                  // visible surface, not the invisible margin, meets the content edge.
                  Transform.translate(
                    offset: const Offset(
                      (DovahControlMetrics.minimumTapTargetSize -
                              DovahControlMetrics.iconButtonSize) /
                          2,
                      0,
                    ),
                    child: DovahIconButton(
                      icon: Icons.settings_outlined,
                      label: 'Appearance settings',
                      onPressed: onOpenAppearance,
                    ),
                  ),
                ],
              ),
              Positioned.fill(
                child: Align(
                  alignment: Alignment.bottomLeft,
                  child: FractionallySizedBox(
                    key: const Key('root-header-rule'),
                    widthFactor: tokens.rootHeaderRuleFraction,
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
                        height: DovahRootMetrics.headerRuleHeight,
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
