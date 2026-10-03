import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/appearance/presentation/sections/appearance.section.dart';
import 'package:dovahlink_client/features/device_identity/presentation/sections/device_identity.section.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// The single Settings surface shared by Connections and the Session Shell.
class SettingsDialog extends StatelessWidget {
  /// Creates the body shown inside the shared [DovahDialog].
  const SettingsDialog({super.key});

  /// Shows the canonical Settings surface.
  /// @param context The screen that opens Settings.
  /// @return Completes when the dialog closes.
  static Future<void> show(BuildContext context) async {
    await DovahDialog.show<void>(
      context,
      title: 'Settings',
      child: const SettingsDialog(),
    );
  }

  /// Builds device identity, appearance choices, and the prototype's information footer.
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final DovahDialogMetrics metrics = context.dovahDialogMetrics;

    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const DeviceIdentitySection(),
        const AppearanceSection(),
        SizedBox(height: metrics.settingsSharedTopGap),
        DovahSurface(
          key: const Key('settings-shared-principles'),
          role: DovahMaterialRole.surface,
          cornerStyle: DovahPanelCornerStyle.rounded,
          cornerRadius: tokens.cornerRadius,
          material: context.dovahMaterials.surface,
          borderColor: tokens.lineStrong,
          padding: EdgeInsets.symmetric(
            horizontal: metrics.settingsSharedHorizontalPadding,
            vertical: metrics.settingsSharedVerticalPadding,
          ),
          child: Row(
            children: [
              SizedBox(
                width: DovahDialogMetrics.settingsSharedMarkSize,
                height: DovahDialogMetrics.settingsSharedMarkSize,
                child: Center(
                  child: Opacity(
                    opacity: 0.7,
                    child: ColorFiltered(
                      colorFilter: const DovahColorFilter(
                        grayscale: 1,
                      ).toColorFilter(),
                      child: const DovahSigil(
                        size: DovahDialogMetrics.settingsSharedSigilSize,
                      ),
                    ),
                  ),
                ),
              ),
              const SizedBox(
                width: DovahDialogMetrics.settingsSharedContentGap,
              ),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      'One DovahLink',
                      style: TextStyle(
                        color: tokens.textPrimary,
                        fontSize:
                            DovahDialogMetrics.settingsSharedTitleFontSize,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(
                      height: DovahDialogMetrics.settingsSharedDetailGap,
                    ),
                    Text(
                      'Navigation, connection state and accessibility remain consistent in every preset.',
                      style: TextStyle(
                        color: tokens.textMuted,
                        fontSize:
                            DovahDialogMetrics.settingsSharedDetailFontSize,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
        Container(
          key: const Key('settings-landscape-note'),
          margin: EdgeInsets.only(top: metrics.settingsOrientationTopGap),
          padding: EdgeInsets.only(top: metrics.settingsOrientationTopPadding),
          decoration: BoxDecoration(
            border: Border(top: BorderSide(color: tokens.lineSubtle)),
          ),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'Designed for landscape',
                style: TextStyle(
                  color: tokens.textMuted,
                  fontSize: DovahDialogMetrics.settingsOrientationFontSize,
                ),
              ),
              Text(
                'Landscape',
                style: TextStyle(
                  color: tokens.success,
                  fontSize: DovahDialogMetrics.settingsOrientationFontSize,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
