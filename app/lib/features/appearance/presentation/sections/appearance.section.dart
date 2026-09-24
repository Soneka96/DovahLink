import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_card.widget.dart';
import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/viewmodels/appearance_section.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The appearance picker's content: one card per [DovahThemePreset], shown inside a
/// [DovahDialog][DovahThemeContext] by whatever screen offers theme selection.
class AppearanceSection extends StatelessWidget {
  /// Creates the appearance picker section.
  const AppearanceSection({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, AppearanceSectionViewModel>(
      distinct: true,
      converter: (Store<AppState> store) =>
          sl<AppearanceSectionViewModel>(param1: store),
      builder: (BuildContext context, AppearanceSectionViewModel viewModel) {
        final tokens = context.dovahTokens;
        return Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Choose your Skyrim atmosphere',
              style: TextStyle(
                color: tokens.textPrimary,
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: DovahThemeTokens.spacing4),
            Text(
              'The interface stays familiar, but its material, shape, density and motion change.',
              style: TextStyle(
                color: tokens.textMuted,
                fontSize: DovahThemeTokens.compactFontSize,
              ),
            ),
            const SizedBox(height: DovahThemeTokens.spacing16),
            LayoutBuilder(
              builder: (BuildContext context, BoxConstraints constraints) {
                const double columnGap = DovahThemeTokens.spacing6 * 2;
                final int columnCount =
                    (constraints.maxWidth /
                            (DovahThemeTokens.appearancePresetCardMinimumWidth +
                                columnGap))
                        .floor()
                        .clamp(1, DovahThemePreset.values.length)
                        .toInt();
                final double columnWidth = constraints.maxWidth / columnCount;

                return Wrap(
                  runSpacing: columnGap,
                  children: [
                    for (final DovahThemePreset preset
                        in DovahThemePreset.values)
                      SizedBox(
                        width: columnWidth,
                        child: Padding(
                          padding: const EdgeInsets.symmetric(
                            horizontal: DovahThemeTokens.spacing6,
                          ),
                          child: AppearancePresetCard(
                            key: Key('appearance-preset-card-${preset.name}'),
                            preset: preset,
                            selected: preset == viewModel.activePreset,
                            onTap: () => viewModel.onSelectPreset(preset),
                          ),
                        ),
                      ),
                  ],
                );
              },
            ),
          ],
        );
      },
    );
  }
}
