import 'dart:ui';

import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_icon_button.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil.widget.dart';

/// The root screen's header bar: the DovahLink sigil and wordmark on the left and the appearance
/// action on the right, over a translucent blurred surface with a gradient rule along its bottom
/// edge. Takes its one callback as a prop.
class RootHeader extends StatelessWidget {
  /// Creates the root header.
  const RootHeader({required this.onOpenAppearance, super.key});

  /// Called when the appearance action is tapped.
  final VoidCallback onOpenAppearance;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;

    return ClipRect(
      child: BackdropFilter(
        filter: ImageFilter.blur(
          sigmaX: DovahThemeTokens.rootHeaderBlurSigma,
          sigmaY: DovahThemeTokens.rootHeaderBlurSigma,
        ),
        child: Container(
          constraints: BoxConstraints(minHeight: tokens.rootHeaderHeight),
          decoration: BoxDecoration(
            color: tokens.surface.withValues(
              alpha: DovahThemeTokens.rootHeaderBackgroundOpacity,
            ),
            border: Border(bottom: BorderSide(color: tokens.lineSubtle)),
          ),
          child: Stack(
            alignment: Alignment.centerLeft,
            children: [
              Row(
                children: [
                  const DovahSigil(size: DovahThemeTokens.rootBrandMarkSize),
                  const SizedBox(width: DovahThemeTokens.rootBrandGap),
                  Expanded(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'DOVAHLINK',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            color: tokens.textPrimary,
                            fontSize: DovahThemeTokens.brandNameFontSize,
                            fontWeight: FontWeight.w800,
                            letterSpacing:
                                DovahThemeTokens.brandNameLetterSpacingEm *
                                DovahThemeTokens.brandNameFontSize,
                          ),
                        ),
                        const SizedBox(
                          height: DovahThemeTokens.brandTaglineTopGap,
                        ),
                        Text(
                          'SKYRIM COMPANION',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            color: tokens.textFaint,
                            fontSize: DovahThemeTokens.brandTaglineFontSize,
                            fontWeight: FontWeight.w700,
                            letterSpacing:
                                DovahThemeTokens.brandTaglineLetterSpacingEm *
                                DovahThemeTokens.brandTaglineFontSize,
                          ),
                        ),
                      ],
                    ),
                  ),
                  DovahIconButton(
                    icon: Icons.settings_outlined,
                    label: 'Appearance settings',
                    onPressed: onOpenAppearance,
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
                        height: DovahThemeTokens.rootHeaderRuleHeight,
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
