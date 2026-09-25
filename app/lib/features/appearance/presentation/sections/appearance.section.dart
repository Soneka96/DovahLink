import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/viewmodels/appearance_section.viewmodel.dart';
import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_card.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_appearance_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';

/// The appearance picker's content: an introduction and one card per [DovahThemePreset] in a
/// three-column grid (the prototype's `.preset-intro` and `.preset-grid`), shown inside a
/// [DovahDialog][DovahThemeContext] by whatever screen offers theme selection. The cards of a row
/// share the tallest one's height, as grid items do. The grid drops to fewer columns only when a
/// card would get narrower than [DovahAppearanceMetrics.cardMinimumWidth], which the prototype
/// never meets because its screen is at least 720px wide.
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
        final DovahAppearanceMetrics metrics = context.dovahAppearanceMetrics;

        return Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Choose your Skyrim atmosphere',
              style: TextStyle(
                color: tokens.textPrimary,
                fontSize: DovahAppearanceMetrics.introTitleFontSize,
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: DovahAppearanceMetrics.introGap),
            Text(
              'The interface stays familiar, but its material, shape, density and motion change.',
              style: TextStyle(
                color: tokens.textMuted,
                fontSize: DovahAppearanceMetrics.introBodyFontSize,
                height: DovahAppearanceMetrics.introBodyLineHeight,
              ),
            ),
            SizedBox(height: metrics.introBottomGap),
            LayoutBuilder(
              builder: (BuildContext context, BoxConstraints constraints) {
                final int columns = metrics.columnsFor(constraints.maxWidth);
                const List<DovahThemePreset> presets = DovahThemePreset.values;

                return Column(
                  spacing: metrics.gridGap,
                  children: [
                    for (
                      int start = 0;
                      start < presets.length;
                      start += columns
                    )
                      IntrinsicHeight(
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          spacing: metrics.gridGap,
                          children: [
                            for (int slot = 0; slot < columns; slot++)
                              Expanded(
                                child: start + slot < presets.length
                                    ? AppearancePresetCard(
                                        key: Key(
                                          'appearance-preset-card-${presets[start + slot].name}',
                                        ),
                                        preset: presets[start + slot],
                                        selected:
                                            presets[start + slot] ==
                                            viewModel.activePreset,
                                        onTap: () => viewModel.onSelectPreset(
                                          presets[start + slot],
                                        ),
                                      )
                                    : const SizedBox.shrink(),
                              ),
                          ],
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
