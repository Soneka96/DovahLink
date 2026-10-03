import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/device_identity/presentation/state/viewmodels/device_identity_section.viewmodel.dart';
import 'package:dovahlink_client/features/device_identity/presentation/widgets/device_name_editor.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The Settings `THIS DEVICE` row and its locally persisted display-name editor.
class DeviceIdentitySection extends StatelessWidget {
  /// Creates the device-identity Settings section.
  const DeviceIdentitySection({super.key});

  /// Builds the prototype's side-by-side identity copy and name controls.
  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, DeviceIdentitySectionViewModel>(
      distinct: true,
      converter: (Store<AppState> store) =>
          sl<DeviceIdentitySectionViewModel>(param1: store),
      builder:
          (BuildContext context, DeviceIdentitySectionViewModel viewModel) {
            final DovahThemeTokens tokens = context.dovahTokens;
            final DovahDialogMetrics metrics = context.dovahDialogMetrics;
            final Widget identityCopy = Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'This device',
                  style: TextStyle(
                    color: tokens.textPrimary,
                    fontSize: DovahDialogMetrics.settingsDeviceLabelFontSize,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(
                  height: DovahDialogMetrics.settingsDeviceCopyGap,
                ),
                Text(
                  'Used to identify this companion after pairing.',
                  style: TextStyle(
                    color: tokens.textMuted,
                    fontSize: DovahDialogMetrics.settingsDeviceCopyFontSize,
                  ),
                ),
              ],
            );
            final DeviceNameEditor editor = DeviceNameEditor(
              key: const Key('settings-device-name-editor'),
              displayName: viewModel.displayName,
              loadFailure: viewModel.loadFailure,
              isSaving: viewModel.isSaving,
              saveFailure: viewModel.saveFailure,
              remoteRenameStatus: viewModel.remoteRenameStatus,
              onSave: viewModel.onSave,
            );

            return LayoutBuilder(
              builder: (BuildContext context, BoxConstraints constraints) {
                final bool isStacked =
                    constraints.maxWidth <
                    DovahDialogMetrics.settingsDeviceRowStackBreakpoint;
                return Padding(
                  padding: const EdgeInsets.symmetric(
                    vertical:
                        DovahDialogMetrics.settingsDeviceRowVerticalPadding,
                  ),
                  child: isStacked
                      ? Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            identityCopy,
                            SizedBox(height: metrics.bodyHorizontalPadding / 2),
                            editor,
                          ],
                        )
                      : Row(
                          crossAxisAlignment: CrossAxisAlignment.center,
                          children: [
                            Expanded(child: identityCopy),
                            const SizedBox(
                              width: DovahDialogMetrics.settingsDeviceRowGap,
                            ),
                            editor,
                          ],
                        ),
                );
              },
            );
          },
    );
  }
}
